using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

internal sealed partial class CoreTestableSessionService
{
    public Task<ThreadMessageSearchPage> SearchThreadMessageOccurrencesAsync(
        string threadId,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        CancellationToken ct = default) =>
        _store.SearchThreadMessageOccurrencesAsync(threadId, searchTerm, cursor, limit, ct);

    public Task<IReadOnlyList<ThreadHistoryItem>> ReadThreadItemsAsync(
        string threadId,
        string turnId,
        IReadOnlyList<string> itemIds,
        CancellationToken ct = default) =>
        _store.ReadThreadItemsAsync(threadId, turnId, itemIds, ct);
}
