using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.Relay;

internal sealed class MobileRelayServer
{
    public const string HostOffline = "hostOffline";

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);

    private readonly byte[] _tokenHash;
    private readonly CancellationToken _stopping;
    private readonly ConcurrentDictionary<string, HostChannel> _hosts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingTunnel> _tunnels = new(StringComparer.Ordinal);

    private MobileRelayServer(string token, CancellationToken stopping)
    {
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        _stopping = stopping;
    }

    public static WebApplication Build(string listenUrl, string token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        var app = builder.Build();
        app.Urls.Add(listenUrl);
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20),
            KeepAliveTimeout = TimeSpan.FromSeconds(20)
        });
        var relay = new MobileRelayServer(token, app.Lifetime.ApplicationStopping);
        app.Map("/r/host", relay.HostAsync);
        app.Map("/r/connect", relay.ConnectAsync);
        app.Map("/r/accept", relay.AcceptAsync);
        return app;
    }

    private async Task HostAsync(HttpContext context)
    {
        if (!IsAuthorized(context.Request))
        {
            await UnauthorizedAsync(context);
            return;
        }
        var hostId = context.Request.Query["host"].ToString();
        if (!context.WebSockets.IsWebSocketRequest || hostId.Length == 0)
        {
            await InvalidRequestAsync(context);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var channel = new HostChannel(socket);
        HostChannel? replaced = null;
        _hosts.AddOrUpdate(hostId, channel, (_, previous) =>
        {
            replaced = previous;
            return channel;
        });
        if (replaced is not null)
            _ = replaced.ReplaceAsync();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping, channel.Replaced);
        try
        {
            var buffer = new byte[1024];
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), stop.Token);
                if (received.MessageType != WebSocketMessageType.Close)
                    continue;
                await CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, null);
                return;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            _hosts.TryRemove(KeyValuePair.Create(hostId, channel));
        }
    }

    private async Task ConnectAsync(HttpContext context)
    {
        var hostId = context.Request.Query["host"].ToString();
        if (!_hosts.TryGetValue(hostId, out var host))
        {
            await ErrorAsync(context, StatusCodes.Status404NotFound, HostOffline, "The computer is not connected to this relay.");
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await InvalidRequestAsync(context);
            return;
        }

        using var phone = await context.WebSockets.AcceptWebSocketAsync();
        var tunnelId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var tunnel = new PendingTunnel();
        _tunnels[tunnelId] = tunnel;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping);
        try
        {
            WebSocket? hub = null;
            try
            {
                await host.SendOpenAsync(tunnelId, stop.Token);
                hub = await tunnel.Hub.Task.WaitAsync(OpenTimeout, stop.Token);
            }
            catch (Exception)
            {
            }
            if (hub is null && !_tunnels.TryRemove(tunnelId, out _))
                hub = await tunnel.Hub.Task;
            if (hub is null)
            {
                await CloseQuietlyAsync(phone, WebSocketCloseStatus.EndpointUnavailable, HostOffline);
                return;
            }
            await SatelliteWebSocketBridge.RelayAsync(phone, hub, stop.Token);
        }
        finally
        {
            tunnel.Done.TrySetResult();
        }
    }

    private async Task AcceptAsync(HttpContext context)
    {
        if (!IsAuthorized(context.Request))
        {
            await UnauthorizedAsync(context);
            return;
        }
        var tunnelId = context.Request.Query["tunnel"].ToString();
        if (!context.WebSockets.IsWebSocketRequest || tunnelId.Length == 0)
        {
            await InvalidRequestAsync(context);
            return;
        }
        if (!_tunnels.TryRemove(tunnelId, out var tunnel))
        {
            await ErrorAsync(context, StatusCodes.Status404NotFound, "tunnelNotFound", "No phone is waiting on that tunnel.");
            return;
        }

        WebSocket socket;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
        }
        catch (Exception)
        {
            tunnel.Hub.TrySetResult(null);
            throw;
        }
        using (socket)
        {
            tunnel.Hub.TrySetResult(socket);
            await tunnel.Done.Task;
        }
    }

    private bool IsAuthorized(HttpRequest request) =>
        SatelliteWire.ReadBearer(request.Headers.Authorization) is { } bearer
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(bearer)), _tokenHash);

    private static Task UnauthorizedAsync(HttpContext context) =>
        ErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "Missing or invalid relay token.");

    private static Task InvalidRequestAsync(HttpContext context) =>
        ErrorAsync(context, StatusCodes.Status400BadRequest, "invalidRequest", "This route requires a WebSocket upgrade and its query parameter.");

    private static Task ErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new HubErrorResponse(new HubError(code, message, null)), MessageJson);
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string? description)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(status, description, timeout.Token);
        }
        catch (Exception)
        {
        }
    }

    private sealed class HostChannel(WebSocket socket)
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly CancellationTokenSource _replaced = new();

        public CancellationToken Replaced => _replaced.Token;

        public async Task SendOpenAsync(string tunnelId, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "open", tunnel = tunnelId }, MessageJson);
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

        public async Task ReplaceAsync()
        {
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "replaced");
            _replaced.CancelAfter(CloseGrace);
        }
    }

    private sealed class PendingTunnel
    {
        public TaskCompletionSource<WebSocket?> Hub { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
