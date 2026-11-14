using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

internal sealed partial class CoreTestableSessionService
{
    public Task<ThreadHistoryBootstrap> ReadThreadHistoryBootstrapAsync(string threadId, CancellationToken ct = default) =>
        _store.ReadThreadHistoryBootstrapAsync(threadId, ct);

    public Task<ThreadTimelinePage> ListThreadTimelineAsync(
        string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct = default) =>
        _store.ListThreadTimelineAsync(threadId, cursor, limit, ct);
}
