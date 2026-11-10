using DotCraft.Plugins;

namespace DotCraft.Runtime;

public sealed record InstalledExecutionPlugin(string Id, string Version, string RootPath);

internal sealed partial class DotNetPluginRuntimeManager
{
    private IReadOnlyList<PluginDiagnostic> _installedDiscoveryDiagnostics = [];

    internal IReadOnlyList<InstalledExecutionPlugin> InstalledPlugins =>
        Snapshot.Plugins.Where(static plugin => plugin.State == PluginDotnetRuntimeState.Active)
            .Select(plugin => new InstalledExecutionPlugin(plugin.PluginId, plugin.Version,
                _effectivePlugins[plugin.PluginId].Manifest.RootPath))
            .ToArray();

    internal IReadOnlyList<PluginDiagnostic> ExecutionDiagnostics =>
        PluginDiagnostic.Deduplicate(_installedDiscoveryDiagnostics
            .Concat(Snapshot.Diagnostics)
            .Concat(Snapshot.Plugins.SelectMany(plugin => plugin.Blockers.Select(blocker =>
                PluginDiagnostic.Error(blocker.Code, blocker.Message, plugin.PluginId,
                    parameters: blocker.Parameters)))));

    internal async Task LoadInstalledExecutionAsync(string directory, CancellationToken cancellationToken)
    {
        if (!_executionOnly) throw new InvalidOperationException("This runtime does not accept execution bundles.");
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_started) return;
            _started = true;
            var discovery = _discovery.DiscoverDirectory(directory, _config);
            var diagnostics = discovery.Diagnostics.ToList();
            foreach (var plugin in discovery.Plugins)
            {
                _effectivePlugins[plugin.Manifest.Id] = plugin;
                if (plugin.Manifest.Dotnet is null) continue;
                try
                {
                    var snapshot = _bundleStore.Accept(plugin);
                    _executionQualifications[plugin.Manifest.Id] = snapshot.DotnetFingerprint;
                    _nodes.Add(plugin.Manifest.Id, new(snapshot, plugin.Enabled, _paths.WorkspacePath));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(PluginDiagnostic.Error(PluginDotnetDiagnosticCodes.AdmissionFailed,
                        exception.Message, plugin.Manifest.Id, path: plugin.Manifest.RootPath));
                }
            }
            _installedDiscoveryDiagnostics = diagnostics;
            await ReplanAllAsync(_nodes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }
}
