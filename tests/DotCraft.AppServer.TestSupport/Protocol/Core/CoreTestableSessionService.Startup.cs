using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

internal sealed partial class CoreTestableSessionService : ISubAgentStartupLifecycleService
{
    public Func<string, CancellationToken, Task>? PersistPreparationHandler { get; set; }
    public Func<ThreadSpawnEdge, CancellationToken, Task>? SpawnEdgeHandler { get; set; }
    public Func<string, CancellationToken, Task>? SyntheticStartHandler { get; set; }
    public Func<string, CancellationToken, IAsyncEnumerable<SessionEvent>>? SubmitEventsHandler { get; set; }

    async Task ISubAgentStartupLifecycleService.PersistPreparedSubAgentAsync(string parentThreadId, string childThreadId, CancellationToken ct)
    {
        var child = await GetOrLoadAsync(childThreadId, ct);
        if (child.Source.SubAgent?.ParentThreadId != parentThreadId) throw new InvalidOperationException();
        if (PersistPreparationHandler != null) await PersistPreparationHandler(childThreadId, ct);
        await _store.SaveThreadAsync(child, ct);
    }

    async Task ISubAgentStartupLifecycleService.DiscardFailedSubAgentAsync(string parentThreadId, string childThreadId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_cache.TryGetValue(childThreadId, out var child)) return;
        if (child.Source.SubAgent?.ParentThreadId != parentThreadId)
            throw new InvalidOperationException("Startup cleanup requires the owning parent.");
        await _store.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, ThreadSpawnEdgeStatus.Closed, ct);
        _cache.Remove(childThreadId);
        _store.DeleteThread(childThreadId);
        ThreadDeletedForBroadcast?.Invoke(childThreadId);
    }
}
