using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public enum ThreadObservationView
{
    Metadata,
    Summary,
    Full
}

public sealed record ThreadPendingRequest
{
    public required string RequestId { get; init; }
    public required string ItemId { get; init; }
    public required ItemType Type { get; init; }
    public bool IsBlocking { get; init; }
}

public sealed record ThreadObservationSnapshot
{
    internal Func<bool>? IsCurrent { get; init; }

    public void EnsureCurrent()
    {
        if (IsCurrent?.Invoke() == false)
            throw new OperationCanceledException("The captured thread view is no longer current.");
    }

    public required string ThreadId { get; init; }
    public string? Mode { get; init; }
    public ThreadConfiguration? Configuration { get; init; }
    public required ThreadSummaryRuntime Runtime { get; init; }
    public SessionTurn? Turn { get; init; }
    public int ItemCount { get; init; }
    public int ToolExecutionCount { get; init; }
    public bool HasMoreItems { get; init; }
    public string? PlanTurnId { get; init; }
    public IReadOnlyDictionary<string, string> ToolArguments { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ToolExecutionProgressPayload> ToolProgress { get; init; } =
        new Dictionary<string, ToolExecutionProgressPayload>();
    public string? Diff { get; init; }
    public ContextUsageSnapshot? ContextUsage { get; init; }
    public DotCraft.Memory.StructuredPlan? Plan { get; init; }
    public IReadOnlyList<QueuedTurnInput> QueuedInputs { get; init; } = [];
    public IReadOnlyList<ThreadPendingRequest> PendingRequests { get; init; } = [];
}
