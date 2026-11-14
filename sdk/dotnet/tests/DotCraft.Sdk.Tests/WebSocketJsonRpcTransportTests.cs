using System.Net.WebSockets;
using System.Text;
using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class WebSocketJsonRpcTransportTests
{
    [Fact]
    public async Task FragmentedServerMessageAboveFourMiB_IsReadAsOneMessage()
    {
        var text = new string('x', 4 * 1024 * 1024 + 123);
        var socket = new FragmentedSocket(Encoding.UTF8.GetBytes("{\"result\":\"" + text + "\"}"));
        await using var transport = WebSocketJsonRpcTransport.FromWebSocket(socket);

        using var message = await transport.ReadAsync();

        Assert.NotNull(message);
        Assert.Equal(text, message.RootElement.GetProperty("result").GetString());
        Assert.True(socket.FragmentsRead > 1);
        Assert.Null(await transport.ReadAsync());
    }

    private sealed class FragmentedSocket(byte[] message) : WebSocket
    {
        private int _offset;
        private WebSocketState _state = WebSocketState.Open;
        public int FragmentsRead { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override WebSocketState State => _state;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
            => CloseAsync(closeStatus, statusDescription, ct);
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
            => throw new NotSupportedException();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_offset == message.Length)
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            var count = Math.Min(buffer.Count, Math.Min(16 * 1024, message.Length - _offset));
            message.AsSpan(_offset, count).CopyTo(buffer.AsSpan());
            _offset += count;
            FragmentsRead++;
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, _offset == message.Length));
        }
    }
}
