using Microsoft.Extensions.Logging;

namespace DotCraft.Mcp;

public sealed partial class McpClientManager
{
    public async Task ConnectAsync(IEnumerable<McpServerConfig> servers, CancellationToken cancellationToken = default)
    {
        var configs = servers.Select(static server => server.Clone())
            .Where(static server => !string.IsNullOrWhiteSpace(server.Name))
            .GroupBy(static server => server.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var identities = new Dictionary<string, McpConnectionIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
            identities.Add(config.Name, await McpConnectionIdentity.CreateAsync(config, _userDataPath, cancellationToken)
                .ConfigureAwait(false));
        var clientsToDispose = new List<IAsyncDisposable>();
        var statuses = new List<McpServerStatusSnapshot>();
        Task cleanup;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            foreach (var name in _servers.Keys.Where(name => !identities.ContainsKey(name)).ToArray())
            {
                var removed = _servers[name];
                _servers.Remove(name);
                CancelStartupUnsafe(removed);
                CollectClientUnsafe(removed, clientsToDispose);
            }

            foreach (var config in configs)
            {
                if (_servers.TryGetValue(config.Name, out var state)
                    && CanReuseConnection(state, identities[config.Name], config))
                {
                    state.Config = config;
                    state.ConnectionIdentity = identities[config.Name];
                    state.Status.Origin = config.Origin.Clone();
                    continue;
                }

                if (state is not null)
                {
                    CancelStartupUnsafe(state);
                    CollectClientUnsafe(state, clientsToDispose);
                }
                state = CreateServerState(config, identities[config.Name]);
                _servers[config.Name] = state;
                if (config.Enabled)
                    StartConnectInBackgroundUnsafe(state);
                else
                    statuses.Add(CloneStatus(state.Status));
            }
            RebuildToolIndexUnsafe();
            cleanup = DisposeRetiredClientsUnsafe(clientsToDispose);
        }
        finally
        {
            _mutex.Release();
        }

        foreach (var status in statuses)
            OnStatusChanged(status);
        await cleanup;
    }

