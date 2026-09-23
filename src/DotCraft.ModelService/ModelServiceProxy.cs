using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.ModelService;

internal sealed class ModelServiceProxy(
    IModelServiceAccess access,
    IHttpClientFactory clients,
    IEnumerable<IModelServiceObserver> observers,
    ILogger<ModelServiceProxy> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task CatalogAsync(HttpContext context)
    {
        var caller = await AuthenticateAsync(context).ConfigureAwait(false);
        if (caller is null)
            return;
        var providers = await access.GetProvidersAsync(caller, context.RequestAborted).ConfigureAwait(false);
        var descriptions = await Task.WhenAll(providers.Select(provider =>
            provider.Description.Models is null
                ? ModelServiceProviderCatalog.DescribeAsync(provider, context.RequestAborted)
                : Task.FromResult(provider.Description))).ConfigureAwait(false);
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
        var providers = await access.GetProvidersAsync(caller, cancellationToken).ConfigureAwait(false);
        var provider = providers.FirstOrDefault(item => item.Description.Id == providerId);
        if (provider is null)
        {
            await ErrorAsync(context, 403, "provider_forbidden", "This client cannot use the requested provider.");
            return;
        }
        if (!IsAllowedOperation(provider.Runtime.Protocol, context.Request.Method, operation))
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
        IDisposable? lease;
        try
        {
            lease = await access.BeginRequestAsync(caller, requestContext, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelServiceException ex)
        {
            await ErrorAsync(context, (int)ex.StatusCode!, ex.Code, ex.Message);
            return;
        }
        using var requestLease = lease;

        var started = DateTimeOffset.UtcNow;
        var completed = false;
        ProviderHttpUsage? usage = null;
        IProviderHttpUsageObserver? reader = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(provider.Runtime.NetworkTimeoutSeconds));
            if (provider.Authentication is not null)
                context.Request.EnableBuffering();
            var token = provider.Authentication is null ? provider.Runtime.ApiKey
                : await provider.Authentication.GetAccessTokenAsync(false, timeout.Token).ConfigureAwait(false);
            using var response = await SendWithAuthenticationAsync(context, provider, operation, token, timeout.Token)
                .ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (ModelServiceProtocol.IsResponseHeader(header.Key))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            var eventStream = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
            reader = provider.Runtime.Protocol == ModelProviderProtocols.Anthropic
                ? AnthropicHttpUsageObserver.Create(eventStream)
                : OpenAIHttpUsageObserver.Create(eventStream);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            if (context.Response.HasStarted)
                context.Abort();
            else
                await ErrorAsync(context, 502, "upstream_unavailable", "The upstream model request could not be completed.");
        }
        finally
        {
            usage ??= reader?.Usage;
            var call = new ModelServiceCall(caller, providerId, requestContext, context.Response.StatusCode,
                usage, started, DateTimeOffset.UtcNow - started, completed);
            foreach (var observer in observers)
            {
                try { await observer.OnCompletedAsync(call, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { logger.LogWarning(ex, "Model service usage recording failed."); }
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithAuthenticationAsync(
        HttpContext context, ModelServiceProvider provider, string operation, string token, CancellationToken cancellationToken)
    {
        var response = await SendAsync(context, provider, operation, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || provider.Authentication is null)
            return response;
        response.Dispose();
        token = await provider.Authentication.RecoverUnauthorizedAsync(token, cancellationToken).ConfigureAwait(false);
        context.Request.Body.Position = 0;
        return await SendAsync(context, provider, operation, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpContext context, ModelServiceProvider provider, string operation, string token, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(provider.Runtime.EndPoint.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            new Uri(endpoint, operation + context.Request.QueryString));
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
        if (provider.Runtime.Protocol == ModelProviderProtocols.Anthropic)
            request.Headers.TryAddWithoutValidation("x-api-key", token);
        else
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (provider.Authentication?.GetAccountId() is { } account)
            request.Headers.TryAddWithoutValidation("chatgpt-account-id", account);
        using var client = clients.CreateClient("DotCraft.ModelService.Upstream");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
                or ("POST", "images/generations") or ("POST", "images/edits");

    private static Task ErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.Headers[ModelServiceProtocol.ErrorHeader] = code;
        return context.Response.WriteAsJsonAsync(new ModelServiceError(code, message), context.RequestAborted);
    }
}
