using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Workspaces;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostExecutionHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<RemoteToolHostMcpHandlers> _sessions = [];
    private readonly Dictionary<string, PluginExecutionWorkspace> _plugins = new(StringComparer.Ordinal);
    private bool _disposed;

    internal RemoteToolHostExecutionHost(RemoteToolHostStorage storage, WorkspaceLeaseManager leases,
        RemoteToolHostActivityMonitor? activity = null, IRemoteToolApprovalPresenter? approvals = null,
        Func<bool>? isPaused = null)
    {
        Storage = storage;
        Leases = leases;
        Activity = activity;
        Approvals = approvals;
        IsPaused = isPaused;
        leases.DrainResourcesAsync = DrainWorkspaceAsync;
    }

    internal RemoteToolHostStorage Storage { get; }
    internal WorkspaceLeaseManager Leases { get; }
    internal RemoteToolHostActivityMonitor? Activity { get; }
    internal IRemoteToolApprovalPresenter? Approvals { get; }
    internal Func<bool>? IsPaused { get; }
    internal string InstanceId { get; } = "host_" + Guid.NewGuid().ToString("N");

    internal RemoteToolHostMcpHandlers CreateSession(string peerId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = new RemoteToolHostMcpHandlers(this, peerId);
            _sessions.Add(session);
            return session;
        }
    }

    internal void Remove(RemoteToolHostMcpHandlers session)
    {
        lock (_gate) _sessions.Remove(session);
    }

    internal PluginExecutionWorkspace Plugins(string workspaceId, string workspacePath)
    {
        lock (_gate)
        {
            if (_plugins.TryGetValue(workspaceId, out var existing)) return existing;
            var data = Path.Combine(Storage.RootPath, "workspaces", workspaceId);
            var created = new PluginExecutionWorkspace(
                DotCraftPaths.CreateForExecutionHost(workspacePath, data, data),
                AppConfig.Load(Storage.GlobalConfigPath), new ExecutionSessionTerminals());
            _plugins.Add(workspaceId, created);
            return created;
        }
    }

    internal async Task DrainWorkspaceAsync(string workspaceId)
    {
        Leases.ReleaseWorkspace(workspaceId);
        await Leases.WaitForDrainAsync(workspaceId).ConfigureAwait(false);
    }

    private async Task DrainWorkspaceAsync(WorkspaceLeaseReleased lease, Task callsDrained)
    {
        await Task.Yield();
        RemoteToolHostMcpHandlers[] sessions;
        lock (_gate) sessions = _sessions.Where(session => session.WorkspaceId == lease.WorkspaceId).ToArray();
        PluginExecutionWorkspace? plugins;
        lock (_gate) _plugins.Remove(lease.WorkspaceId, out plugins);
        var stopPlugins = plugins?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        await Task.WhenAll(sessions.Select(session => session.CloseResourcesAsync())
            .Append(stopPlugins).Append(callsDrained)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        RemoteToolHostMcpHandlers[] sessions;
        lock (_gate)
        {
            _disposed = true;
            sessions = _sessions.ToArray();
        }
        await Task.WhenAll(sessions.Select(async session => await session.DisposeAsync().ConfigureAwait(false)))
            .ConfigureAwait(false);
        Leases.ReleaseAll();
        await Leases.WaitForAllDrainsAsync().ConfigureAwait(false);
    }
}
