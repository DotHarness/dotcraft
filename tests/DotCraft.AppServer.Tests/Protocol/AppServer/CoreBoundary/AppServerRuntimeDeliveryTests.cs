using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerRuntimeDeliveryTests
{
    [Fact]
    public async Task LifecycleEvents_SendTheirPublishedRuntimeBeforeTheTurnNotification()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var current = new ThreadSummaryRuntime { Running = true, Busy = true, ActiveTurnId = "turn-2" };
        var events = new[]
        {
            new SessionEvent
            {
                ThreadId = "thread-1", TurnId = "turn-2", EventType = SessionEventType.TurnStarted,
                Payload = new SessionTurn { Id = "turn-2", ThreadId = "thread-1", Status = TurnStatus.Running },
                RuntimeSnapshot = current
            },
            new SessionEvent
            {
                ThreadId = "thread-1", TurnId = "turn-1", EventType = SessionEventType.TurnCompleted,
                Payload = new SessionTurn { Id = "turn-1", ThreadId = "thread-1", Status = TurnStatus.Completed },
                RuntimeSnapshot = current
            }
        };
        await new AppServerEventDispatcher(Stream(), harness.Connection, harness.Transport, harness.Service).RunAsync();
        await harness.Connection.CompleteThreadNotificationsAsync();
        var sent = harness.Transport.DrainSent();
        Assert.Equal(["thread/status/changed", "turn/started", "thread/status/changed", "turn/completed"],
            sent.Select(message => message.RootElement.GetProperty("method").GetString()));
        foreach (var index in new[] { 0, 2 })
        {
            var runtime = sent[index].RootElement.GetProperty("params").GetProperty("runtime");
            Assert.True(runtime.GetProperty("running").GetBoolean());
            Assert.Equal("turn-2", runtime.GetProperty("activeTurnId").GetString());
        }

        async IAsyncEnumerable<SessionEvent> Stream()
        {
            foreach (var evt in events)
            {
                yield return evt;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task FailedQueuedWrite_SettlesAllWaitingSendersAndClosesTheConnection()
    {
        var transport = new FailingTransport();
        var connection = new AppServerConnection();
        var first = connection.SendThreadNotificationAsync(transport, "thread/renamed", new Contract.ThreadRenamedNotification { ThreadId = "thread-1", DisplayName = "first" });
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = connection.SendThreadNotificationAsync(transport, "thread/renamed", new Contract.ThreadRenamedNotification { ThreadId = "thread-1", DisplayName = "second" });
        transport.Release.TrySetResult();
        await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<IOException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
        await connection.CompleteThreadNotificationsAsync();
        Assert.True(connection.IsClosed);
    }

    private sealed class FailingTransport : IAppServerTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);
        public async Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            throw new IOException("Transport failed.");
        }
        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method, object? parameters, CancellationToken ct = default, TimeSpan? timeout = null) =>
            throw new NotSupportedException();
    }
}
