using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

internal static class ProviderWireMapper
{
    public static List<ProviderInfo> BuildProviderInfos(AppConfig config)
    {
        return config.Providers
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => BuildProviderInfo(pair.Key, pair.Value, isImplicit: false))
            .ToList();
    }

    public static ProviderInfo BuildProviderInfo(
        string id,
        AppConfig.ModelProviderConfig provider,
        bool isImplicit)
    {
        var protocol = ModelProviderProtocols.Normalize(provider.Protocol);
        var authMethod = ModelProviderAuthMethods.Normalize(provider.AuthMethod);
        return new ProviderInfo
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? id : provider.DisplayName.Trim(),
            Protocol = protocol,
            ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : "********",
            HasApiKey = !string.IsNullOrWhiteSpace(provider.ApiKey),
            EndPoint = provider.EndPoint?.Trim() ?? string.Empty,
            NetworkTimeoutSeconds = provider.NetworkTimeoutSeconds,
            MaxOutputTokens = provider.MaxOutputTokens,
            StreamMaxRetries = provider.StreamMaxRetries,
            StreamIdleTimeoutMs = provider.StreamIdleTimeoutMs,
            SupportsHostedImageGeneration = ModelProviderResolver.ResolveHostedImageGenerationSupport(provider),
            IsImplicit = isImplicit,
            AuthMethod = authMethod,
            ChatGptAccountId = authMethod == ModelProviderAuthMethods.ChatGptOAuth && !string.IsNullOrWhiteSpace(provider.ChatGptAccountId)
                ? provider.ChatGptAccountId.Trim()
                : null,
            ChatGptPlanType = authMethod == ModelProviderAuthMethods.ChatGptOAuth && !string.IsNullOrWhiteSpace(provider.ChatGptPlanType)
                ? provider.ChatGptPlanType.Trim()
                : null,
            Capabilities = MapProviderCapabilities(ModelProviderCapabilities.ForProtocol(protocol))
        };
    }

    public static ModelReasoningCapability? MapReasoningCapability(
        ModelThinkingAdapterCatalog.ReasoningCapabilityData? capability)
    {
        if (capability == null)
            return null;

        return new ModelReasoningCapability
        {
            SupportsDisable = capability.SupportsDisable,
            SupportedEfforts = [.. capability.SupportedEfforts.Select(option => new ModelReasoningEffortOption
            {
                Effort = option.Effort,
                Label = string.IsNullOrWhiteSpace(option.Label) ? DefaultReasoningEffortLabel(option.Effort) : option.Label!,
                Description = string.IsNullOrWhiteSpace(option.Description)
                    ? DefaultReasoningEffortDescription(option.Effort)
                    : option.Description!
            })],
            DefaultEffort = capability.DefaultEffort,
            SupportedOutputs = [.. capability.SupportedOutputs],
            DefaultOutput = capability.DefaultOutput
        };
    }

    public static ModelContextWindowCapabilityWire MapContextWindowCapability(
        ModelContextWindowCapability capability)
        => new()
        {
            CatalogWindow = capability.CatalogWindow,
            ConfiguredWindow = capability.ConfiguredWindow,
            SupportsMax = capability.SupportsMax,
            MaxWindow = capability.MaxWindow
        };

    public static ModelSpeedCapability? MapSpeedCapability(
        AppConfig config,
        string? protocol,
        string? model) =>
        ModelCatalog.SupportsFast(config, protocol, model) ? new ModelSpeedCapability() : null;

    public static string? FormatModelListErrorMessage(string? message, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return message;

        var baseMessage = string.IsNullOrWhiteSpace(message)
            ? "Model list request failed."
            : message.Trim();
        return $"{baseMessage} Endpoint: {endpoint.Trim()}";
    }

    private static ProviderCapabilitiesWire MapProviderCapabilities(ModelProviderCapabilities capabilities) => new()
    {
        StreamingChat = capabilities.StreamingChat,
        ToolCalling = capabilities.ToolCalling,
        ModelListing = capabilities.ModelListing,
        TokenUsageReporting = capabilities.TokenUsageReporting,
        CachedInputUsageReporting = capabilities.CachedInputUsageReporting,
        PromptCacheRequestShaping = capabilities.PromptCacheRequestShaping,
        ExtendedThinking = capabilities.ExtendedThinking,
        ToolChoiceControls = capabilities.ToolChoiceControls,
        RawMetadataPassthrough = capabilities.RawMetadataPassthrough,
        ResponsesApi = capabilities.ResponsesApi,
        NativeDeferredToolLoading = capabilities.NativeDeferredToolLoading
    };

    private static string DefaultReasoningEffortLabel(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "Low",
        ReasoningEffort.Medium => "Medium",
        ReasoningEffort.High => "High",
        ReasoningEffort.ExtraHigh => "Extra High",
        _ => effort.ToString()
    };

    private static string DefaultReasoningEffortDescription(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "Faster, lighter reasoning.",
        ReasoningEffort.Medium => "Balanced reasoning.",
        ReasoningEffort.High => "Deeper reasoning.",
        ReasoningEffort.ExtraHigh => "Maximum depth for supported models.",
        _ => string.Empty
    };
}
