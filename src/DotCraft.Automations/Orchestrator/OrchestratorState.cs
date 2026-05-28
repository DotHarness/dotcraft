using System.Collections.Concurrent;

namespace DotCraft.Automations.Orchestrator;

/// <summary>
/// In-memory orchestrator state (active task IDs, concurrency tracking, and retry scheduling).
/// </summary>
public sealed class OrchestratorState
{
    private readonly ConcurrentDictionary<string, byte> _activeTaskIds = new(StringComparer.Ordinal);
    
    /// <summary>
    /// Tracks retry attempts per task. Key is task key (source::id), value is retry count.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _retryCounts = new(StringComparer.Ordinal);
    
    /// <summary>
    /// Tracks when a task became eligible for retry. Used for backoff calculation.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryScheduledAt = new(StringComparer.Ordinal);

    /// <summary>Returns whether a task ID is currently being processed.</summary>
    public bool IsTaskActive(string taskId) => _activeTaskIds.ContainsKey(taskId);

    /// <summary>
    /// Attempts to mark a task as active. Returns false if the ID is already active.
    /// </summary>
    public bool TryBeginTask(string taskId) => _activeTaskIds.TryAdd(taskId, 0);

    /// <summary>Removes a task from the active set when work completes.</summary>
    public void EndTask(string taskId)
    {
        _activeTaskIds.TryRemove(taskId, out _);
    }

    /// <summary>
    /// Records a failed task for retry. Returns the retry attempt number (1-based).
    /// Returns 0 if max retries exceeded.
    /// </summary>
    public int ScheduleRetry(string taskKey, int maxRetries, DateTimeOffset scheduledAt)
    {
        var currentRetries = _retryCounts.AddOrUpdate(taskKey, 1, (_, count) => count + 1);
        
        if (currentRetries > maxRetries)
        {
            // Max retries exceeded, clear the retry state
            _retryCounts.TryRemove(taskKey, out _);
            _retryScheduledAt.TryRemove(taskKey, out _);
            return 0;
        }
        
        _retryScheduledAt[taskKey] = scheduledAt;
        return currentRetries;
    }

    /// <summary>
    /// Gets the number of retries for a task.
    /// </summary>
    public int GetRetryCount(string taskKey) => _retryCounts.TryGetValue(taskKey, out var count) ? count : 0;

    /// <summary>
    /// Clears retry state for a task (e.g., when it succeeds).
    /// </summary>
    public void ClearRetries(string taskKey)
    {
        _retryCounts.TryRemove(taskKey, out _);
        _retryScheduledAt.TryRemove(taskKey, out _);
    }

    /// <summary>
    /// Checks if a task is eligible for retry based on backoff delay.
    /// </summary>
    public bool IsEligibleForRetry(string taskKey, TimeSpan initialDelay, TimeSpan maxDelay, DateTimeOffset now)
    {
        if (!_retryScheduledAt.TryGetValue(taskKey, out var scheduledAt))
            return true; // No retry scheduled, eligible immediately

        var retryCount = GetRetryCount(taskKey);
        if (retryCount == 0)
            return true;

        // Exponential backoff: delay = min(initialDelay * 2^(retryCount-1), maxDelay)
        var delayMs = Math.Min(
            initialDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1),
            maxDelay.TotalMilliseconds);
        var delay = TimeSpan.FromMilliseconds(delayMs);

        return now >= scheduledAt.Add(delay);
    }

    /// <summary>Number of tasks currently in the active set.</summary>
    public int ActiveTaskCount => _activeTaskIds.Count;
}
