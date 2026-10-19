using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public static class ProviderContractMapper
{
    public static async Task<IReadOnlyList<Contract.ProviderInfo>> BuildProviderInfosAsync(
        AppConfig config, IModelRuntimeResolver? resolver, ProviderAuthenticationCoordinator? authentication, CancellationToken ct)
    {
        var infos = new List<Contract.ProviderInfo>();
        foreach (var (id, provider) in config.Providers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var authenticated = authentication is null
                ? provider.RemoteAuthentication?.IsAuthenticated
                : await authentication.TryGetAuthenticatedAsync(config, id, ct).ConfigureAwait(false);
            infos.Add(BuildProviderInfo(id, provider, ResolveRuntime(config, id, resolver), authenticated));
        }
        return infos;
    }

    public static IReadOnlyList<Contract.ProviderInfo> BuildProviderInfos(AppConfig config, IModelRuntimeResolver? resolver = null)
    {
        return config.Providers
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => BuildProviderInfo(pair.Key, pair.Value, ResolveRuntime(config, pair.Key, resolver)))
            .ToList();
    }

    public static Contract.ProviderInfo BuildProviderInfo(
        string id,
        AppConfig.ModelProviderConfig provider,
        EffectiveModelRuntime? runtime = null,
        bool? authenticated = null)
    {
        var custom = !string.IsNullOrWhiteSpace(provider.BackendId);
        var protocol = custom ? runtime?.Protocol ?? provider.Protocol : ModelProviderProtocols.Normalize(provider.Protocol);
        var authMethod = custom ? runtime?.AuthMethod ?? provider.AuthMethod : ModelProviderAuthMethods.Normalize(provider.AuthMethod);
        return new Contract.ProviderInfo
        {
            Id = id,
            BackendId = custom ? provider.BackendId! : default(Protocol.Optional<string>),
            ManagedBy = provider.RemoteAuthentication is null ? default : "modelService",
            IsAuthenticated = authenticated is { } ready ? ready
                : provider.RemoteAuthentication is null ? default(Protocol.Optional<bool>) : provider.RemoteAuthentication.IsAuthenticated,
            DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? id : provider.DisplayName.Trim(),
            Protocol = protocol,
            ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : "********",
            HasApiKey = !string.IsNullOrWhiteSpace(provider.ApiKey),
            WebSearchBackend = ModelProviderResolver.ResolveWebSearchBackend(provider) is { } backend ? backend : default(Protocol.Optional<string>),
            EndPoint = provider.EndPoint?.Trim() ?? string.Empty,
            NetworkTimeoutSeconds = provider.NetworkTimeoutSeconds,
            MaxOutputTokens = provider.MaxOutputTokens,
            StreamMaxRetries = provider.StreamMaxRetries,
            StreamIdleTimeoutMs = provider.StreamIdleTimeoutMs,
            SupportsImageGeneration = ModelProviderResolver.ResolveImageGenerationSupport(provider),
            AuthMethod = authMethod,
            Capabilities = MapProviderCapabilities(custom
                ? runtime?.Capabilities ?? new ModelProviderCapabilities { StreamingChat = false, ToolCalling = false, ModelListing = false, TokenUsageReporting = false }
                : ModelProviderCapabilities.ForProtocol(protocol))
        };
    }

    private static EffectiveModelRuntime? ResolveRuntime(AppConfig config, string id, IModelRuntimeResolver? resolver)
    {
        if (resolver is null || string.IsNullOrWhiteSpace(config.Providers[id].BackendId)) return null;
        try { return resolver.Normalize(ModelProviderResolver.ResolveProvider(config, id)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return null; }
    }

    internal static Contract.ModelReasoningCapability? MapReasoningCapability(
        ModelThinkingAdapterCatalog.ReasoningCapabilityData? capability,
        bool includeUltra)
    {
        if (capability == null)
            return null;

        var efforts = capability.SupportedEfforts.Select(option => new Contract.ModelReasoningEffortOption
        {
            Effort = ReasoningEffortToken(option.Effort),
            Label = string.IsNullOrWhiteSpace(option.Label) ? DefaultReasoningEffortLabel(option.Effort) : option.Label!,
            Description = string.IsNullOrWhiteSpace(option.Description)
                ? DefaultReasoningEffortDescription(option.Effort)
                : option.Description!
        }).ToList();
        if (includeUltra && capability.SupportedEfforts.Any(static option => option.Effort == ProviderReasoningEffort.Max))
        {
            efforts.Add(new Contract.ModelReasoningEffortOption
            {
                Effort = "ultra",
                Label = "Ultra",
                Description = "Proactively plans and coordinates dynamic workflows for substantive tasks."
            });
        }

        return new Contract.ModelReasoningCapability
        {
            SupportsDisable = capability.SupportsDisable,
            SupportedEfforts = new Protocol.Optional<IReadOnlyList<Contract.ModelReasoningEffortOption>>(
                efforts),
            DefaultEffort = ReasoningEffortToken(capability.DefaultEffort),
            SupportedOutputs = new Protocol.Optional<IReadOnlyList<string>>(
                capability.SupportedOutputs.Select(ReasoningOutputToken).ToArray()),
            DefaultOutput = ReasoningOutputToken(capability.DefaultOutput)
        };
    }

    public static Contract.ModelSpeedCapability? MapSpeedCapability(
        AppConfig config,
        string? protocol,
        string? model) =>
        ModelCatalog.SupportsFast(config, protocol, model)
            ? new Contract.ModelSpeedCapability
            {
                SupportedModes = new Protocol.Optional<IReadOnlyList<string>>(["standard", "fast"]),
                DefaultMode = "standard"
            }
            : null;

    public static Contract.ModelCatalogItem[] BuildModelCatalogItems(
        AppConfig config,
        string? providerId,
        string? protocol,
        string? endpoint,
        IReadOnlyList<ModelCatalogEntry> models,
        bool includeUltra)
    {
        var preferred = ModelPreferenceRules.Find(config.ProviderPreferences, providerId)?.Model.Trim();
        var defaultId = models.Any(model => string.Equals(model.Id, preferred, StringComparison.Ordinal))
            ? preferred
            : models.FirstOrDefault()?.Id;
        return [.. models.Select(model => BuildModelCatalogItem(config, protocol, endpoint, model, model.Id == defaultId, includeUltra))];
    }

    private static Contract.ModelCatalogItem BuildModelCatalogItem(
        AppConfig config,
        string? protocol,
        string? endpoint,
        ModelCatalogEntry model,
        bool isDefault,
        bool includeUltra) => new()
        {
            Id = model.Id,
            IsDefault = isDefault,
            OwnedBy = model.OwnedBy,
            CreatedAt = model.CreatedAt,
            Reasoning = MapReasoningCapability(ModelThinkingAdapterResolver.ResolveReasoningCapability(
                config,
                protocol,
                endpoint,
                model.Id), includeUltra),
            Speed = MapSpeedCapability(config, protocol, model.Id),
            ContextWindow = ModelCatalog.Resolve(config, model.Id)
        };

    public static string? FormatModelListErrorMessage(string? message, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return message;

        var baseMessage = string.IsNullOrWhiteSpace(message)
            ? "Model list request failed."
            : message.Trim();
        return $"{baseMessage} Endpoint: {endpoint.Trim()}";
    }

    private static Contract.ProviderCapabilities MapProviderCapabilities(ModelProviderCapabilities capabilities) => new()
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

    private static string DefaultReasoningEffortLabel(ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.Low => "Low",
        ProviderReasoningEffort.Medium => "Medium",
        ProviderReasoningEffort.High => "High",
        ProviderReasoningEffort.ExtraHigh => "Extra High",
        ProviderReasoningEffort.Max => "Max",
        _ => effort.ToString()
    };

    private static string DefaultReasoningEffortDescription(ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.Low => "Faster, lighter reasoning.",
        ProviderReasoningEffort.Medium => "Balanced reasoning.",
        ProviderReasoningEffort.High => "Deeper reasoning.",
        ProviderReasoningEffort.ExtraHigh => "Extra high reasoning.",
        ProviderReasoningEffort.Max => "Maximum reasoning effort.",
        _ => string.Empty
    };

    private static string ReasoningEffortToken(ProviderReasoningEffort effort) => effort switch
    {
        ProviderReasoningEffort.None => "none",
        ProviderReasoningEffort.Low => "low",
        ProviderReasoningEffort.Medium => "medium",
        ProviderReasoningEffort.High => "high",
        ProviderReasoningEffort.ExtraHigh => "extraHigh",
        ProviderReasoningEffort.Max => "max",
        _ => effort.ToString()
    };

    private static string ReasoningOutputToken(ReasoningOutput output) => output switch
    {
        ReasoningOutput.None => "none",
        ReasoningOutput.Summary => "summary",
        ReasoningOutput.Full => "full",
        _ => output.ToString()
    };
}
