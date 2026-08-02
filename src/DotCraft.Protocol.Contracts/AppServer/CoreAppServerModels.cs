#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.Contracts.AppServer;
/// <summary>Executable wire contract for AgentProfileAuditWire.</summary>
public sealed class AgentProfileAuditWire : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("event")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Event { get; init; }

    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>> Fields { get; init; }

    [JsonPropertyName("profileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProfileId { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("timestamp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> Timestamp { get; init; }

}

/// <summary>Executable wire contract for AgentProfileBuilderDraftReadParams.</summary>
public sealed class AgentProfileBuilderDraftReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AgentProfileBuilderDraftResult.</summary>
public sealed class AgentProfileBuilderDraftResult : ExtensibleJsonObject
{
    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("targetId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TargetId { get; init; }

    [JsonPropertyName("targetSource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TargetSource { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AgentProfileBuilderDraftUpdateParams.</summary>
public sealed class AgentProfileBuilderDraftUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AgentProfileDiagnosticWire.</summary>
public sealed class AgentProfileDiagnosticWire : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Severity { get; init; }

}

/// <summary>Executable wire contract for AgentProfileEntryWire.</summary>
public sealed class AgentProfileEntryWire : ExtensibleJsonObject
{
    [JsonPropertyName("avatar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Avatar { get; init; }

    [JsonPropertyName("compiledConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> CompiledConfig { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileDiagnosticWire>> Diagnostics { get; init; }

    [JsonPropertyName("fingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Fingerprint { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("isBuiltIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsBuiltIn { get; init; }

    [JsonPropertyName("lockedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> LockedFields { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Name { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

    [JsonPropertyName("providerPreference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileProviderPreferenceWire?> ProviderPreference { get; init; }

    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RawContent { get; init; }

    [JsonPropertyName("readOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ReadOnly { get; init; }

    [JsonPropertyName("restrictedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RestrictedFields { get; init; }

    [JsonPropertyName("shadowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Shadowed { get; init; }

    [JsonPropertyName("shadowedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShadowedBy { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("sourceStack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SourceStack { get; init; }

    [JsonPropertyName("staleThreadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> StaleThreadIds { get; init; }

    [JsonPropertyName("trustRestricted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> TrustRestricted { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> UpdatedAt { get; init; }

    [JsonPropertyName("valid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Valid { get; init; }

}

/// <summary>Executable wire contract for AgentProfileListParams.</summary>
public sealed class AgentProfileListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeInvalid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeInvalid { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

/// <summary>Executable wire contract for AgentProfileListResult.</summary>
public sealed class AgentProfileListResult : ExtensibleJsonObject
{
    [JsonPropertyName("profiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileEntryWire>> Profiles { get; init; }

}

/// <summary>Executable wire contract for AgentProfileProviderPreferenceWire.</summary>
public sealed class AgentProfileProviderPreferenceWire : ExtensibleJsonObject
{
    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelPreferenceContextWindow> ContextWindow { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Model { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ProviderId { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileReasoningPreferenceWire> Reasoning { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Speed { get; init; }

}

/// <summary>Executable wire contract for AgentProfileReadParams.</summary>
public sealed class AgentProfileReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

/// <summary>Executable wire contract for AgentProfileReadResult.</summary>
public sealed class AgentProfileReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntryWire> Profile { get; init; }

}

/// <summary>Executable wire contract for AgentProfileReasoningPreferenceWire.</summary>
public sealed class AgentProfileReasoningPreferenceWire : ExtensibleJsonObject
{
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Effort { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

}

/// <summary>Executable wire contract for AgentProfileRefreshThreadParams.</summary>
public sealed class AgentProfileRefreshThreadParams : ExtensibleJsonObject
{
    [JsonPropertyName("profileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProfileId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for AgentProfileRefreshThreadResult.</summary>
public sealed class AgentProfileRefreshThreadResult : ExtensibleJsonObject
{
    [JsonPropertyName("audit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileAuditWire> Audit { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration> Config { get; init; }

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntryWire> Profile { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("wasStale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WasStale { get; init; }

}

/// <summary>Executable wire contract for AgentProfileRemoveParams.</summary>
public sealed class AgentProfileRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

/// <summary>Executable wire contract for AgentProfileRemoveResult.</summary>
public sealed class AgentProfileRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for AgentProfileSummaryWire.</summary>
public sealed class AgentProfileSummaryWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Id { get; init; }

}

/// <summary>Executable wire contract for AgentProfileUpsertParams.</summary>
public sealed class AgentProfileUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

/// <summary>Executable wire contract for AgentProfileUpsertResult.</summary>
public sealed class AgentProfileUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntryWire> Profile { get; init; }

}

/// <summary>Executable wire contract for AgentProfileValidateParams.</summary>
public sealed class AgentProfileValidateParams : ExtensibleJsonObject
{
    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

/// <summary>Executable wire contract for AgentProfileValidateResult.</summary>
public sealed class AgentProfileValidateResult : ExtensibleJsonObject
{
    [JsonPropertyName("compiledConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> CompiledConfig { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileDiagnosticWire>> Diagnostics { get; init; }

    [JsonPropertyName("lockedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> LockedFields { get; init; }

    [JsonPropertyName("providerPreference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileProviderPreferenceWire?> ProviderPreference { get; init; }

    [JsonPropertyName("restrictedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RestrictedFields { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileSummaryWire> Summary { get; init; }

    [JsonPropertyName("valid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Valid { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiAuthorizeUrlNotification.</summary>
public sealed class AuthOpenAiAuthorizeUrlNotification : ExtensibleJsonObject
{
    [JsonPropertyName("callbackPort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CallbackPort { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiLoginParams.</summary>
public sealed class AuthOpenAiLoginParams : ExtensibleJsonObject
{
    [JsonPropertyName("openBrowser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> OpenBrowser { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiLogoutParams.</summary>
public sealed class AuthOpenAiLogoutParams : ExtensibleJsonObject
{
    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiStatusResult.</summary>
public sealed class AuthOpenAiStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("accessTokenExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> AccessTokenExpiresAt { get; init; }

    [JsonPropertyName("accountId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AccountId { get; init; }

    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Email { get; init; }

    [JsonPropertyName("lastRefresh")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> LastRefresh { get; init; }

    [JsonPropertyName("loggedIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> LoggedIn { get; init; }

    [JsonPropertyName("planType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PlanType { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiUsageCredits.</summary>
public sealed class AuthOpenAiUsageCredits : ExtensibleJsonObject
{
    [JsonPropertyName("balance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Balance { get; init; }

    [JsonPropertyName("hasCredits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasCredits { get; init; }

    [JsonPropertyName("unlimited")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Unlimited { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiUsageResult.</summary>
public sealed class AuthOpenAiUsageResult : ExtensibleJsonObject
{
    [JsonPropertyName("available")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Available { get; init; }

    [JsonPropertyName("credits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AuthOpenAiUsageCredits?> Credits { get; init; }

    [JsonPropertyName("fetchedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> FetchedAt { get; init; }

    [JsonPropertyName("limitReachedKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LimitReachedKind { get; init; }

    [JsonPropertyName("planType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PlanType { get; init; }

    [JsonPropertyName("primary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AuthOpenAiUsageWindow?> Primary { get; init; }

    [JsonPropertyName("secondary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AuthOpenAiUsageWindow?> Secondary { get; init; }

}

/// <summary>Executable wire contract for AuthOpenAiUsageWindow.</summary>
public sealed class AuthOpenAiUsageWindow : ExtensibleJsonObject
{
    [JsonPropertyName("resetAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ResetAt { get; init; }

    [JsonPropertyName("usedPercent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> UsedPercent { get; init; }

    [JsonPropertyName("windowSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> WindowSeconds { get; init; }

}

/// <summary>Executable wire contract for BackgroundTerminalSnapshot.</summary>
public sealed class BackgroundTerminalSnapshot : ExtensibleJsonObject
{
    [JsonPropertyName("backgroundReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BackgroundReason { get; init; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CallId { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> CompletedAt { get; init; }

    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ExitCode { get; init; }

    [JsonPropertyName("originalOutputChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> OriginalOutputChars { get; init; }

    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Output { get; init; }

    [JsonPropertyName("outputPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OutputPath { get; init; }

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> StartedAt { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Truncated { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

    [JsonPropertyName("wallTimeMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> WallTimeMs { get; init; }

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkingDirectory { get; init; }

}

/// <summary>Executable wire contract for ChannelInfo.</summary>
public sealed class ChannelInfo : ExtensibleJsonObject
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Category { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for ChannelListResult.</summary>
public sealed class ChannelListResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ChannelInfo>> Channels { get; init; }

}

/// <summary>Executable wire contract for ChannelStatusInfo.</summary>
public sealed class ChannelStatusInfo : ExtensibleJsonObject
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Category { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

}

/// <summary>Executable wire contract for ChannelStatusResult.</summary>
public sealed class ChannelStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ChannelStatusInfo>> Channels { get; init; }

}

/// <summary>Executable wire contract for CommandExecuteParams.</summary>
public sealed class CommandExecuteParams : ExtensibleJsonObject
{
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Arguments { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SenderContext?> Sender { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for CommandExecuteResult.</summary>
public sealed class CommandExecuteResult : ExtensibleJsonObject
{
    [JsonPropertyName("archivedThreadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> ArchivedThreadIds { get; init; }

    [JsonPropertyName("createdLazily")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> CreatedLazily { get; init; }

    [JsonPropertyName("expandedPrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ExpandedPrompt { get; init; }

    [JsonPropertyName("handled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Handled { get; init; }

    [JsonPropertyName("isMarkdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsMarkdown { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("sessionReset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SessionReset { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread?> Thread { get; init; }

}

/// <summary>Executable wire contract for CommandInfoWire.</summary>
public sealed class CommandInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Aliases { get; init; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Category { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("descriptionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DescriptionKey { get; init; }

    [JsonPropertyName("fallbackDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FallbackDescription { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("requiresAdmin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RequiresAdmin { get; init; }

}

/// <summary>Executable wire contract for CommandListParams.</summary>
public sealed class CommandListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeBuiltins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeBuiltins { get; init; }

    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Language { get; init; }

}

/// <summary>Executable wire contract for CommandListResult.</summary>
public sealed class CommandListResult : ExtensibleJsonObject
{
    [JsonPropertyName("commands")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<CommandInfoWire>> Commands { get; init; }

}

/// <summary>Executable wire contract for ConfigSchemaField.</summary>
public sealed class ConfigSchemaField : ExtensibleJsonObject
{
    [JsonPropertyName("defaultValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> DefaultValue { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Hint { get; init; }

    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Max { get; init; }

    [JsonPropertyName("min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Min { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Options { get; init; }

    [JsonPropertyName("reload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Reload { get; init; }

    [JsonPropertyName("sensitive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Sensitive { get; init; }

    [JsonPropertyName("subsystemKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SubsystemKey { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

}

/// <summary>Executable wire contract for ConfigSchemaSection.</summary>
public sealed class ConfigSchemaSection : ExtensibleJsonObject
{
    [JsonPropertyName("fields")]
    public required IReadOnlyList<ConfigSchemaField> Fields { get; init; }

    [JsonPropertyName("itemFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ConfigSchemaField>?> ItemFields { get; init; }

    [JsonPropertyName("order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Order { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Path { get; init; }

    [JsonPropertyName("rootKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RootKey { get; init; }

    [JsonPropertyName("section")]
    public required string Section { get; init; }

}

/// <summary>Executable wire contract for ContextUsageSnapshot.</summary>
public sealed class ContextUsageSnapshot : ExtensibleJsonObject
{
    [JsonPropertyName("autoCompactThreshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> AutoCompactThreshold { get; init; }

    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ContextWindow { get; init; }

    [JsonPropertyName("errorThreshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ErrorThreshold { get; init; }

    [JsonPropertyName("isEstimate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsEstimate { get; init; }

    [JsonPropertyName("percentLeft")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> PercentLeft { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

    [JsonPropertyName("tokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Tokens { get; init; }

    [JsonPropertyName("warningThreshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> WarningThreshold { get; init; }

}

/// <summary>Executable wire contract for CronEnableParams.</summary>
public sealed class CronEnableParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("jobId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> JobId { get; init; }

}

/// <summary>Executable wire contract for CronEnableResult.</summary>
public sealed class CronEnableResult : ExtensibleJsonObject
{
    [JsonPropertyName("job")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<CronJobWireInfo> Job { get; init; }

}

/// <summary>Executable wire contract for CronJobStateWireInfo.</summary>
public sealed class CronJobStateWireInfo : ExtensibleJsonObject
{
    [JsonPropertyName("lastError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastError { get; init; }

    [JsonPropertyName("lastResult")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastResult { get; init; }

    [JsonPropertyName("lastRunAtMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> LastRunAtMs { get; init; }

    [JsonPropertyName("lastStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastStatus { get; init; }

    [JsonPropertyName("lastThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastThreadId { get; init; }

    [JsonPropertyName("nextRunAtMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> NextRunAtMs { get; init; }

}

/// <summary>Executable wire contract for CronJobWireInfo.</summary>
public sealed class CronJobWireInfo : ExtensibleJsonObject
{
    [JsonPropertyName("createdAtMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CreatedAtMs { get; init; }

    [JsonPropertyName("deleteAfterRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> DeleteAfterRun { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("schedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<CronScheduleWireInfo> Schedule { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<CronJobStateWireInfo> State { get; init; }

}

/// <summary>Executable wire contract for CronListParams.</summary>
public sealed class CronListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeDisabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IncludeDisabled { get; init; }

}

/// <summary>Executable wire contract for CronListResult.</summary>
public sealed class CronListResult : ExtensibleJsonObject
{
    [JsonPropertyName("jobs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<CronJobWireInfo>> Jobs { get; init; }

}

/// <summary>Executable wire contract for CronRemoveParams.</summary>
public sealed class CronRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("jobId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> JobId { get; init; }

}

/// <summary>Executable wire contract for CronRemoveResult.</summary>
public sealed class CronRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for CronRunParams.</summary>
public sealed class CronRunParams : ExtensibleJsonObject
{
    [JsonPropertyName("jobId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> JobId { get; init; }

}

/// <summary>Executable wire contract for CronRunResult.</summary>
public sealed class CronRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("job")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<CronJobWireInfo?> Job { get; init; }

    [JsonPropertyName("queued")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Queued { get; init; }

}

/// <summary>Executable wire contract for CronScheduleWireInfo.</summary>
public sealed class CronScheduleWireInfo : ExtensibleJsonObject
{
    [JsonPropertyName("atMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> AtMs { get; init; }

    [JsonPropertyName("dailyHour")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DailyHour { get; init; }

    [JsonPropertyName("dailyMinute")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DailyMinute { get; init; }

    [JsonPropertyName("everyMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> EveryMs { get; init; }

    [JsonPropertyName("initialDelayMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> InitialDelayMs { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("tz")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Tz { get; init; }

}

/// <summary>Executable wire contract for CronStateChangedNotification.</summary>
public sealed class CronStateChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("job")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<CronJobWireInfo> Job { get; init; }

    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for DreamsCreateParams.</summary>
public sealed class DreamsCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Instructions { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Model { get; init; }

    [JsonPropertyName("threadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> ThreadIds { get; init; }

    [JsonPropertyName("threadLookbackCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ThreadLookbackCount { get; init; }

}

/// <summary>Executable wire contract for DreamsListParams.</summary>
public sealed class DreamsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeArchived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IncludeArchived { get; init; }

}

/// <summary>Executable wire contract for DreamsListResult.</summary>
public sealed class DreamsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("runs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<DreamsRunStateWire>> Runs { get; init; }

}

/// <summary>Executable wire contract for DreamsRunIdParams.</summary>
public sealed class DreamsRunIdParams : ExtensibleJsonObject
{
    [JsonPropertyName("runId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RunId { get; init; }

}

/// <summary>Executable wire contract for DreamsRunParams.</summary>
public sealed class DreamsRunParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for DreamsRunPreviewWire.</summary>
public sealed class DreamsRunPreviewWire : ExtensibleJsonObject
{
    [JsonPropertyName("activeIndexMarkdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ActiveIndexMarkdown { get; init; }

    [JsonPropertyName("activeStoreId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActiveStoreId { get; init; }

    [JsonPropertyName("activeTopicPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ActiveTopicPaths { get; init; }

    [JsonPropertyName("outputIndexMarkdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OutputIndexMarkdown { get; init; }

    [JsonPropertyName("outputStoreId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputStoreId { get; init; }

    [JsonPropertyName("outputTopicPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> OutputTopicPaths { get; init; }

}

/// <summary>Executable wire contract for DreamsRunResult.</summary>
public sealed class DreamsRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("activeDreamStoreId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActiveDreamStoreId { get; init; }

    [JsonPropertyName("preview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunPreviewWire?> Preview { get; init; }

    [JsonPropertyName("run")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunStateWire?> Run { get; init; }

}

/// <summary>Executable wire contract for DreamsRunStateWire.</summary>
public sealed class DreamsRunStateWire : ExtensibleJsonObject
{
    [JsonPropertyName("autoApplied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AutoApplied { get; init; }

    [JsonPropertyName("candidateThreadCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CandidateThreadCount { get; init; }

    [JsonPropertyName("dreamWritten")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> DreamWritten { get; init; }

    [JsonPropertyName("endedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> EndedAt { get; init; }

    [JsonPropertyName("errorType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorType { get; init; }

    [JsonPropertyName("evidenceReadCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> EvidenceReadCount { get; init; }

    [JsonPropertyName("evidenceSearchCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> EvidenceSearchCount { get; init; }

    [JsonPropertyName("evidenceThreadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> EvidenceThreadIds { get; init; }

    [JsonPropertyName("historyWritten")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HistoryWritten { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("inputManifestPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputManifestPath { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("outputStoreId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputStoreId { get; init; }

    [JsonPropertyName("processedThreadCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ProcessedThreadCount { get; init; }

    [JsonPropertyName("reviewStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReviewStatus { get; init; }

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> StartedAt { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("topicFilesDeleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TopicFilesDeleted { get; init; }

    [JsonPropertyName("topicFilesWritten")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TopicFilesWritten { get; init; }

    [JsonPropertyName("trigger")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Trigger { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

    [JsonPropertyName("turnIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> TurnIds { get; init; }

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TokenUsageInfo?> Usage { get; init; }

    [JsonPropertyName("writtenPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> WrittenPaths { get; init; }

}

/// <summary>Executable wire contract for DreamsStatusParams.</summary>
public sealed class DreamsStatusParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for DreamsStatusResult.</summary>
public sealed class DreamsStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("activeDreamStoreId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActiveDreamStoreId { get; init; }

    [JsonPropertyName("autoApply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AutoApply { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("historyTailChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> HistoryTailChars { get; init; }

    [JsonPropertyName("interval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Interval { get; init; }

    [JsonPropertyName("lastRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunStateWire?> LastRun { get; init; }

    [JsonPropertyName("minCompletedTurnsSinceLastRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MinCompletedTurnsSinceLastRun { get; init; }

    [JsonPropertyName("nextRunAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> NextRunAt { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

    [JsonPropertyName("threadLookbackCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ThreadLookbackCount { get; init; }

}

/// <summary>Executable wire contract for HeartbeatTriggerResult.</summary>
public sealed class HeartbeatTriggerResult : ExtensibleJsonObject
{
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Result { get; init; }

}

/// <summary>Executable wire contract for HookErrorInfoWire.</summary>
public sealed class HookErrorInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

}

/// <summary>Executable wire contract for HookMetadataWire.</summary>
public sealed class HookMetadataWire : ExtensibleJsonObject
{
    [JsonPropertyName("asyncRewake")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AsyncRewake { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Command { get; init; }

    [JsonPropertyName("condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Condition { get; init; }

    [JsonPropertyName("currentHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> CurrentHash { get; init; }

    [JsonPropertyName("displayOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> DisplayOrder { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("eventName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EventName { get; init; }

    [JsonPropertyName("executionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ExecutionMode { get; init; }

    [JsonPropertyName("handlerType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> HandlerType { get; init; }

    [JsonPropertyName("isManaged")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsManaged { get; init; }

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

    [JsonPropertyName("matcher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Matcher { get; init; }

    [JsonPropertyName("once")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Once { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

    [JsonPropertyName("rewakeMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RewakeMessage { get; init; }

    [JsonPropertyName("rewakeSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RewakeSummary { get; init; }

    [JsonPropertyName("shell")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Shell { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("sourcePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourcePath { get; init; }

    [JsonPropertyName("statusMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> StatusMessage { get; init; }

    [JsonPropertyName("timeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TimeoutSec { get; init; }

    [JsonPropertyName("trustStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TrustStatus { get; init; }

}

/// <summary>Executable wire contract for HooksListParams.</summary>
public sealed class HooksListParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for HooksListResult.</summary>
public sealed class HooksListResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfoWire>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadataWire>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

/// <summary>Executable wire contract for HooksSetStateParams.</summary>
public sealed class HooksSetStateParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> Enabled { get; init; }

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

    [JsonPropertyName("trustedHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TrustedHash { get; init; }

}

/// <summary>Executable wire contract for HooksSetStateResult.</summary>
public sealed class HooksSetStateResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfoWire>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadataWire>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

/// <summary>Executable wire contract for HooksTrustPluginParams.</summary>
public sealed class HooksTrustPluginParams : ExtensibleJsonObject
{
    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PluginId { get; init; }

}

/// <summary>Executable wire contract for HooksTrustPluginResult.</summary>
public sealed class HooksTrustPluginResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfoWire>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadataWire>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewCloseParams.</summary>
public sealed class InlineVisualizationViewCloseParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewCloseResult.</summary>
public sealed class InlineVisualizationViewCloseResult : ExtensibleJsonObject
{
    [JsonPropertyName("closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Closed { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewMessageParams.</summary>
public sealed class InlineVisualizationViewMessageParams : ExtensibleJsonObject
{
    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewMessageResult.</summary>
public sealed class InlineVisualizationViewMessageResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewOpenParams.</summary>
public sealed class InlineVisualizationViewOpenParams : ExtensibleJsonObject
{
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> File { get; init; }

    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ItemId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

/// <summary>Executable wire contract for InlineVisualizationViewOpenResult.</summary>
public sealed class InlineVisualizationViewOpenResult : ExtensibleJsonObject
{
    [JsonPropertyName("fragment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Fragment { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MimeType { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for ItemWidgetStateSetParams.</summary>
public sealed class ItemWidgetStateSetParams : ExtensibleJsonObject
{
    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> CallId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("widgetState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> WidgetState { get; init; }

}

/// <summary>Executable wire contract for ItemWidgetStateSetResult.</summary>
public sealed class ItemWidgetStateSetResult : ExtensibleJsonObject
{
    [JsonPropertyName("cleared")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Cleared { get; init; }

}

/// <summary>Executable wire contract for MarketplaceAddParams.</summary>
public sealed class MarketplaceAddParams : ExtensibleJsonObject
{
    [JsonPropertyName("marketplacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MarketplacePath { get; init; }

    [JsonPropertyName("ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Ref { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("sparsePaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> SparsePaths { get; init; }

}

/// <summary>Executable wire contract for MarketplaceAddResult.</summary>
public sealed class MarketplaceAddResult : ExtensibleJsonObject
{
    [JsonPropertyName("alreadyAdded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AlreadyAdded { get; init; }

    [JsonPropertyName("marketplace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<MarketplaceInfoWire> Marketplace { get; init; }

}

/// <summary>Executable wire contract for MarketplaceFailureWire.</summary>
public sealed class MarketplaceFailureWire : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for MarketplaceInfoWire.</summary>
public sealed class MarketplaceInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("lastUpdated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastUpdated { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("pluginIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> PluginIds { get; init; }

    [JsonPropertyName("ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Ref { get; init; }

    [JsonPropertyName("removable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removable { get; init; }

    [JsonPropertyName("revision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Revision { get; init; }

    [JsonPropertyName("root")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Root { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("sourceType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourceType { get; init; }

    [JsonPropertyName("sparsePaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SparsePaths { get; init; }

}

/// <summary>Executable wire contract for MarketplaceRefreshParams.</summary>
public sealed class MarketplaceRefreshParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Name { get; init; }

}

/// <summary>Executable wire contract for MarketplaceRefreshResult.</summary>
public sealed class MarketplaceRefreshResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceFailureWire>> Errors { get; init; }

    [JsonPropertyName("marketplaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceInfoWire>> Marketplaces { get; init; }

}

/// <summary>Executable wire contract for MarketplaceRemoveParams.</summary>
public sealed class MarketplaceRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for MarketplaceRemoveResult.</summary>
public sealed class MarketplaceRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("removedRoot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RemovedRoot { get; init; }

}

/// <summary>Executable wire contract for McpAppMessageContentWire.</summary>
public sealed class McpAppMessageContentWire : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Text { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Type { get; init; }

}

/// <summary>Executable wire contract for McpAppResourceCspWire.</summary>
public sealed class McpAppResourceCspWire : ExtensibleJsonObject
{
    [JsonPropertyName("baseUriDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> BaseUriDomains { get; init; }

    [JsonPropertyName("connectDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ConnectDomains { get; init; }

    [JsonPropertyName("frameDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> FrameDomains { get; init; }

    [JsonPropertyName("resourceDomains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ResourceDomains { get; init; }

}

/// <summary>Executable wire contract for McpAppResourceMetadataWire.</summary>
public sealed class McpAppResourceMetadataWire : ExtensibleJsonObject
{
    [JsonPropertyName("csp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResourceCspWire> Csp { get; init; }

    [JsonPropertyName("prefersBorder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PrefersBorder { get; init; }

    [JsonPropertyName("requestedDomain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RequestedDomain { get; init; }

}

/// <summary>Executable wire contract for McpAppResourceWire.</summary>
public sealed class McpAppResourceWire : ExtensibleJsonObject
{
    [JsonPropertyName("html")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Html { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MimeType { get; init; }

    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResourceMetadataWire> Ui { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

/// <summary>Executable wire contract for McpAppToolResultWire.</summary>
public sealed class McpAppToolResultWire : ExtensibleJsonObject
{
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Meta { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Content { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsError { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> StructuredContent { get; init; }

}

/// <summary>Executable wire contract for McpAppToolWire.</summary>
public sealed class McpAppToolWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> InputSchema { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for McpAppViewCloseParams.</summary>
public sealed class McpAppViewCloseParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewCloseResult.</summary>
public sealed class McpAppViewCloseResult : ExtensibleJsonObject
{
    [JsonPropertyName("closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Closed { get; init; }

}

/// <summary>Executable wire contract for McpAppViewMessageParams.</summary>
public sealed class McpAppViewMessageParams : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppMessageContentWire> Content { get; init; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Role { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewMessageResult.</summary>
public sealed class McpAppViewMessageResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

}

/// <summary>Executable wire contract for McpAppViewModelContextUpdateParams.</summary>
public sealed class McpAppViewModelContextUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Content { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> StructuredContent { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewModelContextUpdateResult.</summary>
public sealed class McpAppViewModelContextUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("cleared")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Cleared { get; init; }

}

/// <summary>Executable wire contract for McpAppViewOpenLinkParams.</summary>
public sealed class McpAppViewOpenLinkParams : ExtensibleJsonObject
{
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewOpenLinkResult.</summary>
public sealed class McpAppViewOpenLinkResult : ExtensibleJsonObject
{
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

}

/// <summary>Executable wire contract for McpAppViewOpenParams.</summary>
public sealed class McpAppViewOpenParams : ExtensibleJsonObject
{
    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ItemId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

/// <summary>Executable wire contract for McpAppViewOpenResult.</summary>
public sealed class McpAppViewOpenResult : ExtensibleJsonObject
{
    [JsonPropertyName("resource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResourceWire> Resource { get; init; }

    [JsonPropertyName("toolInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> ToolInput { get; init; }

    [JsonPropertyName("toolResult")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppToolResultWire> ToolResult { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewResourceReadParams.</summary>
public sealed class McpAppViewResourceReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewResourceReadResult.</summary>
public sealed class McpAppViewResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Contents { get; init; }

}

/// <summary>Executable wire contract for McpAppViewStatusUpdatedParams.</summary>
public sealed class McpAppViewStatusUpdatedParams : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FallbackText { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewToolCallParams.</summary>
public sealed class McpAppViewToolCallParams : ExtensibleJsonObject
{
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Arguments { get; init; }

    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Tool { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewToolsListParams.</summary>
public sealed class McpAppViewToolsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

/// <summary>Executable wire contract for McpAppViewToolsListResult.</summary>
public sealed class McpAppViewToolsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpAppToolWire>> Tools { get; init; }

}

/// <summary>Executable wire contract for McpGetParams.</summary>
public sealed class McpGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for McpGetResult.</summary>
public sealed class McpGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfigWire> Server { get; init; }

}

/// <summary>Executable wire contract for McpListResult.</summary>
public sealed class McpListResult : ExtensibleJsonObject
{
    [JsonPropertyName("servers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpServerConfigWire>> Servers { get; init; }

}

/// <summary>Executable wire contract for McpRemoveParams.</summary>
public sealed class McpRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for McpRemoveResult.</summary>
public sealed class McpRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for McpRuntimeToolWire.</summary>
public sealed class McpRuntimeToolWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> InputSchema { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("outputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> OutputSchema { get; init; }

}

/// <summary>Executable wire contract for McpServerConfig.</summary>
public sealed class McpServerConfig : ExtensibleJsonObject
{
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Arguments { get; init; }

    [JsonPropertyName("bearerTokenEnvVar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BearerTokenEnvVar { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("envHttpHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>> EnvHttpHeaders { get; init; }

    [JsonPropertyName("envVars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> EnvVars { get; init; }

    [JsonPropertyName("environmentVariables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>> EnvironmentVariables { get; init; }

    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>> Headers { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("startupTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> StartupTimeoutSec { get; init; }

    [JsonPropertyName("toolTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> ToolTimeoutSec { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

}

/// <summary>Executable wire contract for McpServerConfigWire.</summary>
public sealed class McpServerConfigWire : ExtensibleJsonObject
{
    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Args { get; init; }

    [JsonPropertyName("bearerTokenEnvVar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BearerTokenEnvVar { get; init; }

    [JsonPropertyName("builtinModule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BuiltinModule { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Command { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Env { get; init; }

    [JsonPropertyName("envHttpHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> EnvHttpHeaders { get; init; }

    [JsonPropertyName("envVars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> EnvVars { get; init; }

    [JsonPropertyName("httpHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> HttpHeaders { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerOriginWire?> Origin { get; init; }

    [JsonPropertyName("readOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ReadOnly { get; init; }

    [JsonPropertyName("startupTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> StartupTimeoutSec { get; init; }

    [JsonPropertyName("toolTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> ToolTimeoutSec { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Url { get; init; }

}

/// <summary>Executable wire contract for McpServerElicitationRequestParams.</summary>
public sealed class McpServerElicitationRequestParams : ExtensibleJsonObject
{
    [JsonPropertyName("elicitationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ElicitationId { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("requestedSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> RequestedSchema { get; init; }

    [JsonPropertyName("serverName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ServerName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Url { get; init; }

}

/// <summary>Executable wire contract for McpServerElicitationResponse.</summary>
public sealed class McpServerElicitationResponse : ExtensibleJsonObject
{
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Action { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>?> Content { get; init; }

}

/// <summary>Executable wire contract for McpServerOAuthLoginCompletedNotification.</summary>
public sealed class McpServerOAuthLoginCompletedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for McpServerOAuthLoginParams.</summary>
public sealed class McpServerOAuthLoginParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("scopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Scopes { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("timeoutSecs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> TimeoutSecs { get; init; }

}

/// <summary>Executable wire contract for McpServerOAuthLoginResult.</summary>
public sealed class McpServerOAuthLoginResult : ExtensibleJsonObject
{
    [JsonPropertyName("authorizationUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthorizationUrl { get; init; }

}

/// <summary>Executable wire contract for McpServerOriginWire.</summary>
public sealed class McpServerOriginWire : ExtensibleJsonObject
{
    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BindingId { get; init; }

    [JsonPropertyName("declaredName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DeclaredName { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("pluginDisplayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginDisplayName { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for McpServerReloadResult.</summary>
public sealed class McpServerReloadResult : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for McpServerResourceReadParams.</summary>
public sealed class McpServerResourceReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Server { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

/// <summary>Executable wire contract for McpServerResourceReadResult.</summary>
public sealed class McpServerResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Contents { get; init; }

}

/// <summary>Executable wire contract for McpServerRuntimeStatusWire.</summary>
public sealed class McpServerRuntimeStatusWire : ExtensibleJsonObject
{
    [JsonPropertyName("authState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthState { get; init; }

    [JsonPropertyName("authStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthStatus { get; init; }

    [JsonPropertyName("declaredName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeclaredName { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FailureReason { get; init; }

    [JsonPropertyName("generation")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> Generation { get; init; }

    [JsonPropertyName("lastError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastError { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerOriginWire?> Origin { get; init; }

    [JsonPropertyName("resourceCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ResourceCount { get; init; }

    [JsonPropertyName("resourceTemplateCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ResourceTemplateCount { get; init; }

    [JsonPropertyName("resourceTemplates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<JsonElement>> ResourceTemplates { get; init; }

    [JsonPropertyName("resources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<JsonElement>> Resources { get; init; }

    [JsonPropertyName("runtimeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RuntimeName { get; init; }

    [JsonPropertyName("serverInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> ServerInfo { get; init; }

    [JsonPropertyName("startupState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> StartupState { get; init; }

    [JsonPropertyName("toolCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ToolCount { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, McpRuntimeToolWire>> Tools { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

}

/// <summary>Executable wire contract for McpServerStartupStatusUpdatedNotification.</summary>
public sealed class McpServerStartupStatusUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("authStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AuthStatus { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FailureReason { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Transport { get; init; }

}

/// <summary>Executable wire contract for McpServerStatusListParams.</summary>
public sealed class McpServerStatusListParams : ExtensibleJsonObject
{
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cursor { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Detail { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Limit { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for McpServerStatusListResult.</summary>
public sealed class McpServerStatusListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpServerRuntimeStatusWire>> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> NextCursor { get; init; }

}

/// <summary>Executable wire contract for McpServerToolCallParams.</summary>
public sealed class McpServerToolCallParams : ExtensibleJsonObject
{
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Meta { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>?> Arguments { get; init; }

    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Server { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Tool { get; init; }

}

/// <summary>Executable wire contract for McpServerToolCallResult.</summary>
public sealed class McpServerToolCallResult : ExtensibleJsonObject
{
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Meta { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Content { get; init; }

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsError { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> StructuredContent { get; init; }

}

/// <summary>Executable wire contract for McpTestParams.</summary>
public sealed class McpTestParams : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfigWire> Server { get; init; }

}

/// <summary>Executable wire contract for McpTestResult.</summary>
public sealed class McpTestResult : ExtensibleJsonObject
{
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

    [JsonPropertyName("toolCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ToolCount { get; init; }

}

/// <summary>Executable wire contract for McpUpsertParams.</summary>
public sealed class McpUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfigWire> Server { get; init; }

}

/// <summary>Executable wire contract for McpUpsertResult.</summary>
public sealed class McpUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfigWire> Server { get; init; }

}

/// <summary>Executable wire contract for MemoryResetResult.</summary>
public sealed class MemoryResetResult : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for ModelCatalogItem.</summary>
public sealed class ModelCatalogItem : ExtensibleJsonObject
{
    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelContextWindowCapabilityWire?> ContextWindow { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("ownedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OwnedBy { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelReasoningCapability?> Reasoning { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelSpeedCapability?> Speed { get; init; }

}

/// <summary>Executable wire contract for ModelContextWindowCapabilityWire.</summary>
public sealed class ModelContextWindowCapabilityWire : ExtensibleJsonObject
{
    [JsonPropertyName("catalogWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CatalogWindow { get; init; }

    [JsonPropertyName("configuredWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ConfiguredWindow { get; init; }

    [JsonPropertyName("maxWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MaxWindow { get; init; }

    [JsonPropertyName("supportsMax")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsMax { get; init; }

}

/// <summary>Executable wire contract for ModelListParams.</summary>
public sealed class ModelListParams : ExtensibleJsonObject
{
    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

}

/// <summary>Executable wire contract for ModelListResult.</summary>
public sealed class ModelListResult : ExtensibleJsonObject
{
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ModelCatalogItem>> Models { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Protocol { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

}

/// <summary>Executable wire contract for ModelPreference.</summary>
public sealed class ModelPreference : ExtensibleJsonObject
{
    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ModelPreferenceContextWindow> ContextWindow { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Model { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ReasoningConfig> Reasoning { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Speed { get; init; }

}

/// <summary>Executable wire contract for ModelPreferenceContextWindow.</summary>
public sealed class ModelPreferenceContextWindow : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

}

/// <summary>Executable wire contract for ModelReasoningCapability.</summary>
public sealed class ModelReasoningCapability : ExtensibleJsonObject
{
    [JsonPropertyName("defaultEffort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultEffort { get; init; }

    [JsonPropertyName("defaultOutput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultOutput { get; init; }

    [JsonPropertyName("supportedEfforts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ModelReasoningEffortOption>> SupportedEfforts { get; init; }

    [JsonPropertyName("supportedOutputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SupportedOutputs { get; init; }

    [JsonPropertyName("supportsDisable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsDisable { get; init; }

}

/// <summary>Executable wire contract for ModelReasoningEffortOption.</summary>
public sealed class ModelReasoningEffortOption : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Effort { get; init; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Label { get; init; }

}

/// <summary>Executable wire contract for ModelSpeedCapability.</summary>
public sealed class ModelSpeedCapability : ExtensibleJsonObject
{
    [JsonPropertyName("defaultMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultMode { get; init; }

    [JsonPropertyName("supportedModes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SupportedModes { get; init; }

}

/// <summary>Executable wire contract for PerforceConnectionWire.</summary>
public sealed class PerforceConnectionWire : ExtensibleJsonObject
{
    [JsonPropertyName("autoOffline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AutoOffline { get; init; }

    [JsonPropertyName("charset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Charset { get; init; }

    [JsonPropertyName("client")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Client { get; init; }

    [JsonPropertyName("online")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Online { get; init; }

    [JsonPropertyName("p4ConfigName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> P4ConfigName { get; init; }

    [JsonPropertyName("p4ExecutablePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> P4ExecutablePath { get; init; }

    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Port { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TimeoutSeconds { get; init; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> User { get; init; }

}

/// <summary>Executable wire contract for PlanTodoWire.</summary>
public sealed class PlanTodoWire : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Priority { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

}

/// <summary>Executable wire contract for PlanUpdatedNotification.</summary>
public sealed class PlanUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("overview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Overview { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

    [JsonPropertyName("todos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PlanTodoWire>> Todos { get; init; }

}

/// <summary>Executable wire contract for PluginAppInfoWire.</summary>
public sealed class PluginAppInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AppId { get; init; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Category { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("developerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeveloperName { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("nativeApplication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginAppNativeApplicationWire?> NativeApplication { get; init; }

    [JsonPropertyName("releasePage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReleasePage { get; init; }

}

/// <summary>Executable wire contract for PluginAppNativeApplicationWire.</summary>
public sealed class PluginAppNativeApplicationWire : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("installUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InstallUrl { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Protocol { get; init; }

}

/// <summary>Executable wire contract for PluginDesktopExtensionInfoWire.</summary>
public sealed class PluginDesktopExtensionInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("connectOrigins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ConnectOrigins { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("entry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Entry { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("requiredAppIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RequiredAppIds { get; init; }

    [JsonPropertyName("styles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Styles { get; init; }

    [JsonPropertyName("surfaceWriteScopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SurfaceWriteScopes { get; init; }

    [JsonPropertyName("surfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDesktopExtensionSurfaceWire>> Surfaces { get; init; }

}

/// <summary>Executable wire contract for PluginDesktopExtensionSurfaceWire.</summary>
public sealed class PluginDesktopExtensionSurfaceWire : ExtensibleJsonObject
{
    [JsonPropertyName("actionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActionId { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Icon { get; init; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Label { get; init; }

    [JsonPropertyName("localizedLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> LocalizedLabel { get; init; }

    [JsonPropertyName("order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Order { get; init; }

    [JsonPropertyName("placement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Placement { get; init; }

    [JsonPropertyName("rendererId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RendererId { get; init; }

    [JsonPropertyName("settingsId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> SettingsId { get; init; }

    [JsonPropertyName("slot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Slot { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Title { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Type { get; init; }

    [JsonPropertyName("viewId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ViewId { get; init; }

}

/// <summary>Executable wire contract for PluginDiagnosticWire.</summary>
public sealed class PluginDiagnosticWire : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Severity { get; init; }

}

/// <summary>Executable wire contract for PluginFunctionInfoWire.</summary>
public sealed class PluginFunctionInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Namespace { get; init; }

}

/// <summary>Executable wire contract for PluginHookInfoWire.</summary>
public sealed class PluginHookInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("eventName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EventName { get; init; }

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

}

/// <summary>Executable wire contract for PluginInfoWire.</summary>
public sealed class PluginInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("apps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginAppInfoWire>> Apps { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("desktopExtensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDesktopExtensionInfoWire>> DesktopExtensions { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDiagnosticWire>> Diagnostics { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("functions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginFunctionInfoWire>> Functions { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginHookInfoWire>> Hooks { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("installable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Installable { get; init; }

    [JsonPropertyName("installed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Installed { get; init; }

    [JsonPropertyName("interface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInterfaceWire?> Interface { get; init; }

    [JsonPropertyName("lspServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginLspServerInfoWire>> LspServers { get; init; }

    [JsonPropertyName("marketplaceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MarketplaceName { get; init; }

    [JsonPropertyName("mcpServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginMcpServerInfoWire>> McpServers { get; init; }

    [JsonPropertyName("removable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removable { get; init; }

    [JsonPropertyName("rootPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RootPath { get; init; }

    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginSkillInfoWire>> Skills { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Version { get; init; }

}

/// <summary>Executable wire contract for PluginInstallLocalParams.</summary>
public sealed class PluginInstallLocalParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

}

/// <summary>Executable wire contract for PluginInstallParams.</summary>
public sealed class PluginInstallParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for PluginInstallResult.</summary>
public sealed class PluginInstallResult : ExtensibleJsonObject
{
    [JsonPropertyName("plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInfoWire> Plugin { get; init; }

}

/// <summary>Executable wire contract for PluginInterfaceWire.</summary>
public sealed class PluginInterfaceWire : ExtensibleJsonObject
{
    [JsonPropertyName("brandColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BrandColor { get; init; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Capabilities { get; init; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Category { get; init; }

    [JsonPropertyName("composerIconDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ComposerIconDataUrl { get; init; }

    [JsonPropertyName("defaultPrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultPrompt { get; init; }

    [JsonPropertyName("developerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DeveloperName { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("logoDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LogoDataUrl { get; init; }

    [JsonPropertyName("longDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LongDescription { get; init; }

    [JsonPropertyName("privacyPolicyUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PrivacyPolicyUrl { get; init; }

    [JsonPropertyName("shortDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShortDescription { get; init; }

    [JsonPropertyName("termsOfServiceUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TermsOfServiceUrl { get; init; }

    [JsonPropertyName("websiteUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WebsiteUrl { get; init; }

}

/// <summary>Executable wire contract for PluginListParams.</summary>
public sealed class PluginListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeDisabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeDisabled { get; init; }

}

/// <summary>Executable wire contract for PluginListResult.</summary>
public sealed class PluginListResult : ExtensibleJsonObject
{
    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDiagnosticWire>> Diagnostics { get; init; }

    [JsonPropertyName("marketplaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceInfoWire>> Marketplaces { get; init; }

    [JsonPropertyName("plugins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginInfoWire>> Plugins { get; init; }

}

/// <summary>Executable wire contract for PluginLspServerInfoWire.</summary>
public sealed class PluginLspServerInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Active { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Extensions { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("runtimeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RuntimeName { get; init; }

    [JsonPropertyName("shadowedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShadowedBy { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

}

/// <summary>Executable wire contract for PluginMcpServerInfoWire.</summary>
public sealed class PluginMcpServerInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Active { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("runtimeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RuntimeName { get; init; }

    [JsonPropertyName("shadowedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShadowedBy { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

}

/// <summary>Executable wire contract for PluginRemoveParams.</summary>
public sealed class PluginRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for PluginRemoveResult.</summary>
public sealed class PluginRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInfoWire?> Plugin { get; init; }

}

/// <summary>Executable wire contract for PluginSetEnabledParams.</summary>
public sealed class PluginSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for PluginSetEnabledResult.</summary>
public sealed class PluginSetEnabledResult : ExtensibleJsonObject
{
    [JsonPropertyName("plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInfoWire> Plugin { get; init; }

}

/// <summary>Executable wire contract for PluginSkillInfoWire.</summary>
public sealed class PluginSkillInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("shortDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShortDescription { get; init; }

}

/// <summary>Executable wire contract for PluginViewParams.</summary>
public sealed class PluginViewParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for PluginViewResult.</summary>
public sealed class PluginViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInfoWire> Plugin { get; init; }

}

/// <summary>Executable wire contract for ProfileInsightsParams.</summary>
public sealed class ProfileInsightsParams : ExtensibleJsonObject
{
    [JsonPropertyName("topSkills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TopSkills { get; init; }

}

/// <summary>Executable wire contract for ProfileInsightsResult.</summary>
public sealed class ProfileInsightsResult : ExtensibleJsonObject
{
    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SkillUsageWire>> Skills { get; init; }

    [JsonPropertyName("skillsExplored")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> SkillsExplored { get; init; }

    [JsonPropertyName("topModel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RankedMetric?> TopModel { get; init; }

    [JsonPropertyName("topReasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<RankedMetric?> TopReasoning { get; init; }

    [JsonPropertyName("totalSkillsUsed")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalSkillsUsed { get; init; }

    [JsonPropertyName("totalThreads")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalThreads { get; init; }

}

/// <summary>Executable wire contract for ProviderCapabilitiesWire.</summary>
public sealed class ProviderCapabilitiesWire : ExtensibleJsonObject
{
    [JsonPropertyName("cachedInputUsageReporting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> CachedInputUsageReporting { get; init; }

    [JsonPropertyName("extendedThinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ExtendedThinking { get; init; }

    [JsonPropertyName("modelListing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ModelListing { get; init; }

    [JsonPropertyName("nativeDeferredToolLoading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> NativeDeferredToolLoading { get; init; }

    [JsonPropertyName("promptCacheRequestShaping")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PromptCacheRequestShaping { get; init; }

    [JsonPropertyName("rawMetadataPassthrough")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> RawMetadataPassthrough { get; init; }

    [JsonPropertyName("responsesApi")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ResponsesApi { get; init; }

    [JsonPropertyName("streamingChat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> StreamingChat { get; init; }

    [JsonPropertyName("tokenUsageReporting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> TokenUsageReporting { get; init; }

    [JsonPropertyName("toolCalling")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ToolCalling { get; init; }

    [JsonPropertyName("toolChoiceControls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ToolChoiceControls { get; init; }

}

/// <summary>Executable wire contract for ProviderCreateParams.</summary>
public sealed class ProviderCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("apiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ApiKey { get; init; }

    [JsonPropertyName("authMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AuthMethod { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("endPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EndPoint { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputTokens { get; init; }

    [JsonPropertyName("networkTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> NetworkTimeoutSeconds { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Protocol { get; init; }

    [JsonPropertyName("streamIdleTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("streamMaxRetries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamMaxRetries { get; init; }

    [JsonPropertyName("supportsHostedImageGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SupportsHostedImageGeneration { get; init; }

}

/// <summary>Executable wire contract for ProviderDeleteParams.</summary>
public sealed class ProviderDeleteParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

/// <summary>Executable wire contract for ProviderDeleteResult.</summary>
public sealed class ProviderDeleteResult : ExtensibleJsonObject
{
    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Deleted { get; init; }

}

/// <summary>Executable wire contract for ProviderInfo.</summary>
public sealed class ProviderInfo : ExtensibleJsonObject
{
    [JsonPropertyName("apiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApiKey { get; init; }

    [JsonPropertyName("authMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthMethod { get; init; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProviderCapabilitiesWire> Capabilities { get; init; }

    [JsonPropertyName("chatGptAccountId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChatGptAccountId { get; init; }

    [JsonPropertyName("chatGptPlanType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChatGptPlanType { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("endPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EndPoint { get; init; }

    [JsonPropertyName("hasApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasApiKey { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("isImplicit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsImplicit { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputTokens { get; init; }

    [JsonPropertyName("networkTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> NetworkTimeoutSeconds { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Protocol { get; init; }

    [JsonPropertyName("streamIdleTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("streamMaxRetries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamMaxRetries { get; init; }

    [JsonPropertyName("supportsHostedImageGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsHostedImageGeneration { get; init; }

}

/// <summary>Executable wire contract for ProviderListParams.</summary>
public sealed class ProviderListParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for ProviderListResult.</summary>
public sealed class ProviderListResult : ExtensibleJsonObject
{
    [JsonPropertyName("providers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ProviderInfo>> Providers { get; init; }

}

/// <summary>Executable wire contract for ProviderMutationResult.</summary>
public sealed class ProviderMutationResult : ExtensibleJsonObject
{
    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProviderInfo> Provider { get; init; }

}

/// <summary>Executable wire contract for ProviderTestParams.</summary>
public sealed class ProviderTestParams : ExtensibleJsonObject
{
    [JsonPropertyName("apiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApiKey { get; init; }

    [JsonPropertyName("endPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> EndPoint { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputTokens { get; init; }

    [JsonPropertyName("networkTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> NetworkTimeoutSeconds { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Protocol { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("streamIdleTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("streamMaxRetries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamMaxRetries { get; init; }

}

/// <summary>Executable wire contract for ProviderTestResult.</summary>
public sealed class ProviderTestResult : ExtensibleJsonObject
{
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorMessage { get; init; }

    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ModelCatalogItem>> Models { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Protocol { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Success { get; init; }

}

/// <summary>Executable wire contract for ProviderUpdateParams.</summary>
public sealed class ProviderUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("apiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApiKey { get; init; }

    [JsonPropertyName("authMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AuthMethod { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("endPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> EndPoint { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("maxOutputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputTokens { get; init; }

    [JsonPropertyName("networkTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> NetworkTimeoutSeconds { get; init; }

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Protocol { get; init; }

    [JsonPropertyName("streamIdleTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamIdleTimeoutMs { get; init; }

    [JsonPropertyName("streamMaxRetries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> StreamMaxRetries { get; init; }

    [JsonPropertyName("supportsHostedImageGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SupportsHostedImageGeneration { get; init; }

}

/// <summary>Executable wire contract for RankedMetric.</summary>
public sealed class RankedMetric : ExtensibleJsonObject
{
    [JsonPropertyName("count")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Count { get; init; }

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

    [JsonPropertyName("total")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Total { get; init; }

}

/// <summary>Executable wire contract for ReasoningConfig.</summary>
public sealed class ReasoningConfig : ExtensibleJsonObject
{
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Effort { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Output { get; init; }

}

/// <summary>Executable wire contract for SkillInfoWire.</summary>
public sealed class SkillInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("available")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Available { get; init; }

    [JsonPropertyName("defaultPrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultPrompt { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("hasVariant")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasVariant { get; init; }

    [JsonPropertyName("iconLargeDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> IconLargeDataUrl { get; init; }

    [JsonPropertyName("iconSmallDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> IconSmallDataUrl { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Metadata { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("pluginDisplayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginDisplayName { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

    [JsonPropertyName("shortDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShortDescription { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("unavailableReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> UnavailableReason { get; init; }

}

/// <summary>Executable wire contract for SkillUsageWire.</summary>
public sealed class SkillUsageWire : ExtensibleJsonObject
{
    [JsonPropertyName("count")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Count { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("pluginDisplayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginDisplayName { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

}

/// <summary>Executable wire contract for SkillsListParams.</summary>
public sealed class SkillsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeUnavailable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeUnavailable { get; init; }

}

/// <summary>Executable wire contract for SkillsListResult.</summary>
public sealed class SkillsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SkillInfoWire>> Skills { get; init; }

}

/// <summary>Executable wire contract for SkillsReadParams.</summary>
public sealed class SkillsReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsReadResult.</summary>
public sealed class SkillsReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Metadata { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsRestoreOriginalParams.</summary>
public sealed class SkillsRestoreOriginalParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsRestoreOriginalResult.</summary>
public sealed class SkillsRestoreOriginalResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("restored")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Restored { get; init; }

}

/// <summary>Executable wire contract for SkillsSetEnabledParams.</summary>
public sealed class SkillsSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsSetEnabledResult.</summary>
public sealed class SkillsSetEnabledResult : ExtensibleJsonObject
{
    [JsonPropertyName("skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SkillInfoWire> Skill { get; init; }

}

/// <summary>Executable wire contract for SkillsUninstallParams.</summary>
public sealed class SkillsUninstallParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsUninstallResult.</summary>
public sealed class SkillsUninstallResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("removedSourcePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RemovedSourcePath { get; init; }

    [JsonPropertyName("removedVariantCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> RemovedVariantCount { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("uninstalled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Uninstalled { get; init; }

}

/// <summary>Executable wire contract for SkillsViewParams.</summary>
public sealed class SkillsViewParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SkillsViewResult.</summary>
public sealed class SkillsViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SourceControlCapabilitiesWire.</summary>
public sealed class SourceControlCapabilitiesWire : ExtensibleJsonObject
{
    [JsonPropertyName("gitCommit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> GitCommit { get; init; }

    [JsonPropertyName("perforceBinding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PerforceBinding { get; init; }

    [JsonPropertyName("perforceChangelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PerforceChangelist { get; init; }

    [JsonPropertyName("perforceShelve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PerforceShelve { get; init; }

    [JsonPropertyName("perforceSubmit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PerforceSubmit { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistCreateParams.</summary>
public sealed class SourceControlChangelistCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("setAsTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SetAsTarget { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistCreateResult.</summary>
public sealed class SourceControlChangelistCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("changelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlChangelistEntryWire> Changelist { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTargetWire> Target { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistEntryWire.</summary>
public sealed class SourceControlChangelistEntryWire : ExtensibleJsonObject
{
    [JsonPropertyName("client")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Client { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("isDefault")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsDefault { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> User { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistListParams.</summary>
public sealed class SourceControlChangelistListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistListResult.</summary>
public sealed class SourceControlChangelistListResult : ExtensibleJsonObject
{
    [JsonPropertyName("changelists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlChangelistEntryWire>> Changelists { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTargetWire> Target { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistPrepareParams.</summary>
public sealed class SourceControlChangelistPrepareParams : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("paths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Paths { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Target { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for SourceControlChangelistPrepareResult.</summary>
public sealed class SourceControlChangelistPrepareResult : ExtensibleJsonObject
{
    [JsonPropertyName("changelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Changelist { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("created")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Created { get; init; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlDiagnosticItem>> Errors { get; init; }

    [JsonPropertyName("movedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> MovedPaths { get; init; }

    [JsonPropertyName("skippedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SkippedPaths { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlDiagnosticItem>> Warnings { get; init; }

}

/// <summary>Executable wire contract for SourceControlDiagnosticItem.</summary>
public sealed class SourceControlDiagnosticItem : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FallbackText { get; init; }

}

/// <summary>Executable wire contract for SourceControlGetParams.</summary>
public sealed class SourceControlGetParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for SourceControlSnapshot.</summary>
public sealed class SourceControlSnapshot : ExtensibleJsonObject
{
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlCapabilitiesWire> Capabilities { get; init; }

    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("effectiveProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EffectiveProvider { get; init; }

    [JsonPropertyName("perforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PerforceConnectionWire?> Perforce { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestAuthentication.</summary>
public sealed class SourceControlTestAuthentication : ExtensibleJsonObject
{
    [JsonPropertyName("expiresMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ExpiresMessage { get; init; }

    [JsonPropertyName("loginRequired")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> LoginRequired { get; init; }

    [JsonPropertyName("ticketStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TicketStatus { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestDiagnostics.</summary>
public sealed class SourceControlTestDiagnostics : ExtensibleJsonObject
{
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ErrorCode { get; init; }

    [JsonPropertyName("p4Version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> P4Version { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TimeoutSeconds { get; init; }

    [JsonPropertyName("warningCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> WarningCount { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestIdentity.</summary>
public sealed class SourceControlTestIdentity : ExtensibleJsonObject
{
    [JsonPropertyName("charset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Charset { get; init; }

    [JsonPropertyName("client")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Client { get; init; }

    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("serverAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ServerAddress { get; init; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> User { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestParams.</summary>
public sealed class SourceControlTestParams : ExtensibleJsonObject
{
    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Password { get; init; }

    [JsonPropertyName("perforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PerforceConnectionWire?> Perforce { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestResult.</summary>
public sealed class SourceControlTestResult : ExtensibleJsonObject
{
    [JsonPropertyName("authentication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlTestAuthentication> Authentication { get; init; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlTestDiagnostics> Diagnostics { get; init; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlDiagnosticItem>> Errors { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FallbackText { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlTestIdentity> Identity { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Summary { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlDiagnosticItem>> Warnings { get; init; }

    [JsonPropertyName("workspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlTestWorkspace> Workspace { get; init; }

}

/// <summary>Executable wire contract for SourceControlTestWorkspace.</summary>
public sealed class SourceControlTestWorkspace : ExtensibleJsonObject
{
    [JsonPropertyName("altRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> AltRoots { get; init; }

    [JsonPropertyName("clientRoot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ClientRoot { get; init; }

    [JsonPropertyName("mappingOk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> MappingOk { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for SourceControlThreadTargetParams.</summary>
public sealed class SourceControlThreadTargetParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for SourceControlThreadTargetResult.</summary>
public sealed class SourceControlThreadTargetResult : ExtensibleJsonObject
{
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTargetWire> Target { get; init; }

}

/// <summary>Executable wire contract for SourceControlThreadTargetUpdateParams.</summary>
public sealed class SourceControlThreadTargetUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTargetWire?> Target { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for SourceControlThreadTargetWire.</summary>
public sealed class SourceControlThreadTargetWire : ExtensibleJsonObject
{
    [JsonPropertyName("changelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Changelist { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

/// <summary>Executable wire contract for SourceControlUpdateParams.</summary>
public sealed class SourceControlUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("perforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PerforceConnectionWire?> Perforce { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

/// <summary>Executable wire contract for SubAgentChildWire.</summary>
public sealed class SubAgentChildWire : ExtensibleJsonObject
{
    [JsonPropertyName("edge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadSpawnEdge> Edge { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread?> Thread { get; init; }

}

/// <summary>Executable wire contract for SubAgentChildrenListParams.</summary>
public sealed class SubAgentChildrenListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeClosed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeClosed { get; init; }

    [JsonPropertyName("includeThreads")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeThreads { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

}

/// <summary>Executable wire contract for SubAgentChildrenListResult.</summary>
public sealed class SubAgentChildrenListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentChildWire>> Data { get; init; }

}

/// <summary>Executable wire contract for SubAgentControlResult.</summary>
public sealed class SubAgentControlResult : ExtensibleJsonObject
{
    [JsonPropertyName("agentNickname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentNickname { get; init; }

    [JsonPropertyName("agentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentPath { get; init; }

    [JsonPropertyName("agentRole")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentRole { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("profileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProfileName { get; init; }

    [JsonPropertyName("runtimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RuntimeType { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("supportsClose")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsClose { get; init; }

    [JsonPropertyName("supportsFollowupTask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsFollowupTask { get; init; }

    [JsonPropertyName("supportsSendMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsSendMessage { get; init; }

    [JsonPropertyName("taskName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TaskName { get; init; }

}

/// <summary>Executable wire contract for SubAgentGraphChangedNotification.</summary>
public sealed class SubAgentGraphChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("childThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChildThreadId { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileDiagnosticWire.</summary>
public sealed class SubAgentProfileDiagnosticWire : ExtensibleJsonObject
{
    [JsonPropertyName("binaryResolved")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> BinaryResolved { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("hiddenFromPrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HiddenFromPrompt { get; init; }

    [JsonPropertyName("hiddenReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> HiddenReason { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileEntryWire.</summary>
public sealed class SubAgentProfileEntryWire : ExtensibleJsonObject
{
    [JsonPropertyName("builtInDefaults")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWriteWire?> BuiltInDefaults { get; init; }

    [JsonPropertyName("definition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWriteWire> Definition { get; init; }

    [JsonPropertyName("diagnostic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileDiagnosticWire> Diagnostic { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("hasWorkspaceOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasWorkspaceOverride { get; init; }

    [JsonPropertyName("isBuiltIn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsBuiltIn { get; init; }

    [JsonPropertyName("isDefault")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsDefault { get; init; }

    [JsonPropertyName("isTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsTemplate { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileListResult.</summary>
public sealed class SubAgentProfileListResult : ExtensibleJsonObject
{
    [JsonPropertyName("defaultName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultName { get; init; }

    [JsonPropertyName("profiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentProfileEntryWire>> Profiles { get; init; }

    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentSettingsWire> Settings { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileRemoveParams.</summary>
public sealed class SubAgentProfileRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileRemoveResult.</summary>
public sealed class SubAgentProfileRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileSetEnabledParams.</summary>
public sealed class SubAgentProfileSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileSetEnabledResult.</summary>
public sealed class SubAgentProfileSetEnabledResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileEntryWire> Profile { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileUpsertParams.</summary>
public sealed class SubAgentProfileUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("definition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWriteWire> Definition { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileUpsertResult.</summary>
public sealed class SubAgentProfileUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileEntryWire> Profile { get; init; }

}

/// <summary>Executable wire contract for SubAgentProfileWriteWire.</summary>
public sealed class SubAgentProfileWriteWire : ExtensibleJsonObject
{
    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Args { get; init; }

    [JsonPropertyName("bin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Bin { get; init; }

    [JsonPropertyName("deleteOutputFileAfterRead")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DeleteOutputFileAfterRead { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> Env { get; init; }

    [JsonPropertyName("envPassthrough")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> EnvPassthrough { get; init; }

    [JsonPropertyName("inputArgTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputArgTemplate { get; init; }

    [JsonPropertyName("inputEnvKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputEnvKey { get; init; }

    [JsonPropertyName("inputFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputFormat { get; init; }

    [JsonPropertyName("inputMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputMode { get; init; }

    [JsonPropertyName("maxOutputBytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputBytes { get; init; }

    [JsonPropertyName("outputFileArgTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputFileArgTemplate { get; init; }

    [JsonPropertyName("outputFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputFormat { get; init; }

    [JsonPropertyName("outputInputTokensJsonPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputInputTokensJsonPath { get; init; }

    [JsonPropertyName("outputJsonPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputJsonPath { get; init; }

    [JsonPropertyName("outputOutputTokensJsonPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputOutputTokensJsonPath { get; init; }

    [JsonPropertyName("outputTotalTokensJsonPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OutputTotalTokensJsonPath { get; init; }

    [JsonPropertyName("permissionModeMapping")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, string>?> PermissionModeMapping { get; init; }

    [JsonPropertyName("readOutputFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ReadOutputFile { get; init; }

    [JsonPropertyName("resumeArgTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ResumeArgTemplate { get; init; }

    [JsonPropertyName("resumeSessionIdJsonPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ResumeSessionIdJsonPath { get; init; }

    [JsonPropertyName("resumeSessionIdRegex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ResumeSessionIdRegex { get; init; }

    [JsonPropertyName("runtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Runtime { get; init; }

    [JsonPropertyName("sanitizationRules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> SanitizationRules { get; init; }

    [JsonPropertyName("supportsModelSelection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SupportsModelSelection { get; init; }

    [JsonPropertyName("supportsResume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SupportsResume { get; init; }

    [JsonPropertyName("supportsStreaming")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SupportsStreaming { get; init; }

    [JsonPropertyName("timeout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Timeout { get; init; }

    [JsonPropertyName("trustLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TrustLevel { get; init; }

    [JsonPropertyName("workingDirectoryMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkingDirectoryMode { get; init; }

}

/// <summary>Executable wire contract for SubAgentProgressEntry.</summary>
public sealed class SubAgentProgressEntry : ExtensibleJsonObject
{
    [JsonPropertyName("cacheWriteInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CacheWriteInputTokens { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("currentTool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CurrentTool { get; init; }

    [JsonPropertyName("currentToolDisplay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CurrentToolDisplay { get; init; }

    [JsonPropertyName("freshInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> FreshInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("isCompleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsCompleted { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("reasoningOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> ReasoningOutputTokens { get; init; }

}

/// <summary>Executable wire contract for SubAgentProgressNotification.</summary>
public sealed class SubAgentProgressNotification : ExtensibleJsonObject
{
    [JsonPropertyName("entries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentProgressEntry>> Entries { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for SubAgentSettingsUpdateParams.</summary>
public sealed class SubAgentSettingsUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("defaultWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DefaultWaitTimeoutMs { get; init; }

    [JsonPropertyName("externalCliSessionResumeEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ExternalCliSessionResumeEnabled { get; init; }

    [JsonPropertyName("maxWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxWaitTimeoutMs { get; init; }

    [JsonPropertyName("minWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MinWaitTimeoutMs { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

}

/// <summary>Executable wire contract for SubAgentSettingsUpdateResult.</summary>
public sealed class SubAgentSettingsUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentSettingsWire> Settings { get; init; }

}

/// <summary>Executable wire contract for SubAgentSettingsWire.</summary>
public sealed class SubAgentSettingsWire : ExtensibleJsonObject
{
    [JsonPropertyName("defaultWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> DefaultWaitTimeoutMs { get; init; }

    [JsonPropertyName("externalCliSessionResumeEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ExternalCliSessionResumeEnabled { get; init; }

    [JsonPropertyName("maxWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MaxWaitTimeoutMs { get; init; }

    [JsonPropertyName("minWaitTimeoutMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MinWaitTimeoutMs { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

}

/// <summary>Executable wire contract for SubAgentTargetMessageParams.</summary>
public sealed class SubAgentTargetMessageParams : ExtensibleJsonObject
{
    [JsonPropertyName("deliveryMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DeliveryMode { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Target { get; init; }

}

/// <summary>Executable wire contract for SubAgentTargetParams.</summary>
public sealed class SubAgentTargetParams : ExtensibleJsonObject
{
    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Target { get; init; }

}

/// <summary>Executable wire contract for SystemEventNotification.</summary>
public sealed class SystemEventNotification : ExtensibleJsonObject
{
    [JsonPropertyName("contextUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ContextUsageSnapshot?> ContextUsage { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> FallbackText { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Kind { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("messageKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MessageKey { get; init; }

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>?> Params { get; init; }

    [JsonPropertyName("percentLeft")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> PercentLeft { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("tokenCount")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TokenCount { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for SystemJobResultNotification.</summary>
public sealed class SystemJobResultNotification : ExtensibleJsonObject
{
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("jobId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> JobId { get; init; }

    [JsonPropertyName("jobName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> JobName { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Result { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("tokenUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SystemJobTokenUsageWire?> TokenUsage { get; init; }

}

/// <summary>Executable wire contract for SystemJobTokenUsageWire.</summary>
public sealed class SystemJobTokenUsageWire : ExtensibleJsonObject
{
    [JsonPropertyName("inputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> OutputTokens { get; init; }

}

/// <summary>Executable wire contract for TerminalCleanParams.</summary>
public sealed class TerminalCleanParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TerminalCleanResult.</summary>
public sealed class TerminalCleanResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<BackgroundTerminalSnapshot>> Terminals { get; init; }

}

/// <summary>Executable wire contract for TerminalLifecycleNotification.</summary>
public sealed class TerminalLifecycleNotification : ExtensibleJsonObject
{
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Delta { get; init; }

    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

/// <summary>Executable wire contract for TerminalListParams.</summary>
public sealed class TerminalListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TerminalListResult.</summary>
public sealed class TerminalListResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<BackgroundTerminalSnapshot>> Terminals { get; init; }

}

/// <summary>Executable wire contract for TerminalReadParams.</summary>
public sealed class TerminalReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("maxOutputChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputChars { get; init; }

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

    [JsonPropertyName("waitMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> WaitMs { get; init; }

}

/// <summary>Executable wire contract for TerminalReadResult.</summary>
public sealed class TerminalReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

/// <summary>Executable wire contract for TerminalStopParams.</summary>
public sealed class TerminalStopParams : ExtensibleJsonObject
{
    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

}

/// <summary>Executable wire contract for TerminalStopResult.</summary>
public sealed class TerminalStopResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

/// <summary>Executable wire contract for TerminalWriteParams.</summary>
public sealed class TerminalWriteParams : ExtensibleJsonObject
{
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Input { get; init; }

    [JsonPropertyName("maxOutputChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxOutputChars { get; init; }

    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

    [JsonPropertyName("yieldTimeMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> YieldTimeMs { get; init; }

}

/// <summary>Executable wire contract for TerminalWriteResult.</summary>
public sealed class TerminalWriteResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

/// <summary>Executable wire contract for ThreadArchiveParams.</summary>
public sealed class ThreadArchiveParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadCompactStartParams.</summary>
public sealed class ThreadCompactStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadCompactStartResponse.</summary>
public sealed class ThreadCompactStartResponse : ExtensibleJsonObject
{
    [JsonPropertyName("contextUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ContextUsageSnapshot?> ContextUsage { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Outcome { get; init; }

}

/// <summary>Executable wire contract for ThreadConfigUpdateParams.</summary>
public sealed class ThreadConfigUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration> Config { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadConfiguration.</summary>
public sealed class ThreadConfiguration : ExtensibleJsonObject
{
    [JsonPropertyName("agentBuilderTargetId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentBuilderTargetId { get; init; }

    [JsonPropertyName("agentBuilderTargetSource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentBuilderTargetSource { get; init; }

    [JsonPropertyName("agentControlToolAccess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentControlToolAccess { get; init; }

    [JsonPropertyName("agentInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentInstructions { get; init; }

    [JsonPropertyName("agentProfileFingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileFingerprint { get; init; }

    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileId { get; init; }

    [JsonPropertyName("agentProfileSource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentProfileSource { get; init; }

    [JsonPropertyName("allowedAgentControlTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> AllowedAgentControlTools { get; init; }

    [JsonPropertyName("approvalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ApprovalPolicy { get; init; }

    [JsonPropertyName("approvalTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ApprovalTimeoutSeconds { get; init; }

    [JsonPropertyName("automationTaskDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AutomationTaskDirectory { get; init; }

    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadContextWindowConfig?> ContextWindow { get; init; }

    [JsonPropertyName("customTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> CustomTools { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("executionWorkspaceOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ExecutionWorkspaceOverride { get; init; }

    [JsonPropertyName("extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Extensions { get; init; }

    [JsonPropertyName("mcpPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadMcpPolicy?> McpPolicy { get; init; }

    [JsonPropertyName("mcpServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpServerConfig>?> McpServers { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Model { get; init; }

    [JsonPropertyName("overrideBasePrompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> OverrideBasePrompt { get; init; }

    [JsonPropertyName("pluginPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadPluginPolicy?> PluginPolicy { get; init; }

    [JsonPropertyName("promptProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PromptProfile { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ReasoningConfig?> Reasoning { get; init; }

    [JsonPropertyName("requireApprovalOutsideWorkspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> RequireApprovalOutsideWorkspace { get; init; }

    [JsonPropertyName("roleInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RoleInstructions { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("skillsPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadSkillsPolicy?> SkillsPolicy { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Speed { get; init; }

    [JsonPropertyName("teamsPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadTeamsPolicy?> TeamsPolicy { get; init; }

    [JsonPropertyName("toolAllowList")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> ToolAllowList { get; init; }

    [JsonPropertyName("toolDenyList")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> ToolDenyList { get; init; }

    [JsonPropertyName("toolPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadToolPolicy?> ToolPolicy { get; init; }

    [JsonPropertyName("toolProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ToolProfile { get; init; }

    [JsonPropertyName("useToolProfileOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> UseToolProfileOnly { get; init; }

    [JsonPropertyName("workspaceOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkspaceOverride { get; init; }

}

/// <summary>Executable wire contract for ThreadContextWindowConfig.</summary>
public sealed class ThreadContextWindowConfig : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

}

/// <summary>Executable wire contract for ThreadDeleteParams.</summary>
public sealed class ThreadDeleteParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadForkParams.</summary>
public sealed class ThreadForkParams : ExtensibleJsonObject
{
    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>?> AdditionalContext { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> Config { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<JsonElement>?> DynamicTools { get; init; }

    [JsonPropertyName("ephemeral")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> Ephemeral { get; init; }

    [JsonPropertyName("excludeTurns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ExcludeTurns { get; init; }

    [JsonPropertyName("forkPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadForkPoint?> ForkPoint { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity?> Identity { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("runtimeWorkspaceRoots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> RuntimeWorkspaceRoots { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadForkPoint.</summary>
public sealed class ThreadForkPoint : ExtensibleJsonObject
{
    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ItemId { get; init; }

    [JsonPropertyName("position")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Position { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for ThreadForkResult.</summary>
public sealed class ThreadForkResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalClearParams.</summary>
public sealed class ThreadGoalClearParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalClearResult.</summary>
public sealed class ThreadGoalClearResult : ExtensibleJsonObject
{
    [JsonPropertyName("cleared")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Cleared { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalClearedNotification.</summary>
public sealed class ThreadGoalClearedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalGetParams.</summary>
public sealed class ThreadGoalGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalGetResult.</summary>
public sealed class ThreadGoalGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoalWire?> Goal { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalSetParams.</summary>
public sealed class ThreadGoalSetParams : ExtensibleJsonObject
{
    [JsonPropertyName("objective")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Objective { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("tokenBudget")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TokenBudget { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalSetResult.</summary>
public sealed class ThreadGoalSetResult : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoalWire> Goal { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalUpdatedNotification.</summary>
public sealed class ThreadGoalUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoalWire?> Goal { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

/// <summary>Executable wire contract for ThreadGoalWire.</summary>
public sealed class ThreadGoalWire : ExtensibleJsonObject
{
    [JsonPropertyName("createdAt")]
    [JsonSafeInteger]
    public required long CreatedAt { get; init; }

    [JsonPropertyName("objective")]
    public required string Objective { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("timeUsedSeconds")]
    [JsonSafeInteger]
    public required long TimeUsedSeconds { get; init; }

    [JsonPropertyName("tokenBudget")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TokenBudget { get; init; }

    [JsonPropertyName("tokensUsed")]
    [JsonSafeInteger]
    public required long TokensUsed { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonSafeInteger]
    public required long UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for ThreadMaintenanceInterruptParams.</summary>
public sealed class ThreadMaintenanceInterruptParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadMcpPolicy.</summary>
public sealed class ThreadMcpPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("servers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Servers { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadNamePolicy?> Tools { get; init; }

}

/// <summary>Executable wire contract for ThreadMemoryConsolidateStartParams.</summary>
public sealed class ThreadMemoryConsolidateStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadMemoryConsolidateStartResponse.</summary>
public sealed class ThreadMemoryConsolidateStartResponse : ExtensibleJsonObject
{
    [JsonPropertyName("historyWritten")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HistoryWritten { get; init; }

    [JsonPropertyName("memoryWritten")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> MemoryWritten { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Outcome { get; init; }

}

/// <summary>Executable wire contract for ThreadModeSetParams.</summary>
public sealed class ThreadModeSetParams : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadNamePolicy.</summary>
public sealed class ThreadNamePolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

/// <summary>Executable wire contract for ThreadPauseParams.</summary>
public sealed class ThreadPauseParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadPluginPolicy.</summary>
public sealed class ThreadPluginPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

/// <summary>Executable wire contract for ThreadQueueUpdatedNotification.</summary>
public sealed class ThreadQueueUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadRenameParams.</summary>
public sealed class ThreadRenameParams : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadRenamedNotification.</summary>
public sealed class ThreadRenamedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadRollbackParams.</summary>
public sealed class ThreadRollbackParams : ExtensibleJsonObject
{
    [JsonPropertyName("numTurns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> NumTurns { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadRollbackResponse.</summary>
public sealed class ThreadRollbackResponse : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

}

/// <summary>Executable wire contract for ThreadRuntimeChangedParams.</summary>
public sealed class ThreadRuntimeChangedParams : ExtensibleJsonObject
{
    [JsonPropertyName("runtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadRuntimeState> Runtime { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadRuntimeState.</summary>
public sealed class ThreadRuntimeState : ExtensibleJsonObject
{
    [JsonPropertyName("busy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Busy { get; init; }

    [JsonPropertyName("maintenanceKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MaintenanceKind { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

    [JsonPropertyName("waitingOnApproval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnApproval { get; init; }

    [JsonPropertyName("waitingOnInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnInput { get; init; }

    [JsonPropertyName("waitingOnPlanConfirmation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WaitingOnPlanConfirmation { get; init; }

}

/// <summary>Executable wire contract for ThreadSkillsPolicy.</summary>
public sealed class ThreadSkillsPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("allowManage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> AllowManage { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

    [JsonPropertyName("preload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Preload { get; init; }

}

/// <summary>Executable wire contract for ThreadSpawnEdge.</summary>
public sealed class ThreadSpawnEdge : ExtensibleJsonObject
{
    [JsonPropertyName("agentNickname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentNickname { get; init; }

    [JsonPropertyName("agentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentPath { get; init; }

    [JsonPropertyName("agentRole")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentRole { get; init; }

    [JsonPropertyName("childThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChildThreadId { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("depth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Depth { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

    [JsonPropertyName("parentTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ParentTurnId { get; init; }

    [JsonPropertyName("profileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProfileName { get; init; }

    [JsonPropertyName("runtimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RuntimeType { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("supportsClose")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsClose { get; init; }

    [JsonPropertyName("supportsFollowupTask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsFollowupTask { get; init; }

    [JsonPropertyName("supportsResume")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsResume { get; init; }

    [JsonPropertyName("supportsSendInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsSendInput { get; init; }

    [JsonPropertyName("supportsSendMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsSendMessage { get; init; }

    [JsonPropertyName("taskName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TaskName { get; init; }

    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> UpdatedAt { get; init; }

}

/// <summary>Executable wire contract for ThreadStatusChangedNotification.</summary>
public sealed class ThreadStatusChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("newStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> NewStatus { get; init; }

    [JsonPropertyName("previousStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PreviousStatus { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadSubscribeParams.</summary>
public sealed class ThreadSubscribeParams : ExtensibleJsonObject
{
    [JsonPropertyName("replayRecent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ReplayRecent { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadTeamsPolicy.</summary>
public sealed class ThreadTeamsPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("reservedTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReservedTools { get; init; }

}

/// <summary>Executable wire contract for ThreadToolPolicy.</summary>
public sealed class ThreadToolPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("agentControl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AgentControl { get; init; }

    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("allowedAgentControlTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> AllowedAgentControlTools { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

/// <summary>Executable wire contract for ThreadUnarchiveParams.</summary>
public sealed class ThreadUnarchiveParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadUnsubscribeParams.</summary>
public sealed class ThreadUnsubscribeParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadWorktreeDirtyHandoffInfo.</summary>
public sealed class ThreadWorktreeDirtyHandoffInfo : ExtensibleJsonObject
{
    [JsonPropertyName("copiedFileCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CopiedFileCount { get; init; }

    [JsonPropertyName("deletedFileCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> DeletedFileCount { get; init; }

    [JsonPropertyName("requested")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Requested { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

}

/// <summary>Executable wire contract for ThreadWorktreeHandoffParams.</summary>
public sealed class ThreadWorktreeHandoffParams : ExtensibleJsonObject
{
    [JsonPropertyName("baseRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BaseRef { get; init; }

    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BranchName { get; init; }

    [JsonPropertyName("copyDirtyChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> CopyDirtyChanges { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for ThreadWorktreeHandoffResponse.</summary>
public sealed class ThreadWorktreeHandoffResponse : ExtensibleJsonObject
{
    [JsonPropertyName("dirtyHandoff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeDirtyHandoffInfo?> DirtyHandoff { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo?> Worktree { get; init; }

}

/// <summary>Executable wire contract for ThreadWorktreeInfo.</summary>
public sealed class ThreadWorktreeInfo : ExtensibleJsonObject
{
    [JsonPropertyName("baseHead")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BaseHead { get; init; }

    [JsonPropertyName("baseRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BaseRef { get; init; }

    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BranchName { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("dirtyHandoff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeDirtyHandoffInfo?> DirtyHandoff { get; init; }

    [JsonPropertyName("head")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Head { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("ownerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OwnerId { get; init; }

    [JsonPropertyName("ownerKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OwnerKind { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("sourceThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourceThreadId { get; init; }

    [JsonPropertyName("sourceWorkspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourceWorkspacePath { get; init; }

    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> WorkspacePath { get; init; }

}

/// <summary>Executable wire contract for ThreadWorktreeStatus.</summary>
public sealed class ThreadWorktreeStatus : ExtensibleJsonObject
{
    [JsonPropertyName("aheadCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> AheadCount { get; init; }

    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> BranchName { get; init; }

    [JsonPropertyName("exists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Exists { get; init; }

    [JsonPropertyName("hasCommitsAheadOfBase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasCommitsAheadOfBase { get; init; }

    [JsonPropertyName("hasUncommittedChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasUncommittedChanges { get; init; }

    [JsonPropertyName("head")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Head { get; init; }

    [JsonPropertyName("isGitWorktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsGitWorktree { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

/// <summary>Executable wire contract for TokenUsageInfo.</summary>
public sealed class TokenUsageInfo : ExtensibleJsonObject
{
    [JsonPropertyName("cacheHitRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> CacheHitRate { get; init; }

    [JsonPropertyName("cacheWriteInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CacheWriteInputTokens { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("freshInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> FreshInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("llmCallCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> LlmCallCount { get; init; }

    [JsonPropertyName("nonCachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> NonCachedInputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("reasoningOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> ReasoningOutputTokens { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

}

/// <summary>Executable wire contract for ToolInfoWire.</summary>
public sealed class ToolInfoWire : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Icon { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("planMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PlanMode { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

/// <summary>Executable wire contract for ToolListParams.</summary>
public sealed class ToolListParams : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Mode { get; init; }

}

/// <summary>Executable wire contract for ToolListResult.</summary>
public sealed class ToolListResult : ExtensibleJsonObject
{
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ToolInfoWire>> Tools { get; init; }

}

/// <summary>Executable wire contract for TurnQueueRemoveParams.</summary>
public sealed class TurnQueueRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TurnQueueRemoveResponse.</summary>
public sealed class TurnQueueRemoveResponse : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

/// <summary>Executable wire contract for TurnQueueReorderParams.</summary>
public sealed class TurnQueueReorderParams : ExtensibleJsonObject
{
    [JsonPropertyName("orderedQueuedInputIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> OrderedQueuedInputIds { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TurnQueueReorderResponse.</summary>
public sealed class TurnQueueReorderResponse : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

/// <summary>Executable wire contract for TurnQueueUpdateParams.</summary>
public sealed class TurnQueueUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("expectedTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ExpectedTurnId { get; init; }

    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for TurnQueueUpdateResult.</summary>
public sealed class TurnQueueUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

/// <summary>Executable wire contract for UiResourceContent.</summary>
public sealed class UiResourceContent : ExtensibleJsonObject
{
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MimeType { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Text { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

/// <summary>Executable wire contract for UiResourceReadParams.</summary>
public sealed class UiResourceReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Namespace { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

/// <summary>Executable wire contract for UiResourceReadResult.</summary>
public sealed class UiResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UiResourceContent>> Contents { get; init; }

}

/// <summary>Executable wire contract for UsageDeltaNotification.</summary>
public sealed class UsageDeltaNotification : ExtensibleJsonObject
{
    [JsonPropertyName("cacheWriteInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CacheWriteInputTokens { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("contextInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> ContextInputTokens { get; init; }

    [JsonPropertyName("contextUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ContextUsageSnapshot?> ContextUsage { get; init; }

    [JsonPropertyName("freshInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> FreshInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("llmCallDelta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> LlmCallDelta { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("reasoningOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> ReasoningOutputTokens { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("totalInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TotalInputTokens { get; init; }

    [JsonPropertyName("totalOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TotalOutputTokens { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

    [JsonPropertyName("turnInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TurnInputTokens { get; init; }

    [JsonPropertyName("turnLlmCalls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TurnLlmCalls { get; init; }

    [JsonPropertyName("turnOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long?> TurnOutputTokens { get; init; }

}

/// <summary>Executable wire contract for UsageSummaryResult.</summary>
public sealed class UsageSummaryResult : ExtensibleJsonObject
{
    [JsonPropertyName("avgToolDurationMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> AvgToolDurationMs { get; init; }

    [JsonPropertyName("cacheHitRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> CacheHitRate { get; init; }

    [JsonPropertyName("maxToolDurationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> MaxToolDurationMs { get; init; }

    [JsonPropertyName("sessionCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> SessionCount { get; init; }

    [JsonPropertyName("totalCacheWriteInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalCacheWriteInputTokens { get; init; }

    [JsonPropertyName("totalCachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalCachedInputTokens { get; init; }

    [JsonPropertyName("totalContextCompactions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalContextCompactions { get; init; }

    [JsonPropertyName("totalErrors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalErrors { get; init; }

    [JsonPropertyName("totalFreshInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalFreshInputTokens { get; init; }

    [JsonPropertyName("totalInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalInputTokens { get; init; }

    [JsonPropertyName("totalNonCachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalNonCachedInputTokens { get; init; }

    [JsonPropertyName("totalOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalOutputTokens { get; init; }

    [JsonPropertyName("totalReasoningOutputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalReasoningOutputTokens { get; init; }

    [JsonPropertyName("totalRequests")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalRequests { get; init; }

    [JsonPropertyName("totalResponses")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalResponses { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

    [JsonPropertyName("totalToolCalls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TotalToolCalls { get; init; }

    [JsonPropertyName("totalToolDurationMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalToolDurationMs { get; init; }

}

/// <summary>Executable wire contract for UsageTimeseriesDay.</summary>
public sealed class UsageTimeseriesDay : ExtensibleJsonObject
{
    [JsonPropertyName("date")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Date { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("sessionCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> SessionCount { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

}

/// <summary>Executable wire contract for UsageTimeseriesParams.</summary>
public sealed class UsageTimeseriesParams : ExtensibleJsonObject
{
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> From { get; init; }

    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> To { get; init; }

    [JsonPropertyName("tzOffsetMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TzOffsetMinutes { get; init; }

}

/// <summary>Executable wire contract for UsageTimeseriesResult.</summary>
public sealed class UsageTimeseriesResult : ExtensibleJsonObject
{
    [JsonPropertyName("days")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageTimeseriesDay>> Days { get; init; }

    [JsonPropertyName("longestTaskMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> LongestTaskMs { get; init; }

    [JsonPropertyName("tzOffsetMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TzOffsetMinutes { get; init; }

}

/// <summary>Executable wire contract for WelcomeSuggestionItem.</summary>
public sealed class WelcomeSuggestionItem : ExtensibleJsonObject
{
    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Reason { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Title { get; init; }

}

/// <summary>Executable wire contract for WelcomeSuggestionsParams.</summary>
public sealed class WelcomeSuggestionsParams : ExtensibleJsonObject
{
    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity> Identity { get; init; }

    [JsonPropertyName("maxItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxItems { get; init; }

}

/// <summary>Executable wire contract for WelcomeSuggestionsResult.</summary>
public sealed class WelcomeSuggestionsResult : ExtensibleJsonObject
{
    [JsonPropertyName("fingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Fingerprint { get; init; }

    [JsonPropertyName("generatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> GeneratedAt { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<WelcomeSuggestionItem>> Items { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

/// <summary>Executable wire contract for WorkspaceCommitMessageSuggestParams.</summary>
public sealed class WorkspaceCommitMessageSuggestParams : ExtensibleJsonObject
{
    [JsonPropertyName("maxDiffChars")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> MaxDiffChars { get; init; }

    [JsonPropertyName("paths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Paths { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for WorkspaceCommitMessageSuggestResult.</summary>
public sealed class WorkspaceCommitMessageSuggestResult : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

}

/// <summary>Executable wire contract for WorkspaceConfigChangedParams.</summary>
public sealed class WorkspaceConfigChangedParams : ExtensibleJsonObject
{
    [JsonPropertyName("changedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ChangedAt { get; init; }

    [JsonPropertyName("regions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Regions { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

/// <summary>Executable wire contract for WorkspaceConfigSchemaParams.</summary>
public sealed class WorkspaceConfigSchemaParams : ExtensibleJsonObject
{
}

/// <summary>Executable wire contract for WorkspaceConfigSchemaResult.</summary>
public sealed class WorkspaceConfigSchemaResult : ExtensibleJsonObject
{
    [JsonPropertyName("sections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ConfigSchemaSection>> Sections { get; init; }

}

/// <summary>Executable wire contract for WorkspaceConfigUpdateParams.</summary>
public sealed class WorkspaceConfigUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("defaultApprovalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultApprovalPolicy { get; init; }

    [JsonPropertyName("dreamsAutoApply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DreamsAutoApply { get; init; }

    [JsonPropertyName("dreamsEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DreamsEnabled { get; init; }

    [JsonPropertyName("dreamsInterval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DreamsInterval { get; init; }

    [JsonPropertyName("dreamsThreadLookbackCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DreamsThreadLookbackCount { get; init; }

    [JsonPropertyName("memoryAutoConsolidateEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> MemoryAutoConsolidateEnabled { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

    [JsonPropertyName("skillsSelfLearningEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SkillsSelfLearningEnabled { get; init; }

    [JsonPropertyName("toolsLspEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ToolsLspEnabled { get; init; }

    [JsonPropertyName("welcomeSuggestionsEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> WelcomeSuggestionsEnabled { get; init; }

}

/// <summary>Executable wire contract for WorkspaceConfigUpdateResult.</summary>
public sealed class WorkspaceConfigUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("defaultApprovalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DefaultApprovalPolicy { get; init; }

    [JsonPropertyName("dreamsAutoApply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DreamsAutoApply { get; init; }

    [JsonPropertyName("dreamsEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> DreamsEnabled { get; init; }

    [JsonPropertyName("dreamsInterval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DreamsInterval { get; init; }

    [JsonPropertyName("dreamsThreadLookbackCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> DreamsThreadLookbackCount { get; init; }

    [JsonPropertyName("memoryAutoConsolidateEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> MemoryAutoConsolidateEnabled { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

    [JsonPropertyName("skillsSelfLearningEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SkillsSelfLearningEnabled { get; init; }

    [JsonPropertyName("toolsLspEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ToolsLspEnabled { get; init; }

    [JsonPropertyName("welcomeSuggestionsEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> WelcomeSuggestionsEnabled { get; init; }

}

/// <summary>Executable wire contract for WorktreeCreateAndForkParams.</summary>
public sealed class WorktreeCreateAndForkParams : ExtensibleJsonObject
{
    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>?> AdditionalContext { get; init; }

    [JsonPropertyName("baseRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BaseRef { get; init; }

    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BranchName { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> Config { get; init; }

    [JsonPropertyName("copyDirtyChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> CopyDirtyChanges { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<JsonElement>?> DynamicTools { get; init; }

    [JsonPropertyName("excludeTurns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ExcludeTurns { get; init; }

    [JsonPropertyName("forkPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadForkPoint?> ForkPoint { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity?> Identity { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("sourceThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourceThreadId { get; init; }

}

/// <summary>Executable wire contract for WorktreeCreateAndForkResult.</summary>
public sealed class WorktreeCreateAndForkResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

/// <summary>Executable wire contract for WorktreeCreateAndStartParams.</summary>
public sealed class WorktreeCreateAndStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("additionalContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, RuntimeAdditionalContextEntry>?> AdditionalContext { get; init; }

    [JsonPropertyName("baseRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BaseRef { get; init; }

    [JsonPropertyName("branchName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> BranchName { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> Config { get; init; }

    [JsonPropertyName("copyDirtyChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> CopyDirtyChanges { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("dynamicTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<JsonElement>?> DynamicTools { get; init; }

    [JsonPropertyName("historyMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> HistoryMode { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity> Identity { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

}

/// <summary>Executable wire contract for WorktreeCreateAndStartResult.</summary>
public sealed class WorktreeCreateAndStartResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

/// <summary>Executable wire contract for WorktreeListParams.</summary>
public sealed class WorktreeListParams : ExtensibleJsonObject
{
    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity?> Identity { get; init; }

}

/// <summary>Executable wire contract for WorktreeListResult.</summary>
public sealed class WorktreeListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ThreadWorktreeStatus>> Data { get; init; }

}

/// <summary>Executable wire contract for WorktreeStatusParams.</summary>
public sealed class WorktreeStatusParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

/// <summary>Executable wire contract for WorktreeStatusResult.</summary>
public sealed class WorktreeStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeStatus> Status { get; init; }

}
