using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using McpToolCallPayload = DotCraft.Sessions.McpToolCallPayload;
using Xunit;

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
    public async Task PublishThreadStatusChanged_DeliversLiveWithoutRetainingForReplay()
    {
        var broker = new ThreadEventBroker("thread_001");
        using var liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var liveTask = CollectAsync(
            broker.SubscribeAsync(ct: liveCts.Token),
            expectedCount: 2,
            liveCts);

        broker.PublishThreadStatusChanged(ThreadStatus.Active, ThreadStatus.Archived);
        broker.PublishThreadStatusChanged(ThreadStatus.Archived, ThreadStatus.Active);

        var liveEvents = await liveTask;
        Assert.Equal(2, liveEvents.Count);
        Assert.All(liveEvents, evt => Assert.Equal(SessionEventType.ThreadStatusChanged, evt.EventType));
        Assert.Equal(ThreadStatus.Archived, liveEvents[0].StatusChangedPayload?.NewStatus);
        Assert.Equal(ThreadStatus.Active, liveEvents[1].StatusChangedPayload?.NewStatus);

        var channel = broker.CreateTurnChannel("turn_001");
        channel.EmitTurnCompleted(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        using var replayCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var replayEvents = await CollectAsync(
            broker.SubscribeAsync(replayRecent: true, ct: replayCts.Token),
            expectedCount: 1,
            replayCts);

        var replayed = Assert.Single(replayEvents);
        Assert.Equal(SessionEventType.TurnCompleted, replayed.EventType);
        Assert.True(replayed.IsReplay);
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

    [Fact]
    public async Task SubscribeAsync_ReplayedEventsAreMarkedWithoutMutatingLiveEvents()
    {
        var broker = new ThreadEventBroker("thread-1");
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.McpToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new McpToolCallPayload
            {
                ProviderFlatName = "mcp__server__tool",
                ToolName = "tool",
                Server = "server",
                SourceToolId = "tool",
                CallId = "call-1",
                Status = "completed"
            }
        };
        broker.PublishItemEvent(SessionEventType.ItemCompleted, item.TurnId, item);

        await using var replay = broker.SubscribeAsync(replayRecent: true).GetAsyncEnumerator();
        Assert.True(await replay.MoveNextAsync());
        Assert.True(replay.Current.IsReplay);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var live = broker.SubscribeAsync(ct: cancellation.Token).GetAsyncEnumerator();
        broker.PublishItemEvent(SessionEventType.ItemCompleted, item.TurnId, item);
        Assert.True(await live.MoveNextAsync());
        Assert.False(live.Current.IsReplay);
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
