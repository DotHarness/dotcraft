using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class TransportBackpressureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullIncomingBuffer_RejectsRequestsButRoutesResponsesAndPreservesNotifications(bool webSocket)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var peer = new TransportPeer(webSocket);
        var ct = cts.Token;
        for (var id = 1; id <= 129; id++)
            await peer.SendAsync(new { jsonrpc = "2.0", id, method = "thread/list" }, ct);

        using var overload = await peer.ReceiveAsync(ct);
        var root = overload.RootElement;
        Assert.Equal(129, root.GetProperty("id").GetInt32());
        var error = root.GetProperty("error");
        Assert.Equal(AppServerErrors.ServerOverloadedCode, error.GetProperty("code").GetInt32());
        Assert.Equal("ServerOverloaded", error.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("errors.serverOverloaded", error.GetProperty("data").GetProperty("messageKey").GetString());

        var approval = await peer.Transport.StartClientRequestAsync("item/approval/request", new { }, ct);
        using var serverRequest = await peer.ReceiveAsync(ct);
        await peer.SendAsync(new
        {
            jsonrpc = "2.0",
            id = serverRequest.RootElement.GetProperty("id").GetInt32(),
            result = new { decision = "accept" }
        }, ct);
        Assert.Equal(approval.Id, serverRequest.RootElement.GetProperty("id").GetInt32());
        var response = await approval.Response;
        Assert.Equal("accept", response.Result!.Value.GetProperty("decision").GetString());

        await peer.SendAsync(new { jsonrpc = "2.0", method = "initialized" }, ct);
        for (var id = 1; id <= 128; id++)
        {
            var request = await peer.Transport.ReadMessageAsync(ct);
            Assert.NotNull(request);
            Assert.Equal(id, request.Id!.Value.GetInt32());
        }

        var notification = await peer.Transport.ReadMessageAsync(ct);
        Assert.NotNull(notification);
        Assert.True(notification.IsNotification);
        Assert.Equal("initialized", notification.Method);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EndedClientRequest_DropsLateResponsesWithoutBlockingLaterMessages(bool webSocket, bool timeout)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource();
        await using var peer = new TransportPeer(webSocket);
        var pending = await peer.Transport.StartClientRequestAsync(
            "item/approval/request", new { }, cancelled.Token,
            timeout ? TimeSpan.Zero : Timeout.InfiniteTimeSpan);
        using var request = await peer.ReceiveAsync(lifetime.Token);
        Assert.Equal(pending.Id, request.RootElement.GetProperty("id").GetInt32());
        if (timeout)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Response.WaitAsync(lifetime.Token));
        else
            cancelled.Cancel();

        await peer.SendAsync(new { jsonrpc = "2.0", id = pending.Id, result = new { decision = "accept" } }, lifetime.Token);
        await peer.SendAsync(new { jsonrpc = "2.0", method = "initialized" }, lifetime.Token);
        var notification = await peer.Transport.ReadMessageAsync(lifetime.Token);
        Assert.NotNull(notification);
        Assert.True(notification.IsNotification);
        Assert.Equal("initialized", notification.Method);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Response.WaitAsync(lifetime.Token));

        var next = await peer.Transport.StartClientRequestAsync("item/approval/request", new { }, lifetime.Token);
        using var nextRequest = await peer.ReceiveAsync(lifetime.Token);
        Assert.NotEqual(pending.Id, next.Id);
        Assert.Equal(next.Id, nextRequest.RootElement.GetProperty("id").GetInt32());
        await peer.SendAsync(new { jsonrpc = "2.0", id = next.Id, result = new { decision = "decline" } }, lifetime.Token);
        var response = await next.Response.WaitAsync(lifetime.Token);
        Assert.Equal("decline", response.Result!.Value.GetProperty("decision").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientConnectionCloses_CancelsOutstandingResponseWait(bool webSocket)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var peer = new TransportPeer(webSocket);
        var pending = await peer.Transport.StartClientRequestAsync(
            "item/approval/request", new { }, timeout: Timeout.InfiniteTimeSpan);
        using var request = await peer.ReceiveAsync(lifetime.Token);
        Assert.False(pending.Response.IsCompleted);

        await peer.CloseClientAsync(lifetime.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Response.WaitAsync(lifetime.Token));
        Assert.Null(await peer.Transport.ReadMessageAsync(lifetime.Token));
    }

    private sealed class TransportPeer : IAsyncDisposable
    {
        private readonly WebSocket? _client;
        private readonly StreamReader? _reader;
        private readonly StreamWriter? _writer;

        public IAppServerTransport Transport { get; }

        public TransportPeer(bool webSocket)
        {
            if (webSocket)
            {
                var (server, client) = WebSocketTestHelper.CreateWebSocketPair();
                _client = client;
                var transport = new WebSocketTransport(server);
                transport.Start();
                Transport = transport;
            }
            else
            {
                var incoming = new Pipe();
                var outgoing = new Pipe();
                _writer = new StreamWriter(incoming.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
                _reader = new StreamReader(outgoing.Reader.AsStream());
                var transport = StdioTransport.Create(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
                transport.Start();
                Transport = transport;
            }
        }

        public async Task SendAsync(object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message, SessionWireJsonOptions.Default);
            if (_client != null)
                await _client.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, ct);
            else
                await _writer!.WriteLineAsync(json.AsMemory(), ct);
        }

        public async Task<JsonDocument> ReceiveAsync(CancellationToken ct)
        {
            if (_client == null)
                return JsonDocument.Parse((await _reader!.ReadLineAsync(ct))!);

            var buffer = new byte[4096];
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await _client.ReceiveAsync(buffer.AsMemory(length), ct);
                length += result.Count;
            } while (!result.EndOfMessage);
            return JsonDocument.Parse(buffer.AsMemory(0, length));
        }

        public async Task CloseClientAsync(CancellationToken ct)
        {
            if (_client != null)
                await _client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client closed", ct);
            else
                await _writer!.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            _reader?.Dispose();
            _writer?.Dispose();
            _client?.Dispose();
        }
    }
}
