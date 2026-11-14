using System.Net.WebSockets;
using System.Security.Authentication;
using System.Threading.Channels;
using DotCraft.CodexGateway;
using DotCraft.CodexGateway.Protocol;
using DotCraft.CodexGateway.Workspace;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed class CodexGateway : IAsyncDisposable
{
    private readonly HubEventBus _events;
    private readonly ILogger _logger;
    private readonly CodexGatewayStateStore _store;
    private readonly CodexGatewayAuthentication _authentication;
    private readonly CodexWorkspaceCatalog _catalog;
    private readonly Func<Action<RemoteControlHostStatus>, ICodexGatewayConnection> _connect;
    private readonly SemaphoreSlim _operation = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Task _authChanges;
    private readonly Task _projectChanges;
    private volatile HubCodexRemoteStatus _status;
    private ICodexGatewayConnection? _connection;
    private CancellationTokenSource? _runCancellation;
    private Task? _run;
    private CancellationTokenSource? _pairingCancellation;
    private Task? _pairing;
    private string? _accountId;
    private bool _enabled;
    private int _generation;
    private int _disposed;

    internal CodexGateway(
        HubPaths paths, ProjectRegistry projects, HubEventBus events, ILogger logger, string? executable = null,
        Func<Action<RemoteControlHostStatus>, ICodexGatewayConnection>? connect = null)
    {
        _events = events;
        _logger = logger;
        _store = new(paths.CodexRemoteConfigPath);
        _authentication = new(paths.CraftHomePath, () => _changes.Writer.TryWrite(true));
        _status = new("off", _authentication.AccountId is not null, Environment.MachineName + " · DotCraft");
        _catalog = new(() => projects.List().Select(project => new CodexWorkspaceRegistration(
            project.Path, Path.GetFileName(project.Path), project.AddedAt, project.LastOpenedAt)).ToArray(),
            paths.CodexRemoteStatePath);
        _connect = connect ?? (changed =>
        {
            var options = new RemoteControlHostOptions { StateDirectory = paths.CodexRemoteStatePath, Name = _status.HostName };
            RemoteControlHost? host = null;
            host = new(options, _authentication.CredentialsAsync,
                (_, send) => new CodexGatewaySession(options, _catalog, () => host!.Status, send, executable,
                    observedMethod: diagnostic => _logger.LogInformation("Codex gateway {Diagnostic}", diagnostic)), changed);
            return new CodexGatewayConnection(host);
        });
        _authChanges = ObserveAuthenticationAsync();
        _projectChanges = ObserveProjectsAsync();
    }

    public HubCodexRemoteStatus Snapshot() => _status with { SignedIn = _authentication.AccountId is not null };

    public async Task StartIfEnabledAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            _enabled = _store.Read();
            await ReconcileAsync();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Failed(error);
        }
        finally { _operation.Release(); }
    }

    public async Task<HubCodexRemoteStatus> EnableAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            RequireAuthentication();
            _store.Write(true);
            _enabled = true;
            await ReconcileAsync();
            return Snapshot();
        }
        finally { _operation.Release(); }
    }

    public async Task<HubCodexRemoteStatus> DisableAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            _store.Write(false);
            _enabled = false;
            await StopAsync();
            Publish("off");
            return Snapshot();
        }
        finally { _operation.Release(); }
    }

    public async Task<HubCodexRemotePairing> CreatePairingAsync(CancellationToken ct)
    {
        await _operation.WaitAsync(ct);
        try
        {
            RequireAuthentication();
            await ReconcileAsync();
            if (!_enabled || _connection is null || _status.State != "on")
                throw new HubProtocolException("codexRemoteNotConnected",
                    "Enable Codex remote access and wait for the connection before pairing.", StatusCodes.Status409Conflict);
            await StopPairingAsync();
            RemoteControlPairing pairing;
            try { pairing = await _connection.PairAsync(ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _logger.LogWarning("Codex remote pairing failed errorType={ErrorType}", error.GetType().Name);
                throw new HubProtocolException("codexRemotePairingFailed",
                    "Unable to create a Codex pairing invitation.", StatusCodes.Status502BadGateway);
            }
            _pairingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var remaining = pairing.ExpiresAt - DateTimeOffset.UtcNow;
            _pairingCancellation.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            _pairing = ObservePairingAsync(_connection, pairing, _pairingCancellation.Token);
            return new(pairing.PairingUri.ToString(), pairing.ManualPairingCode, pairing.ExpiresAt);
        }
        finally { _operation.Release(); }
    }

    private void RequireAuthentication()
    {
        if (_authentication.AccountId is null)
            throw new HubProtocolException("codexRemoteSignInRequired",
                "Sign in to ChatGPT before enabling Codex remote access.", StatusCodes.Status409Conflict);
    }

    private async Task ReconcileAsync()
    {
        var account = _authentication.AccountId;
        if (_connection is not null && (!_enabled || account != _accountId || _run!.IsCompleted))
            await StopAsync();
        if (!_enabled || account is null)
        {
            Publish("off");
            return;
        }
        if (_connection is not null) return;
        _accountId = account;
        var generation = ++_generation;
        Publish("connecting");
        try
        {
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _connection = _connect(status =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                Publish(status.Status switch
                {
                    "connected" => "on",
                    "connecting" => "connecting",
                    "errored" => "failed",
                    _ => "off"
                });
            });
            _run = ObserveRunAsync(_connection, generation, _runCancellation.Token);
        }
        catch (Exception error)
        {
            await StopAsync();
            Failed(error);
        }
    }

    private async Task ObserveRunAsync(ICodexGatewayConnection connection, int generation, CancellationToken ct)
    {
        try { await connection.RunAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (generation == Volatile.Read(ref _generation)) Failed(error);
        }
    }

    private async Task ObserveAuthenticationAsync()
    {
        try
        {
            await foreach (var _ in _changes.Reader.ReadAllAsync(_lifetime.Token))
            {
                await _operation.WaitAsync(_lifetime.Token);
                try { await ReconcileAsync(); }
                catch (Exception error) { Failed(error); }
                finally { _operation.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task ObserveProjectsAsync()
    {
        try
        {
            await foreach (var change in _events.Subscribe(_lifetime.Token).ReadAllAsync(_lifetime.Token))
            {
                if (change.Kind != "projects.changed") continue;
                try { _catalog.Refresh(); }
                catch (Exception error) { _logger.LogWarning("Codex project catalog update failed errorType={ErrorType}", error.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task ObservePairingAsync(ICodexGatewayConnection connection, RemoteControlPairing pairing, CancellationToken ct)
    {
        try
        {
            while (DateTimeOffset.UtcNow < pairing.ExpiresAt)
            {
                if (await connection.IsPairingClaimedAsync(pairing, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTimeOffset.UtcNow >= pairing.ExpiresAt) return;
                    _events.Publish("codexRemote.paired");
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) { _logger.LogWarning("Codex pairing status check failed errorType={ErrorType}", error.GetType().Name); }
    }

    private async Task StopPairingAsync()
    {
        if (_pairingCancellation is null) return;
        await _pairingCancellation.CancelAsync();
        await _pairing!;
        _pairingCancellation.Dispose();
        _pairingCancellation = null;
        _pairing = null;
    }

    private async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        await StopPairingAsync();
        try
        {
            if (_connection is not null)
            {
                await _runCancellation!.CancelAsync();
                try { await _connection.DisposeAsync(); }
                catch (Exception error) { _logger.LogWarning("Codex remote connection stopped with an error errorType={ErrorType}", error.GetType().Name); }
                if (_run is not null) await _run;
            }
        }
        finally
        {
            _connection = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            _run = null;
            _accountId = null;
        }
    }

    private void Failed(Exception error)
    {
        var (code, message) = error switch
        {
            AuthenticationException => ("codexRemoteAuthenticationFailed",
                "ChatGPT authentication failed. Sign in again and retry Codex remote access."),
            HttpRequestException or WebSocketException => ("codexRemoteRelayFailed",
                "Unable to connect to the Codex relay. Try again later."),
            _ => ("codexRemoteInternalFailed",
                "Codex remote access stopped because of an internal error. Retry and check gateway diagnostics.")
        };
        _logger.LogWarning("Codex remote gateway failed: failureCode={FailureCode} errorType={ErrorType}",
            code, error.GetType().Name);
        Publish("failed", code, message, _status.SignedIn);
    }

    private void Publish(string state, string? code = null, string? message = null, bool? signedIn = null)
    {
        var next = new HubCodexRemoteStatus(state, signedIn ?? (_authentication.AccountId is not null), _status.HostName, code, message);
        if (next == _status) return;
        _status = next;
        _events.Publish("codexRemote.stateChanged", data: next);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _authentication.Dispose();
        await _lifetime.CancelAsync();
        await Task.WhenAll(_authChanges, _projectChanges);
        await _operation.WaitAsync();
        try { await StopAsync(); }
        finally { _operation.Release(); }
        _operation.Dispose();
        _lifetime.Dispose();
    }
}
