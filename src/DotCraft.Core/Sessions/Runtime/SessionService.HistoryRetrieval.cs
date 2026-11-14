namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Task<ThreadHistoryBootstrap> ReadThreadHistoryBootstrapAsync(string threadId, CancellationToken ct = default) =>
        ThreadAccess.ReadThreadHistoryBootstrapAsync(threadId, ct);

    public Task<ThreadTimelinePage> ListThreadTimelineAsync(
        string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct = default) =>
        ThreadAccess.ListThreadTimelineAsync(threadId, cursor, limit, ct);

    public Task<ThreadMessageSearchPage> SearchThreadMessageOccurrencesAsync(
        string threadId,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        CancellationToken ct = default) =>
        ThreadAccess.SearchThreadMessageOccurrencesAsync(threadId, searchTerm, cursor, limit, ct);

    public Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadCallItemsAsync(
        string threadId, string turnId, IReadOnlyList<string> callIds, CancellationToken ct = default) =>
        ThreadAccess.ReadThreadCallItemsAsync(threadId, turnId, callIds, ct);

    public Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadItemsAsync(
        string threadId,
        string turnId,
        IReadOnlyList<string> itemIds,
        CancellationToken ct = default) =>
        ThreadAccess.ReadThreadItemsAsync(threadId, turnId, itemIds, ct);
}
