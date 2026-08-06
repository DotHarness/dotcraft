using DotCraft.Agents;

namespace DotCraft.Configuration;

public static class ModelProviderCatalog
{
    public static async Task<ModelCatalogResult> FetchAsync(
        AppConfig config,
        ModelProviderRegistry providerRegistry,
        string? providerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(providerRegistry);

        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveProvider(config, providerId);
        }
        catch (ModelProviderConfigurationException ex)
        {
            return Failure(ex.ErrorCode, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Failure(ModelCatalogErrorCode.Unknown, ex.Message);
        }

        ModelCatalogResult result;
        try
        {
            var catalog = providerRegistry.GetService<IModelCatalogProvider>(runtime.Protocol);
            result = catalog == null
                ? Failure(
                    ModelCatalogErrorCode.UnsupportedProtocol,
                    $"Protocol '{runtime.Protocol}' does not support model listing.")
                : await catalog.FetchModelsAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelProviderNotRegisteredException ex)
        {
            result = Failure(ModelCatalogErrorCode.UnsupportedProtocol, ex.Message);
        }

        result.ProviderId = runtime.ProviderId;
        result.Protocol = runtime.Protocol;
        result.EndPoint = runtime.EndPoint;
        return result;
    }

    private static ModelCatalogResult Failure(ModelCatalogErrorCode code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        Models = []
    };
}
