using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed class ThreadRuntimeNotificationCoordinatorTests
{
    [Fact]
    public async Task ApplySnapshot_DeliversCreationAndRuntimeInOrderWithoutConcurrentWrites()
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

        var thread = new SessionThread { Id = firstTurn.ThreadId, WorkspacePath = Path.GetTempPath() };
        coordinator.BroadcastThreadStarted(new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(thread.ToWire())
        }, activeTransports, static (_, _) => true);
        thread.Turns.Add(firstTurn);
        Assert.True(coordinator.ApplySnapshot(
            firstTurn.ThreadId,
            ThreadSummaryRuntime.FromThread(thread),
            activeTransports,
            static _ => true));
        await transport.FirstWriteObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        firstTurn.Status = TurnStatus.Completed;
        Assert.True(coordinator.ApplySnapshot(
            firstTurn.ThreadId,
            ThreadSummaryRuntime.FromThread(thread),
            activeTransports,
            static _ => true));

        var followUpTurn = new SessionTurn
        {
            Id = "turn-2",
            ThreadId = firstTurn.ThreadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.Parse("2026-08-25T00:01:00Z")
        };
        thread.Turns.Add(followUpTurn);
        Assert.True(coordinator.ApplySnapshot(
            followUpTurn.ThreadId,
            ThreadSummaryRuntime.FromThread(thread),
            activeTransports,
            static _ => true));

        transport.ReleaseFirstWrite.TrySetResult();
        await coordinator.CompleteAsync();

        Assert.Equal(1, transport.MaxConcurrentWrites);
        Assert.Collection(
            transport.Writes,
            created => Assert.Equal("thread/started", created.GetProperty("method").GetString()),
            first => AssertRuntime(first, running: true, activeTurnId: "turn-1"),
            second => AssertRuntime(second, running: false, activeTurnId: null),
            third => AssertRuntime(third, running: true, activeTurnId: "turn-2"));
    }

    [Fact]
    public async Task ApplySnapshot_ForwardsPlanConfirmationAndItsRemoval()
    {
        var transport = new RecordingTransport();
        var activeTransports = new ConcurrentDictionary<IAppServerTransport, AppServerConnection>();
        Assert.True(activeTransports.TryAdd(transport, new AppServerConnection()));
        var coordinator = new ThreadRuntimeNotificationCoordinator();
        const string threadId = "thread-1";
        Assert.True(coordinator.ApplySnapshot(
            threadId,
            new ThreadSummaryRuntime { WaitingOnPlanConfirmation = true },
            activeTransports,
            static _ => true));
        Assert.True(coordinator.ApplySnapshot(
            threadId,
            new ThreadSummaryRuntime(),
            activeTransports,
            static _ => true));
        await coordinator.CompleteAsync();

        Assert.Collection(
            transport.Writes,
            first => Assert.True(RuntimeOf(first).GetProperty("waitingOnPlanConfirmation").GetBoolean()),
            second =>
            {
                AssertRuntime(second, running: false, activeTurnId: null);
                Assert.False(RuntimeOf(second).GetProperty("waitingOnPlanConfirmation").GetBoolean());
            });
    }

    private static JsonElement RuntimeOf(JsonElement notification) =>
        notification.GetProperty("params").GetProperty("runtime");

    private static void AssertRuntime(JsonElement notification, bool running, string? activeTurnId)
    {
        Assert.Equal("thread/status/changed", notification.GetProperty("method").GetString());
        var parameters = notification.GetProperty("params");
        Assert.Equal("thread-1", parameters.GetProperty("threadId").GetString());
        var runtime = parameters.GetProperty("runtime");
        Assert.Equal(running, runtime.GetProperty("running").GetBoolean());
        if (activeTurnId is null)
            Assert.False(runtime.TryGetProperty("activeTurnId", out _));
        else
            Assert.Equal(activeTurnId, runtime.GetProperty("activeTurnId").GetString());
    }

    private sealed class RecordingTransport : IAppServerTransport
    {
        private readonly object _writesGate = new();

        public List<JsonElement> Writes { get; } = [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            var notification = JsonSerializer.SerializeToElement(message, SessionWireJsonOptions.Default);
            lock (_writesGate)
                Writes.Add(notification);
            return Task.CompletedTask;
        }

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) =>
            Task.FromException<AppServerIncomingMessage>(new NotSupportedException());
    }

    private sealed class BlockingFirstWriteTransport : IAppServerTransport
    {
        private readonly object _writesGate = new();
        private int _concurrentWrites;

        public List<JsonElement> Writes { get; } = [];

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
