namespace DotCraft.Sessions;

public sealed partial class SessionPersistenceService
{
    public Task<ThreadHistoryBootstrap> ReadThreadHistoryBootstrapAsync(string threadId, CancellationToken ct = default) =>
        threadStore.ReadThreadHistoryBootstrapAsync(threadId, ct);

    public Task<ThreadTimelinePage> ListThreadTimelineAsync(
        string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct = default) =>
        threadStore.ListThreadTimelineAsync(threadId, cursor, limit, ct);
}
