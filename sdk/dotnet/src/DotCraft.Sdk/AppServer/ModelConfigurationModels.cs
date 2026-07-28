using System.Text.Json;

namespace DotCraft.Sdk.AppServer;

/// <summary>Public-safe configured Provider metadata.</summary>
public sealed record DotCraftProviderInfo(
    string Id,
    string DisplayName,
    string Protocol,
    bool IsImplicit);

/// <summary>Result of <c>provider/list</c>.</summary>
public sealed record DotCraftProviderListResult(
    IReadOnlyList<DotCraftProviderInfo> Providers,
    JsonElement Raw);

/// <summary>One supported reasoning effort.</summary>
public sealed record DotCraftReasoningEffort(string Effort, string Label);

/// <summary>Reasoning capability advertised for one model.</summary>
public sealed record DotCraftReasoningCapability(
    bool SupportsDisable,
    IReadOnlyList<DotCraftReasoningEffort> SupportedEfforts,
    string DefaultEffort,
    IReadOnlyList<string> SupportedOutputs,
    string DefaultOutput);

/// <summary>Inference speed capability advertised for one model.</summary>
public sealed record DotCraftSpeedCapability(
    IReadOnlyList<string> SupportedModes,
    string DefaultMode);

/// <summary>Context-window capability advertised for one model.</summary>
public sealed record DotCraftContextWindowCapability(
    long CatalogWindow,
    long ConfiguredWindow,
    bool SupportsMax,
    long MaxWindow);

/// <summary>One model returned by <c>model/list</c>.</summary>
public sealed record DotCraftModelCatalogItem(
    string Id,
    string OwnedBy,
    DateTimeOffset? CreatedAt,
    DotCraftReasoningCapability? Reasoning,
    DotCraftSpeedCapability? Speed,
    DotCraftContextWindowCapability? ContextWindow);

/// <summary>Structured result of <c>model/list</c>.</summary>
public sealed record DotCraftModelCatalogResult(
    bool Success,
    string? ProviderId,
    string? Protocol,
    IReadOnlyList<DotCraftModelCatalogItem> Models,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement Raw);

/// <summary>Provider-neutral reasoning configuration captured on a Thread.</summary>
public sealed record DotCraftReasoningConfiguration(
    bool Enabled,
    string Effort,
    string Output);

/// <summary>Context-window mode captured on a Thread.</summary>
public sealed record DotCraftContextWindowConfiguration(string Mode);

/// <summary>
/// Complete Provider and model configuration used by an AppServer client.
/// Values use the AppServer wire vocabulary.
/// </summary>
public sealed record DotCraftModelConfiguration(
    string ProviderId,
    string Model,
    DotCraftReasoningConfiguration Reasoning,
    string Speed,
    DotCraftContextWindowConfiguration ContextWindow);
