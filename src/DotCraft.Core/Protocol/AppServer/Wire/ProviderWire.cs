using System.Text.Json.Serialization;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── model/list (model catalog management) ─────

// ───── provider/* (personal model provider management) ─────

public sealed class ProviderCapabilitiesWire
{
    public bool StreamingChat { get; set; }

    public bool ToolCalling { get; set; }

    public bool ModelListing { get; set; }

    public bool TokenUsageReporting { get; set; }

    public bool CachedInputUsageReporting { get; set; }

    public bool PromptCacheRequestShaping { get; set; }

    public bool ExtendedThinking { get; set; }

    public bool ToolChoiceControls { get; set; }

    public bool RawMetadataPassthrough { get; set; }

    public bool ResponsesApi { get; set; }

    public bool NativeDeferredToolLoading { get; set; }
}

public sealed class ProviderInfo
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApiKey { get; set; }

    public bool HasApiKey { get; set; }

    public string EndPoint { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NetworkTimeoutSeconds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamMaxRetries { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamIdleTimeoutMs { get; set; }

    public bool IsImplicit { get; set; }

    /// <summary>
    /// Authentication method ("apiKey" or "chatgptOAuth"). Defaults to apiKey.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string AuthMethod { get; set; } = ModelProviderAuthMethods.ApiKey;

    /// <summary>ChatGPT account id when <see cref="AuthMethod"/> is chatgptOAuth.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatGptAccountId { get; set; }

    /// <summary>ChatGPT plan tier (free, plus, pro, business, enterprise, edu).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatGptPlanType { get; set; }

    public ProviderCapabilitiesWire Capabilities { get; set; } = new();
}

public sealed class ProviderListParams
{
}

public sealed class ProviderListResult
{
    public List<ProviderInfo> Providers { get; set; } = [];
}

public sealed class ProviderCreateParams
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    public string ApiKey { get; set; } = string.Empty;

    public string EndPoint { get; set; } = string.Empty;

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }

    public string? AuthMethod { get; set; }
}

public sealed class ProviderUpdateParams
{
    public string Id { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Protocol { get; set; }

    public string? ApiKey { get; set; }

    public string? EndPoint { get; set; }

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }

    public string? AuthMethod { get; set; }
}

public sealed class ProviderDeleteParams
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ProviderDeleteResult
{
    public bool Deleted { get; set; }
}

public sealed class ProviderMutationResult
{
    public ProviderInfo Provider { get; set; } = new();
}

public sealed class ProviderTestParams
{
    public string? ProviderId { get; set; }

    public string? Protocol { get; set; }

    public string? ApiKey { get; set; }

    public string? EndPoint { get; set; }

    public int? NetworkTimeoutSeconds { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? StreamMaxRetries { get; set; }

    public int? StreamIdleTimeoutMs { get; set; }
}

public sealed class ProviderTestResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }

    public string Protocol { get; set; } = ModelProviderProtocols.OpenAIChatCompletions;

    public List<ModelCatalogItem> Models { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

/// <summary>Params for <see cref="AppServerMethods.AuthOpenAiLogin"/>.</summary>
public sealed class AuthOpenAiLoginParams
{
    /// <summary>Provider id to bind to ChatGPT subscription; defaults to "openai".</summary>
    public string? ProviderId { get; set; }

    /// <summary>When false, suppresses browser launch — only the URL is returned via the authorizeUrl notification.</summary>
    public bool? OpenBrowser { get; set; }
}

/// <summary>Params for <see cref="AppServerMethods.AuthOpenAiLogout"/>.</summary>
public sealed class AuthOpenAiLogoutParams
{
    public string? ProviderId { get; set; }
}

/// <summary>Server → Client notification payload for <see cref="AppServerMethods.AuthOpenAiAuthorizeUrl"/>.</summary>
public sealed class AuthOpenAiAuthorizeUrlNotification
{
    public string Url { get; set; } = string.Empty;
    public int CallbackPort { get; set; }
}

/// <summary>One rate-limit window snapshot (5h primary or weekly secondary).</summary>
public sealed class AuthOpenAiUsageWindow
{
    /// <summary>0-100 percent of the window's quota consumed.</summary>
    public int UsedPercent { get; set; }
    /// <summary>Window length in seconds (e.g. 18000 for 5h, 604800 for 7d).</summary>
    public int WindowSeconds { get; set; }
    /// <summary>UTC timestamp when this window rolls over and the counter resets to zero.</summary>
    public DateTimeOffset ResetAt { get; set; }
}

/// <summary>Credit balance (only populated for credit-based accounts).</summary>
public sealed class AuthOpenAiUsageCredits
{
    public bool HasCredits { get; set; }
    public bool Unlimited { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Balance { get; set; }
}

/// <summary>
/// Result of <see cref="AppServerMethods.AuthOpenAiUsage"/> and payload of
/// <see cref="AppServerMethods.AuthOpenAiUsageChanged"/>.
/// </summary>
public sealed class AuthOpenAiUsageResult
{
    /// <summary>True when a snapshot is available; false when not signed in or no fetch has succeeded yet.</summary>
    public bool Available { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageWindow? Primary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageWindow? Secondary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthOpenAiUsageCredits? Credits { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LimitReachedKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? FetchedAt { get; set; }
}

/// <summary>Common status result returned by login/logout/status.</summary>
public sealed class AuthOpenAiStatusResult
{
    public bool LoggedIn { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastRefresh { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>Provider id this status applies to (after a successful login). Omitted for logout.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }
}

/// <summary>
/// Params for <see cref="AppServerMethods.ModelList"/>.
/// </summary>
public sealed class ModelListParams
{
    /// <summary>
    /// Optional provider id. When omitted, the workspace-selected provider is used.
    /// </summary>
    public string? ProviderId { get; set; }
}

/// <summary>
/// One provider model entry.
/// </summary>
public sealed class ModelCatalogItem
{
    public string Id { get; set; } = string.Empty;

    public string OwnedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelReasoningCapability? Reasoning { get; set; }
}

public sealed class ModelReasoningCapability
{
    public bool SupportsDisable { get; set; } = true;

    public List<ModelReasoningEffortOption> SupportedEfforts { get; set; } = [];

    public ReasoningEffort DefaultEffort { get; set; } = ReasoningEffort.Medium;

    public List<ReasoningOutput> SupportedOutputs { get; set; } = [];

    public ReasoningOutput DefaultOutput { get; set; } = ReasoningOutput.Full;
}

public sealed class ModelReasoningEffortOption
{
    public ReasoningEffort Effort { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Result for <see cref="AppServerMethods.ModelList"/>.
/// </summary>
public sealed class ModelListResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    public List<ModelCatalogItem> Models { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}
