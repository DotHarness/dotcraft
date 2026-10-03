using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed class MobileRelayLink : IAsyncDisposable
{
    public const string Connecting = "connecting";
    public const string Connected = "connected";
    public const string Failed = "failed";

    private const int TunnelBufferBytes = 32 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly MobileRelayRecord _relay;
    private readonly string _hostId;
    private readonly int _gatewayPort;
    private readonly Action _stateChanged;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task _run = Task.CompletedTask;
    private string _state = Connecting;

    public MobileRelayLink(MobileRelayRecord relay, string hostId, int gatewayPort, Action stateChanged, ILogger logger)
    {
        _relay = relay;
        _hostId = hostId;
        _gatewayPort = gatewayPort;
        _stateChanged = stateChanged;
        _logger = logger;
    }

    public string State => Volatile.Read(ref _state);

    public void Start() => _run = Task.Run(() => RunAsync(_stop.Token));

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "wss" or "http" or "ws"
        && uri.Host.Length > 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            while (true)
            {
                ClientWebSocket control;
                try
                {
                    control = await ConnectAsync("/r/host", "host", _hostId, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Mobile relay connection failed: {Reason}", ex.GetType().Name);
                    SetState(Failed);
                    await Task.Delay(Backoff(attempt++), cancellationToken);
                    continue;
                }

                using (control)
                {
                    attempt = 0;
                    SetState(Connected);
                    try
                    {
                        await ReceiveOpensAsync(control, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                SetState(Connecting);
                await Task.Delay(Backoff(attempt++), cancellationToken);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReceiveOpensAsync(ClientWebSocket control, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var received = await control.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close || !received.EndOfMessage)
                return;
            if (TryReadOpen(buffer.AsMemory(0, received.Count)) is { } tunnelId)
                _ = RunTunnelAsync(tunnelId, cancellationToken);
        }
    }

    private static string? TryReadOpen(ReadOnlyMemory<byte> message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            return root.TryGetProperty("type", out var type) && type.ValueEquals("open")
                   && root.TryGetProperty("tunnel", out var tunnel) && tunnel.ValueKind == JsonValueKind.String
                ? tunnel.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task RunTunnelAsync(string tunnelId, CancellationToken cancellationToken)
    {
        try
        {
            using var gateway = new TcpClient { NoDelay = true };
            await gateway.ConnectAsync(IPAddress.Loopback, _gatewayPort, cancellationToken);
            using var relay = await ConnectAsync("/r/accept", "tunnel", tunnelId, cancellationToken);
            using var tunnel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stream = gateway.GetStream();
            var toPhone = CopyToRelayAsync(stream, relay, tunnel.Token);
            var toGateway = CopyToGatewayAsync(relay, gateway.Client, stream, tunnel.Token);
            await Task.WhenAny(toPhone, toGateway);
            await tunnel.CancelAsync();
            await Task.WhenAll(toPhone, toGateway);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Mobile relay tunnel ended: {Reason}", ex.GetType().Name);
        }
    }

    private static async Task CopyToRelayAsync(NetworkStream gateway, WebSocket relay, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(TunnelBufferBytes);
        try
        {
            while (true)
            {
                var read = await gateway.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    await relay.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    return;
                }
                await relay.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or WebSocketException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task CopyToGatewayAsync(WebSocket relay, Socket socket, NetworkStream gateway, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(TunnelBufferBytes);
        try
        {
            while (true)
            {
                var received = await relay.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    socket.Shutdown(SocketShutdown.Send);
                    return;
                }
                await gateway.WriteAsync(buffer.AsMemory(0, received.Count), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or WebSocketException or SocketException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(string path, string name, string value, CancellationToken cancellationToken)
    {
        var builder = new UriBuilder(_relay.Url);
        builder.Scheme = builder.Scheme is "https" or "wss" ? "wss" : "ws";
        builder.Path = builder.Path.TrimEnd('/') + path;
        builder.Query = name + "=" + Uri.EscapeDataString(value);
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + _relay.Token);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(builder.Uri, timeout.Token);
            return socket;
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }
    }

    private void SetState(string state)
    {
        if (Interlocked.Exchange(ref _state, state) == state)
            return;
        _logger.LogInformation("Mobile relay {State}", state);
        _stateChanged();
    }

    private static TimeSpan Backoff(int attempt)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 5))));
        return baseDelay * (1 + RandomNumberGenerator.GetInt32(0, 201) / 1000d);
    }
}
