using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>Owns the accepted .NET bundle snapshots of one workspace and their collectible generations.</summary>
internal sealed partial class DotnetPluginRuntimeManager :
    IHostedService,
    IAsyncDisposable,
    IPluginDotnetRuntimeCoordinator
{
    private readonly PluginDiscoveryService _discovery;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly IServiceProvider _services;
    private readonly ContributionRegistry _contributions;
    private readonly DotnetPluginRuntimeOptions _options;
    private readonly ILogger<DotnetPluginRuntimeManager>? _logger;
    private readonly PluginBundleSnapshotStore _bundleStore;
    private readonly PluginDotnetTrust _trust;
    private readonly PluginReclaimPoller _reclaim;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly Dictionary<string, PluginRuntimeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiscoveredPlugin> _effectivePlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginDotnetRuntimeState> _pendingStopClosures = new(StringComparer.OrdinalIgnoreCase);
    private PluginRuntimeSnapshot _snapshot = new(0, [], []);
    private JsonElement? _snapshotSemantic;
    private long _generationSequence;
    private bool _started;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Creates a runtime manager for one configured workspace.</summary>
    internal DotnetPluginRuntimeManager(
        PluginDiscoveryService discovery,
        AppConfig config,
        DotCraftPaths paths,
        IServiceProvider services,
        ContributionRegistry contributions,
        DotnetPluginRuntimeOptions? options = null,
        ILogger<DotnetPluginRuntimeManager>? logger = null,
        IPluginDotnetTrustStore? trustStore = null)
    {
        _discovery = discovery;
        _config = config;
        _paths = paths;
        _services = services;
        _contributions = contributions;
        _options = options ?? new DotnetPluginRuntimeOptions();
        _logger = logger;
        _trust = new PluginDotnetTrust(config, trustStore);
        _bundleStore = new PluginBundleSnapshotStore(Path.Combine(
            paths.Data.RootPath,
            "runtime",
            "plugins",
            $"{Environment.ProcessId}-{Guid.NewGuid():N}"));
        _reclaim = new PluginReclaimPoller(_options, ReclaimAsync, _bundleStore.DeleteGeneration, logger);
        CallGates = new PluginCallGateRegistry();
        ToolSource = new DotnetPluginToolSource(contributions, CallGates);
        _trust.Changed += OnTrustChanged;
    }

    /// <summary>Gets the lookup a Host-side Tool proxy enters a live generation through.</summary>
    public PluginCallGateRegistry CallGates { get; }

    /// <summary>Gets the aggregate Tool source that projects every active plugin's Tools.</summary>
    public DotnetPluginToolSource ToolSource { get; }

    /// <summary>Gets the latest Host-owned immutable runtime projection.</summary>
    public PluginRuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;

            _started = true;
            var discovery = _discovery.DiscoverAll(
                _config,
                _paths.WorkspacePath,
                _paths.Data.RootPath);
            foreach (var plugin in discovery.Plugins)
                _effectivePlugins[plugin.Manifest.Id] = plugin;
            foreach (var plugin in discovery.Plugins
                         .Where(static plugin => plugin.Installed && plugin.Manifest.Dotnet != null)
                         .OrderBy(static plugin => plugin.Manifest.Id, StringComparer.Ordinal))
            {
                AcceptNode(plugin);
            }

            await ReplanAllAsync(_nodes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);
            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>Changes process-local enablement and converges the selected runtime.</summary>
    /// <param name="cancellationToken">Cancels waiting for the transition, not the transition.</param>
    public async Task SetEnabledAsync(
        string pluginId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_nodes.TryGetValue(pluginId, out var node))
                throw new KeyNotFoundException($"Code plugin '{pluginId}' is not accepted by this runtime.");

            node.Enabled = enabled;
            if (enabled)
            {
                await ReplanAllAsync(ReplanIdsFor(pluginId), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StopClosureAsync(pluginId, node, cancellationToken).ConfigureAwait(false);
                await ReplanAllAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken)
                    .ConfigureAwait(false);
            }

            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stopping = true;
            // A mutation may leave teardown pending, but shutdown cannot dispose a provider or the
            // Host while an admitted consumer can still call it. The process owns the hard deadline.
            foreach (var node in StopOrder(_nodes.Values))
            {
                while (!await StopNodeAsync(node, PluginDotnetRuntimeState.Stopped, cancellationToken)
                           .ConfigureAwait(false))
                {
                    var teardown = node.PendingTeardown!;
                    var remnant = await teardown.ConfigureAwait(false);
                    CompletePendingTeardown(node, teardown, remnant);
                }
            }

            foreach (var remnant in await _reclaim.StopAsync(cancellationToken).ConfigureAwait(false))
                SettleReclaimed(remnant);
            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _trust.Changed -= OnTrustChanged;
        _disposed = true;
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            await WaitForTrustChangeWorkerAsync().ConfigureAwait(false);

            // Retained assemblies are mapped from the shadow copies, so the store only goes once nothing is outstanding.
            if (!_reclaim.HasOutstanding
                && _nodes.Values.All(static node =>
                    node.Generation == null
                    && node.PendingActivation == null
                    && node.PendingTeardown == null))
            {
                _bundleStore.DeleteAll();
            }
        }
        finally
        {
            _bundleStore.Dispose();
            _mutationLock.Dispose();
        }
    }

    private void AcceptNode(DiscoveredPlugin plugin)
    {
        PluginAcceptedSnapshot accepted;
        try
        {
            accepted = _bundleStore.Accept(plugin);
        }
        catch (Exception)
        {
            _logger?.LogError("Failed to accept .NET plugin bundle {PluginId}.", plugin.Manifest.Id);
            return;
        }

        var node = new PluginRuntimeNode(accepted, plugin.Enabled);
        _nodes.Add(plugin.Manifest.Id, node);
        if (!plugin.Enabled)
        {
            node.State = PluginDotnetRuntimeState.Stopped;
            return;
        }

        var admissionBlockers = PreflightBlockers(accepted).Concat(TrustBlockers(node)).ToArray();
        if (admissionBlockers.Length > 0)
        {
            node.State = PluginDotnetRuntimeState.Blocked;
            node.Blockers = admissionBlockers;
        }
    }
}
