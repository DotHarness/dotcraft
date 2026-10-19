using System.Runtime.CompilerServices;
using DotCraft.Agents;

namespace DotCraft.Configuration;

public static class ModelProviderCatalog
{
    // A catalog belongs to the provider stack that fetched it, not to the process.
    private static readonly ConditionalWeakTable<IModelRuntimeResolver, ModelProviderCatalogCache> Caches = new();

    public static Task<ModelCatalogResult> FetchAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        string? providerId = null,
        ModelCatalogRefreshStrategy refreshStrategy = ModelCatalogRefreshStrategy.OnlineIfUncached,
        CancellationToken cancellationToken = default) =>
        FetchAsync(config, (IModelRuntimeResolver)providerRegistry, providerId, refreshStrategy, cancellationToken);

    public static async Task<ModelCatalogResult> FetchAsync(
        AppConfig config,
        IModelRuntimeResolver providerRegistry,
        string? providerId = null,
        ModelCatalogRefreshStrategy refreshStrategy = ModelCatalogRefreshStrategy.OnlineIfUncached,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(providerRegistry);

        EffectiveModelRuntime runtime;
        try
        {
            runtime = providerRegistry.Normalize(ModelProviderResolver.ResolveProvider(config, providerId));
        }
        catch (ModelProviderConfigurationException ex)
        {
            return Failure(ex.ErrorCode, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Failure(ModelCatalogErrorCode.Unknown, ex.Message);
        }
        catch (ModelBackendUnavailableException ex)
        {
            return Failure(ModelCatalogErrorCode.UnsupportedProtocol, ex.Message);
        }

        var cache = Caches.GetValue(providerRegistry, static _ => new ModelProviderCatalogCache());
        var result = await cache.GetOrFetchAsync(
            ModelProviderCatalogCache.BuildIdentity(runtime),
            refreshStrategy,
            ct => FetchFromProviderAsync(providerRegistry, runtime, ct),
            cancellationToken).ConfigureAwait(false);

        result.ProviderId = runtime.ProviderId;
        result.Protocol = runtime.Protocol;
        result.EndPoint = runtime.EndPoint;
        return result;
    }

    private static async Task<ModelCatalogResult> FetchFromProviderAsync(
        IModelRuntimeResolver providerRegistry,
        EffectiveModelRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = providerRegistry.Resolve(runtime).GetService(typeof(IModelCatalogProvider)) as IModelCatalogProvider;
            return catalog == null
                ? Failure(
                    ModelCatalogErrorCode.UnsupportedProtocol,
                    $"Protocol '{runtime.Protocol}' does not support model listing.")
                : await catalog.FetchModelsAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelProviderNotRegisteredException ex)
        {
            return Failure(ModelCatalogErrorCode.UnsupportedProtocol, ex.Message);
        }
        catch (ModelBackendUnavailableException ex)
        {
            return Failure(ModelCatalogErrorCode.UnsupportedProtocol, ex.Message);
        }
    }

    private static ModelCatalogResult Failure(ModelCatalogErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        Models = []
    };
}
