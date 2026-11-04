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

        var approval = peer.Transport.SendClientRequestAsync("item/approval/request", new { }, ct);
        using var serverRequest = await peer.ReceiveAsync(ct);
        await peer.SendAsync(new
        {
            jsonrpc = "2.0",
            id = serverRequest.RootElement.GetProperty("id").GetInt32(),
            result = new { decision = "accept" }
        }, ct);
        var response = await approval;
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

        public async ValueTask DisposeAsync()
        {
            await Transport.DisposeAsync();
            _reader?.Dispose();
            _writer?.Dispose();
            _client?.Dispose();
        }
    }
}
