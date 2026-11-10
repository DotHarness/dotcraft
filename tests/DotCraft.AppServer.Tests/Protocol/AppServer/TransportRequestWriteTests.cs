using System.IO.Pipelines;
using System.Text.Json;
using DotCraft.AppServer;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class TransportRequestWriteTests
{
    [Fact]
    public async Task CancellationAfterRequestIsWritten_ReturnsHandleForEndingTheOriginalRequest()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var requestCancellation = new CancellationTokenSource();
        var input = new Pipe();
        var output = new Pipe();
        await using var gatedOutput = new GatedFlushStream(output.Writer.AsStream());
        using var clientReader = new StreamReader(output.Reader.AsStream());
        await using var transport = StdioTransport.Create(input.Reader.AsStream(), gatedOutput);
        transport.Start();

        var starting = transport.StartClientRequestAsync(
            "mcpServer/elicitation/request", new { threadId = "thread-a" },
            requestCancellation.Token, Timeout.InfiniteTimeSpan);
        using var request = JsonDocument.Parse((await clientReader.ReadLineAsync(lifetime.Token))!);
        await gatedOutput.FlushStarted.Task.WaitAsync(lifetime.Token);
        Assert.False(starting.IsCompleted);

        requestCancellation.Cancel();
        var pending = await starting.WaitAsync(lifetime.Token);
        var requestId = request.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(requestId, pending.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Response.WaitAsync(lifetime.Token));

        await transport.NotifyContractAsync(Contract.AppServerRpc.ServerRequestResolved,
            new Contract.ServerRequestResolvedNotification { ThreadId = "thread-a", RequestId = pending.Id },
            lifetime.Token);
        using var resolved = JsonDocument.Parse((await clientReader.ReadLineAsync(lifetime.Token))!);
        Assert.Equal("serverRequest/resolved", resolved.RootElement.GetProperty("method").GetString());
        Assert.Equal(requestId, resolved.RootElement.GetProperty("params").GetProperty("requestId").GetInt32());
    }

    private sealed class GatedFlushStream(Stream inner) : Stream
    {
        private int _flushes;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FlushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _flushes) == 1)
            {
                FlushStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
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
