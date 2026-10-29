#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;
public sealed class AgentProfileAudit : ExtensibleJsonObject
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

public sealed class AgentProfileBuilderDraftReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

public sealed class AgentProfileBuilderDraftUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class AgentProfileDiagnostic : ExtensibleJsonObject
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

public sealed class AgentProfileEntry : ExtensibleJsonObject
{
    [JsonPropertyName("compiledConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> CompiledConfig { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileDiagnostic>> Diagnostics { get; init; }

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
    public Optional<AgentProfileProviderPreference?> ProviderPreference { get; init; }

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

public sealed class AgentProfileListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeInvalid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeInvalid { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

public sealed class AgentProfileListResult : ExtensibleJsonObject
{
    [JsonPropertyName("profiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileEntry>> Profiles { get; init; }

}

public sealed class AgentProfileProviderPreference : ExtensibleJsonObject
{

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Model { get; init; }

    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ProviderId { get; init; }

    [JsonPropertyName("reasoning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileReasoningPreference> Reasoning { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Speed { get; init; }

}

public sealed class AgentProfileReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

public sealed class AgentProfileReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntry> Profile { get; init; }

}

public sealed class AgentProfileReasoningPreference : ExtensibleJsonObject
{
    [JsonPropertyName("effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Effort { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

}

public sealed class AgentProfileRefreshThreadParams : ExtensibleJsonObject
{
    [JsonPropertyName("profileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProfileId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class AgentProfileRefreshThreadResult : ExtensibleJsonObject
{
    [JsonPropertyName("audit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileAudit> Audit { get; init; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration> Config { get; init; }

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntry> Profile { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("wasStale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> WasStale { get; init; }

}

public sealed class AgentProfileRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

}

public sealed class AgentProfileRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

public sealed class AgentProfileSummary : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Id { get; init; }

}

public sealed class AgentProfileUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("previousName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PreviousName { get; init; }

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

public sealed class AgentProfileUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileEntry> Profile { get; init; }

}

public sealed class AgentProfileValidateParams : ExtensibleJsonObject
{
    [JsonPropertyName("rawContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RawContent { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Source { get; init; }

}

public sealed class AgentProfileValidateResult : ExtensibleJsonObject
{
    [JsonPropertyName("compiledConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration?> CompiledConfig { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<AgentProfileDiagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("lockedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> LockedFields { get; init; }

    [JsonPropertyName("providerPreference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileProviderPreference?> ProviderPreference { get; init; }

    [JsonPropertyName("restrictedFields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> RestrictedFields { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<AgentProfileSummary> Summary { get; init; }

    [JsonPropertyName("valid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Valid { get; init; }

}

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

public sealed class ChannelInfo : ExtensibleJsonObject
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Category { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class ChannelListResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ChannelInfo>> Channels { get; init; }

}

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

    [JsonPropertyName("runtimeState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RuntimeState { get; init; }

    [JsonPropertyName("failureCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FailureCode { get; init; }

}

public sealed class ChannelStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ChannelStatusInfo>> Channels { get; init; }

}

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

public sealed class CommandChoice : ExtensibleJsonObject
{
    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Label { get; init; }

}

public sealed class CommandExecuteResult : ExtensibleJsonObject
{
    [JsonPropertyName("choices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<CommandChoice>?> Choices { get; init; }

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

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread?> Thread { get; init; }

}

public sealed class CommandInfo : ExtensibleJsonObject
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

public sealed class CommandListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeBuiltins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeBuiltins { get; init; }

    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Language { get; init; }

}

public sealed class CommandListResult : ExtensibleJsonObject
{
    [JsonPropertyName("commands")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<CommandInfo>> Commands { get; init; }

}

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

public sealed class ContextUsageBreakdown : ExtensibleJsonObject
{
    [JsonPropertyName("categories")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ContextUsageCategory>> Categories { get; init; }

    [JsonPropertyName("contextUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ContextUsageSnapshot> ContextUsage { get; init; }

}

public sealed class ContextUsageCategory : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ContextUsageItem>> Items { get; init; }

    [JsonPropertyName("onDemand")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> OnDemand { get; init; }

    [JsonPropertyName("tokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Tokens { get; init; }

}

public sealed class ContextUsageItem : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("tokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Tokens { get; init; }

}

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

public sealed class DreamsCreateParams : ExtensibleJsonObject
{
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Instructions { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Model { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Scope { get; init; }

    [JsonPropertyName("threadIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> ThreadIds { get; init; }

}

public sealed class DreamsFileChange : ExtensibleJsonObject
{
    [JsonPropertyName("baseline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Baseline { get; init; }

    [JsonPropertyName("candidate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Candidate { get; init; }

    [JsonPropertyName("change")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Change { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class DreamsGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<DreamsFileChange>> Files { get; init; }

    [JsonPropertyName("run")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunState?> Run { get; init; }

}

public sealed class DreamsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeArchived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IncludeArchived { get; init; }

}

public sealed class DreamsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("runs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<DreamsRunState>> Runs { get; init; }

}

public sealed class DreamsRunChanges : ExtensibleJsonObject
{
    [JsonPropertyName("added")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Added { get; init; }

    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Deleted { get; init; }

    [JsonPropertyName("indexChanged")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IndexChanged { get; init; }

    [JsonPropertyName("updated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Updated { get; init; }

}

public sealed class DreamsRunIdParams : ExtensibleJsonObject
{
    [JsonPropertyName("runId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RunId { get; init; }

}

public sealed class DreamsRunParams : ExtensibleJsonObject
{
}

public sealed class DreamsRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("run")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunState?> Run { get; init; }

}

public sealed class DreamsRunState : ExtensibleJsonObject
{
    [JsonPropertyName("autoApplied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AutoApplied { get; init; }

    [JsonPropertyName("candidateThreadCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> CandidateThreadCount { get; init; }

    [JsonPropertyName("changes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunChanges?> Changes { get; init; }

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

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("inputManifestPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> InputManifestPath { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Message { get; init; }

    [JsonPropertyName("reviewStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReviewStatus { get; init; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Scope { get; init; }

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> StartedAt { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Summary { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("trigger")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Trigger { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TokenUsageInfo?> Usage { get; init; }

}

public sealed class DreamsStatusParams : ExtensibleJsonObject
{
}

public sealed class DreamsStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("autoApply")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AutoApply { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("interval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Interval { get; init; }

    [JsonPropertyName("lastRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DreamsRunState?> LastRun { get; init; }

    [JsonPropertyName("minSessionsSinceLastRun")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> MinSessionsSinceLastRun { get; init; }

    [JsonPropertyName("nextRunAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> NextRunAt { get; init; }

    [JsonPropertyName("running")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Running { get; init; }

}

public sealed class HookErrorInfo : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

}

public sealed class HookMetadata : ExtensibleJsonObject
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

public sealed class HooksListParams : ExtensibleJsonObject
{
}

public sealed class HooksListResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfo>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadata>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

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

public sealed class HooksSetStateResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfo>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadata>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

public sealed class HooksTrustPluginParams : ExtensibleJsonObject
{
    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> PluginId { get; init; }

}

public sealed class HooksTrustPluginResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookErrorInfo>> Errors { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<HookMetadata>> Hooks { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Warnings { get; init; }

}

public sealed class VisualizationViewCloseParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class VisualizationViewCloseResult : ExtensibleJsonObject
{
    [JsonPropertyName("closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Closed { get; init; }

}

public sealed class VisualizationViewMessageParams : ExtensibleJsonObject
{
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Context { get; init; }

    [JsonPropertyName("prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Prompt { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class VisualizationViewMessageResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

}

public sealed class VisualizationViewOpenParams : ExtensibleJsonObject
{
    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ItemId { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

public sealed class VisualizationViewOpenResult : ExtensibleJsonObject
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

    [JsonPropertyName("widgetState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<VisualizationWidgetState?> WidgetState { get; init; }

}

public sealed class VisualizationViewWidgetStateUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("modelContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> ModelContent { get; init; }

    [JsonPropertyName("privateContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> PrivateContent { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class VisualizationWidgetState : ExtensibleJsonObject
{
    [JsonPropertyName("modelContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> ModelContent { get; init; }

    [JsonPropertyName("privateContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> PrivateContent { get; init; }

}

public sealed class VisualizationWidgetStateUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")] public required string ThreadId { get; init; }

    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("widgetState")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public VisualizationWidgetState? WidgetState { get; init; }
}

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

public sealed class MarketplaceAddResult : ExtensibleJsonObject
{
    [JsonPropertyName("alreadyAdded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> AlreadyAdded { get; init; }

    [JsonPropertyName("marketplace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<MarketplaceInfo> Marketplace { get; init; }

}

public sealed class MarketplaceFailure : ExtensibleJsonObject
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

public sealed class MarketplaceInfo : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("lastUpdated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> LastUpdated { get; init; }

    [JsonPropertyName("marketplacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MarketplacePath { get; init; }

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

public sealed class MarketplaceRefreshParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Name { get; init; }

}

public sealed class MarketplaceRefreshResult : ExtensibleJsonObject
{
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceFailure>> Errors { get; init; }

    [JsonPropertyName("marketplaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceInfo>> Marketplaces { get; init; }

}

public sealed class MarketplaceRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class MarketplaceRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("removedRoot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RemovedRoot { get; init; }

}

public sealed class McpAppMessageContent : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Text { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Type { get; init; }

}

public sealed class McpAppResourceCsp : ExtensibleJsonObject
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

public sealed class McpAppResourceMetadata : ExtensibleJsonObject
{
    [JsonPropertyName("csp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResourceCsp> Csp { get; init; }

    [JsonPropertyName("prefersBorder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> PrefersBorder { get; init; }

    [JsonPropertyName("requestedDomain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> RequestedDomain { get; init; }

}

public sealed class McpAppResource : ExtensibleJsonObject
{
    [JsonPropertyName("html")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Html { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MimeType { get; init; }

    [JsonPropertyName("ui")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResourceMetadata> Ui { get; init; }

    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

}

public sealed class McpAppToolResult : ExtensibleJsonObject
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

public sealed class McpAppTool : ExtensibleJsonObject
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

public sealed class McpAppViewCloseParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewCloseResult : ExtensibleJsonObject
{
    [JsonPropertyName("closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Closed { get; init; }

}

public sealed class McpAppViewMessageParams : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppMessageContent> Content { get; init; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Role { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewMessageResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

}

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

public sealed class McpAppViewModelContextUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("cleared")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Cleared { get; init; }

}

public sealed class McpAppViewOpenLinkParams : ExtensibleJsonObject
{
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewOpenLinkResult : ExtensibleJsonObject
{
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Url { get; init; }

}

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

public sealed class McpAppViewOpenResult : ExtensibleJsonObject
{
    [JsonPropertyName("resource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppResource> Resource { get; init; }

    [JsonPropertyName("toolInput")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> ToolInput { get; init; }

    [JsonPropertyName("toolResult")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpAppToolResult> ToolResult { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewResourceReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Uri { get; init; }

    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Contents { get; init; }

}

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

public sealed class McpAppViewToolsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("viewHandle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ViewHandle { get; init; }

}

public sealed class McpAppViewToolsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpAppTool>> Tools { get; init; }

}

public sealed class McpGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class McpGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfig> Server { get; init; }

}

public sealed class McpListResult : ExtensibleJsonObject
{
    [JsonPropertyName("servers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpServerConfig>> Servers { get; init; }

}

public sealed class McpRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("scope")] public required string Scope { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class McpRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

public sealed class McpRuntimeTool : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("inputSchema")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> InputSchema { get; init; }

    [JsonPropertyName("name")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("outputSchema")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> OutputSchema { get; init; }

}

public sealed class ThreadMcpServerConfig : ExtensibleJsonObject
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

    [JsonPropertyName("oauth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpOAuthConfig?> OAuth { get; init; }

}

public sealed class McpServerConfig : ExtensibleJsonObject
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
    public Optional<McpServerOrigin?> Origin { get; init; }

    [JsonPropertyName("readOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ReadOnly { get; init; }

    [JsonPropertyName("startupTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> StartupTimeoutSec { get; init; }

    [JsonPropertyName("toolApprovalMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ToolApprovalMode { get; init; }

    [JsonPropertyName("toolTimeoutSec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double?> ToolTimeoutSec { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Url { get; init; }

    [JsonPropertyName("oauth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpOAuthConfig?> OAuth { get; init; }

}

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

public sealed class McpServerElicitationResponse : ExtensibleJsonObject
{
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Action { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>?> Content { get; init; }

}

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

public sealed class McpServerOAuthLoginResult : ExtensibleJsonObject
{
    [JsonPropertyName("authorizationUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthorizationUrl { get; init; }

}

public sealed class McpServerOrigin : ExtensibleJsonObject
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

public sealed class McpServerReloadResult : ExtensibleJsonObject
{
}

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

public sealed class McpServerResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement?> Contents { get; init; }

}

public sealed class McpServerRuntimeStatus : ExtensibleJsonObject
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
    public Optional<McpServerOrigin?> Origin { get; init; }

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
    public Optional<IReadOnlyDictionary<string, McpRuntimeTool>> Tools { get; init; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Transport { get; init; }

}

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

public sealed class McpServerStatusListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<McpServerRuntimeStatus>> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> NextCursor { get; init; }

}

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

public sealed class McpTestParams : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfig> Server { get; init; }

}

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

public sealed class McpUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("scope")] public required string Scope { get; init; }

    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfig> Server { get; init; }

}

public sealed class McpUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<McpServerConfig> Server { get; init; }

}

public sealed class MemoryResetResult : ExtensibleJsonObject
{
}

public sealed class ModelCatalogItem : ExtensibleJsonObject
{
    [JsonPropertyName("contextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ContextWindow { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("isDefault")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsDefault { get; init; }

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

public sealed class ModelListParams : ExtensibleJsonObject
{
    [JsonPropertyName("providerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ProviderId { get; init; }

}

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

public sealed class ModelPreference : ExtensibleJsonObject
{

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

public sealed class ModelSpeedCapability : ExtensibleJsonObject
{
    [JsonPropertyName("defaultMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultMode { get; init; }

    [JsonPropertyName("supportedModes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> SupportedModes { get; init; }

}

public sealed class PerforceConnection : ExtensibleJsonObject
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

public sealed class PlanTodo : ExtensibleJsonObject
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
    public Optional<IReadOnlyList<PlanTodo>> Todos { get; init; }

}

public sealed class PluginAppInfo : ExtensibleJsonObject
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
    public Optional<PluginAppNativeApplication?> NativeApplication { get; init; }

    [JsonPropertyName("releasePage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReleasePage { get; init; }

}

public sealed class PluginAppNativeApplication : ExtensibleJsonObject
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

public sealed class PluginDesktopInfo : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("entry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Entry { get; init; }

    [JsonPropertyName("revision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Revision { get; init; }

    [JsonPropertyName("styles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Styles { get; init; }

}

public sealed class PluginDependencyInfo : ExtensibleJsonObject
{
    [JsonPropertyName("availability")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Availability { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("observedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ObservedVersion { get; init; }

    [JsonPropertyName("requiredVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RequiredVersion { get; init; }
}

public sealed class PluginDiagnostic : ExtensibleJsonObject
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

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, JsonElement>> Parameters { get; init; }

    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Severity { get; init; }

}

public sealed class PluginDotnetInfo : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("entryAssembly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EntryAssembly { get; init; }

    [JsonPropertyName("entryType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EntryType { get; init; }

    [JsonPropertyName("exportedApiAssemblies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> ExportedApiAssemblies { get; init; }

    [JsonPropertyName("minHostVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MinHostVersion { get; init; }
}

public sealed class PluginDotnetRuntimeInfo : ExtensibleJsonObject
{
    [JsonPropertyName("state")]
    public Optional<string> State { get; init; }

    [JsonPropertyName("generationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> GenerationId { get; init; }

    [JsonPropertyName("blockers")]
    public Optional<IReadOnlyList<PluginRuntimeBlocker>> Blockers { get; init; }

    [JsonPropertyName("leakedGenerations")]
    [JsonSafeInteger]
    public Optional<long> LeakedGenerations { get; init; }

    [JsonPropertyName("restartRecommended")]
    public Optional<bool> RestartRecommended { get; init; }

    [JsonPropertyName("trustStatus")]
    public Optional<string> TrustStatus { get; init; }
}

public sealed class PluginRuntimeBlocker : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("message")]
    public Optional<string> Message { get; init; }

    [JsonPropertyName("parameters")]
    public Optional<IReadOnlyDictionary<string, JsonElement>> Parameters { get; init; }
}

public sealed class PluginFunctionInfo : ExtensibleJsonObject
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

public sealed class PluginHookInfo : ExtensibleJsonObject
{
    [JsonPropertyName("eventName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EventName { get; init; }

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

}

public sealed class PluginInfo : ExtensibleJsonObject
{
    [JsonPropertyName("apps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginAppInfo>> Apps { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("desktop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginDesktopInfo?> Desktop { get; init; }

    [JsonPropertyName("dependencies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDependencyInfo>> Dependencies { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDiagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("dotnet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginDotnetInfo?> Dotnet { get; init; }

    [JsonPropertyName("dotnetRuntime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginDotnetRuntimeInfo?> DotnetRuntime { get; init; }

    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("functions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginFunctionInfo>> Functions { get; init; }

    [JsonPropertyName("hooks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginHookInfo>> Hooks { get; init; }

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
    public Optional<PluginInterface?> Interface { get; init; }

    [JsonPropertyName("lspServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginLspServerInfo>> LspServers { get; init; }

    [JsonPropertyName("marketplaceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MarketplaceName { get; init; }

    [JsonPropertyName("mcpServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginMcpServerInfo>> McpServers { get; init; }

    [JsonPropertyName("removable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removable { get; init; }

    [JsonPropertyName("rootPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RootPath { get; init; }

    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginSkillInfo>> Skills { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Version { get; init; }

    [JsonPropertyName("workflows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginWorkflowInfo>> Workflows { get; init; }

}

public sealed class PluginWorkflowInfo : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("command")]
    public Optional<string> Command { get; init; }

    [JsonPropertyName("description")]
    public Optional<string> Description { get; init; }

    [JsonPropertyName("whenToUse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WhenToUse { get; init; }
}

public sealed class PluginInstallLocalParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

}

public sealed class PluginInstallParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

public sealed class PluginConfigGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public sealed class PluginConfigField : ExtensibleJsonObject
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("defaultValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> DefaultValue { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Options { get; init; }

    [JsonPropertyName("min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> Min { get; init; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> Max { get; init; }
}

public sealed class PluginConfigSchema : ExtensibleJsonObject
{
    [JsonPropertyName("fields")]
    public required IReadOnlyList<PluginConfigField> Fields { get; init; }
}

public sealed class PluginConfigMutationOperation : ExtensibleJsonObject
{
    [JsonPropertyName("op")]
    public required string Op { get; init; }

    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<JsonElement> Value { get; init; }
}

public sealed class PluginConfigMutateParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("operations")]
    public required IReadOnlyList<PluginConfigMutationOperation> Operations { get; init; }
}

public sealed class PluginConfigSnapshot : ExtensibleJsonObject
{
    [JsonPropertyName("schema")]
    public required PluginConfigSchema Schema { get; init; }

    [JsonPropertyName("personal")]
    public required JsonElement Personal { get; init; }

    [JsonPropertyName("workspace")]
    public required JsonElement Workspace { get; init; }

    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    [JsonPropertyName("writableScopes")]
    public required IReadOnlyList<string> WritableScopes { get; init; }
}

public sealed class PluginInterface : ExtensibleJsonObject
{
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

public sealed class PluginListParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeDisabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> IncludeDisabled { get; init; }

}

public sealed class PluginListResult : ExtensibleJsonObject
{
    [JsonPropertyName("workspacePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> WorkspacePath { get; init; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginDiagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("marketplaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<MarketplaceInfo>> Marketplaces { get; init; }

    [JsonPropertyName("plugins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<PluginInfo>> Plugins { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    public Optional<long> SnapshotRevision { get; init; }

}

public sealed class PluginRuntimeProjection : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public Optional<string> Id { get; init; }

    [JsonPropertyName("installed")]
    public Optional<bool> Installed { get; init; }

    [JsonPropertyName("enabled")]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("dotnetRuntime")]
    public Optional<PluginDotnetRuntimeInfo> DotnetRuntime { get; init; }
}

public sealed class PluginOperationResult : ExtensibleJsonObject
{
    [JsonPropertyName("outcome")]
    public Optional<string> Outcome { get; init; }

    [JsonPropertyName("plugin")]
    public Optional<PluginInfo?> Plugin { get; init; }

    [JsonPropertyName("affectedPlugins")]
    public Optional<IReadOnlyList<PluginRuntimeProjection>> AffectedPlugins { get; init; }

    [JsonPropertyName("diagnostics")]
    public Optional<IReadOnlyList<PluginDiagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    public Optional<long> SnapshotRevision { get; init; }
}

public sealed class PluginSnapshotUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    public Optional<long> SnapshotRevision { get; init; }

    [JsonPropertyName("pluginIds")]
    public Optional<IReadOnlyList<string>> PluginIds { get; init; }
}

public sealed class PluginLspServerInfo : ExtensibleJsonObject
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

public sealed class PluginMcpServerInfo : ExtensibleJsonObject
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

public sealed class PluginRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

public sealed class PluginSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

}

public sealed class PluginSetTrustedParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("trusted")]
    public required bool Trusted { get; init; }
}

public sealed class PluginSkillInfo : ExtensibleJsonObject
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

    [JsonPropertyName("iconSmallDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> IconSmallDataUrl { get; init; }

    [JsonPropertyName("iconLargeDataUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> IconLargeDataUrl { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("shortDescription")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ShortDescription { get; init; }

}

public sealed class PluginSkillReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed class PluginSkillReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

public sealed class PluginViewParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Id { get; init; }

}

public sealed class PluginViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PluginInfo> Plugin { get; init; }

    [JsonPropertyName("snapshotRevision")]
    [JsonSafeInteger]
    public Optional<long> SnapshotRevision { get; init; }

}

public sealed class ProfileInsightsParams : ExtensibleJsonObject
{
    [JsonPropertyName("topSkills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TopSkills { get; init; }

}

public sealed class ProfileInsightsResult : ExtensibleJsonObject
{
    [JsonPropertyName("longestTaskMs")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> LongestTaskMs { get; init; }

    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SkillUsage>> Skills { get; init; }

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

public sealed class ProviderCapabilities : ExtensibleJsonObject
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

public sealed partial class ProviderInfo : ExtensibleJsonObject
{
    [JsonPropertyName("apiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ApiKey { get; init; }

    [JsonPropertyName("authMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> AuthMethod { get; init; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ProviderCapabilities> Capabilities { get; init; }



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

    [JsonPropertyName("supportsImageGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SupportsImageGeneration { get; init; }

}

public sealed class ProviderListParams : ExtensibleJsonObject
{
}

public sealed partial class ProviderListResult : ExtensibleJsonObject
{
    [JsonPropertyName("providers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ProviderInfo>> Providers { get; init; }

}

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

public sealed class SkillInfo : ExtensibleJsonObject
{
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

}

public sealed class SkillUsage : ExtensibleJsonObject
{
    [JsonPropertyName("count")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Count { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("pluginId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> PluginId { get; init; }

}

public sealed class SkillsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("skills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SkillInfo>> Skills { get; init; }

}

public sealed class SkillsReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

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

public sealed class SkillsSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SkillsSetEnabledResult : ExtensibleJsonObject
{
    [JsonPropertyName("skill")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SkillInfo> Skill { get; init; }

}

public sealed class SkillsUninstallParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SkillsUninstallResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("removedSourcePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RemovedSourcePath { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("uninstalled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Uninstalled { get; init; }

}

public sealed class SkillsViewParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SkillsViewResult : ExtensibleJsonObject
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Content { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SourceControlCapabilities : ExtensibleJsonObject
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

public sealed class SourceControlChangelistCreateResult : ExtensibleJsonObject
{
    [JsonPropertyName("changelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlChangelistEntry> Changelist { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTarget> Target { get; init; }

}

public sealed class SourceControlChangelistEntry : ExtensibleJsonObject
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

public sealed class SourceControlChangelistListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class SourceControlChangelistListResult : ExtensibleJsonObject
{
    [JsonPropertyName("changelists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SourceControlChangelistEntry>> Changelists { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTarget> Target { get; init; }

}

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

public sealed class SourceControlDiagnosticItem : ExtensibleJsonObject
{
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Code { get; init; }

    [JsonPropertyName("fallbackText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> FallbackText { get; init; }

}

public sealed class SourceControlGetParams : ExtensibleJsonObject
{
}

public sealed class SourceControlSnapshot : ExtensibleJsonObject
{
    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlCapabilities> Capabilities { get; init; }

    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("effectiveProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> EffectiveProvider { get; init; }

    [JsonPropertyName("perforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PerforceConnection?> Perforce { get; init; }

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
    public Optional<PerforceConnection?> Perforce { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

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

public sealed class SourceControlThreadTargetParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class SourceControlThreadTargetResult : ExtensibleJsonObject
{
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTarget> Target { get; init; }

}

public sealed class SourceControlThreadTargetUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SourceControlThreadTarget?> Target { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class SourceControlThreadTarget : ExtensibleJsonObject
{
    [JsonPropertyName("changelist")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Changelist { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

public sealed class SourceControlUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("connectionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ConnectionMode { get; init; }

    [JsonPropertyName("perforce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PerforceConnection?> Perforce { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Provider { get; init; }

}

public sealed class SubAgentBoardChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("rootThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> RootThreadId { get; init; }

}

public sealed class SubAgentBoardChannel : ExtensibleJsonObject
{
    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("createdBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> CreatedBy { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Description { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

    [JsonPropertyName("topicCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> TopicCount { get; init; }

}

public sealed class SubAgentBoardPost : ExtensibleJsonObject
{
    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Author { get; init; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("createdAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> CreatedAt { get; init; }

    [JsonPropertyName("messageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> MessageId { get; init; }

    [JsonPropertyName("notified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Notified { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Text { get; init; }

    [JsonPropertyName("topicId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TopicId { get; init; }

}

public sealed class SubAgentBoardTopic : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChannelName { get; init; }

    [JsonPropertyName("lastActivityAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> LastActivityAt { get; init; }

    [JsonPropertyName("replyCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> ReplyCount { get; init; }

    [JsonPropertyName("rootPost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentBoardPost> RootPost { get; init; }

    [JsonPropertyName("topicId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TopicId { get; init; }

}

public sealed class SubAgentBoardTopicReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("topicId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TopicId { get; init; }

}

public sealed class SubAgentBoardTopicReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("hasMore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasMore { get; init; }

    [JsonPropertyName("posts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentBoardPost>> Posts { get; init; }

    [JsonPropertyName("topic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentBoardTopic?> Topic { get; init; }

}

public sealed class SubAgentBoardTopicsListParams : ExtensibleJsonObject
{
    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChannelName { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cursor { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Limit { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class SubAgentBoardTopicsListResult : ExtensibleJsonObject
{
    [JsonPropertyName("channels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentBoardChannel>> Channels { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentBoardTopic>> Data { get; init; }

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> NextCursor { get; init; }

}

public sealed class SubAgentChild : ExtensibleJsonObject
{
    [JsonPropertyName("edge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadSpawnEdge> Edge { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread?> Thread { get; init; }

}

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

public sealed class SubAgentChildrenListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentChild>> Data { get; init; }

}

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

public sealed class SubAgentGraphChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("childThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ChildThreadId { get; init; }

    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

}

public sealed class SubAgentProfileDiagnostic : ExtensibleJsonObject
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

public sealed class SubAgentProfileEntry : ExtensibleJsonObject
{
    [JsonPropertyName("builtInDefaults")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWrite?> BuiltInDefaults { get; init; }

    [JsonPropertyName("definition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWrite> Definition { get; init; }

    [JsonPropertyName("diagnostic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileDiagnostic> Diagnostic { get; init; }

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

public sealed class SubAgentProfileListResult : ExtensibleJsonObject
{
    [JsonPropertyName("defaultName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DefaultName { get; init; }

    [JsonPropertyName("profiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<SubAgentProfileEntry>> Profiles { get; init; }

    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentSettings> Settings { get; init; }

}

public sealed class SubAgentProfileRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SubAgentProfileRemoveResult : ExtensibleJsonObject
{
    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Removed { get; init; }

}

public sealed class SubAgentProfileSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Enabled { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SubAgentProfileSetEnabledResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileEntry> Profile { get; init; }

}

public sealed class SubAgentProfileUpsertParams : ExtensibleJsonObject
{
    [JsonPropertyName("definition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileWrite> Definition { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Name { get; init; }

}

public sealed class SubAgentProfileUpsertResult : ExtensibleJsonObject
{
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentProfileEntry> Profile { get; init; }

}

public sealed class SubAgentProfileWrite : ExtensibleJsonObject
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

public sealed class SubAgentSettingsUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("externalCliSessionResumeEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ExternalCliSessionResumeEnabled { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

}

public sealed class SubAgentSettingsUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SubAgentSettings> Settings { get; init; }

}

public sealed class SubAgentSettings : ExtensibleJsonObject
{
    [JsonPropertyName("externalCliSessionResumeEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> ExternalCliSessionResumeEnabled { get; init; }

    [JsonPropertyName("providerPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyDictionary<string, ModelPreference>?> ProviderPreferences { get; init; }

}

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

public sealed class SubAgentTargetParams : ExtensibleJsonObject
{
    [JsonPropertyName("parentThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ParentThreadId { get; init; }

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Target { get; init; }

}

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

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ChannelName { get; init; }

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

public sealed class SystemJobResultNotification : ExtensibleJsonObject
{
    [JsonPropertyName("error")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Error { get; init; }

    [JsonPropertyName("jobId")]
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> JobId { get; init; }

    [JsonPropertyName("jobName")]
    [JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> JobName { get; init; }

    [JsonPropertyName("result")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Result { get; init; }

    [JsonPropertyName("source")]
    [JsonPropertyOrder(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }

    [JsonPropertyName("threadId")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("tokenUsage")]
    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SystemJobTokenUsage?> TokenUsage { get; init; }

}

public sealed class SystemJobTokenUsage : ExtensibleJsonObject
{
    [JsonPropertyName("inputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> OutputTokens { get; init; }

}

public sealed class TerminalCleanParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class TerminalCleanResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<BackgroundTerminalSnapshot>> Terminals { get; init; }

}

public sealed class TerminalLifecycleNotification : ExtensibleJsonObject
{
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Delta { get; init; }

    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

public sealed class TerminalListParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ThreadId { get; init; }

}

public sealed class TerminalListResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<BackgroundTerminalSnapshot>> Terminals { get; init; }

}

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

public sealed class TerminalReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

public sealed class TerminalStopParams : ExtensibleJsonObject
{
    [JsonPropertyName("sessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SessionId { get; init; }

}

public sealed class TerminalStopResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

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

public sealed class TerminalWriteResult : ExtensibleJsonObject
{
    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<BackgroundTerminalSnapshot> Terminal { get; init; }

}

public sealed class ThreadArchiveParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadCompactStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

public sealed class ThreadConfigUpdateParams : ExtensibleJsonObject
{
    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadConfiguration> Config { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

    [JsonPropertyName("automationTaskDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> AutomationTaskDirectory { get; init; }

    [JsonPropertyName("customTools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> CustomTools { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Cwd { get; init; }

    [JsonPropertyName("developerInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DeveloperInstructions { get; init; }

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
    public Optional<IReadOnlyList<ThreadMcpServerConfig>?> McpServers { get; init; }

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

public sealed class ThreadContextUsageReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadContextUsageReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("breakdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ContextUsageBreakdown?> Breakdown { get; init; }

}

public sealed class ThreadDeleteParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

    [JsonPropertyName("promptSuggestion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> PromptSuggestion { get; init; }

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

    [JsonPropertyName("sideChat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> SideChat { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

public sealed class ThreadForkResult : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("instructionSources")]
    public required IReadOnlyList<string> InstructionSources { get; init; }

}

public sealed class ThreadGitInfo : ExtensibleJsonObject
{
    [JsonPropertyName("branch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Branch { get; init; }

    [JsonPropertyName("originUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OriginUrl { get; init; }

}

public sealed class ThreadGoalClearParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadGoalClearResult : ExtensibleJsonObject
{
    [JsonPropertyName("cleared")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Cleared { get; init; }

}

public sealed class ThreadGoalClearedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadGoalGetParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadGoalGetResult : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoal?> Goal { get; init; }

}

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

public sealed class ThreadGoalSetResult : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoal> Goal { get; init; }

}

public sealed class ThreadGoalUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadGoal?> Goal { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> TurnId { get; init; }

}

public sealed class ThreadGoal : ExtensibleJsonObject
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

public sealed class ThreadMaintenanceInterruptParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadMcpPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("servers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Servers { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadNamePolicy?> Tools { get; init; }

}

public sealed class ThreadModeSetParams : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Mode { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadNamePolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

public sealed class ThreadPluginPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

public sealed class ThreadQueueUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadRenameParams : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> DisplayName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadRenamedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> DisplayName { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadRollbackParams : ExtensibleJsonObject
{
    [JsonPropertyName("numTurns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> NumTurns { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadRollbackResponse : ExtensibleJsonObject
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

}

public sealed class ThreadRuntimeState : ExtensibleJsonObject
{
    [JsonPropertyName("activeTurnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ActiveTurnId { get; init; }

    [JsonPropertyName("activeTurnStartedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset?> ActiveTurnStartedAt { get; init; }

    [JsonPropertyName("busy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Busy { get; init; }

    [JsonPropertyName("maintenanceKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> MaintenanceKind { get; init; }

    [JsonPropertyName("queuePaused")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> QueuePaused { get; init; }

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

public sealed class ThreadSkillsPolicy : ExtensibleJsonObject
{
    [JsonPropertyName("allow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Allow { get; init; }

    [JsonPropertyName("deny")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>?> Deny { get; init; }

}

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

public sealed class ThreadStatusChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("runtime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadRuntimeState> Runtime { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadSubscribeParams : ExtensibleJsonObject
{
    [JsonPropertyName("replayRecent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool?> ReplayRecent { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadTokenUsage : ExtensibleJsonObject
{
    [JsonPropertyName("last")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TokenUsageBreakdown> Last { get; init; }

    [JsonPropertyName("modelContextWindow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> ModelContextWindow { get; init; }

    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<TokenUsageBreakdown> Total { get; init; }

}

public sealed class ThreadTokenUsageUpdatedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("tokenUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadTokenUsage> TokenUsage { get; init; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

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

public sealed class ThreadUnarchiveParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class ThreadUnsubscribeParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

    [JsonPropertyName("targetStashed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> TargetStashed { get; init; }

}

public sealed class ThreadWorktreeHandoffProgressNotification : ExtensibleJsonObject
{
    [JsonPropertyName("operationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OperationId { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Status { get; init; }

    [JsonPropertyName("step")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Step { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

    [JsonPropertyName("operationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OperationId { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

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

    [JsonPropertyName("environmentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> EnvironmentPath { get; init; }

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
    public Optional<string?> ThreadId { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

public sealed class TokenUsageBreakdown : ExtensibleJsonObject
{
    [JsonPropertyName("cacheWriteInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CacheWriteInputTokens { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

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

public sealed class ToolInfo : ExtensibleJsonObject
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Description { get; init; }

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

public sealed class ToolListParams : ExtensibleJsonObject
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Mode { get; init; }

}

public sealed class ToolListResult : ExtensibleJsonObject
{
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ToolInfo>> Tools { get; init; }

}

public sealed class TurnQueueRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> QueuedInputId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class TurnQueueRemoveResponse : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

public sealed class TurnQueueReorderParams : ExtensibleJsonObject
{
    [JsonPropertyName("orderedQueuedInputIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> OrderedQueuedInputIds { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class TurnQueueReorderResponse : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

public sealed class TurnQueueStartParams : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> QueuedInputId { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class TurnQueueStartResponse : ExtensibleJsonObject
{
    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> TurnId { get; init; }

}

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

public sealed class TurnQueueUpdateResult : ExtensibleJsonObject
{
    [JsonPropertyName("queuedInputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<QueuedTurnInput>> QueuedInputs { get; init; }

}

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

public sealed class UiResourceReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("contents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UiResourceContent>> Contents { get; init; }

}

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

    [JsonPropertyName("requestTiming")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<UsageRequestTiming?> RequestTiming { get; init; }

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

public sealed class UsageRequestTiming : ExtensibleJsonObject
{
    [JsonPropertyName("firstOutputLatencyMs")]
    public required double FirstOutputLatencyMs { get; init; }

    [JsonPropertyName("generationMs")]
    public required double GenerationMs { get; init; }

}

public sealed class UsageHistoryDay : ExtensibleJsonObject
{
    [JsonPropertyName("date")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Date { get; init; }

    [JsonPropertyName("total")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Total { get; init; }

    [JsonPropertyName("values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageHistoryValue>> Values { get; init; }

}

public sealed class UsageHistoryParams : ExtensibleJsonObject
{
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> From { get; init; }

    [JsonPropertyName("groupBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> GroupBy { get; init; }

    [JsonPropertyName("metric")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Metric { get; init; }

    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> To { get; init; }

    [JsonPropertyName("topLimit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TopLimit { get; init; }

    [JsonPropertyName("tzOffsetMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TzOffsetMinutes { get; init; }

}

public sealed class UsageHistoryResult : ExtensibleJsonObject
{
    [JsonPropertyName("days")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageHistoryDay>> Days { get; init; }

    [JsonPropertyName("groupBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> GroupBy { get; init; }

    [JsonPropertyName("series")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageHistorySeries>> Series { get; init; }

    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Unit { get; init; }

}

public sealed class UsageHistorySeries : ExtensibleJsonObject
{
    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

    [JsonPropertyName("total")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Total { get; init; }

}

public sealed class UsageHistoryValue : ExtensibleJsonObject
{
    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Key { get; init; }

    [JsonPropertyName("value")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> Value { get; init; }

}

public sealed class UsageSummaryParams : ExtensibleJsonObject
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

public sealed class UsageThreadGroup : ExtensibleJsonObject
{
    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Model { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("reasoningEffort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ReasoningEffort { get; init; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Speed { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Turns { get; init; }

}

public sealed class UsageThreadParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class UsageThreadResult : ExtensibleJsonObject
{
    [JsonPropertyName("cacheHitRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> CacheHitRate { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageThreadGroup>> Groups { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Turns { get; init; }

}

public sealed class UsageThreadRow : ExtensibleJsonObject
{
    [JsonPropertyName("archived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Archived { get; init; }

    [JsonPropertyName("cacheHitRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<double> CacheHitRate { get; init; }

    [JsonPropertyName("cachedInputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> CachedInputTokens { get; init; }

    [JsonPropertyName("inputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> InputTokens { get; init; }

    [JsonPropertyName("lastActiveAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> LastActiveAt { get; init; }

    [JsonPropertyName("originChannel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> OriginChannel { get; init; }

    [JsonPropertyName("outputTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> OutputTokens { get; init; }

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Title { get; init; }

    [JsonPropertyName("totalTokens")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<long> TotalTokens { get; init; }

    [JsonPropertyName("turns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int> Turns { get; init; }

}

public sealed class UsageThreadsParams : ExtensibleJsonObject
{
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> From { get; init; }

    [JsonPropertyName("limit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> Limit { get; init; }

    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> To { get; init; }

    [JsonPropertyName("tzOffsetMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> TzOffsetMinutes { get; init; }

}

public sealed class UsageThreadsResult : ExtensibleJsonObject
{
    [JsonPropertyName("threads")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<UsageThreadRow>> Threads { get; init; }

}

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

public sealed class WorkspaceCommitMessageSuggestResult : ExtensibleJsonObject
{
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Message { get; init; }

}

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

    [JsonPropertyName("environmentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> EnvironmentPath { get; init; }

    [JsonPropertyName("forkPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadForkPoint?> ForkPoint { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity?> Identity { get; init; }

    [JsonPropertyName("operationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OperationId { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

    [JsonPropertyName("sourceThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SourceThreadId { get; init; }

}

public sealed class WorktreeCreateAndForkResult : ExtensibleJsonObject
{
    [JsonPropertyName("setup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<WorktreeSetupResult> Setup { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

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

    [JsonPropertyName("environmentPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> EnvironmentPath { get; init; }

    [JsonPropertyName("historyMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> HistoryMode { get; init; }

    [JsonPropertyName("identity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionIdentity> Identity { get; init; }

    [JsonPropertyName("operationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> OperationId { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> Path { get; init; }

}

public sealed class WorktreeCreateAndStartResult : ExtensibleJsonObject
{
    [JsonPropertyName("setup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<WorktreeSetupResult> Setup { get; init; }

    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<SessionThread> Thread { get; init; }

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeInfo> Worktree { get; init; }

}

public sealed class WorktreeListParams : ExtensibleJsonObject
{
}

public sealed class WorktreeListResult : ExtensibleJsonObject
{
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ThreadWorktreeStatus>> Data { get; init; }

}

public sealed class WorktreeRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("discardChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> DiscardChanges { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Path { get; init; }

    [JsonPropertyName("skipCleanup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> SkipCleanup { get; init; }

}

public sealed class WorktreeRemoveResult : ExtensibleJsonObject
{
}

public sealed class WorktreeStatusParams : ExtensibleJsonObject
{
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> ThreadId { get; init; }

}

public sealed class WorktreeStatusResult : ExtensibleJsonObject
{
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<ThreadWorktreeStatus> Status { get; init; }

}

public sealed class McpOAuthConfig : ExtensibleJsonObject
{
    [JsonPropertyName("clientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ClientId { get; init; }

    [JsonPropertyName("clientSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ClientSecret { get; init; }

    [JsonPropertyName("clientSecretEnvVar")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> ClientSecretEnvVar { get; init; }

    [JsonPropertyName("callbackUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string?> CallbackUrl { get; init; }

    [JsonPropertyName("callbackPort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<int?> CallbackPort { get; init; }

    [JsonPropertyName("hasClientSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> HasClientSecret { get; init; }

}
