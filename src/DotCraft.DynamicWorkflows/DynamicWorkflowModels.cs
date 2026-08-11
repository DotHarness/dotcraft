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
    internal IReadOnlyList<DynamicWorkflowReplayCall> ReplayCalls { get; init; } = [];
}

internal sealed record DynamicWorkflowReplayCall(string Fingerprint, JsonNode? Result, bool Completed);

public sealed record DynamicWorkflowRun
{
    public int SchemaVersion { get; init; } = 1;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Name { get; init; }
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
