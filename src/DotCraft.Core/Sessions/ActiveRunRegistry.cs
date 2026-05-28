using System.Collections.Concurrent;

namespace DotCraft.Sessions;

/// <summary>
/// Thread-safe registry that tracks in-flight agent runs keyed by session ID.
/// Allows external callers (e.g. a /stop command handler) to cancel a running session.
/// </summary>
public sealed class ActiveRunRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a <see cref="CancellationTokenSource"/> for an in-flight agent run.
    /// If a previous entry exists for the same session it is silently replaced.
    /// </summary>
    public void Register(string sessionId, CancellationTokenSource cts)
        => _active[sessionId] = cts;

    /// <summary>
    /// Removes the registration for the given session without cancelling.
    /// Should be called in a <c>finally</c> block after the run completes.
    /// </summary>
    public void Unregister(string sessionId)
        => _active.TryRemove(sessionId, out _);

    /// <summary>
    /// Cancels and removes the active run for the given session.
    /// </summary>
    /// <returns><c>true</c> if a run was found and cancelled; <c>false</c> if no run was active.</returns>
    public bool TryCancelAndRemove(string sessionId)
    {
        if (!_active.TryRemove(sessionId, out var cts))
            return false;

        cts.Cancel();
        return true;
    }
}
