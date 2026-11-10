using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpElicitationBackpressureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowRecipientResolution_DoesNotBlockAnotherRecipientsAnswer(bool failDelivery)
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var slow = new Peer(subscribers, blockResolution: true);
        await using var answering = new Peer(subscribers);
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context(), "test", Request(), default).AsTask();
        var slowId = await slow.ReadPromptAsync();
        var answeringId = await answering.ReadPromptAsync();

        await answering.AnswerAsync(answeringId);
        await slow.Output.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("accept", (await answer.WaitAsync(TimeSpan.FromSeconds(10))).Action);
        Assert.False(slow.Output.Release.Task.IsCompleted);
        await answering.AssertResolvedAsync(answeringId);

        slow.Output.FailDelivery = failDelivery;
        slow.Output.Release.TrySetResult();
        if (failDelivery)
        {
            await slow.Connection.Closed.WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            await slow.AssertResolvedAsync(slowId);
            await slow.Connection.CompleteThreadNotificationsAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(slow.Connection.IsClosed);
        }
        Assert.False(answering.Connection.IsClosed);
    }

    [Fact]
    public async Task SlowResolution_DoesNotBlockInvocationCancellation()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, blockResolution: true);
        using var cancellation = new CancellationTokenSource();
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context(), "test", Request(), cancellation.Token).AsTask();
        var id = await peer.ReadPromptAsync();

        await cancellation.CancelAsync();
        await peer.Output.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(peer.Output.Release.Task.IsCompleted);

        peer.Output.Release.TrySetResult();
        await peer.AssertResolvedAsync(id);
    }

    private static ToolInvocationContext Context() => new(
        "thread-a", "turn-a", "call-a", ToolInvocationAudience.Model,
        new ToolName("test", "choose"),
        new ToolDefinitionId(ToolSourceKind.Mcp, "test", new SourceToolId("choose")),
        new RuntimeBindingId("test:1"), 1, DateTimeOffset.UtcNow);

    private static ElicitRequestParams Request() => new()
    {
        Mode = "url", Url = "https://example.com/verify", ElicitationId = "verify", Message = "Verify"
    };

    private sealed class Peer : IAsyncDisposable
    {
        private readonly StreamWriter _clientWriter;
        private readonly StreamReader _clientReader;
        private readonly StdioTransport _transport;
        public AppServerConnection Connection { get; } = new();
        public GatedFlushStream Output { get; }

        public Peer(AppServerThreadSubscribers subscribers, bool blockResolution = false)
        {
            var input = new Pipe();
            var output = new Pipe();
            Output = new GatedFlushStream(output.Writer.AsStream(), blockResolution);
            _clientWriter = new StreamWriter(input.Writer.AsStream(), new UTF8Encoding(false)) { AutoFlush = true };
            _clientReader = new StreamReader(output.Reader.AsStream());
            _transport = StdioTransport.Create(input.Reader.AsStream(), Output);
            _transport.Start();
            Connection.TryMarkInitialized(new ClientConnectionInfo { Name = "test", Version = "1" },
                new ClientConnectionCapabilities { McpElicitation = true });
            Connection.MarkClientReady();
            Connection.TryAddSubscription("thread-a", new CancellationTokenSource());
            subscribers.Register(_transport, Connection);
        }

        public async Task<int> ReadPromptAsync()
        {
            using var message = await ReadAsync();
            Assert.Equal("mcpServer/elicitation/request", message.RootElement.GetProperty("method").GetString());
            return message.RootElement.GetProperty("id").GetInt32();
        }

        public Task AnswerAsync(int id) =>
            _clientWriter.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new { action = "accept" } }));

        public async Task AssertResolvedAsync(int id)
        {
            using var message = await ReadAsync();
            Assert.Equal("serverRequest/resolved", message.RootElement.GetProperty("method").GetString());
            Assert.Equal(id, message.RootElement.GetProperty("params").GetProperty("requestId").GetInt32());
            Assert.Equal("thread-a", message.RootElement.GetProperty("params").GetProperty("threadId").GetString());
        }

        private async Task<JsonDocument> ReadAsync() =>
            JsonDocument.Parse((await _clientReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);

        public async ValueTask DisposeAsync()
        {
            Output.Release.TrySetResult();
            Connection.MarkClosed();
            await Connection.CompleteThreadNotificationsAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await _transport.DisposeAsync();
            _clientWriter.Dispose();
            _clientReader.Dispose();
        }
    }

    private sealed class GatedFlushStream(Stream inner, bool blockResolution) : Stream
    {
        private int _flushes;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailDelivery { get; set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _flushes) == 2 && blockResolution)
            {
                Blocked.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                if (FailDelivery)
                    throw new IOException("Notification delivery failed.");
            }
            await inner.FlushAsync(cancellationToken);
        }
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
