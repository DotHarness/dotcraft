using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService
{
    public Task<ThreadListPage> ListThreadsAsync(ThreadListQuery query, CancellationToken ct = default) =>
        _store.ListThreadsAsync(query, _cache.Values
            .Where(thread => !thread.Ephemeral)
            .Select(thread =>
            {
                var summary = ThreadSummary.FromThread(thread);
                summary.Runtime = GetThreadRuntimeSnapshot(thread);
                return summary;
            }).ToList(), ct);
}
