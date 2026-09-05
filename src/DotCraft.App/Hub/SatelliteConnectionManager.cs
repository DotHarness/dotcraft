using System.Collections.Concurrent;
using System.Net.WebSockets;
using DotCraft.Common;
using DotCraft.RemoteTools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

/// <summary>
/// Owns the live satellite control connections, brokers data sessions, and starts the satellite
/// listener on demand.
/// </summary>
internal sealed class SatelliteConnectionManager(
    SatelliteRegistry registry,
    HubConfig config,
    HubEventBus events,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PeerConnection> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _listenerGate = new(1, 1);
    private readonly ILogger _logger = loggerFactory.CreateLogger<SatelliteConnectionManager>();
    private HubSatelliteListener? _listener;
    private bool _disposed;

    public SatelliteRegistry Registry => registry;

    public int? ListenerPort => _listener?.Port;

    public bool IsOnline(string peerId) =>
        _connections.TryGetValue(peerId, out var connection) && connection.IsLive;

    public async Task<int> EnsureListenerAsync(CancellationToken cancellationToken)
    {
        if (_listener is not null)
            return _listener.Port;
        await _listenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _listener ??= await HubSatelliteListener.StartAsync(
                config.SatelliteHost,
                config.SatellitePort,
                this,
                loggerFactory,
                cancellationToken).ConfigureAwait(false);
            return _listener.Port;
        }
        finally
        {
            _listenerGate.Release();
        }
    }

    /// <summary>Starts the listener when pairings already exist so peers can reconnect unattended.</summary>
    public async Task StartForExistingPeersAsync(CancellationToken cancellationToken)
    {
        if (!config.SatellitesEnabled && !registry.HasPeers())
            return;
        try
        {
            await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HubProtocolException ex)
        {
            _logger.LogWarning("Satellite listener did not start: {Code}", ex.Code);
        }
    }

    public bool CanAdmitControl(string? peerId, string? bearer)
    {
        if (string.IsNullOrEmpty(bearer))
            return false;
        return string.IsNullOrEmpty(peerId)
            ? registry.FindInvite(bearer) is not null
            : registry.Verify(peerId, bearer) is not null;
    }

    public bool CanAdmitData(string peerId, string credential) => registry.Verify(peerId, credential) is not null;

    public bool IsSessionActive(string sessionId) => _sessions.ContainsKey(sessionId);

    public async Task RunControlAsync(
        WebSocket socket,
        string? peerId,
        string bearer,
        CancellationToken cancellationToken)
    {
        var hello = await SatelliteWire.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (hello is null || !string.Equals(hello.Kind, SatelliteWire.Hello, StringComparison.Ordinal))
            return;

        SatellitePeerRecord peer;
        string? credential = null;
        var joined = false;
        if (string.IsNullOrEmpty(peerId))
        {
            if (!registry.TryConsumeInvite(bearer, out var invite) || invite is null)
                return;
            var label = string.IsNullOrWhiteSpace(invite.Label)
                ? hello.DisplayName ?? hello.MachineName ?? "Remote Tool Host"
                : invite.Label;
            (peer, credential) = registry.Pair(label, hello);
            joined = true;
        }
        else
        {
            var verified = registry.Verify(peerId, bearer);
            if (verified is null)
                return;
            peer = verified;
            registry.UpdateFromHeartbeat(peer.PeerId, hello.Workspaces ?? [], DateTimeOffset.UtcNow);
        }

        var connection = new PeerConnection(peer.PeerId, socket);
        var previous = _connections.TryGetValue(peer.PeerId, out var existing) ? existing : null;
        _connections[peer.PeerId] = connection;
        if (previous is not null)
            await previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced").ConfigureAwait(false);

        await connection.SendAsync(
            new SatelliteFrame
            {
                Kind = SatelliteWire.Welcome,
                PeerId = peer.PeerId,
                Credential = credential,
                HubVersion = AppVersion.Informational,
                HubLabel = peer.DisplayName
            },
            cancellationToken).ConfigureAwait(false);
        events.Publish(joined ? "satellite.joined" : "satellite.online", data: new { peerId = peer.PeerId });

        try
        {
            await ReceiveControlFramesAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connections.TryRemove(new KeyValuePair<string, PeerConnection>(peer.PeerId, connection));
            FailSessionsFor(peer.PeerId, SatelliteWire.OfflineClose);
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed").ConfigureAwait(false);
            events.Publish("satellite.offline", data: new { peerId = peer.PeerId });
        }
    }

    /// <summary>Hands a peer data socket to its waiting bridge and returns when the relay ends.</summary>
    public Task? TryAttachData(string peerId, string sessionId, WebSocket socket) =>
        _sessions.TryGetValue(sessionId, out var session)
        && string.Equals(session.PeerId, peerId, StringComparison.Ordinal)
        && session.PeerSocket.TrySetResult(socket)
            ? session.Finished.Task
            : null;

    /// <summary>Opens one relayed session for a local AppServer and returns the failure code, if any.</summary>
    public async Task<string?> BridgeAsync(
        string peerId,
        string sessionId,
        WebSocket agentSocket,
        CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(peerId, out var connection) || !connection.IsLive)
            return SatelliteWire.OfflineClose;

        var session = new PendingSession(peerId);
        if (!_sessions.TryAdd(sessionId, session))
            return SatelliteWire.SessionFailedClose;

        try
        {
            await connection.SendAsync(
                new SatelliteFrame { Kind = SatelliteWire.OpenSession, SessionId = sessionId },
                cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SatelliteWire.OpenSessionTimeout);
            WebSocket peerSocket;
            try
            {
                peerSocket = await session.PeerSocket.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return SatelliteWire.SessionFailedClose;
            }

            await SatelliteWebSocketBridge.RelayAsync(agentSocket, peerSocket, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
            session.Finished.TrySetResult();
        }
    }

    public async Task RevokeLiveAsync(string peerId)
    {
        FailSessionsFor(peerId, SatelliteWire.OfflineClose);
        if (!_connections.TryRemove(peerId, out var connection))
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.SendAsync(new SatelliteFrame { Kind = SatelliteWire.Revoked }, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The peer may already be gone; the pairing record is removed either way.
        }
        await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "revoked").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var connection in _connections.Values)
            await connection.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "hubStopping").ConfigureAwait(false);
        _connections.Clear();
        foreach (var session in _sessions.Values)
            session.Finished.TrySetResult();
        _sessions.Clear();
        if (_listener is not null)
            await _listener.DisposeAsync().ConfigureAwait(false);
        _listener = null;
        _listenerGate.Dispose();
    }

    private async Task ReceiveControlFramesAsync(PeerConnection connection, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SatelliteFrame? frame;
            try
            {
                frame = await SatelliteWire.ReceiveAsync(connection.Socket, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
            if (frame is null)
                return;

            switch (frame.Kind)
            {
                case SatelliteWire.Heartbeat:
                    connection.Touch();
                    registry.UpdateFromHeartbeat(
                        connection.PeerId,
                        frame.Workspaces ?? [],
                        DateTimeOffset.UtcNow);
                    break;
                case SatelliteWire.SessionFailed when frame.SessionId is { Length: > 0 } sessionId:
                    connection.Touch();
                    if (_sessions.TryGetValue(sessionId, out var session))
                        session.PeerSocket.TrySetCanceled();
                    break;
                default:
                    connection.Touch();
                    break;
            }
        }
    }

    private void FailSessionsFor(string peerId, string reason)
    {
        foreach (var pair in _sessions)
        {
            if (string.Equals(pair.Value.PeerId, peerId, StringComparison.Ordinal))
                pair.Value.PeerSocket.TrySetException(new InvalidOperationException(reason));
        }
    }

    private sealed class PeerConnection(string peerId, WebSocket socket)
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private DateTimeOffset _lastSeenAt = DateTimeOffset.UtcNow;

        public string PeerId => peerId;
        public WebSocket Socket => socket;

        public bool IsLive =>
            socket.State == WebSocketState.Open
            && DateTimeOffset.UtcNow - _lastSeenAt <= SatelliteWire.OfflineAfter;

        public void Touch() => _lastSeenAt = DateTimeOffset.UtcNow;

        public async Task SendAsync(SatelliteFrame frame, CancellationToken cancellationToken)
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

        public async Task CloseAsync(WebSocketCloseStatus status, string description)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await socket.CloseOutputAsync(status, description, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Closing is best effort; the control loop exits on the next receive either way.
            }
        }
    }

    private sealed class PendingSession(string peerId)
    {
        public string PeerId => peerId;

        public TaskCompletionSource<WebSocket> PeerSocket { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
