using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

/// <summary>
/// Runs the provider-free execution host in outbound mode: one control connection per pairing over
/// one process-wide lease manager, terminal registry, and tool handler set.
/// </summary>
internal sealed class RemoteToolHostOutboundHost : IAsyncDisposable
{
    private readonly RemoteToolHostStorage _storage;
    private readonly TimeSpan? _heartbeatInterval;
    private readonly LeaseTerminalRegistry _leaseTerminals = new();
    private readonly RemoteToolHostMcpHandlers _handlers;
    private readonly List<RemoteToolHostPeerConnector> _connectors = [];
    private IReadOnlyList<RemoteToolHubPeer> _pairings = [];
    private IDisposable? _serveLock;
    private volatile bool _paused;

    public RemoteToolHostOutboundHost(
        RemoteToolHostStorage storage,
        RemoteToolHostActivityMonitor? activity = null,
        TimeSpan? heartbeatInterval = null)
    {
        _storage = storage;
        _heartbeatInterval = heartbeatInterval;
        Leases = new WorkspaceLeaseManager(
            onReleased: released =>
            {
                _leaseTerminals.ReleaseLease(released.LeaseId);
                RemoteToolArtifactStore.CleanupLeaseArtifacts(storage.ArtifactsRootPath, released.LeaseId);
            });
        _handlers = new RemoteToolHostMcpHandlers(storage, Leases, _leaseTerminals, activity);
    }

    public event Action? Changed;

    public WorkspaceLeaseManager Leases { get; }

    public IReadOnlyList<RemoteToolHostPeerConnector> Connectors
    {
        get
        {
            lock (_connectors)
                return [.. _connectors];
        }
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Takes the machine-wide serve lock and validates the Host before any connection.</summary>
    public void Prepare()
    {
        if (!RemoteToolHostServeLock.TryAcquire(_storage, out var serveLock))
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.HostNotRegistered,
                $"Another Remote Tool Host is already serving on this machine ({_storage.ServeLockPath}).");
        try
        {
            var state = _storage.LoadHostState()
                ?? throw new RemoteToolHostException(
                    RemoteToolErrorCodes.HostNotRegistered,
                    "Remote Tool Host is not set up. Run 'dotcraft tool-host setup' first.");
            EnsureArtifactRootIsReachable(state);
            if (state.Peers.Count == 0)
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.HostNotRegistered,
                    "This machine is not paired with a Hub. "
                    + "Run 'dotcraft tool-host join <invite-url> --workspace <folder>' first.");
            RemoteToolArtifactStore.CleanupStaleArtifacts(_storage.ArtifactsRootPath);
            _pairings = state.Peers;
            _serveLock = serveLock;
        }
        catch
        {
            serveLock!.Dispose();
            throw;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<Task> running;
        lock (_connectors)
        {
            _connectors.Clear();
            foreach (var peer in _pairings)
            {
                var connector = new RemoteToolHostPeerConnector(
                    _storage,
                    peer,
                    _handlers,
                    Leases,
                    () => _paused,
                    _heartbeatInterval);
                connector.StateChanged += _ => Changed?.Invoke();
                _connectors.Add(connector);
            }
            running = [.. _connectors.Select(connector => connector.RunAsync(cancellationToken))];
        }
        Changed?.Invoke();
        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        finally
        {
            lock (_connectors)
                _connectors.Clear();
            Changed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _handlers.DisposeAsync().ConfigureAwait(false);
        _serveLock?.Dispose();
        _serveLock = null;
    }

    private void EnsureArtifactRootIsReachable(RemoteToolHostState state)
    {
        var artifactsRoot = Path.GetFullPath(_storage.ArtifactsRootPath);
        var configs = new List<AppConfig> { AppConfig.Load(_storage.GlobalConfigPath) };
        configs.AddRange(state.Workspaces.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath, path)));
        foreach (var config in configs)
        {
            if (new PathBlacklist(config.Security.BlacklistedPaths).IsBlacklisted(artifactsRoot))
                throw new InvalidOperationException(
                    $"The Remote Tool Host result directory '{artifactsRoot}' is blacklisted. "
                    + "Remove it from Security.BlacklistedPaths before serving.");
        }
    }
}
