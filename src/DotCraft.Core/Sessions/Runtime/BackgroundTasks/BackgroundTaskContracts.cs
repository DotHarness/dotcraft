namespace DotCraft.Sessions;

public static class BackgroundTaskKinds
{
    public const string Shell = "shell";
    public const string Agent = "agent";
    public const string Workflow = "workflow";

    public static string Of(string taskId) =>
        taskId.StartsWith("term_", StringComparison.Ordinal) ? Shell
        : taskId.StartsWith("run_", StringComparison.Ordinal) ? Workflow
        : Agent;
}

public static class BackgroundTaskStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Stopped = "stopped";
    public const string Stopping = "stopping";
    public const string WaitingForInput = "waitingForInput";
}

public sealed record BackgroundTask
{
    public required string TaskId { get; init; }

    public required string Kind { get; init; }

    public required string OwnerThreadId { get; init; }

    public string? TurnId { get; init; }

    public string? CallId { get; init; }

    public string Label { get; init; } = string.Empty;

    public required string Status { get; init; }

    public string? KindStatus { get; init; }

    public string? OutputPath { get; init; }

    public string? LastAgentMessage { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public BackgroundTaskLatch? Latch { get; init; }
}

public sealed record BackgroundTaskCaller(SessionThread Thread, string RootThreadId);

/// <summary>
/// Decides once per task run whether its ending is still owed to the model as a notification.
/// </summary>
public sealed class BackgroundTaskLatch
{
    private readonly Lock _sync = new();
    private bool _stopping;
    private bool _notified;

    public bool Notified
    {
        get
        {
            lock (_sync)
                return _notified;
        }
    }

    public string? StopOrigin { get; private set; }

    /// <summary>A stopped task the model did not stop was stopped by the user, since shutdown endings do not notify.</summary>
    public static bool IsUserStop(string status, BackgroundTaskLatch? latch) =>
        status == BackgroundTaskStatus.Stopped && latch?.StopOrigin != BackgroundTaskRegistry.ModelStopOrigin;

    /// <summary>Called by the owning runtime when the task ends; false means the model already has the outcome.</summary>
    public bool TryClaimNotification()
    {
        lock (_sync)
        {
            if (_notified)
                return false;
            _notified = true;
            return !_stopping;
        }
    }

    internal void BeginStop(string origin)
    {
        lock (_sync)
        {
            _stopping = true;
            StopOrigin = origin;
        }
    }

    internal bool EndStop(bool settled)
    {
        lock (_sync)
        {
            _stopping = false;
            if (settled)
                _notified = true;
            return _notified;
        }
    }
}

public interface IBackgroundTaskKind
{
    string Kind { get; }

    /// <summary>Returns the task when <paramref name="caller"/> may control it, otherwise null.</summary>
    Task<BackgroundTask?> FindAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct);

    Task<IReadOnlyList<BackgroundTask>> ListAsync(BackgroundTaskCaller caller, CancellationToken ct);

    /// <summary>Stops the task and returns once it has settled; a task that already ended is left unchanged.</summary>
    Task StopAsync(BackgroundTask task, CancellationToken ct);
}

public sealed record BackgroundTaskEnding
{
    public required string TaskId { get; init; }

    public required string Kind { get; init; }

    public required string OwnerThreadId { get; init; }

    public string? StartTurnId { get; init; }

    public required string Label { get; init; }

    /// <summary><see cref="BackgroundTaskStatus"/> value, or <c>waitingForInput</c> for a stall.</summary>
    public required string Status { get; init; }

    public required string KindStatus { get; init; }

    public required string Summary { get; init; }

    public int? ExitCode { get; init; }

    public string? OutputPath { get; init; }

    public string? Result { get; init; }

    public bool StoppedByUser { get; init; }

    /// <summary>For a stall: whether the process still reads stdin.</summary>
    public bool StdinOpen { get; init; }

    /// <summary>False keeps the notification queued without starting a Turn.</summary>
    public bool MayWake { get; init; } = true;
}

public interface IBackgroundTaskNotifications
{
    /// <summary>Returns the id of the queued notification input, or null when the owner Thread no longer exists.</summary>
    Task<string?> NotifyBackgroundTaskEndedAsync(BackgroundTaskEnding ending, CancellationToken ct = default);
}
