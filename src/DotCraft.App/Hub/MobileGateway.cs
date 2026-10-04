using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed class MobileGateway : IAsyncDisposable
{
    public const string DeviceRevoked = "deviceRevoked";
    public const string GatewayOff = "gatewayOff";

    private static readonly TimeSpan HelloSeenInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);

    private readonly MobileRegistry _registry;
    private readonly HubConfig _config;
    private readonly HubPaths _paths;
    private readonly HubEventBus _events;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Lock _connectionsGate = new();
    private readonly List<MobileConnection> _connections = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _helloSeenAt = new(StringComparer.Ordinal);
    private HubMobileListener? _listener;
    private MobileRelayLink? _relay;
    private X509Certificate2? _certificate;
    private GatewayStatus _status = GatewayStatus.Off;
    private string _fingerprint = string.Empty;
    private bool _accepting;
    private bool _disposed;

    public MobileGateway(
        MobileRegistry registry,
        HubConfig config,
        HubPaths paths,
        HubEventBus events,
        ManagedAppServerRegistry appServers,
        ProjectRegistry projects,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _config = config;
        _paths = paths;
        _events = events;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MobileGateway>();
        Projects = new MobileProjects(projects, appServers, paths);
    }

    public MobileRegistry Registry => _registry;

    public MobileProjects Projects { get; }

    public HubMobileState Snapshot()
    {
        var status = Volatile.Read(ref _status);
        return new HubMobileState(
            status.State,
            status.FailureCode,
            status.FailureMessage,
            _config.MobilePort,
            MobileNetwork.AdvertisedAddresses(),
            [.. _registry.ListDevices().Select(device => new HubMobileDevice(
                device.DeviceId,
                device.DisplayName,
                device.Platform,
                device.OsVersion,
                device.AppVersion,
                device.PairedAt,
                device.LastSeenAt,
                IsConnected(device.DeviceId)))],
            _registry.Relay is { } relay
                ? new HubMobileRelay(relay.Url, _relay?.State ?? MobileRelayLink.Connecting)
                : null);
    }

    public async Task StartIfEnabledAsync()
    {
        if (!_registry.Enabled)
            return;
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartListenerAsync().ConfigureAwait(false);
        }
        catch (HubProtocolException ex)
        {
            _logger.LogWarning("Mobile gateway did not start: {Code}", ex.Code);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<HubMobileState> EnableAsync()
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _registry.SetEnabled(true);
            if (_listener is null)
                await StartListenerAsync().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
        return Snapshot();
    }

    public async Task<HubMobileState> DisableAsync()
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _registry.SetEnabled(false);
            await StopListenerAsync(GatewayOff).ConfigureAwait(false);
            await StopRelayAsync().ConfigureAwait(false);
            SetStatus(GatewayStatus.Off);
        }
        finally
        {
            _stateGate.Release();
        }
        return Snapshot();
    }

    public HubMobilePairing MintPairing()
    {
        if (Volatile.Read(ref _status).State != GatewayStatus.On.State)
            throw new HubProtocolException("gatewayOff", "Phone access is off on this computer.", StatusCodes.Status409Conflict);

        var (pairingId, code, expiresAt) = _registry.MintPairing();
        var computer = Computer();
        var payload = "dotcraft://pair?v=1"
                      + "&name=" + Uri.EscapeDataString(computer.Name)
                      + "&port=" + computer.Port
                      + "&fp=" + Uri.EscapeDataString(computer.Fingerprint)
                      + "&addr=" + string.Join(',', computer.Addresses.Select(Uri.EscapeDataString))
                      + "&code=" + Uri.EscapeDataString(code);
        if (_registry.Relay is { } relay && _registry.HostId is { } hostId)
            payload += "&relay=" + Uri.EscapeDataString(relay.Url) + "&host=" + Uri.EscapeDataString(hostId);
        return new HubMobilePairing(pairingId, payload, expiresAt);
    }

    public async Task<bool> RevokeAsync(string deviceId)
    {
        if (!_registry.Revoke(deviceId))
            return false;
        _helloSeenAt.TryRemove(deviceId, out _);
        await CloseConnectionsAsync(connection => connection.DeviceId == deviceId, DeviceRevoked).ConfigureAwait(false);
        _events.Publish("mobile.deviceRevoked", data: new { deviceId });
        return true;
    }

    public MobilePairResponse Pair(MobilePairRequest request)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName) || request.Platform is not ("ios" or "android"))
            throw new HubProtocolException(
                "invalidRequest",
                "A display name and a platform of 'ios' or 'android' are required.",
                StatusCodes.Status400BadRequest);
        if (string.IsNullOrEmpty(request.Code) || _registry.TryConsumePairing(request.Code) is not { } pairingId)
            throw new HubProtocolException(
                "pairingCodeInvalid",
                "The pairing code is not valid. Ask the computer for a new one.",
                StatusCodes.Status400BadRequest);

        var (device, credential) = _registry.AddDevice(
            displayName,
            request.Platform,
            request.OsVersion?.Trim() ?? string.Empty,
            request.AppVersion?.Trim() ?? string.Empty);
        _events.Publish("mobile.devicePaired", data: new
        {
            deviceId = device.DeviceId,
            displayName = device.DisplayName,
            platform = device.Platform,
            pairingId
        });
        return new MobilePairResponse(device.DeviceId, credential, Computer());
    }

    public MobileHello Hello(MobileDeviceRecord device)
    {
        var seenAt = _registry.Touch(device.DeviceId);
        if (seenAt is { } at
            && (!_helloSeenAt.TryGetValue(device.DeviceId, out var last) || at - last >= HelloSeenInterval))
        {
            _helloSeenAt[device.DeviceId] = at;
            PublishSeen(device.DeviceId, at, IsConnected(device.DeviceId));
        }

        var computer = Computer();
        var relay = _registry.Relay is { } configured && _registry.HostId is { } hostId
            ? new MobileHelloRelay(configured.Url, hostId)
            : null;
        return new MobileHello(computer.Name, AppVersion.Informational, computer.Port, computer.Fingerprint, computer.Addresses, relay);
    }

    public Task<HubMobileState> SetRelayAsync(MobileRelayRequest request)
    {
        var url = request.Url?.Trim().TrimEnd('/') ?? string.Empty;
        var token = request.Token?.Trim() ?? string.Empty;
        if (!MobileRelayLink.IsValidUrl(url) || token.Length == 0)
            throw new HubProtocolException(
                "invalidRequest",
                "A relay URL starting with https:// or wss:// and a relay token are required.",
                StatusCodes.Status400BadRequest);
        return ChangeRelayAsync(new MobileRelayRecord(url, token));
    }

    public Task<HubMobileState> RemoveRelayAsync() => ChangeRelayAsync(null);

    public async Task RelayAsync(HttpContext context, MobileDeviceRecord device, string projectId)
    {
        var project = Projects.Find(projectId);
        if (project is null)
        {
            await MobileGatewayRoutes.WriteErrorAsync(context, "projectNotFound", "No project on this computer has that id.", StatusCodes.Status404NotFound);
            return;
        }
        if (Projects.AppServerEndpoint(project) is not { } endpoint)
        {
            await MobileGatewayRoutes.WriteErrorAsync(context, "projectNotRunning", "The project is not running on this computer.", StatusCodes.Status409Conflict);
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await MobileGatewayRoutes.WriteErrorAsync(context, "invalidRequest", "This route requires a WebSocket upgrade.", StatusCodes.Status400BadRequest);
            return;
        }

        using var appServer = new ClientWebSocket();
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await appServer.ConnectAsync(endpoint, connectTimeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or HttpRequestException)
        {
            await MobileGatewayRoutes.WriteErrorAsync(context, "projectNotRunning", "The project is not running on this computer.", StatusCodes.Status409Conflict);
            return;
        }

        using var phone = await context.WebSockets.AcceptWebSocketAsync();
        if (Track(device.DeviceId, phone, appServer) is not { } connection)
        {
            await RefuseAsync(phone, device.DeviceId);
            return;
        }
        try
        {
            await SatelliteWebSocketBridge.RelayAsync(appServer, phone, connection.Stopping);
        }
        finally
        {
            Untrack(connection);
        }
    }

    public async Task RunEventsAsync(HttpContext context, MobileDeviceRecord device)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await MobileGatewayRoutes.WriteErrorAsync(context, "invalidRequest", "This route requires a WebSocket upgrade.", StatusCodes.Status400BadRequest);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        if (Track(device.DeviceId, socket, upstream: null) is not { } connection)
        {
            await RefuseAsync(socket, device.DeviceId);
            return;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, connection.Stopping);
        try
        {
            var forwarding = ForwardEventsAsync(connection, _events.Subscribe(stop.Token), stop.Token);
            await DrainUntilClosedAsync(socket, stop.Token);
            await stop.CancelAsync();
            try
            {
                await forwarding;
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
            }
        }
        finally
        {
            Untrack(connection);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            await StopListenerAsync(reason: null).ConfigureAwait(false);
            await StopRelayAsync().ConfigureAwait(false);
            _certificate?.Dispose();
            _certificate = null;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task StartListenerAsync()
    {
        try
        {
            _certificate ??= MobileCertificate.LoadOrCreate(_paths.MobileCertificatePath);
            _fingerprint = MobileCertificate.Fingerprint(_certificate);
            _registry.EnsureHostId();
            lock (_connectionsGate)
                _accepting = true;
            _listener = await HubMobileListener.StartAsync(
                _config.MobileHost,
                _config.MobilePort,
                _certificate,
                this,
                _loggerFactory).ConfigureAwait(false);
            SetStatus(GatewayStatus.On);
            StartRelay();
        }
        catch (Exception ex)
        {
            lock (_connectionsGate)
                _accepting = false;
            var failure = ex as HubProtocolException ?? new HubProtocolException(
                "hubInternalError",
                "Phone access could not start.",
                StatusCodes.Status500InternalServerError,
                new { type = ex.GetType().Name });
            SetStatus(new GatewayStatus("failed", failure.Code, failure.Message));
            throw failure;
        }
    }

    private async Task StopListenerAsync(string? reason)
    {
        await CloseConnectionsAsync(_ => true, reason, stopAccepting: true).ConfigureAwait(false);
        if (_listener is { } listener)
        {
            _listener = null;
            await listener.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<HubMobileState> ChangeRelayAsync(MobileRelayRecord? relay)
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _registry.SetRelay(relay);
            await StopRelayAsync().ConfigureAwait(false);
            if (_listener is not null)
                StartRelay();
        }
        finally
        {
            _stateGate.Release();
        }
        PublishState();
        return Snapshot();
    }

    private void StartRelay()
    {
        if (_registry.Relay is not { } relay || _registry.HostId is not { } hostId)
            return;
        _relay = new MobileRelayLink(relay, hostId, _listener!.LocalEndPoint, PublishState, _loggerFactory.CreateLogger<MobileRelayLink>());
        _relay.Start();
    }

    private async Task StopRelayAsync()
    {
        if (_relay is not { } relay)
            return;
        _relay = null;
        await relay.DisposeAsync().ConfigureAwait(false);
    }

    private void SetStatus(GatewayStatus status)
    {
        Volatile.Write(ref _status, status);
        PublishState();
    }

    private void PublishState() => _events.Publish("mobile.stateChanged", data: Snapshot());

    private MobileComputer Computer() =>
        new(Environment.MachineName, _config.MobilePort, _fingerprint, MobileNetwork.AdvertisedAddresses());

    private bool IsConnected(string deviceId)
    {
        lock (_connectionsGate)
            return _connections.Any(connection => connection.DeviceId == deviceId);
    }

    private MobileConnection? Track(string deviceId, WebSocket socket, WebSocket? upstream)
    {
        MobileConnection connection;
        bool first;
        lock (_connectionsGate)
        {
            if (!_accepting || _registry.Find(deviceId) is null)
                return null;
            first = !_connections.Any(item => item.DeviceId == deviceId);
            connection = new MobileConnection(deviceId, socket, upstream);
            _connections.Add(connection);
        }
        if (first && _registry.Touch(deviceId) is { } seenAt)
            PublishSeen(deviceId, seenAt, connected: true);
        return connection;
    }

    private void Untrack(MobileConnection connection)
    {
        bool last;
        lock (_connectionsGate)
        {
            if (!_connections.Remove(connection))
                return;
            last = !_connections.Any(item => item.DeviceId == connection.DeviceId);
        }
        if (last && _registry.Touch(connection.DeviceId) is { } seenAt)
            PublishSeen(connection.DeviceId, seenAt, connected: false);
    }

    private void PublishSeen(string deviceId, DateTimeOffset lastSeenAt, bool connected) =>
        _events.Publish("mobile.deviceSeen", data: new { deviceId, lastSeenAt, connected });

    private async Task CloseConnectionsAsync(Func<MobileConnection, bool> match, string? reason, bool stopAccepting = false)
    {
        MobileConnection[] closing;
        lock (_connectionsGate)
        {
            if (stopAccepting)
                _accepting = false;
            closing = [.. _connections.Where(match)];
        }
        await Task.WhenAll(closing.Select(connection => connection.CloseAsync(reason))).ConfigureAwait(false);
    }

    private Task RefuseAsync(WebSocket socket, string deviceId) =>
        MobileConnection.CloseSocketAsync(socket, _registry.Find(deviceId) is null ? DeviceRevoked : GatewayOff);

    private static async Task ForwardEventsAsync(
        MobileConnection connection,
        ChannelReader<HubEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (var evt in events.ReadAllAsync(cancellationToken))
        {
            var type = evt.Kind switch
            {
                "appserver.running" => "projectStarted",
                "appserver.exited" => "projectStopped",
                _ => null
            };
            if (type is null || string.IsNullOrEmpty(evt.WorkspacePath))
                continue;
            await connection.SendAsync(
                new { type, projectId = MobileProjects.ProjectId(evt.WorkspacePath) },
                cancellationToken);
        }
    }

    private static async Task DrainUntilClosedAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (received.MessageType != WebSocketMessageType.Close)
                    continue;
                await MobileConnection.CloseSocketAsync(socket, description: null);
                return;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
    }

    private sealed record GatewayStatus(string State, string? FailureCode, string? FailureMessage)
    {
        public static readonly GatewayStatus Off = new("off", null, null);
        public static readonly GatewayStatus On = new("on", null, null);
    }

    private sealed class MobileConnection(string deviceId, WebSocket socket, WebSocket? upstream)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public string DeviceId => deviceId;

        public CancellationToken Stopping => _stop.Token;

        public async Task SendAsync(object message, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, MessageJson);
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task CloseAsync(string? reason)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await _sendGate.WaitAsync(timeout.Token);
                try
                {
                    if (reason is not null && upstream is null && socket.State == WebSocketState.Open)
                    {
                        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = reason }, MessageJson);
                        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                    }
                    await CloseSocketAsync(socket, reason, timeout.Token);
                }
                finally
                {
                    _sendGate.Release();
                }
            }
            catch (Exception)
            {
            }
            upstream?.Abort();
            _stop.CancelAfter(CloseGrace);
        }

        public static async Task CloseSocketAsync(
            WebSocket socket,
            string? description,
            CancellationToken cancellationToken = default)
        {
            var status = description switch
            {
                DeviceRevoked => WebSocketCloseStatus.PolicyViolation,
                GatewayOff => WebSocketCloseStatus.EndpointUnavailable,
                _ => WebSocketCloseStatus.NormalClosure
            };
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(status, description, cancellationToken);
            }
            catch (Exception)
            {
            }
        }
    }
}
