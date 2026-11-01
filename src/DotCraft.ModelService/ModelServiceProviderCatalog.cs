using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;

namespace DotCraft.ModelService;

public static class ModelServiceProviderCatalog
{
    public static async Task<RemoteModelProvider> DescribeAsync(ModelServiceProvider source, CancellationToken cancellationToken)
    {
        var runtime = source.Runtime;
        if (source.Backend is IModelCatalogProvider backendCatalog)
        {
            using var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            refresh.CancelAfter(TimeSpan.FromSeconds(5));
            var cached = (source.Backend as OpenRouterProvider)?.ReadCatalog();
            ModelCatalogResult result;
            try
            {
                result = cached is { Source: not "bundled", RefreshedAt: { } date } && date <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - date < TimeSpan.FromMinutes(5)
                    ? cached : await backendCatalog.FetchModelsAsync(runtime, refresh.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && cached is not null)
            {
                result = cached;
                result.Success = false; result.ErrorCode = ModelCatalogErrorCode.Timeout;
                result.ErrorMessage = "The upstream catalog refresh timed out.";
                result.RefreshNotice = "usingCachedCatalog";
            }
            var resolver = source.Backend.GetService(typeof(IProviderModelRuntimeResolver)) as IProviderModelRuntimeResolver
                ?? throw new InvalidOperationException("The built-in backend has no model resolver.");
            ProviderUsageSnapshot? usage = null;
            try { if (result.Success && source.Backend is IProviderUsageReader reader) usage = await reader.ReadAsync(refresh.Token).ConfigureAwait(false); }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            var protocol = runtime.ConfiguredProtocol ?? runtime.Protocol;
            return source.Description with
            {
                Models = result.Models
                    .Where(model => string.IsNullOrWhiteSpace(protocol) || protocol == "auto"
                        || model.SupportedProtocols?.Contains(protocol) == true)
                    .ToDictionary(model => model.Id, model => resolver.ResolveRuntime(runtime with { Model = model.Id }).RequestAdaptation!, StringComparer.Ordinal),
                CatalogState = new ModelCatalogResult { Success = result.Success, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage,
                    Source = result.Source, RefreshedAt = result.RefreshedAt, RefreshNotice = result.RefreshNotice },
                Usage = usage,
                Authentication = result.ErrorCode is ModelCatalogErrorCode.Unauthorized or ModelCatalogErrorCode.Forbidden
                    ? new ProviderAuthenticationStatus(false, FailureCode: result.ErrorCode.ToString(), FailureMessage: result.ErrorMessage) : source.Description.Authentication
            };
        }
        if (runtime.BackendId is not null)
            throw new InvalidOperationException("Local model backends are not supported by the model service.");
        IModelProvider provider = runtime.Protocol == ModelProviderProtocols.Anthropic
            ? new AnthropicClientProvider() : new OpenAIClientProvider(source.Authentication);
        var catalog = await ((IModelCatalogProvider)provider).FetchModelsAsync(runtime, cancellationToken).ConfigureAwait(false);
        var description = Describe(source, catalog.Models.Select(model => model.Id));
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

    public static RemoteModelProvider Describe(ModelServiceProvider source, IEnumerable<string> modelIds)
    {
        var runtime = source.Runtime;
        if (runtime.BackendId is not null)
            throw new InvalidOperationException("Local model backends are not supported by the model service.");
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
                ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(runtime.EndPoint, model, thinkingCatalogPath));
        }, StringComparer.OrdinalIgnoreCase);
        return source.Description with
        {
            Models = models,
            WebSearchBackend = ProviderWebSearchBackend.Resolve(runtime.EndPoint, runtime.AuthMethod)
        };
    }
}
