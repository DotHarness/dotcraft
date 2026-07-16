using DotCraft.AppServer;
using DotCraft.Protocol.AppServer;

namespace DotCraft.App.Tests.AppServer;

public sealed class OrderedAppServerNotificationQueueTests
{
    [Fact]
    public async Task Queue_WritesNotificationsInArrivalOrderWithoutConcurrentTransportWrites()
    {
        var transport = new BlockingFirstWriteTransport();
        var queue = new OrderedAppServerNotificationQueue(transport, () => { });

        Assert.True(queue.Enqueue(1));
        Assert.True(queue.Enqueue(2));
        Assert.True(queue.Enqueue(3));

        await transport.FirstWriteObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1], transport.StartedWrites);

        transport.ReleaseFirstWrite.TrySetResult();
        queue.Complete();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2, 3], transport.CompletedWrites);
        Assert.Equal(1, transport.MaxConcurrentWrites);
    }

    private sealed class BlockingFirstWriteTransport : IAppServerTransport
    {
        private int _concurrentWrites;

        public List<int> StartedWrites { get; } = [];

        public List<int> CompletedWrites { get; } = [];

        public int MaxConcurrentWrites { get; private set; }

        public TaskCompletionSource FirstWriteObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public async Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            var value = Assert.IsType<int>(message);
            var concurrent = Interlocked.Increment(ref _concurrentWrites);
            MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, concurrent);
            StartedWrites.Add(value);
            if (value == 1)
            {
                FirstWriteObserved.TrySetResult();
                await ReleaseFirstWrite.Task.WaitAsync(ct);
            }
            CompletedWrites.Add(value);
            Interlocked.Decrement(ref _concurrentWrites);
        }

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) =>
            Task.FromException<AppServerIncomingMessage>(new NotSupportedException());
    }
}
