namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    public Task<ThreadHistoryBootstrap> ReadThreadHistoryBootstrapAsync(string threadId, CancellationToken ct = default) =>
        ReadHistoryAsync(threadId, "bootstrap",
            (path, repair) => _historyProjectionStore.ReadBootstrapAsync(threadId, path, repair, ct), static _ => 1, ct);

    public Task<ThreadTimelinePage> ListThreadTimelineAsync(
        string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct = default)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "Timeline page limit must be between 1 and 500.");
        return ReadHistoryAsync(threadId, "timeline",
            (path, repair) => _historyProjectionStore.ListTimelineAsync(threadId, path, cursor, limit, repair, ct),
            static page => page.Data.Count, ct);
    }
}
