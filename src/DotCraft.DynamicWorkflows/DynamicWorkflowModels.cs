using System.Text.Json.Nodes;

namespace DotCraft.DynamicWorkflows;

public static class DynamicWorkflowStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
}

public sealed record DynamicWorkflowLimits
{
    public int MaxScriptBytes { get; init; } = 256 * 1024;
    public int MaxArgsBytes { get; init; } = 1024 * 1024;
    public long MaxJintMemoryBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxStatements { get; init; } = 1_000_000;
    public int MaxRecursionDepth { get; init; } = 128;
    public long MaxWorkerRssBytes { get; init; } = 256L * 1024 * 1024;
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromHours(6);
    public int MaxFrameBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxResultBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxLogEntryBytes { get; init; } = 64 * 1024;
    public int MaxLogBytes { get; init; } = 1024 * 1024;
    public int MaxStderrBytes { get; init; } = 1024 * 1024;
    public int MaxAgentCalls { get; init; } = 1000;
    public int MaxConcurrency { get; init; } = Math.Min(16, Math.Max(1, Environment.ProcessorCount - 2));

    internal void Validate()
    {
        if (MaxScriptBytes <= 0 || MaxArgsBytes <= 0 || MaxJintMemoryBytes <= 0
            || MaxStatements <= 0 || MaxRecursionDepth <= 0 || MaxWorkerRssBytes <= 0
            || RunTimeout <= TimeSpan.Zero || MaxFrameBytes <= 0 || MaxResultBytes <= 0
            || MaxLogEntryBytes <= 0 || MaxLogBytes <= 0 || MaxStderrBytes <= 0
            || MaxAgentCalls <= 0 || MaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(DynamicWorkflowLimits), "Workflow limits must be positive.");
        if (MaxLogEntryBytes > MaxLogBytes)
            throw new ArgumentException("The per-entry log limit cannot exceed the cumulative log limit.", nameof(DynamicWorkflowLimits));
    }
}

public sealed record DynamicWorkflowStartRequest
{
    public required string ParentThreadId { get; init; }
    public required string ParentTurnId { get; init; }
    public required string Script { get; init; }
    public JsonNode? Args { get; init; }
    public long? TokenBudget { get; init; }
    public DynamicWorkflowLimits? LimitsOverride { get; init; }
    internal string? ResumedFromRunId { get; init; }
    internal string? Initiator { get; init; }
    internal IReadOnlyList<DynamicWorkflowReplayCall> ReplayCalls { get; init; } = [];
}

internal sealed record DynamicWorkflowReplayCall(
    string Fingerprint,
    JsonNode? Result,
    bool Completed,
    string? Phase = null,
    string? Label = null,
    string? ChildThreadId = null,
    long InputTokens = 0,
    long OutputTokens = 0);

public sealed record DynamicWorkflowRun
{
    public int SchemaVersion { get; init; } = 1;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> DeclaredPhases { get; init; } = [];
    public required string ParentThreadId { get; init; }
    public required string ParentTurnId { get; init; }
    public required string ScriptPath { get; init; }
    public required string ScriptHash { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public JsonNode? Args { get; init; }
    public JsonNode? Result { get; init; }
    public string? Error { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long? TokenBudget { get; init; }
    public DynamicWorkflowLimits Limits { get; init; } = new();
    public string? NotificationStatus { get; init; }
    public string? NotificationInputId { get; init; }
    public string? ResumedFromRunId { get; init; }
}

public sealed record DynamicWorkflowMetadata(
    string Name,
    string Description,
    string? WhenToUse,
    IReadOnlyList<string> Phases);

public sealed record ParsedDynamicWorkflow(
    DynamicWorkflowMetadata Metadata,
    string ExecutableSource,
    string SourceHash);

public interface IDynamicWorkflowService
{
    Task<DynamicWorkflowRun> StartInlineAsync(DynamicWorkflowStartRequest request, CancellationToken cancellationToken = default);
    Task<DynamicWorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task CancelAsync(string runId, CancellationToken cancellationToken = default);
    Task PauseAsync(string runId, CancellationToken cancellationToken = default);
    Task StopRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<DynamicWorkflowRun> ResumeAsync(string runId, string parentThreadId, string parentTurnId, JsonNode? args = null, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record DynamicWorkflowRunChanged(string ThreadId, string RunId, string Reason);

public sealed record DynamicWorkflowAgentView
{
    public required string OperationId { get; init; }
    public required string Label { get; init; }
    public string? Phase { get; init; }
    public required string Status { get; init; }
    public string? ChildThreadId { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public int ToolCallCount { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public bool Replayed { get; init; }
}

public sealed record DynamicWorkflowPhaseView
{
    public required string Name { get; init; }
    public string? Detail { get; init; }
    public required string Status { get; init; }
    public IReadOnlyList<DynamicWorkflowAgentView> Agents { get; init; } = [];
}

public sealed record DynamicWorkflowTotals
{
    public int AgentCount { get; init; }
    public int QueuedCount { get; init; }
    public int RunningCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public int StoppedCount { get; init; }
    public int ReplayedCount { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public int ToolCallCount { get; init; }
}

public sealed record DynamicWorkflowControls(bool CanPause, bool CanStop, bool CanResume);

public sealed record DynamicWorkflowRunView
{
    public required string RunId { get; init; }
    public required string ThreadId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ResumedFromRunId { get; init; }
    public JsonNode? Result { get; init; }
    public string? Error { get; init; }
    public required DynamicWorkflowTotals Totals { get; init; }
    public required DynamicWorkflowControls Controls { get; init; }
    public IReadOnlyList<DynamicWorkflowPhaseView> Phases { get; init; } = [];
    public IReadOnlyList<DynamicWorkflowAgentView> UnphasedAgents { get; init; } = [];
}
