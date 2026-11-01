namespace DotCraft.Configuration;

public static partial class ModelProviderResolver
{
    private static EffectiveModelRuntime ResolveBackendConfiguration(
        AppConfig config, string providerId, AppConfig.ModelProviderConfig provider)
    {
        if (config.ModelService is not null && provider.RemoteModels is null)
            throw new ModelProviderConfigurationException(ModelCatalogErrorCode.UnsupportedProtocol,
                "Local model backends are not supported by the model service.");
        return new EffectiveModelRuntime(
            providerId, string.Empty, provider.Protocol.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(provider.DisplayName) ? providerId : provider.DisplayName.Trim(),
            provider.ApiKey?.Trim() ?? string.Empty, provider.EndPoint?.Trim() ?? string.Empty,
            Math.Max(1, provider.NetworkTimeoutSeconds ?? config.NetworkTimeoutSeconds),
            NormalizePositiveNullable(provider.MaxOutputTokens), new ModelProviderCapabilities(),
            NormalizeStreamMaxRetries(provider.StreamMaxRetries),
            NormalizeStreamIdleTimeoutMs(provider.StreamIdleTimeoutMs),
            provider.AuthMethod.Trim(),
            SupportsImageGeneration: provider.SupportsImageGeneration ?? false,
            SupportsFreeformTools: provider.SupportsFreeformTools ?? false,
            ProviderStateDirectory: config.GlobalConfigPath is null ? null : Path.GetDirectoryName(config.GlobalConfigPath),
            IsRemote: config.ModelService is not null,
            CallerNamespace: config.ModelService?.CallerId)
        {
            BackendId = provider.BackendId!.Trim(),
            ConfiguredProtocol = provider.Protocol,
            ConfiguredEndPoint = provider.EndPoint,
            ConfiguredMaxOutputTokens = provider.MaxOutputTokens,
            RemoteModels = provider.RemoteModels,
            RemoteCatalogState = provider.RemoteCatalogState,
            RemoteUsage = provider.RemoteUsage,
            BackendSettingsJson = provider.BackendSettings?.ToJsonString()
                ?? throw new ArgumentException("BackendSettings must be a JSON object.")
        };
    }
}
