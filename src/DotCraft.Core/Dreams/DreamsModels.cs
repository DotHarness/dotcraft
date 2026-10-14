using System.Text.Json.Serialization;
using DotCraft.Sessions;

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

/// <summary>A consolidatable memory store; <see cref="Scope"/> is null for the workspace store.</summary>
public sealed record DreamsStore(string? Scope, string Directory);

public sealed record DreamsThreadInput(
    string ThreadId,
    string? DisplayName,
    string OriginChannel,
    ThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt);

public sealed record DreamsRunRequest(
    string? Scope = null,
    IReadOnlyList<string>? ThreadIds = null,
    string? Instructions = null,
    string? Model = null);

public sealed record DreamsRunInput(
    string RunId,
    string Trigger,
    string WorkspacePath,
    string BaselineDirectory,
    string CandidateDirectory,
    string Manifest,
    IReadOnlyList<DreamsThreadInput> Threads,
    string? Instructions);

public sealed record DreamsRunDiagnostics(
    IReadOnlyList<string> EvidenceThreadIds,
    int EvidenceSearchCount,
    int EvidenceReadCount);

public sealed record DreamsGenerationResult(
    bool Succeeded,
    string? Summary,
    string? Message = null,
    string? ErrorType = null,
    string? ThreadId = null,
    string? TurnId = null,
    DreamsRunDiagnostics? Diagnostics = null,
    TokenUsageInfo? Usage = null);

public sealed record DreamsRunSessionBinding(string ThreadId, string? TurnId);

public sealed class DreamsRunChanges
{
    public List<string> Added { get; set; } = [];

    public List<string> Updated { get; set; } = [];

    public List<string> Deleted { get; set; } = [];

    public bool IndexChanged { get; set; }

    [JsonIgnore]
    public bool Any => IndexChanged || Added.Count > 0 || Updated.Count > 0 || Deleted.Count > 0;
}

/// <summary>One file that differs between a run's baseline and candidate; an added file has no baseline and a deleted file no candidate.</summary>
public sealed record DreamsFileChange(string Name, string Change, string? Baseline, string? Candidate);

public sealed record DreamsRunReview(DreamsRunState Run, IReadOnlyList<DreamsFileChange> Files);

/// <summary>Raised when a store no longer matches the files a review action expects.</summary>
public sealed class DreamsStoreChangedException(string message) : InvalidOperationException(message);

public sealed class DreamsRunState
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    public string Status { get; set; } = DreamsRunStatuses.Skipped;

    public string Trigger { get; set; } = DreamsConstants.TriggerManual;

    public DateTimeOffset StartedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }

    public int CandidateThreadCount { get; set; }

    public List<string> EvidenceThreadIds { get; set; } = [];

    public int EvidenceSearchCount { get; set; }

    public int EvidenceReadCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DreamsRunChanges? Changes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewStatus { get; set; }

    public bool AutoApplied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TokenUsageInfo? Usage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputManifestPath { get; set; }

    [JsonIgnore]
    public bool IsRunning => Status == DreamsRunStatuses.Running && !EndedAt.HasValue;
}
