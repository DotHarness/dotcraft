using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Tests for thread-scoped event fan-out and replay behavior.
/// </summary>
public sealed class ThreadEventBrokerTests
{
    [Fact]
    public async Task TurnChannel_EventsArePublishedToThreadSubscribers()
    {
        var broker = new ThreadEventBroker("thread_001");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var collectTask = CollectAsync(broker.SubscribeAsync(ct: cts.Token), expectedCount: 1, cts);

        var channel = broker.CreateTurnChannel("turn_001");
        channel.EmitTurnStarted(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var events = await collectTask;
        Assert.Single(events);
        Assert.Equal(SessionEventType.TurnStarted, events[0].EventType);
        Assert.Equal("thread_001", events[0].ThreadId);
        Assert.Equal("turn_001", events[0].TurnId);
    }

    [Fact]
    public async Task SubscribeAsync_ReplayRecent_ReplaysBufferedThreadEvents()
    {
        var broker = new ThreadEventBroker("thread_001");
        broker.PublishThreadEvent(
            SessionEventType.ThreadCreated,
            new SessionThread
            {
                Id = "thread_001",
                WorkspacePath = "/workspace",
                OriginChannel = "cli",
                Status = ThreadStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = await CollectAsync(broker.SubscribeAsync(replayRecent: true, ct: cts.Token), expectedCount: 1, cts);

        Assert.Single(events);
        Assert.Equal(SessionEventType.ThreadCreated, events[0].EventType);
        Assert.Null(events[0].TurnId);
    }

    [Fact]
    public async Task PublishSystemEvent_PublishesThreadScopedSystemPayload()
    {
        var broker = new ThreadEventBroker("thread_001");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var collectTask = CollectAsync(broker.SubscribeAsync(ct: cts.Token), expectedCount: 1, cts);

        broker.PublishSystemEvent("consolidationFailed", message: "provider unavailable");

        var events = await collectTask;
        Assert.Single(events);
        var payload = events[0].SystemEventPayload;
        Assert.NotNull(payload);
        Assert.Equal(SessionEventType.SystemEvent, events[0].EventType);
        Assert.Equal("thread_001", events[0].ThreadId);
        Assert.Null(events[0].TurnId);
        Assert.Equal("consolidationFailed", payload.Kind);
        Assert.Equal("provider unavailable", payload.Message);
    }

    private static async Task<List<SessionEvent>> CollectAsync(
        IAsyncEnumerable<SessionEvent> events,
        int expectedCount,
        CancellationTokenSource cts)
    {
        var collected = new List<SessionEvent>();

        try
        {
            await foreach (var evt in events.WithCancellation(cts.Token))
            {
                collected.Add(evt);
                if (collected.Count >= expectedCount)
                {
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected once enough events have been collected.
        }

        return collected;
    }
}
