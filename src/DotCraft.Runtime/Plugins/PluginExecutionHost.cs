using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.Workspaces;

namespace DotCraft.Runtime;

/// <summary>Runs explicitly admitted plugin bundles without composing an Agent or Session runtime.</summary>
public sealed class PluginExecutionHost(
    DotCraftPaths paths, IServiceProvider services, AppConfig? config = null) : IAsyncDisposable
{
    private readonly ContributionRegistry _contributions = new();
    private readonly object _gate = new();
    private DotNetExtensionRuntimeManager? _runtime;
    private bool _installed;
    private bool _disposed;

    public IReadOnlyList<InstalledExecutionPlugin> InstalledPlugins =>
        _installed ? _runtime?.InstalledPlugins ?? [] : [];

    public IReadOnlyList<PluginDiagnostic> Diagnostics =>
        _runtime?.ExecutionDiagnostics ?? [];

    /// <summary>Loads one installed directory for this host's lifetime; prepared bundles require a separate host.</summary>
    public Task LoadInstalledAsync(string directory, CancellationToken cancellationToken = default) =>
        Runtime(installed: true).LoadInstalledExecutionAsync(directory, cancellationToken);

    public Task PrepareAsync(IReadOnlyList<RemotePluginBundleFiles> bundles, CancellationToken cancellationToken = default) =>
        Runtime().PrepareExecutionAsync(bundles, cancellationToken);

    public static bool MatchesBundle(string rootPath, RemotePluginBundle bundle) =>
        PluginBundleFingerprint.Compute(rootPath) == bundle.ContentFingerprint
        && DotNetExtensionFingerprint.Compute(rootPath) == bundle.DotnetFingerprint;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context, CancellationToken cancellationToken = default) =>
        Runtime(_installed).ToolSource.GetRegistrationsAsync(context, cancellationToken);

    public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (_runtime is not { } runtime) return;
        await runtime.ToolSource.ReleaseThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        _contributions.ReleaseThread(threadId);
    }

    private DotNetExtensionRuntimeManager Runtime(bool installed = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtime is not null)
            {
                if (_installed != installed)
                    throw new InvalidOperationException("Installed and prepared plugins require separate execution hosts.");
                return _runtime;
            }
            _installed = installed;
            return _runtime = new DotNetExtensionRuntimeManager(new PluginDiscoveryService(), config ?? new AppConfig(), paths,
                services, _contributions, executionOnly: true, installedExecution: installed);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            return _runtime?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
