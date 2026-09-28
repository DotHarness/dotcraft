using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class AgentImportCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
}

public sealed class ImportCandidate : ExtensibleJsonObject
{
    [JsonPropertyName("category")] public string Category { get; init; } = "sessions";
    [JsonPropertyName("scope")] public string Scope { get; init; } = "workspace";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; init; } = "";
    [JsonPropertyName("targetPath")] public string TargetPath { get; init; } = "";
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
    [JsonPropertyName("fallbackText")] public string FallbackText { get; init; } = "";
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceId")] public required string SourceId { get; init; }
    [JsonPropertyName("sourcePath")] public required string SourcePath { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("cwd")] public required string Cwd { get; init; }
    [JsonPropertyName("updatedAt")] public required DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("turnCount")] public required int TurnCount { get; init; }
    [JsonPropertyName("state")] public required string State { get; init; }
}

public sealed class ImportSourceDetection : ExtensibleJsonObject
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("available")] public required bool Available { get; init; }
    [JsonPropertyName("items")] public required IReadOnlyList<ImportCandidate> Items { get; init; }
    [JsonPropertyName("importableCount")] public required int ImportableCount { get; init; }
}

public sealed class ImportDetectParams : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<string>> Sources { get; init; }
}

public sealed class ImportDetectResult : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] public required IReadOnlyList<ImportSourceDetection> Sources { get; init; }
}

public sealed class ImportRunParams : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
    [JsonPropertyName("items")] public required IReadOnlyList<ImportItemReference> Items { get; init; }
    [JsonPropertyName("selection")] public required ImportSelection Selection { get; init; }
    [JsonPropertyName("offered")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<ImportSelection> Offered { get; init; }
}

public sealed class ImportItemReference : ExtensibleJsonObject
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceId")] public required string SourceId { get; init; }
    [JsonPropertyName("fingerprint")] public required string Fingerprint { get; init; }
}

public sealed class ImportSelection : ExtensibleJsonObject
{
    [JsonPropertyName("all")] public bool All { get; init; }
    [JsonPropertyName("user")] public IReadOnlyList<string> User { get; init; } = [];
    [JsonPropertyName("workspace")] public IReadOnlyList<string> Workspace { get; init; } = [];
    [JsonPropertyName("sessions")] public bool Sessions { get; init; }
}

public sealed class ImportHistoryResult : ExtensibleJsonObject
{
    [JsonPropertyName("imports")] public required IReadOnlyList<ImportCompletedNotification> Imports { get; init; }
    [JsonPropertyName("attention")] public required IReadOnlyList<ImportOutcome> Attention { get; init; }
}

public sealed class ImportRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
}

public sealed class ImportSettings : ExtensibleJsonObject
{
    [JsonPropertyName("selection")] public ImportSelection Selection { get; init; } = new();
    [JsonPropertyName("hasImported")] public bool HasImported { get; init; }
    [JsonPropertyName("syncEnabled")] public required bool SyncEnabled { get; init; }
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
    [JsonPropertyName("syncIntervalMinutes")] public required int SyncIntervalMinutes { get; init; }
    [JsonPropertyName("lastSyncAt")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<DateTimeOffset> LastSyncAt { get; init; }
    [JsonPropertyName("workspaceOptOut")] public required bool WorkspaceOptOut { get; init; }
}

public sealed class ImportSettingsResult : ExtensibleJsonObject
{
    [JsonPropertyName("settings")] public required ImportSettings Settings { get; init; }
}

public sealed class ImportSettingsSetParams : ExtensibleJsonObject
{
    [JsonPropertyName("selection")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<ImportSelection> Selection { get; init; }
    [JsonPropertyName("syncEnabled")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<bool> SyncEnabled { get; init; }
    [JsonPropertyName("sources")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<string>> Sources { get; init; }
}

public sealed class ImportProgressNotification : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("completed")] public required int Completed { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
}

public sealed class ImportOutcome : ExtensibleJsonObject
{
    [JsonPropertyName("category")] public string Category { get; init; } = "sessions";
    [JsonPropertyName("scope")] public string Scope { get; init; } = "workspace";
    [JsonPropertyName("targetPath")] public string TargetPath { get; init; } = "";
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceId")] public required string SourceId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("threadId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ThreadId { get; init; }
    [JsonPropertyName("title")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Title { get; init; }
    [JsonPropertyName("errorCode")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ErrorCode { get; init; }
    [JsonPropertyName("error")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Error { get; init; }
}

public sealed class ImportCompletedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
    [JsonPropertyName("trigger")] public required string Trigger { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("completedAt")] public required DateTimeOffset CompletedAt { get; init; }
    [JsonPropertyName("outcomes")] public required IReadOnlyList<ImportOutcome> Outcomes { get; init; }
}
