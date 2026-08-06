using System.Net.Http.Headers;
using System.Text.Json;

namespace DotCraft.Configuration;

public static class ModelProviderResolver
{
    public const string MissingProviderMessage = "Create or select a model provider.";

    public static EffectiveModelRuntime ResolveMain(
        AppConfig config,
        string? providerIdOverride = null,
        string? modelOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var provider = ResolveProvider(config, providerIdOverride);
        var model = string.IsNullOrWhiteSpace(modelOverride)
            ? ResolveConfiguredModel(config, provider.ProviderId)
            : modelOverride;

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(config));

        return provider with { Model = model.Trim() };
    }

    public static EffectiveModelRuntime ResolveConsolidation(
        AppConfig config,
        string? providerIdOverride = null,
        string? mainModelOverride = null)
    {
        var model = string.IsNullOrWhiteSpace(config.ConsolidationModel)
            ? mainModelOverride
            : config.ConsolidationModel;
        return ResolveMain(config, providerIdOverride, model);
    }

    public static EffectiveModelRuntime ResolveSubAgent(
        AppConfig config,
        string effectiveMainProviderId,
        string effectiveMainModel)
    {
        var model = ModelPreferenceRules.Find(
                config.SubAgent.ProviderPreferences,
                effectiveMainProviderId)?.Model
            ?? effectiveMainModel;
        return ResolveMain(config, effectiveMainProviderId, model);
    }

    internal static string? ResolveConfiguredModel(AppConfig config, string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var effectiveProviderId = NormalizeProviderId(providerId);
        if (string.IsNullOrWhiteSpace(effectiveProviderId))
            effectiveProviderId = NormalizeProviderId(config.ProviderId);
        return string.IsNullOrWhiteSpace(effectiveProviderId)
            ? null
            : ModelPreferenceRules.Find(config.ProviderPreferences, effectiveProviderId)?.Model;
    }

    /// <summary>Resolves the complete MainAgent preference for a provider.</summary>
    public static ModelPreference ResolveMainPreference(AppConfig config, string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var runtime = ResolveProvider(config, providerId);
        var preference = ModelPreferenceRules.Find(config.ProviderPreferences, runtime.ProviderId);
        if (preference is null)
            throw new ArgumentException("Model must be configured.", nameof(config));
        return ModelPreferenceRules.Normalize(config, runtime.ProviderId, preference);
    }

    /// <summary>
    /// Resolves a native SubAgent preference, inheriting the complete parent preference when absent.
    /// </summary>
    public static ModelPreference ResolveSubAgentPreference(
        AppConfig config,
        string effectiveMainProviderId,
        ModelPreference parentPreference,
        string? roleModel = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(parentPreference);
        var preference = ModelPreferenceRules.Find(
                config.SubAgent.ProviderPreferences,
                effectiveMainProviderId)
            ?? ModelPreferenceRules.Clone(parentPreference);
        if (!string.IsNullOrWhiteSpace(roleModel))
            preference.Model = roleModel.Trim();
        return ModelPreferenceRules.Normalize(config, effectiveMainProviderId, preference);
    }

    public static EffectiveModelRuntime ResolveProvider(AppConfig config, string? providerIdOverride = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerId = NormalizeProviderId(providerIdOverride);
        if (string.IsNullOrWhiteSpace(providerId))
            providerId = NormalizeProviderId(config.ProviderId);

        if (string.IsNullOrWhiteSpace(providerId))
            throw new ModelProviderConfigurationException(
                ModelCatalogErrorCode.ProviderNotConfigured,
                MissingProviderMessage);

        if (TryResolveExplicitProvider(config, providerId, out var explicitRuntime))
            return explicitRuntime;

        throw new ModelProviderConfigurationException(
            ModelCatalogErrorCode.ProviderNotConfigured,
            $"Model provider '{providerId}' is not configured. {MissingProviderMessage}");
    }

    public static bool IsImplicitProviderId(string? providerId) => false;

    public static bool ResolveHostedImageGenerationSupport(AppConfig.ModelProviderConfig provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var configuredAuthMethod = string.IsNullOrWhiteSpace(provider.AuthMethod)
            ? ModelProviderAuthMethods.ApiKey
            : provider.AuthMethod.Trim();
        var authMethod = ModelProviderAuthMethods.Normalize(configuredAuthMethod);
        var protocol = ModelProviderProtocols.Normalize(provider.Protocol);

        if (authMethod == ModelProviderAuthMethods.ChatGptOAuth && ModelProviderProtocols.IsOpenAIProtocol(protocol))
            protocol = ModelProviderProtocols.OpenAIResponses;

        var endpoint = NormalizeEndpoint(protocol, provider.EndPoint, authMethod);
        return provider.SupportsHostedImageGeneration
            ?? DefaultSupportsHostedImageGeneration(protocol, endpoint, configuredAuthMethod);
    }

    public static bool DefaultSupportsHostedImageGeneration(string protocol, string endpoint, string authMethod)
    {
        var configuredAuthMethod = string.IsNullOrWhiteSpace(authMethod)
            ? ModelProviderAuthMethods.ApiKey
            : authMethod.Trim();

        if (ModelProviderAuthMethods.IsChatGptOAuth(configuredAuthMethod))
            return true;

        if (!string.Equals(configuredAuthMethod, ModelProviderAuthMethods.ApiKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ModelProviderProtocols.IsOpenAIResponses(protocol))
            return false;

        return ModelProviderDefaults.IsOfficialOpenAIEndpoint(endpoint);
    }

    private static string? NormalizeProviderId(string? providerId)
    {
        var trimmed = providerId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool TryResolveExplicitProvider(
        AppConfig config,
        string providerId,
        out EffectiveModelRuntime runtime)
    {
        if (config.Providers.TryGetValue(providerId, out var provider))
        {
            var configuredAuthMethod = string.IsNullOrWhiteSpace(provider.AuthMethod)
                ? ModelProviderAuthMethods.ApiKey
                : provider.AuthMethod.Trim();
            var authMethod = ModelProviderAuthMethods.Normalize(configuredAuthMethod);
            var protocol = ModelProviderProtocols.Normalize(provider.Protocol);

            if (authMethod == ModelProviderAuthMethods.ChatGptOAuth)
            {
                if (!ModelProviderProtocols.IsOpenAIProtocol(protocol))
                {
                    throw new ModelProviderConfigurationException(
                        ModelCatalogErrorCode.UnsupportedProtocol,
                        $"Provider '{providerId}' uses chatgptOAuth but its protocol '{protocol}' is not OpenAI-compatible.");
                }
                protocol = ModelProviderProtocols.OpenAIResponses;
            }

            var endPoint = NormalizeEndpoint(protocol, provider.EndPoint, authMethod);
            var timeout = Math.Max(1, provider.NetworkTimeoutSeconds ?? config.NetworkTimeoutSeconds);
            var maxOutputTokens = NormalizePositiveNullable(provider.MaxOutputTokens);
            var streamMaxRetries = NormalizeStreamMaxRetries(provider.StreamMaxRetries);
            var streamIdleTimeoutMs = NormalizeStreamIdleTimeoutMs(provider.StreamIdleTimeoutMs);
            var supportsHostedImageGeneration = provider.SupportsHostedImageGeneration
                ?? DefaultSupportsHostedImageGeneration(protocol, endPoint, configuredAuthMethod);
            var accountId = string.IsNullOrWhiteSpace(provider.ChatGptAccountId)
                ? null
                : provider.ChatGptAccountId.Trim();

            // API key is irrelevant in OAuth mode; the OpenAIClientProvider injects a placeholder so
            // the SDK still constructs a client.
            var apiKey = authMethod == ModelProviderAuthMethods.ChatGptOAuth
                ? string.Empty
                : provider.ApiKey?.Trim() ?? string.Empty;

            runtime = new EffectiveModelRuntime(
                providerId,
                string.Empty,
                protocol,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? providerId : provider.DisplayName.Trim(),
                apiKey,
                endPoint,
                timeout,
                maxOutputTokens,
                IsImplicit: false,
                ModelProviderCapabilities.ForProtocol(protocol),
                streamMaxRetries,
                streamIdleTimeoutMs,
                configuredAuthMethod,
                accountId,
                supportsHostedImageGeneration,
                ProviderStateDirectory: string.IsNullOrWhiteSpace(config.GlobalConfigPath)
                    ? null
                    : Path.GetDirectoryName(config.GlobalConfigPath));
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string NormalizeEndpoint(string protocol, string? endpoint, string authMethod)
    {
        if (authMethod == ModelProviderAuthMethods.ChatGptOAuth)
            return ModelProviderDefaults.ChatGptBackendEndpoint;

        if (!string.IsNullOrWhiteSpace(endpoint))
            return endpoint.Trim();

        return protocol switch
        {
            ModelProviderProtocols.OpenAIChatCompletions => ModelProviderDefaults.DefaultOpenAIEndpoint,
            ModelProviderProtocols.OpenAIResponses => ModelProviderDefaults.DefaultOpenAIEndpoint,
            ModelProviderProtocols.Anthropic => ModelProviderDefaults.DefaultAnthropicEndpoint,
            _ => string.Empty
        };
    }

    private static int? NormalizePositiveNullable(int? value) =>
        value is > 0 ? value : null;

    private static int NormalizeStreamMaxRetries(int? value) =>
        value.HasValue
            ? Math.Clamp(value.Value, 0, ModelProviderDefaults.MaxStreamMaxRetries)
            : ModelProviderDefaults.DefaultStreamMaxRetries;

    private static int NormalizeStreamIdleTimeoutMs(int? value) =>
        value is > 0 ? value.Value : ModelProviderDefaults.DefaultStreamIdleTimeoutMs;
}

public sealed class ModelProviderConfigurationException(
    ModelCatalogErrorCode errorCode,
    string message)
    : InvalidOperationException(message)
{
    public ModelCatalogErrorCode ErrorCode { get; } = errorCode;
}
