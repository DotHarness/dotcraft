using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;

namespace DotCraft.ModelService;

public static class ModelServiceProviderCatalog
{
    public static async Task<RemoteModelProvider> DescribeAsync(ModelServiceProvider source, CancellationToken cancellationToken)
    {
        var runtime = source.Runtime;
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
        return source.Description with { Models = models };
    }
}
