using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.AppServer;

public sealed class ThreadRuntimeNotificationCoordinatorTests
{
    [Fact]
    public async Task ApplySignal_DeliversCommittedSnapshotsInOrderWithoutConcurrentWrites()
    {
        var transport = new BlockingFirstWriteTransport();
        var activeTransports = new ConcurrentDictionary<IAppServerTransport, AppServerConnection>();
        Assert.True(activeTransports.TryAdd(transport, new AppServerConnection()));
        var coordinator = new ThreadRuntimeNotificationCoordinator();
        var firstTurn = new SessionTurn
        {
            Id = "turn-1",
            ThreadId = "thread-1",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.Parse("2026-08-25T00:00:00Z")
        };

        Assert.True(coordinator.ApplySignal(
            firstTurn.ThreadId,
            SessionThreadRuntimeSignal.TurnStarted,
            firstTurn,
            activeTransports));
        await transport.FirstWriteObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        firstTurn.Status = TurnStatus.Completed;
        Assert.True(coordinator.ApplySignal(
            firstTurn.ThreadId,
            SessionThreadRuntimeSignal.TurnCompleted,
            firstTurn,
            activeTransports));

        var followUpTurn = new SessionTurn
        {
            Id = "turn-2",
            ThreadId = firstTurn.ThreadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.Parse("2026-08-25T00:01:00Z")
        };
        Assert.True(coordinator.ApplySignal(
            followUpTurn.ThreadId,
            SessionThreadRuntimeSignal.TurnStarted,
            followUpTurn,
            activeTransports));

        transport.ReleaseFirstWrite.TrySetResult();
        await transport.ThirdWriteObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.CompleteAsync();

        Assert.Equal(1, transport.MaxConcurrentWrites);
        Assert.Collection(
            transport.Writes,
            first => AssertRuntime(first, running: true, activeTurnId: "turn-1"),
            second => AssertRuntime(second, running: false, activeTurnId: null),
            third => AssertRuntime(third, running: true, activeTurnId: "turn-2"));
    }

    private static void AssertRuntime(JsonElement notification, bool running, string? activeTurnId)
    {
        Assert.Equal("thread/runtimeChanged", notification.GetProperty("method").GetString());
        var parameters = notification.GetProperty("params");
        Assert.Equal("thread-1", parameters.GetProperty("threadId").GetString());
        var runtime = parameters.GetProperty("runtime");
        Assert.Equal(running, runtime.GetProperty("running").GetBoolean());
        if (activeTurnId is null)
            Assert.False(runtime.TryGetProperty("activeTurnId", out _));
        else
            Assert.Equal(activeTurnId, runtime.GetProperty("activeTurnId").GetString());
    }

    private sealed class BlockingFirstWriteTransport : IAppServerTransport
    {
        private readonly object _writesGate = new();
        private int _concurrentWrites;

        public List<JsonElement> Writes { get; } = [];

        public int MaxConcurrentWrites { get; private set; }

        public TaskCompletionSource FirstWriteObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ThirdWriteObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public async Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            var concurrentWrites = Interlocked.Increment(ref _concurrentWrites);
            MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, concurrentWrites);
            var notification = JsonSerializer.SerializeToElement(message, SessionWireJsonOptions.Default);
            int writeCount;
            lock (_writesGate)
            {
                Writes.Add(notification);
                writeCount = Writes.Count;
            }

            if (writeCount == 1)
            {
                FirstWriteObserved.TrySetResult();
                await ReleaseFirstWrite.Task.WaitAsync(ct);
            }
            else if (writeCount == 3)
            {
                ThirdWriteObserved.TrySetResult();
            }

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
