namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal Task<ThreadMessageSearchPage> SearchThreadMessageOccurrencesAsync(
        string threadId,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);
        searchTerm = searchTerm.Trim();
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Search limit must be between 1 and 100.");
        if (cursor is { ItemOrdinal: <= 0 } or { TextOffset: < 0 })
            throw new ArgumentOutOfRangeException(nameof(cursor), "Search cursor positions must be valid.");
        return ReadHistoryAsync(
            threadId,
            "message_occurrences",
            (path, repair) => _historyProjectionStore.SearchMessageOccurrencesAsync(
                threadId, path, searchTerm, cursor, limit, repair, ct),
            static page => page.Data.Count,
            ct);
    }

    internal Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadCallItemsAsync(
        string threadId, string turnId, IReadOnlyList<string> callIds, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(callIds);
        if (callIds.Count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(callIds), "Call reads require between 1 and 100 call IDs.");
        foreach (var callId in callIds) ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        return ReadHistoryAsync(threadId, "call_items",
            (path, repair) => _historyProjectionStore.ReadCallItemsAsync(threadId, path, turnId, callIds, repair, ct),
            static items => items.Count, ct);
    }

    internal Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadItemsAsync(
        string threadId,
        string turnId,
        IReadOnlyList<string> itemIds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(itemIds), "Exact reads require between 1 and 100 Item IDs.");
        foreach (var itemId in itemIds)
            ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        return ReadHistoryAsync(
            threadId,
            "exact_items",
            (path, repair) => _historyProjectionStore.ReadItemsAsync(threadId, path, turnId, itemIds, repair, ct),
            static items => items.Count,
            ct);
    }
}