    public async Task<McpServerStatusSnapshot> UpsertAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var clone = config.Clone();
        var identity = await McpConnectionIdentity.CreateAsync(clone, _userDataPath, cancellationToken)
            .ConfigureAwait(false);
        var clientsToDispose = new List<IAsyncDisposable>();
        McpServerStatusSnapshot status;
        Task cleanup;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_servers.TryGetValue(clone.Name, out var existing))
            {
                CancelStartupUnsafe(existing);
                CollectClientUnsafe(existing, clientsToDispose);
            }

            var state = CreateServerState(clone, identity);
            _servers[clone.Name] = state;
            status = CloneStatus(state.Status);
            if (clone.Enabled)
                StartConnectInBackgroundUnsafe(state);
            RebuildToolIndexUnsafe();
            cleanup = DisposeRetiredClientsUnsafe(clientsToDispose);
        }
        finally
        {
            _mutex.Release();
        }

        if (!clone.Enabled)
            OnStatusChanged(status);
        await cleanup;
        return status;
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        var clientsToDispose = new List<IAsyncDisposable>();
        Task cleanup;
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!_servers.Remove(name, out var state))
                return false;
            CancelStartupUnsafe(state);
            CollectClientUnsafe(state, clientsToDispose);
            RebuildToolIndexUnsafe();
            cleanup = DisposeRetiredClientsUnsafe(clientsToDispose);
        }
        finally
        {
            _mutex.Release();
        }
        await cleanup;
        return true;
    }

    private ServerRuntimeState CreateServerState(McpServerConfig config, McpConnectionIdentity identity) => new()
    {
        Config = config,
        ConnectionIdentity = identity,
        Status = CreateStatus(config, config.Enabled ? "starting" : "disabled"),
        Generation = NextGenerationUnsafe()
    };

    // The SDK owns live-client token renewal; cache writeback must not restart a ready business session.
    private static bool CanReuseConnection(ServerRuntimeState state, McpConnectionIdentity identity, McpServerConfig config) =>
        state.ConnectionIdentity.Configuration == identity.Configuration
        && (state.Status.StartupState == "ready" || state.ConnectionIdentity.Credentials == identity.Credentials)
        && state.ProtocolClient?.Completion.IsCompleted != true
        && (state.Status.StartupState != "starting" || GetStartupTimeout(state.Config) == GetStartupTimeout(config));

    private void TrackBackgroundTaskUnsafe(Task task)
    {
        _backgroundTasks.RemoveAll(static previous => previous.IsCompleted);
        _backgroundTasks.Add(task);
    }

    private Task DisposeRetiredClientsUnsafe(List<IAsyncDisposable> clients)
    {
        if (clients.Count == 0)
            return Task.CompletedTask;
        var cleanup = Task.Run(() => DisposeClientsAsync(clients));
        TrackBackgroundTaskUnsafe(cleanup);
        return cleanup;
    }

    private void CancelStartupUnsafe(ServerRuntimeState state)
    {
        if (state.StartupCancellation is { } cancellation)
            TrackBackgroundTaskUnsafe(cancellation.CancelAsync());
    }

    private void StartConnectInBackgroundUnsafe(ServerRuntimeState state)
    {
        var config = state.Config.Clone();
        var generation = state.Generation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        state.StartupCancellation = cancellation;
        var task = Task.Run(async () =>
        {
            try
            {
                await ConnectServerAsync(config, generation, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unhandled MCP background connection error for {ServerName}", config.Name);
            }
            finally
            {
                await _mutex.WaitAsync();
                try
                {
                    if (ReferenceEquals(state.StartupCancellation, cancellation))
                        state.StartupCancellation = null;
                }
                finally
                {
                    _mutex.Release();
                }
                cancellation.Dispose();
            }
        }, CancellationToken.None);
        TrackBackgroundTaskUnsafe(task);
    }

    private async Task ConnectServerAsync(McpServerConfig config, long generation, CancellationToken lifetimeToken)
    {
        IAsyncDisposable? client = null;
        try
        {
            await UpdateStatusAsync(config.Name, generation, CreateStatus(config, "starting"), lifetimeToken);
            McpConnectionResult? result = null;
            McpServerStatusSnapshot status;
            try
            {
                using var timeoutCts = CreateStartupTimeoutToken(config, lifetimeToken);
                result = await _connectServer(config, timeoutCts.Token);
                client = result.Client;
                if (result.RecoveredFromStaleSession)
                    _logger?.LogInformation("MCP connection to {ServerName} recovered from stale session during startup/listTools", config.Name);
                status = CreateStatus(config, "ready");
                status.ToolCount = result.Tools.Count;
                status.AuthStatus = result.AuthStatus;
                status.FailureReason = result.FailureReason;
                _logger?.LogInformation("MCP connected to {ServerName} with {ToolCount} tools",
                    config.Name, result.Tools.Count);
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                status = CreateStatus(config, "error");
                status.LastError = CreateStartupTimeoutMessage(config);
                _logger?.LogError("MCP connection to {ServerName} timed out", config.Name);
            }
            catch (Exception ex)
            {
                status = CreateStatus(config, "error");
                status.LastError = ex.Message;
                if (FindAuthenticationRequired(ex) is { } authenticationRequired)
                {
                    status.AuthStatus = McpAuthenticationStatuses.NotLoggedIn;
                    status.FailureReason = authenticationRequired.ReauthenticationRequired
                        ? "reauthenticationRequired" : null;
                }
                _logger?.LogError(ex, "MCP connection to {ServerName} failed", config.Name);
            }

            var accepted = await ApplyConnectResultAsync(config, generation, status,
                result?.Tools ?? [], client, result?.HasSessionId ?? false, result?.ProtocolClient,
                result?.Resources ?? [], result?.ResourceTemplates ?? [], lifetimeToken);
            if (accepted)
                client = null;
        }
        finally
        {
            if (client is not null)
                await DisposeClientAsync(client);
        }
    }
}
