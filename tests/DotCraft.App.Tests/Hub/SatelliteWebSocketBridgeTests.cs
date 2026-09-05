using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using DotCraft.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatelliteWebSocketBridgeTests
{
    [Fact]
    public async Task Relay_IsByteIdentical_ForFragmentedMessages()
    {
        await using var pair = await SocketPair.CreateAsync();
        var payload = RandomNumberGenerator.GetBytes(1024 * 1024);
        var relay = SatelliteWebSocketBridge.RelayAsync(pair.Left, pair.Right, CancellationToken.None);

        var receiving = ReceiveAllAsync(pair.RightPeer, payload.Length);
        await SendFragmentedAsync(pair.LeftPeer, payload);
        var received = await receiving;

        Assert.Equal(payload, received);

        await pair.LeftPeer.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await relay;
    }

    [Fact]
    public async Task Relay_PropagatesClose_InBothDirections()
    {
        await using var pair = await SocketPair.CreateAsync();
        var relay = SatelliteWebSocketBridge.RelayAsync(pair.Left, pair.Right, CancellationToken.None);

        await pair.RightPeer.CloseOutputAsync(
            WebSocketCloseStatus.InternalServerError,
            "satelliteSessionFailed",
            CancellationToken.None);
        var buffer = new byte[64];
        var result = await pair.LeftPeer.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal("satelliteSessionFailed", pair.LeftPeer.CloseStatusDescription);
        await relay;
    }

    [Fact]
    public async Task Relay_CancelsOppositePump_OnFault()
    {
        await using var pair = await SocketPair.CreateAsync();
        var relay = SatelliteWebSocketBridge.RelayAsync(pair.Left, pair.Right, CancellationToken.None);

        pair.LeftPeer.Abort();

        await relay.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(relay.IsCompletedSuccessfully);
    }

    private static async Task SendFragmentedAsync(WebSocket socket, byte[] payload)
    {
        var offset = 0;
        while (offset < payload.Length)
        {
            var size = Math.Min(RandomNumberGenerator.GetInt32(1, 40_000), payload.Length - offset);
            var end = offset + size >= payload.Length;
            await socket.SendAsync(
                payload.AsMemory(offset, size),
                WebSocketMessageType.Binary,
                end,
                CancellationToken.None);
            offset += size;
        }
    }

    private static async Task<byte[]> ReceiveAllAsync(WebSocket socket, int expected)
    {
        var received = new List<byte>(expected);
        var buffer = new byte[64 * 1024];
        while (received.Count < expected)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            received.AddRange(buffer.AsSpan(0, result.Count).ToArray());
        }
        return [.. received];
    }

    /// <summary>Two real Kestrel loopback WebSockets standing in for the AppServer and the peer.</summary>
    private sealed class SocketPair : IAsyncDisposable
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebApplication _app = null!;
        private ClientWebSocket _leftPeer = null!;
        private ClientWebSocket _rightPeer = null!;

        public WebSocket Left { get; private set; } = null!;
        public WebSocket Right { get; private set; } = null!;
        public WebSocket LeftPeer => _leftPeer;
        public WebSocket RightPeer => _rightPeer;

        public static async Task<SocketPair> CreateAsync()
        {
            var pair = new SocketPair();
            var left = new TaskCompletionSource<WebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
            var right = new TaskCompletionSource<WebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);

            var app = WebApplication.CreateBuilder().Build();
            app.UseWebSockets();
            app.Map("/{side}", async (HttpContext context, string side) =>
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync();
                (side == "left" ? left : right).TrySetResult(socket);
                await pair._closed.Task;
            });
            var port = SatelliteHubFixture.GetAvailablePort();
            app.Urls.Add($"http://127.0.0.1:{port}");
            await app.StartAsync();
            pair._app = app;

            pair._leftPeer = new ClientWebSocket();
            pair._rightPeer = new ClientWebSocket();
            await pair._leftPeer.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/left"), CancellationToken.None);
            await pair._rightPeer.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/right"), CancellationToken.None);
            pair.Left = await left.Task;
            pair.Right = await right.Task;
            return pair;
        }

        public async ValueTask DisposeAsync()
        {
            _closed.TrySetResult();
            _leftPeer.Dispose();
            _rightPeer.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
