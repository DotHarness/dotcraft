using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotCraft.ModelService;

internal sealed class ModelServiceProxy(
    IModelServiceAccess access,
    IHttpClientFactory clients,
    IEnumerable<IModelServiceObserver> observers,
    ILogger<ModelServiceProxy> logger,
    IHostApplicationLifetime hostLifetime)
{
    private const long MaxBackendBodyBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task CatalogAsync(HttpContext context)
    {
        var caller = await AuthenticateAsync(context).ConfigureAwait(false);
        if (caller is null)
            return;
        var providers = await access.GetProvidersAsync(caller, context.RequestAborted).ConfigureAwait(false);
        var descriptions = await Task.WhenAll(providers.Select(provider =>
            ModelServiceProviderCatalog.DescribeAsync(provider, context.RequestAborted))).ConfigureAwait(false);
        await context.Response.WriteAsJsonAsync(
            new ModelServiceCatalog(caller.Id, descriptions),
            context.RequestAborted).ConfigureAwait(false);
    }

    public async Task ForwardAsync(HttpContext context, string providerId, string operation)
    {
        var cancellationToken = context.RequestAborted;
        var caller = await AuthenticateAsync(context).ConfigureAwait(false);
        if (caller is null)
            return;
        ModelServiceProvider? provider;
        try
        {
            provider = await access.GetProviderAsync(caller, providerId, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelServiceException ex)
        {
            await ErrorAsync(context, (int)ex.StatusCode!, ex.Code, ex.Message);
            return;
        }
        if (provider is null)
        {
            await ErrorAsync(context, 403, "provider_forbidden", "This client cannot use the requested provider.");
            return;
        }
        var usesBackend = provider.Backend is not null || provider.Runtime.BackendId is not null;
        IProviderNativeHttpRouter? router;
        try { router = provider.Backend?.GetService(typeof(IProviderNativeHttpRouter)) as IProviderNativeHttpRouter; }
        catch (InvalidOperationException) { router = null; }
        if (provider.Stopping.IsCancellationRequested || usesBackend && router is null)
        {
            await ErrorAsync(context, 503, "backend_unavailable", "The requested backend is unavailable for native HTTP inference.");
            return;
        }
        if (!usesBackend && !(context.Request.Method == "POST" && operation == "tools/search"
            ? ProviderWebSearchBackend.Resolve(provider.Runtime.EndPoint, provider.Runtime.AuthMethod) == ProviderWebSearchBackend.DeepSeek
            : IsAllowedOperation(provider.Runtime.Protocol, context.Request.Method, operation)))
        {
            await ErrorAsync(context, 404, "operation_not_supported", "This model operation is not supported.");
            return;
        }

        ModelServiceRequestContext requestContext;
        try
        {
            requestContext = JsonSerializer.Deserialize<ModelServiceRequestContext>(
                Convert.FromBase64String(context.Request.Headers[ModelServiceProtocol.ContextHeader].ToString()), Json)
                ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            await ErrorAsync(context, 400, "invalid_context", "The model request context is invalid.");
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, provider.Stopping, hostLifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(provider.Runtime.NetworkTimeoutSeconds));
        var upstreamToken = timeout.Token;
        Uri? backendTarget = null;
        if (router is not null)
        {
            try
            {
                var (runtime, target, expected) = ModelServiceBackendRouting.Resolve(provider.Backend!, router,
                    provider.Runtime with { Model = requestContext.Model });
                if (context.Request.Method != HttpMethods.Post || operation != expected
                    || (requestContext.Operation is not null && requestContext.Operation != expected)
                    || context.Request.QueryString.HasValue)
                    throw new ArgumentException();

                await ValidateBackendBodyAsync(context.Request, requestContext.Model, upstreamToken).ConfigureAwait(false);
                provider = provider with { Runtime = runtime };
                backendTarget = target;
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                await ErrorAsync(context, 400, "invalid_model_operation", "The request does not match the backend's native model operation.");
                return;
            }
            catch (IOException)
            {
                await ErrorAsync(context, 413, "request_body_too_large", "The native backend request exceeds the supported size.");
                return;
            }
            catch (InvalidOperationException)
            {
                await ErrorAsync(context, 503, "backend_unavailable", "The requested backend is unavailable.");
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await WriteCancellationErrorAsync(context, provider).ConfigureAwait(false);
                return;
            }
        }

        IDisposable? lease;
        try
        {
            lease = await access.BeginRequestAsync(caller, requestContext, upstreamToken).ConfigureAwait(false);
        }
        catch (ModelServiceException ex)
        {
            await ErrorAsync(context, (int)ex.StatusCode!, ex.Code, ex.Message);
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteCancellationErrorAsync(context, provider).ConfigureAwait(false);
            return;
        }
        using var requestLease = lease;

        var started = DateTimeOffset.UtcNow;
        var completed = false;
        ProviderHttpUsage? usage = null;
        IProviderHttpUsageObserver? reader = null;
        try
        {
            upstreamToken.ThrowIfCancellationRequested();
            using var response = router is null
                ? await SendWithAuthenticationAsync(context, provider, operation, upstreamToken).ConfigureAwait(false)
                : await SendAsync(context, backendTarget!,
                    await router.AuthenticateAsync(provider.Runtime, upstreamToken).ConfigureAwait(false), upstreamToken).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (ModelServiceProtocol.IsResponseHeader(header.Key))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            reader = provider.Runtime.Protocol == ModelProviderProtocols.Anthropic
                ? mediaType is null
                    ? AnthropicHttpUsageObserver.CreateAuto()
                    : AnthropicHttpUsageObserver.Create(mediaType == "text/event-stream")
                : mediaType is null
                    ? OpenAIHttpUsageObserver.CreateAuto()
                    : OpenAIHttpUsageObserver.Create(mediaType == "text/event-stream");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                reader.Append(buffer.AsSpan(0, count));
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            reader.Complete();
            usage = reader.Usage;
            completed = true;
        }
        catch (OpenAIAuthException ex) when (!context.Response.HasStarted)
        {
            var network = ex.Reason == OpenAIAuthFailureReason.Network;
            await ErrorAsync(context, network ? 502 : 401,
                network ? "upstream_unavailable" : "upstream_authentication_required", ex.Message);
        }
        catch (ProviderAuthenticationException) when (!context.Response.HasStarted)
        {
            await ErrorAsync(context, 401, "upstream_authentication_required", "Upstream authentication is required.");
        }
        catch (InvalidOperationException) when (usesBackend && !context.Response.HasStarted)
        {
            await ErrorAsync(context, 503, "backend_unavailable", "The requested backend is unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || context.Response.HasStarted)
        {
            context.Abort();
        }
        catch (OperationCanceledException)
        {
            await WriteCancellationErrorAsync(context, provider).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            if (context.Response.HasStarted)
                context.Abort();
            else
                await ErrorAsync(context, 502, "upstream_unavailable", "The upstream model request could not be completed.");
        }
        finally
        {
            usage ??= reader?.Usage;
            decimal? costUsd = null;
            try
            {
                var multiplier = operation is "responses" or "responses/compact"
                    ? OpenAIServiceTierPricing.GetMultiplier(requestContext.Model, requestContext.RequestedServiceTier,
                        reader?.ServiceTier, provider.Runtime.IsChatGptOAuth)
                    : 1m;
                var pricing = provider.Runtime.ModelMetadata?.Pricing
                    ?? ModelCatalog.ResolvePricing(new AppConfig(), requestContext.Model);
                costUsd = ModelCostCalculator.Resolve(pricing, usage, multiplier, reader?.ReportedCostUsd);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Model service cost estimation failed.");
            }
            var call = new ModelServiceCall(caller, providerId, requestContext, context.Response.StatusCode,
                usage, started, DateTimeOffset.UtcNow - started, completed, costUsd);
            foreach (var observer in observers)
            {
                try { await observer.OnCompletedAsync(call, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { logger.LogWarning(ex, "Model service usage recording failed."); }
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthenticationAsync(
        HttpContext context, ModelServiceProvider provider, string operation, CancellationToken cancellationToken)
    {
        if (provider.Authentication is not null)
            context.Request.EnableBuffering();
        var token = provider.Authentication is null ? provider.Runtime.ApiKey
            : await provider.Authentication.GetAccessTokenAsync(false, cancellationToken).ConfigureAwait(false);
        var endpoint = new Uri(provider.Runtime.EndPoint.TrimEnd('/') + "/");
        var target = operation == "tools/search" ? ProviderWebSearchBackend.DeepSeekSearchEndpoint
            : new Uri(endpoint, operation + context.Request.QueryString);
        var response = await SendAsync(context, target, NativeAuthHeaders(token, provider.Authentication), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || provider.Authentication is null)
            return response;
        response.Dispose();
        token = await provider.Authentication.RecoverUnauthorizedAsync(token, cancellationToken).ConfigureAwait(false);
        context.Request.Body.Position = 0;
        return await SendAsync(context, target, NativeAuthHeaders(token, provider.Authentication), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> NativeAuthHeaders(string token, OpenAIAuthManager? authentication)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = new AuthenticationHeaderValue("Bearer", token).ToString() };
        if (authentication?.GetAccountId() is { } account)
            headers["chatgpt-account-id"] = account;
        return headers;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpContext context, Uri target, IReadOnlyDictionary<string, string> authentication, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        foreach (var header in context.Request.Headers)
            if (ModelServiceProtocol.IsRequestHeader(header.Key))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        if (context.Request.Method == "POST")
        {
            request.Content = new StreamContent(new NonDisposingStream(context.Request.Body));
            foreach (var header in context.Request.Headers)
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        foreach (var (name, value) in authentication)
        {
            if (ModelServiceProtocol.IsRequestHeader(name)
                || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)
                || ForbiddenAuthenticationHeaders.Contains(name)
                || value.Any(c => c is '\r' or '\n' or '\0')
                || !request.Headers.TryAddWithoutValidation(name, value))
                throw new ProviderAuthenticationException("InvalidUpstreamAuthentication", "The provider returned an invalid authentication header.");
        }
        using var client = clients.CreateClient("DotCraft.ModelService.Upstream");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static readonly HashSet<string> ForbiddenAuthenticationHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Cookie", "Host", "Keep-Alive", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    };

    private static async Task ValidateBackendBodyAsync(HttpRequest request, string model, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            || !string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || (media.CharSet is { } charset && !string.Equals(charset.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            || request.Headers.ContentEncoding.Any(encoding => !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException();

        if (request.ContentLength > MaxBackendBodyBytes || (request.Body.CanSeek && request.Body.Length > MaxBackendBodyBytes))
            throw new IOException();
        request.EnableBuffering(bufferLimit: MaxBackendBodyBytes);
        using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;
        if (body.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException();

        var found = false;
        foreach (var property in body.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals("model", StringComparison.OrdinalIgnoreCase))
                continue;
            if (found || !property.NameEquals("model") || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() != model)
                throw new ArgumentException();
            found = true;
        }
        if (!found)
            throw new ArgumentException();
    }

    private Task WriteCancellationErrorAsync(HttpContext context, ModelServiceProvider provider)
    {
        if (hostLifetime.ApplicationStopping.IsCancellationRequested)
            return ErrorAsync(context, 503, "service_stopping", "The model service is stopping.");
        return provider.Stopping.IsCancellationRequested
            ? ErrorAsync(context, 503, "backend_unavailable", "The requested backend is unavailable.")
            : ErrorAsync(context, 502, "upstream_unavailable", "The upstream model request could not be completed.");
    }

    private async ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context)
    {
        var caller = await access.AuthenticateAsync(context, context.RequestAborted).ConfigureAwait(false);
        if (caller is null)
            await ErrorAsync(context, 401, "client_unauthorized", "The model service client credential is invalid.");
        return caller;
    }

    private static bool IsAllowedOperation(string protocol, string method, string operation) =>
        protocol == ModelProviderProtocols.Anthropic
            ? (method, operation) is ("GET", "v1/models") or ("POST", "v1/messages")
            : (method, operation) is ("GET", "models") or ("POST", "chat/completions")
                or ("POST", "responses") or ("POST", "responses/compact")
                or ("POST", "images/generations") or ("POST", "images/edits")
                or ("POST", "alpha/search");

    private static Task ErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.Headers[ModelServiceProtocol.ErrorHeader] = code;
        return context.Response.WriteAsJsonAsync(new ModelServiceError(code, message), context.RequestAborted);
    }
}
