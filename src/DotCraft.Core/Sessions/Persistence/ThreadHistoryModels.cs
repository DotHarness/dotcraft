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
/// Exclusive rollout position used by Session Core history queries.
/// Protocol adapters own the opaque wire encoding around this value.
/// </summary>
public readonly record struct ThreadHistoryCursor(long ExclusiveRolloutOrdinal);

/// <summary>
/// One bounded page from the persisted history projection.
/// </summary>
public sealed record ThreadHistoryPage<T>(
    IReadOnlyList<T> Data,
    ThreadHistoryCursor? NextCursor);

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
