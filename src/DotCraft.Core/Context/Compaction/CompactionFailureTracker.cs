using System.Collections.Concurrent;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Per-thread circuit breaker for the compaction pipeline. When
/// <see cref="RecordFailure"/> is called more than
/// <see cref="CompactionConfig.MaxConsecutiveFailures"/> times in a row, the
/// pipeline short-circuits future attempts for that thread so a doomed session
/// does not keep hammering the API.
/// </summary>
public sealed class CompactionFailureTracker
{
    private readonly ConcurrentDictionary<string, int> _failureCount = new();
    private readonly int _maxFailures;

    public CompactionFailureTracker(int maxFailures)
    {
        _maxFailures = Math.Max(1, maxFailures);
    }

    /// <summary>
    /// True when the breaker for this thread has tripped.
    /// </summary>
    public bool IsTripped(string threadId) =>
        _failureCount.TryGetValue(threadId, out var count) && count >= _maxFailures;

    /// <summary>
    /// Increments the failure counter for this thread. Returns the new count.
    /// </summary>
    public int RecordFailure(string threadId)
    {
        return _failureCount.AddOrUpdate(threadId, 1, static (_, prev) => prev + 1);
    }

    /// <summary>
    /// Resets the counter after a successful compaction.
    /// </summary>
    public void RecordSuccess(string threadId)
    {
        _failureCount.TryRemove(threadId, out _);
    }

    /// <summary>
    /// Removes all tracking for a thread (e.g. when the thread is deleted or
    /// its history is cleared).
    /// </summary>
    public void Forget(string threadId)
    {
        _failureCount.TryRemove(threadId, out _);
    }
}
