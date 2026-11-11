using DotCraft.Mcp;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Task RefreshMcpRuntimesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var runtime in _runtimeRegistry.Values)
        {
            if (runtime.Thread.Status != ThreadStatus.Active || runtime.Commands.IsClosed)
                continue;
            runtime.MarkToolSnapshotDirty();
            runtime.McpRefresh.Request(
                ct => RefreshThreadMcpRuntimeAsync(runtime, ct),
                exception => Logger?.LogWarning(exception, "Refreshing MCP runtime for thread {ThreadId} failed.", runtime.Thread.Id));
        }
        return Task.CompletedTask;
    }

    private async Task RefreshThreadMcpRuntimeAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        McpClientManager? manager;
        using (await AcquireThreadAgentLockAsync(runtime.Thread.Id, ct).ConfigureAwait(false))
        {
            if (!_runtimeRegistry.IsCurrent(runtime.Thread.Id, runtime)
                || runtime.Thread.Status != ThreadStatus.Active
                || IsPendingPermanentDeletion(runtime.Thread.Id))
                return;
            manager = await ReconcileMcpRuntimeAsync(
                runtime.Thread, runtime, runtime.Thread.Configuration ?? new ThreadConfiguration(), ct).ConfigureAwait(false);
        }

        if (manager is not null)
            await manager.WaitForStartupCompletionAsync(ct).ConfigureAwait(false);

        using (await AcquireThreadAgentLockAsync(runtime.Thread.Id, ct).ConfigureAwait(false))
        {
            if (_runtimeRegistry.IsCurrent(runtime.Thread.Id, runtime) && runtime.Thread.Status == ThreadStatus.Active)
                ClearThreadAgentCaches(runtime.Thread.Id);
        }
    }
}
