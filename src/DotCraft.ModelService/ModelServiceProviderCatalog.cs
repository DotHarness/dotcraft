using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;

namespace DotCraft.ModelService;

public static class ModelServiceProviderCatalog
{
    public static async Task<RemoteModelProvider> DescribeAsync(ModelServiceProvider source, CancellationToken cancellationToken)
    {
        var runtime = source.Runtime;
        if (source.Backend is not null || runtime.BackendId is not null)
        {
            try { return await DescribeBackendAsync(source, cancellationToken).ConfigureAwait(false); }
            catch (InvalidOperationException)
            {
                return Unavailable(source.Description with { Endpoint = string.Empty, OpenRouterRouting = null, WebSearchBackend = null });
            }
        }
        if (source.Description.Models is not null)
            return source.Description with
            {
                OpenRouterRouting = null,
                WebSearchBackend = ProviderWebSearchBackend.Resolve(runtime.EndPoint, runtime.AuthMethod)
            };
        IModelProvider provider = runtime.Protocol == ModelProviderProtocols.Anthropic
            ? new AnthropicClientProvider() : new OpenAIClientProvider(source.Authentication);
        var catalog = await ((IModelCatalogProvider)provider).FetchModelsAsync(runtime, cancellationToken).ConfigureAwait(false);
        var modelIds = runtime.IsChatGptOAuth && provider is OpenAIClientProvider openAI
            ? openAI.GetChatGptRuntimeModelIds(runtime)
            : catalog.Models.Select(model => model.Id);
        var description = Describe(source, modelIds);
        if (source.Authentication is { } authentication)
        {
            var status = authentication.GetStatus();
            description = description with
            {
                Authentication = new ProviderAuthenticationStatus(status.LoggedIn, status.AccountId,
                    PlanType: status.PlanType, Email: status.Email, LastRefresh: status.LastRefresh,
                    AccessTokenExpiresAt: status.AccessTokenExpiresAt)
            };
        }
        return description;
    }

    private static async Task<RemoteModelProvider> DescribeBackendAsync(
        ModelServiceProvider source, CancellationToken cancellationToken)
    {
        var backend = source.Backend;
        var description = source.Description with
        {
            Endpoint = string.Empty,
            BackendId = source.Runtime.BackendId ?? source.Description.BackendId,
            WebSearchBackend = null,
            OpenRouterRouting = (backend as OpenRouterProvider)?.GetRemoteRoutingPreferences()
        };
        if (source.Stopping.IsCancellationRequested || backend is null
            || backend.GetService(typeof(IProviderNativeHttpRouter)) is not IProviderNativeHttpRouter router)
            return Unavailable(description);
        if (description.Models is not null)
            return description;

        using var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, source.Stopping);
        refresh.CancelAfter(TimeSpan.FromSeconds(5));
        var token = refresh.Token;
        var cached = (backend.GetService(typeof(IProviderCachedModelCatalog)) as IProviderCachedModelCatalog)?.ReadCatalog();
        ModelCatalogResult result;
        try
        {
            var now = DateTimeOffset.UtcNow;
            result = cached is { Source: not "bundled", RefreshedAt: { } date }
                && date <= now && now - date < TimeSpan.FromMinutes(5)
                ? cached
                : backend.GetService(typeof(IModelCatalogProvider)) is IModelCatalogProvider catalog
                    ? await catalog.FetchModelsAsync(source.Runtime, token).WaitAsync(token).ConfigureAwait(false)
                    : new ModelCatalogResult { ErrorCode = ModelCatalogErrorCode.UnsupportedProtocol };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (source.Stopping.IsCancellationRequested)
                return Unavailable(description);
            result = new ModelCatalogResult
            {
                ErrorCode = ModelCatalogErrorCode.Timeout, Source = cached?.Source, RefreshedAt = cached?.RefreshedAt,
                RefreshNotice = cached is null ? null : "usingCachedCatalog", Models = cached?.Models ?? []
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or ProviderAuthenticationException)
        {
            result = new ModelCatalogResult { ErrorCode = ex is ProviderAuthenticationException
                ? ModelCatalogErrorCode.Unauthorized : ModelCatalogErrorCode.Network };
        }

        var authentication = description.Authentication;
        var usage = description.Usage;
        try
        {
            if (backend.GetService(typeof(IProviderAuthenticator)) is IProviderAuthenticator authenticator)
            {
                var status = await authenticator.GetStatusAsync(token).WaitAsync(token).ConfigureAwait(false);
                authentication = status with { FailureMessage = null };
            }
            if (result.Success && backend is OpenRouterProvider openRouter)
                usage = await openRouter.ReadAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or ProviderAuthenticationException or System.Text.Json.JsonException) { }

        if (source.Stopping.IsCancellationRequested)
            return Unavailable(description);

        var models = new Dictionary<string, ProviderRequestAdaptation>(StringComparer.Ordinal);
        var protocol = source.Runtime.ConfiguredProtocol ?? source.Runtime.Protocol;
        foreach (var model in result.Models)
        {
            if (!string.IsNullOrWhiteSpace(protocol) && protocol != "auto"
                && model.SupportedProtocols?.Contains(protocol) == false)
                continue;
            try
            {
                var (selected, _, _) = ModelServiceBackendRouting.Resolve(backend, router,
                    source.Runtime with { Model = model.Id, ModelMetadata = model });
                var adaptation = selected.RequestAdaptation ?? CreateNativeAdaptation(selected);
                models.TryAdd(model.Id, adaptation with
                {
                    Model = selected.ModelMetadata ?? model,
                    Protocol = selected.Protocol,
                    Capabilities = selected.Capabilities,
                    ContextBudget = adaptation.ContextBudget ?? selected.ContextBudget
                });
            }
            catch (ArgumentException) { }
            catch (ModelBackendUnavailableException) when (source.Stopping.IsCancellationRequested)
            {
                return Unavailable(description);
            }
        }

        return description with
        {
            Models = models,
            CatalogState = new ModelCatalogResult
            {
                Success = result.Success, ErrorCode = result.ErrorCode, Source = result.Source,
                RefreshedAt = result.RefreshedAt, RefreshNotice = result.RefreshNotice,
                ErrorMessage = result.Success ? null : "The upstream model catalog could not be refreshed."
            },
            Usage = usage,
            Authentication = result.ErrorCode is ModelCatalogErrorCode.Unauthorized or ModelCatalogErrorCode.Forbidden
                or ModelCatalogErrorCode.MissingApiKey
                ? new ProviderAuthenticationStatus(false, FailureCode: result.ErrorCode.ToString()) : authentication
        };
    }

