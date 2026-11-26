namespace DotCraft.Configuration;

public static partial class ModelProviderResolver
{
    internal const string RemoteBackendEndpoint = "https://model-service.invalid";

    private static EffectiveModelRuntime ResolveBackendConfiguration(
        AppConfig config, string providerId, AppConfig.ModelProviderConfig provider)
    {
        var isRemote = config.ModelService is not null;
        if (isRemote && provider.RemoteModels is null)
            throw new ModelProviderConfigurationException(ModelCatalogErrorCode.UnsupportedProtocol,
                "The model service did not provide a backend catalog.");
        var endpoint = isRemote ? RemoteBackendEndpoint : provider.EndPoint?.Trim() ?? string.Empty;
        return new EffectiveModelRuntime(
            providerId, string.Empty, provider.Protocol.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(provider.DisplayName) ? providerId : provider.DisplayName.Trim(),
            isRemote ? string.Empty : provider.ApiKey?.Trim() ?? string.Empty, endpoint,
            Math.Max(1, provider.NetworkTimeoutSeconds ?? config.NetworkTimeoutSeconds),
            NormalizePositiveNullable(provider.MaxOutputTokens), new ModelProviderCapabilities(),
            NormalizeStreamMaxRetries(provider.StreamMaxRetries),
            NormalizeStreamIdleTimeoutMs(provider.StreamIdleTimeoutMs),
            provider.AuthMethod.Trim(),
            SupportsImageGeneration: provider.SupportsImageGeneration ?? false,
            SupportsFreeformTools: provider.SupportsFreeformTools ?? false,
            ProviderStateDirectory: config.GlobalConfigPath is null ? null : Path.GetDirectoryName(config.GlobalConfigPath),
            IsRemote: isRemote,
            CallerNamespace: config.ModelService?.CallerId)
        {
            BackendId = provider.BackendId!.Trim(),
            ConfiguredProtocol = provider.Protocol,
            ConfiguredEndPoint = isRemote ? endpoint : provider.EndPoint,
            ConfiguredMaxOutputTokens = provider.MaxOutputTokens,
            RemoteModels = provider.RemoteModels,
            RemoteCatalogState = provider.RemoteCatalogState,
            RemoteUsage = provider.RemoteUsage,
            BackendSettingsJson = provider.BackendSettings?.ToJsonString()
                ?? throw new ArgumentException("BackendSettings must be a JSON object.")
        };
    }
}
