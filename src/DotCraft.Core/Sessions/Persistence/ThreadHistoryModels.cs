namespace DotCraft.Sessions;

/// <summary>
/// Sort order for persisted Thread history pages.
/// </summary>
public enum ThreadHistorySortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Rollout position used by Session Core history queries.
/// Protocol adapters own the opaque wire encoding around this value.
/// </summary>
public readonly record struct ThreadHistoryCursor(long RolloutOrdinal, bool Inclusive = false);

/// <summary>
/// One bounded page from the persisted history projection.
/// </summary>
public sealed record ThreadHistoryPage<T>(
    IReadOnlyList<T> Data,
    ThreadHistoryCursor? NextCursor,
    ThreadHistoryCursor? BackwardsCursor = null);

/// <summary>
/// A projected Item together with its owning Turn identity.
/// </summary>
public sealed record ThreadHistoryItem(string TurnId, SessionItem Item);

/// <summary>
/// Provider-neutral Thread header and runtime reconstructed from persisted domain history.
/// </summary>
public sealed record ThreadHistorySnapshot(
    SessionThread Thread,
    ThreadSummaryRuntime PersistedRuntime);

/// <summary>
/// History-wide totals over a Thread's visible Turns.
/// </summary>
public sealed record ThreadHistoryAggregates(int TurnCount, TokenUsageInfo TokenUsage, int ToolCallCount);

/// <summary>
/// Raised when a consistent paged-history projection cannot be queried or repaired.
/// </summary>
public sealed class ThreadHistoryUnavailableException : Exception
{
    public ThreadHistoryUnavailableException(string message)
        : base(message)
    {
    }

    public ThreadHistoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
