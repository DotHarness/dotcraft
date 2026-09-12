using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using DotCraft.Screen;

namespace DotCraft.RemoteTools;

/// <summary>
/// Keeps one outbound control connection to a paired Hub, answers session requests, and removes the
/// pairing when the Hub revokes it.
/// </summary>
internal sealed class RemoteToolHostPeerConnector(
    RemoteToolHostStorage storage,
    RemoteToolHubPeer peer,
    RemoteToolHostExecutionHost handlers,
    WorkspaceLeaseManager leases,
    Func<bool> isPaused,
    TimeSpan? heartbeatInterval = null,
    Func<IScreenCaptureSource>? screenCapture = null)
{
    private readonly TimeSpan _heartbeatInterval = heartbeatInterval ?? SatelliteWire.HeartbeatInterval;
    private readonly SemaphoreSlim _drainGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task> _dataTasks = new();
    private CancellationTokenSource _dataCancellation = new();
    private volatile string _screenCloseReason = SatelliteWire.ScreenClosedHost;
    private int _screenViewers;
    private int _backoffAttempt;
    private volatile bool _stopped;
    private CancellationTokenSource? _session;

    public string PeerId => peer.PeerId;
    public RemoteToolHubPeer Peer => peer;
    public bool IsConnected { get; private set; }
    public DateTimeOffset? ConnectedSince { get; private set; }
    public bool WasRevoked { get; private set; }

    /// <summary>Open screen views; each is a data session that takes no lease.</summary>
    public int ScreenViewers => Volatile.Read(ref _screenViewers);

    public event Action<RemoteToolHostPeerConnector>? StateChanged;

    public void Stop()
    {
        _stopped = true;
        try { _session?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Ends every data session of either kind; screen views close with <paramref name="screenCloseReason"/>.</summary>
    public async Task DrainAsync(string screenCloseReason = SatelliteWire.ScreenClosedHost)
    {
        await _drainGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _screenCloseReason = screenCloseReason;
            await _dataCancellation.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(_dataTasks.Values).ConfigureAwait(false); }
            catch (Exception) { }
            _dataTasks.Clear();
            await handlers.DrainWorkspaceAsync(peer.WorkspaceId).ConfigureAwait(false);
            _dataCancellation.Dispose();
            _dataCancellation = new CancellationTokenSource();
            _screenCloseReason = SatelliteWire.ScreenClosedHost;
        }
        finally { _drainGate.Release(); }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_stopped)
        {
            try
            {
                await RunSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PairingRevokedException)
            {
                storage.RemovePeer(peer.PeerId);
                WasRevoked = true;
                _stopped = true;
                StateChanged?.Invoke(this);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Every transport failure is retried with the shared backoff schedule.
            }

            if (cancellationToken.IsCancellationRequested || _stopped)
                return;
            try
            {
                await Task.Delay(SatelliteWire.ReconnectDelay(_backoffAttempt), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _backoffAttempt++;
        }
    }

    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var credential = storage.GetPeerCredential(peer);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        await socket.ConnectAsync(peer.ControlUri, cancellationToken).ConfigureAwait(false);
        await SendAsync(socket, BuildHello(), cancellationToken).ConfigureAwait(false);

        var welcome = await SatelliteWire.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (welcome is null || !string.Equals(welcome.Kind, SatelliteWire.Welcome, StringComparison.Ordinal))
            throw new InvalidOperationException("The Hub did not accept the control connection.");

        _backoffAttempt = 0;
        IsConnected = true;
        ConnectedSince = DateTimeOffset.UtcNow;
        StateChanged?.Invoke(this);

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _session = session;
        var heartbeat = RunHeartbeatAsync(socket, session.Token);
        try
        {
            await ReceiveAsync(socket, credential, session).ConfigureAwait(false);
        }
        finally
        {
            await session.CancelAsync().ConfigureAwait(false);
            try { await heartbeat.ConfigureAwait(false); }
            catch (Exception) { }
            await DrainAsync().ConfigureAwait(false);
            _session = null;
            IsConnected = false;
            ConnectedSince = null;
            StateChanged?.Invoke(this);
        }
    }

    private async Task ReceiveAsync(
        ClientWebSocket socket,
        string credential,
        CancellationTokenSource session)
    {
        while (!session.IsCancellationRequested)
        {
            var frame = await SatelliteWire.ReceiveAsync(socket, session.Token).ConfigureAwait(false);
            if (frame is null)
                return;
            switch (frame.Kind)
            {
                case SatelliteWire.Revoked:
                    throw new PairingRevokedException();
                case SatelliteWire.OpenSession when frame.SessionId is { Length: > 0 } sessionId:
                    Track(
                        sessionId,
                        string.Equals(frame.SessionKind, SatelliteWire.SessionKindScreen, StringComparison.Ordinal)
                            ? OpenScreenSessionAsync(socket, credential, sessionId, session.Token)
                            : OpenDataSessionAsync(socket, credential, sessionId, session.Token));
                    break;
            }
        }
    }

    private void Track(string sessionId, Task session)
    {
        foreach (var pair in _dataTasks)
        {
            if (pair.Value.IsCompleted)
                _dataTasks.TryRemove(pair);
        }
        _dataTasks[sessionId] = session;
    }

    private async Task OpenDataSessionAsync(
        ClientWebSocket control,
        string credential,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isPaused() || !RemoteToolAuthorization.IsValid(peer.AuthorizationMode))
                throw new InvalidOperationException("Sharing is paused on this machine.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _dataCancellation.Token);
            await RemoteToolHostDataSession.RunAsync(
                peer.DataUri(sessionId),
                credential,
                peer.PeerId,
                handlers,
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await FailSessionAsync(control, sessionId, SatelliteWire.SessionFailedClose, ex.Message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task OpenScreenSessionAsync(
        ClientWebSocket control,
        string credential,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (screenCapture is not { } capture)
        {
            await FailSessionAsync(control, sessionId, SatelliteWire.SessionFailedClose, null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var refusal = isPaused() ? SatelliteWire.ScreenClosedPaused
            : !RemoteToolAuthorization.IsValid(peer.AuthorizationMode) ? SatelliteWire.ScreenClosedAuthorizationRequired
            : null;
        if (refusal is not null)
        {
            await FailSessionAsync(control, sessionId, refusal, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        Interlocked.Increment(ref _screenViewers);
        StateChanged?.Invoke(this);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _dataCancellation.Token);
            await ScreenViewSession.RunAsync(
                peer.DataUri(sessionId),
                credential,
                capture,
                () => _screenCloseReason,
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await FailSessionAsync(control, sessionId, SatelliteWire.SessionFailedClose, ex.Message, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _screenViewers);
            StateChanged?.Invoke(this);
        }
    }

    private async Task FailSessionAsync(
        ClientWebSocket control,
        string sessionId,
        string code,
        string? message,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(
                control,
                new SatelliteFrame
                {
                    Kind = SatelliteWire.SessionFailed,
                    SessionId = sessionId,
                    Code = code,
                    Message = message
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The control connection is already gone; the Hub times the session out.
        }
    }

    private async Task RunHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await SendAsync(
                socket,
                new SatelliteFrame
                {
                    Kind = SatelliteWire.Heartbeat,
                    PeerId = peer.PeerId,
                    Workspaces = DescribeWorkspaces()
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private SatelliteFrame BuildHello()
    {
        var state = storage.LoadHostState();
        return new SatelliteFrame
        {
            Kind = SatelliteWire.Hello,
            PeerId = peer.PeerId,
            DisplayName = state?.DisplayName ?? Environment.MachineName,
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            UserName = Environment.UserName,
            BuildVersion = RemoteToolHostProtocol.BuildVersion,
            Workspaces = DescribeWorkspaces(),
            Capabilities = screenCapture is null ? null : [SatelliteWire.ScreenCapability]
        };
    }

    private IReadOnlyList<SatelliteWorkspaceInfo> DescribeWorkspaces()
    {
        if (isPaused() || !RemoteToolAuthorization.IsValid(peer.AuthorizationMode))
            return [];
        var state = storage.LoadHostState();
        if (state is null)
            return [];
        return
        [
            .. state.Workspaces.Where(pair => pair.Key == peer.WorkspaceId)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var status = leases.GetStatus(pair.Key);
                    return new SatelliteWorkspaceInfo(
                        pair.Key,
                        Path.GetFullPath(pair.Value),
                        status is not null,
                        status is null ? null : "other",
                        status?.ExpiresAt);
                })
        ];
    }

    private async Task SendAsync(ClientWebSocket socket, SatelliteFrame frame, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SatelliteWire.SendAsync(socket, frame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private sealed class PairingRevokedException : Exception;
}
