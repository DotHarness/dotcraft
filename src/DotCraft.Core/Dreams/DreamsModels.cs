using System.Text.Json.Serialization;
using DotCraft.Protocol;

namespace DotCraft.Dreams;

public static class DreamsRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

public static class DreamsReviewStatuses
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Discarded = "discarded";
    public const string Archived = "archived";
}

public sealed record DreamsThreadInput(
    string ThreadId,
    string? DisplayName,
    string OriginChannel,
    ThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    int CompletedTurnCount);

public sealed record DreamsTopicFileInput(
    string Path,
    long SizeBytes,
    DateTimeOffset LastModifiedAt,
    string Preview);

public sealed record DreamsRunInput(
    string ExplicitMemory,
    string ExistingDream,
    IReadOnlyList<DreamsTopicFileInput> TopicFiles,
    IReadOnlyList<DreamsThreadInput> Threads,
    int CompletedTurnCount,
    bool HasMemoryHistory = false,
    string? AdditionalInstructions = null)
{
    public bool HasEvidence =>
        !string.IsNullOrWhiteSpace(ExplicitMemory)
        || !string.IsNullOrWhiteSpace(ExistingDream)
        || HasMemoryHistory
        || TopicFiles.Count > 0
        || Threads.Count > 0;
}

public sealed record DreamsRunRequest(
    IReadOnlyList<string>? ThreadIds = null,
    int? ThreadLookbackCount = null,
    string? Instructions = null,
    string? Model = null);

public sealed record DreamStoreDescriptor(
    string StoreId,
    string DirectoryPath,
    string IndexPath,
    string TopicDirectoryPath);

public sealed record DreamsRunWorkspace(
    string RunId,
    string OutputStoreId,
    string OutputStorePath,
    string InputPath,
    string ManifestPath,
    string? ActiveStorePath);

public sealed class DreamTopicFileWrite
{
    public string Path { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class DreamEvidenceReference
{
    public string SourceId { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public sealed record DreamsRunDiagnostics(
    int CandidateThreadCount,
    IReadOnlyList<string> EvidenceThreadIds,
    int EvidenceSearchCount,
    int EvidenceReadCount);

public sealed record DreamsGenerationResult(
    bool Succeeded,
    string? DreamMarkdown,
    string? HistoryEntry,
    string? Message,
    string? ThreadId = null,
    string? TurnId = null,
    IReadOnlyList<DreamTopicFileWrite>? TopicFiles = null,
    IReadOnlyList<string>? DeletedTopicPaths = null,
    IReadOnlyList<DreamEvidenceReference>? Evidence = null,
    DreamsRunDiagnostics? Diagnostics = null,
    TokenUsageInfo? Usage = null,
    string? OutputStoreId = null,
    IReadOnlyList<string>? TurnIds = null,
    string? ErrorType = null)
{
    public static DreamsGenerationResult Success(
        string dreamMarkdown,
        string? historyEntry,
        string? threadId = null,
        string? turnId = null,
        IReadOnlyList<DreamTopicFileWrite>? topicFiles = null,
        IReadOnlyList<string>? deletedTopicPaths = null,
        IReadOnlyList<DreamEvidenceReference>? evidence = null,
        DreamsRunDiagnostics? diagnostics = null,
        TokenUsageInfo? usage = null,
        string? outputStoreId = null,
        IReadOnlyList<string>? turnIds = null) =>
        new(true, dreamMarkdown, historyEntry, null, threadId, turnId, topicFiles, deletedTopicPaths, evidence, diagnostics, usage, outputStoreId, turnIds);

    public static DreamsGenerationResult Failed(
        string message,
        string? threadId = null,
        string? turnId = null,
        DreamsRunDiagnostics? diagnostics = null,
        TokenUsageInfo? usage = null,
        IReadOnlyList<string>? turnIds = null,
        string? errorType = null,
        string? outputStoreId = null) =>
        new(false, null, null, message, threadId, turnId, null, null, null, diagnostics, usage, outputStoreId, turnIds, errorType);
}

public sealed record DreamsRunSessionBinding(string ThreadId, string? TurnId);

public sealed class DreamsRunState
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = DreamsRunStatuses.Skipped;

    public DateTimeOffset StartedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAt { get; set; }

    public int ProcessedThreadCount { get; set; }

    public int CandidateThreadCount { get; set; }

    public bool DreamWritten { get; set; }

    public bool HistoryWritten { get; set; }

    public int TopicFilesWritten { get; set; }

    public int TopicFilesDeleted { get; set; }

    public int EvidenceSearchCount { get; set; }

    public int EvidenceReadCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputStoreId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewStatus { get; set; }

    public bool AutoApplied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; set; }

    public int CompletedTurnWatermark { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    public List<string> TurnIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Trigger { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenUsageInfo? Usage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputManifestPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    public List<string> ProcessedThreadIds { get; set; } = [];

    public List<string> EvidenceThreadIds { get; set; } = [];

    public List<string> WrittenPaths { get; set; } = [];
}
