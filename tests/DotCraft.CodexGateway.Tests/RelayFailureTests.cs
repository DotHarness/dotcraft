using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.CodexGateway.Relay;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed class RelayFailureTests
{
    private static readonly RemoteControlPeer Broken = new("phone", "broken");
    private static readonly RemoteControlPeer Healthy = new("phone", "healthy");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceWorkerOrResponseFailure_AndThrowingDispose_LeaveSecondStreamUsable(bool response)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var outbox = new RelayOutbox();
        FaultSession? broken = null;
        await using var sessions = new RelaySessions((peer, send) =>
        {
            var session = new FaultSession(send, peer == Broken);
            if (peer == Broken) broken = session;
            return session;
        }, outbox);
        using var socket = new RecordingSocket();
        var writer = outbox.WriteAsync(socket, ct);
        try
        {
            await sessions.ReceiveAsync(Envelope(Broken, 1, new { id = 1, method = "initialize" }), ct);
            await sessions.ReceiveAsync(Envelope(Healthy, 1, new { id = 1, method = "initialize" }), ct);
            await socket.Messages.Reader.ReadAsync(ct);
            await socket.Messages.Reader.ReadAsync(ct);

            await sessions.ReceiveAsync(Envelope(Broken, 2, response
                ? new { id = 99, result = "private-source-body" }
                : (object)new { id = 2, method = "sourceFailure" }), ct);
            await broken!.FaultObserved.Task.WaitAsync(ct);
            await sessions.ReceiveAsync(JsonSerializer.SerializeToElement(new
            {
                type = "client_closed", client_id = Broken.ClientId, stream_id = Broken.StreamId
            }), ct);
            await sessions.SweepAsync();
            Assert.True(broken.Disposed);

            await sessions.ReceiveAsync(Envelope(Healthy, 2, new { id = 3, method = "echo" }), ct);
            var reply = await socket.Messages.Reader.ReadAsync(ct);
            Assert.Equal(Healthy.StreamId, reply.GetProperty("stream_id").GetString());
            Assert.Equal(3, reply.GetProperty("message").GetProperty("id").GetInt32());
            Assert.False(outbox.HasFailed(Healthy));
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        }
    }

    [Fact]
    public async Task InputOverload_RetiresOnlyTheBlockedStream()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var outbox = new RelayOutbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        await using var sessions = new RelaySessions((peer, send) => peer == Broken
            ? new BlockedSession(entered, () => disposed = true)
            : new FaultSession(send, false), outbox);
        using var socket = new RecordingSocket();
        var writer = outbox.WriteAsync(socket, ct);
        try
        {
            await sessions.ReceiveAsync(Envelope(Broken, 1, new { id = 1, method = "initialize" }), ct);
            await entered.Task.WaitAsync(ct);
            for (var sequence = 2; sequence <= 130; sequence++)
                await sessions.ReceiveAsync(Envelope(Broken, sequence, new { id = sequence, method = "echo" }), ct);
            Assert.True(disposed);

            await sessions.ReceiveAsync(Envelope(Healthy, 1, new { id = 7, method = "initialize" }), ct);
            var reply = await socket.Messages.Reader.ReadAsync(ct);
            Assert.Equal(Healthy.StreamId, reply.GetProperty("stream_id").GetString());
            Assert.Equal(7, reply.GetProperty("message").GetProperty("id").GetInt32());
        }
        finally
        {
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        }
    }

    [Fact]
    public async Task UnacknowledgedOutput_DoesNotBlockOtherStream_AndOverflowStaysLocal()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var outbox = new RelayOutbox();
        var message = JsonSerializer.SerializeToElement(new { id = 1, result = new { } });
        for (var index = 0; index < 128; index++)
            await outbox.SendAsync(Broken, message, ct);
        using var socket = new RecordingSocket();
        var writer = outbox.WriteAsync(socket, ct);
        try
        {
            await outbox.SendAsync(Healthy, message, ct);
            JsonElement reply;
            do { reply = await socket.Messages.Reader.ReadAsync(ct); }
            while (reply.GetProperty("stream_id").GetString() != Healthy.StreamId);

            await Assert.ThrowsAsync<IOException>(() => outbox.SendAsync(Broken, message, ct));
            Assert.True(outbox.HasFailed(Broken));
            Assert.False(outbox.HasFailed(Healthy));
            await outbox.SendAsync(Healthy, JsonSerializer.SerializeToElement(new { id = 2, result = new { } }), ct);
            do { reply = await socket.Messages.Reader.ReadAsync(ct); }
            while (reply.GetProperty("stream_id").GetString() != Healthy.StreamId);
            Assert.Equal(2, reply.GetProperty("message").GetProperty("id").GetInt32());
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        }
    }

    [Fact]
    public async Task MalformedChunk_DoesNotDiscardAnotherStreamFromTheSameClient()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var outbox = new RelayOutbox();
        await using var sessions = new RelaySessions((_, send) => new FaultSession(send, false), outbox);
        using var socket = new RecordingSocket();
        var writer = outbox.WriteAsync(socket, ct);
        var raw = JsonSerializer.SerializeToUtf8Bytes(new { id = 8, method = "initialize" });
        JsonElement Chunk(RemoteControlPeer peer, int index, int count, byte[] bytes) =>
            JsonSerializer.SerializeToElement(new
            {
                type = "client_message_chunk", client_id = peer.ClientId, stream_id = peer.StreamId,
                seq_id = 1, segment_id = index, segment_count = count, message_size_bytes = raw.Length,
                message_chunk_base64 = Convert.ToBase64String(bytes)
            });
        try
        {
            var split = raw.Length / 2;
            await sessions.ReceiveAsync(Chunk(Healthy, 0, 2, raw[..split]), ct);
            await sessions.ReceiveAsync(Chunk(Broken, 0, 2, raw[..split]), ct);
            await sessions.ReceiveAsync(Chunk(Broken, 0, 0, []), ct);
            await sessions.ReceiveAsync(Chunk(Healthy, 1, 2, raw[split..]), ct);

            var reply = await socket.Messages.Reader.ReadAsync(ct);
            Assert.Equal(Healthy.StreamId, reply.GetProperty("stream_id").GetString());
            Assert.Equal(8, reply.GetProperty("message").GetProperty("id").GetInt32());
        }
        finally
        {
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
        }
    }

    private static JsonElement Envelope(RemoteControlPeer peer, int sequence, object message) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "client_message", client_id = peer.ClientId, stream_id = peer.StreamId, seq_id = sequence, message
        });

    private sealed class FaultSession(Func<JsonElement, CancellationToken, Task> send, bool broken) : IRemoteControlRpcSession
    {
        public TaskCompletionSource FaultObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public Task ReceiveAsync(JsonElement message, CancellationToken ct)
        {
            if (broken && (!message.TryGetProperty("method", out var method) || method.GetString() == "sourceFailure"))
            {
                FaultObserved.TrySetResult();
                throw new IOException("source overloaded: private-source-body");
            }
            return send(JsonSerializer.SerializeToElement(new { id = message.GetProperty("id"), result = new { } }), ct);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (broken)
                throw new InvalidOperationException("private-dispose-body");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockedSession(TaskCompletionSource entered, Action disposed) : IRemoteControlRpcSession
    {
        public async Task ReceiveAsync(JsonElement message, CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        public ValueTask DisposeAsync()
        {
            disposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public Channel<JsonElement> Messages { get; } = Channel.CreateUnbounded<JsonElement>();
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override WebSocketState State => _state;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
            => CloseAsync(status, description, ct);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(buffer.AsMemory());
            Messages.Writer.TryWrite(document.RootElement.Clone());
            return Task.CompletedTask;
        }
    }
}
