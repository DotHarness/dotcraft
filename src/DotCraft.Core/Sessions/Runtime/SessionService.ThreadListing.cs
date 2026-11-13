namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public async Task<ThreadListPage> ListThreadsAsync(ThreadListQuery query, CancellationToken ct = default)
    {
        var live = _runtimeRegistry.Values
            .Select(runtime => runtime.Thread)
            .Where(thread => !thread.Ephemeral)
            .Select(ThreadSummary.FromThread)
            .ToList();
        var page = await Persistence.ListThreadsAsync(query, live, ct);
        foreach (var summary in page.Items)
        {
            if (_runtimeRegistry.TryGetRuntime(summary.Id, out var runtime))
                summary.Runtime = GetThreadRuntimeSnapshot(runtime.Thread);
        }
        return page;
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity) =>
        (await ListThreadsAsync(new ThreadListQuery
        {
            Identity = identity,
            IncludeArchived = includeArchived,
            CrossChannelOrigins = crossChannelOrigins,
            IncludeSubAgents = includeSubAgents,
            IncludeInternal = true,
            Scope = scope
        }, ct)).Items;
}
