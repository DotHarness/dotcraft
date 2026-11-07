namespace DotCraft.Sessions;

public sealed record ThreadObservationItemSelection
{
    public IReadOnlySet<string>? ItemIds { get; init; }
    public string? AfterItemId { get; init; }
    public int? Limit { get; init; }
    public ThreadHistorySortDirection Direction { get; init; } = ThreadHistorySortDirection.Ascending;
    public bool IncludeToolExecutions { get; init; } = true;
}

/// <summary>Owns a captured current view and the future events after its publication boundary.</summary>
public sealed class ThreadObservation : IDisposable
{
    private Action? _release;
    private CancellationTokenRegistration _cancellation;
    internal Func<SessionThread, string?, ThreadObservationView, IReadOnlyList<DotCraft.Tools.ToolName>,
        Func<ThreadObservationSnapshot, Task>, CancellationToken, ThreadObservationItemSelection?, Task> QueueRefresh { get; set; } = null!;
    internal Func<ThreadObservationView, IReadOnlyList<DotCraft.Tools.ToolName>,
        Func<ThreadObservationSnapshot, Task>, CancellationToken, ThreadObservationItemSelection?, Task> Refresh { get; set; } = null!;

    /// <summary>Writes a refreshed current view after preceding feed events and before subsequent events.</summary>
    public Task RefreshAsync(
        ThreadObservationView view,
        IReadOnlyList<DotCraft.Tools.ToolName> summaryTools,
        Func<ThreadObservationSnapshot, Task> respond,
        CancellationToken ct = default,
        ThreadObservationItemSelection? selection = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
        return Refresh(view, summaryTools, respond, ct, selection).WaitAsync(ct);
    }

    internal ThreadObservation(
        ThreadObservationSnapshot snapshot,
        IAsyncEnumerable<SessionEvent> events,
        Action release)
    {
        Snapshot = snapshot;
        Events = events;
        _release = release;
    }

    public ThreadObservationSnapshot Snapshot { get; }

    public IAsyncEnumerable<SessionEvent> Events { get; }

    internal void RegisterCancellation(CancellationToken cancellationToken)
    {
        var registration = cancellationToken.UnsafeRegister(
            static state => ((ThreadObservation)state!).Dispose(), this);
        _cancellation = registration;
        if (Volatile.Read(ref _release) is null)
            registration.Dispose();
    }

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        if (release is null) return;
        release();
        _cancellation.Dispose();
    }
}
