namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Task<ThreadMessageSearchPage> SearchThreadMessageOccurrencesAsync(
        string threadId,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        CancellationToken ct = default) =>
        ThreadAccess.SearchThreadMessageOccurrencesAsync(threadId, searchTerm, cursor, limit, ct);

    public Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadItemsAsync(
        string threadId,
        string turnId,
        IReadOnlyList<string> itemIds,
        CancellationToken ct = default) =>
        ThreadAccess.ReadThreadItemsAsync(threadId, turnId, itemIds, ct);
}
