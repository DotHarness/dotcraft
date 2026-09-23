using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class SessionImportCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("version")] public required int Version { get; init; }
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
}

public sealed class ImportSessionCandidate : ExtensibleJsonObject
{
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
    [JsonPropertyName("sessions")] public required IReadOnlyList<ImportSessionCandidate> Sessions { get; init; }
    [JsonPropertyName("importableCount")] public required int ImportableCount { get; init; }
}

public sealed class ImportSessionsDetectParams : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<string>> Sources { get; init; }
}

public sealed class ImportSessionsDetectResult : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] public required IReadOnlyList<ImportSourceDetection> Sources { get; init; }
}

public sealed class ImportSessionsRunParams : ExtensibleJsonObject
{
    [JsonPropertyName("sources")] public required IReadOnlyList<string> Sources { get; init; }
    [JsonPropertyName("sessionIds")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<string>> SessionIds { get; init; }
}

public sealed class ImportSessionsRunResult : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
}

public sealed class ImportSettings : ExtensibleJsonObject
{
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
    [JsonPropertyName("syncEnabled")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<bool> SyncEnabled { get; init; }
    [JsonPropertyName("sources")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<string>> Sources { get; init; }
}

public sealed class ImportSessionsProgressNotification : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("completed")] public required int Completed { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
}

public sealed class ImportSessionOutcome : ExtensibleJsonObject
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceId")] public required string SourceId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("threadId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ThreadId { get; init; }
    [JsonPropertyName("title")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Title { get; init; }
    [JsonPropertyName("errorCode")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> ErrorCode { get; init; }
    [JsonPropertyName("error")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Error { get; init; }
}

public sealed class ImportSessionsCompletedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
    [JsonPropertyName("trigger")] public required string Trigger { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("completedAt")] public required DateTimeOffset CompletedAt { get; init; }
    [JsonPropertyName("outcomes")] public required IReadOnlyList<ImportSessionOutcome> Outcomes { get; init; }
}