    private static RemoteModelProvider Unavailable(RemoteModelProvider description) => description with
    {
        Models = new Dictionary<string, ProviderRequestAdaptation>(),
        CatalogState = new ModelCatalogResult
        {
            ErrorCode = ModelCatalogErrorCode.UnsupportedProtocol,
            ErrorMessage = "The backend is unavailable or does not support native HTTP routing."
        },
        Authentication = new ProviderAuthenticationStatus(false, FailureCode: "backend_unavailable"),
        Usage = null
    };

    private static ProviderRequestAdaptation CreateNativeAdaptation(EffectiveModelRuntime runtime)
    {
        var path = runtime.ProviderStateDirectory is null
            ? null : Path.Combine(runtime.ProviderStateDirectory, ModelThinkingAdapterCatalog.FileName);
        return new ProviderRequestAdaptation(runtime.UseResponsesLite,
            ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(runtime.EndPoint, runtime.Model, path),
            ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(runtime.EndPoint, runtime.Model, path),
            ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(runtime.EndPoint, runtime.Model, path));
    }

    public static RemoteModelProvider Describe(ModelServiceProvider source, IEnumerable<string> modelIds)
    {
        var runtime = source.Runtime;
        if (runtime.BackendId is not null)
            throw new InvalidOperationException("A backend description requires asynchronous catalog resolution.");
        IModelProvider provider = runtime.Protocol == ModelProviderProtocols.Anthropic
            ? new AnthropicClientProvider() : new OpenAIClientProvider(source.Authentication);
        var thinkingCatalogPath = runtime.ProviderStateDirectory is null
            ? null : Path.Combine(runtime.ProviderStateDirectory, ModelThinkingAdapterCatalog.FileName);
        var models = modelIds.ToDictionary(model => model, model =>
        {
            var selected = runtime with { Model = model };
            var metadata = (provider as IProviderRuntimeMetadataResolver)?.Resolve(selected);
            return new ProviderRequestAdaptation(
                metadata?.UseLightweightResponses ?? false,
                ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(runtime.EndPoint, model, thinkingCatalogPath),
                ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(runtime.EndPoint, model, thinkingCatalogPath),
                ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(runtime.EndPoint, model, thinkingCatalogPath))
            {
                ContextBudget = metadata?.ContextBudget
            };
        }, StringComparer.OrdinalIgnoreCase);
        return source.Description with
        {
            Models = models,
            WebSearchBackend = ProviderWebSearchBackend.Resolve(runtime.EndPoint, runtime.AuthMethod)
        };
    }
}
