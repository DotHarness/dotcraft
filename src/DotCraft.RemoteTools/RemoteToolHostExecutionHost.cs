using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostExecutionHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<RemoteToolHostMcpHandlers> _sessions = [];
    private readonly Dictionary<string, PluginExecutionWorkspace> _plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HostInstalledPluginWorkspace> _installed = new(StringComparer.Ordinal);
    private readonly string? _hostPluginsDirectory;
    private readonly Action<RemoteToolHostDiagnostic>? _diagnostic;
    private bool _disposed;

    internal RemoteToolHostExecutionHost(RemoteToolHostStorage storage, WorkspaceLeaseManager leases,
        RemoteToolHostActivityMonitor? activity = null, IRemoteToolApprovalPresenter? approvals = null,
        Func<bool>? isPaused = null, Action<RemoteToolHostDiagnostic>? diagnostic = null,
        string? hostPluginsDirectory = null)
    {
        Storage = storage;
        Leases = leases;
        Activity = activity;
        Approvals = approvals;
        IsPaused = isPaused;
        Logger = new RemoteToolDiagnosticLogger(diagnostic);
        _diagnostic = diagnostic;
        _hostPluginsDirectory = hostPluginsDirectory;
        leases.DrainResourcesAsync = DrainWorkspaceAsync;
    }

    internal RemoteToolHostStorage Storage { get; }
    internal WorkspaceLeaseManager Leases { get; }
    internal RemoteToolHostActivityMonitor? Activity { get; }
    internal IRemoteToolApprovalPresenter? Approvals { get; }
    internal Func<bool>? IsPaused { get; }
    internal ILogger<BackgroundTerminalService> Logger { get; }
    internal string InstanceId { get; } = "host_" + Guid.NewGuid().ToString("N");

    internal void ReportDiagnostic(RemoteToolHostDiagnostic diagnostic) => _diagnostic?.Invoke(diagnostic);

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

    internal async Task<HostInstalledPluginWorkspace?> InstalledPluginsAsync(
        string workspaceId, string workspacePath, CancellationToken ct)
    {
        if (_hostPluginsDirectory is null) return null;
        HostInstalledPluginWorkspace installed;
        while (true)
        {
            await Leases.WaitForDrainAsync(workspaceId).WaitAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!Leases.WaitForDrainAsync(workspaceId).IsCompleted) continue;
                if (!_installed.TryGetValue(workspaceId, out installed!))
                {
                    var data = Path.Combine(Storage.RootPath, "workspaces", workspaceId, "host-installed");
                    installed = new HostInstalledPluginWorkspace(
                        DotCraftPaths.CreateForExecutionHost(workspacePath, data, data),
                        HostWorkspaceRuntime.LoadWorkspaceConfig(Storage.GlobalConfigPath, workspacePath), _diagnostic);
                    _installed.Add(workspaceId, installed);
                }
                break;
            }
        }
        await installed.InitializeAsync(_hostPluginsDirectory, ct).ConfigureAwait(false);
        return installed;
    }

    internal async Task DrainWorkspaceAsync(string workspaceId)
    {
        HostInstalledPluginWorkspace? installed;
        lock (_gate) _installed.TryGetValue(workspaceId, out installed);
        Leases.ReleaseWorkspace(workspaceId);
        await Leases.WaitForDrainAsync(workspaceId).ConfigureAwait(false);
        if (installed is null) return;
        lock (_gate)
        {
            if (!_installed.TryGetValue(workspaceId, out var current) || !ReferenceEquals(current, installed)) return;
            _installed.Remove(workspaceId);
        }
        await installed.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DrainWorkspaceAsync(WorkspaceLeaseReleased lease, Task callsDrained)
    {
        await Task.Yield();
        RemoteToolHostMcpHandlers[] sessions;
        lock (_gate) sessions = _sessions.Where(session => session.WorkspaceId == lease.WorkspaceId).ToArray();
        PluginExecutionWorkspace? plugins;
        HostInstalledPluginWorkspace? installed;
        lock (_gate)
        {
            _plugins.Remove(lease.WorkspaceId, out plugins);
            _installed.Remove(lease.WorkspaceId, out installed);
        }
        var stopPlugins = plugins?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        var stopInstalled = installed?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        await Task.WhenAll(sessions.Select(session => session.CloseResourcesAsync())
            .Append(stopPlugins).Append(stopInstalled).Append(callsDrained)).ConfigureAwait(false);
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
        HostInstalledPluginWorkspace[] installed;
        lock (_gate)
        {
            installed = _installed.Values.ToArray();
            _installed.Clear();
        }
        await Task.WhenAll(installed.Select(async owner => await owner.DisposeAsync().ConfigureAwait(false)))
            .ConfigureAwait(false);
    }
}
