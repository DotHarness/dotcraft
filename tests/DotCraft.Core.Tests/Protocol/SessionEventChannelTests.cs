using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionEventChannelTests
{
    private const string TestThreadId = "thread_test_01";
    private const string TestTurnId = "turn_test_001";

    private static SessionEventChannel MakeChannel() =>
        new(TestThreadId, TestTurnId);

    private static SessionTurn MakeTurn() => new()
    {
        Id = TestTurnId,
        ThreadId = TestThreadId,
        Status = TurnStatus.Running,
        StartedAt = DateTimeOffset.UtcNow
    };

    private static SessionItem MakeItem(ItemType type) => new()
    {
        Id = "item_001",
        TurnId = TestTurnId,
        Type = type,
        Status = ItemStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = type == ItemType.UserMessage
            ? new UserMessagePayload { Text = "hello" }
            : new AgentMessagePayload { Text = "response" }
    };

    [Fact]
    public async Task FullTurnSequence_EventsInOrderWithUniqueIdsAndTurnScope()
    {
        var channel = MakeChannel();
        var turn = MakeTurn();
        var userItem = MakeItem(ItemType.UserMessage);
        var agentItem = MakeItem(ItemType.AgentMessage);

        channel.EmitTurnStarted(turn);
        channel.EmitItemStarted(userItem);
        channel.EmitItemCompleted(userItem);
        channel.EmitItemStarted(agentItem);
        channel.EmitItemDelta(agentItem, new AgentMessageDelta { TextDelta = "Hi" });
        channel.EmitItemCompleted(agentItem);
        channel.EmitTurnCompleted(turn);
        channel.Complete();
        channel.Complete();

        var events = await CollectAsync(channel);

        Assert.Equal(
        [
            SessionEventType.TurnStarted,
            SessionEventType.ItemStarted,
            SessionEventType.ItemCompleted,
            SessionEventType.ItemStarted,
            SessionEventType.ItemDelta,
            SessionEventType.ItemCompleted,
            SessionEventType.TurnCompleted
        ], events.Select(e => e.EventType));
        Assert.All(events, e => Assert.Equal(TestThreadId, e.ThreadId));
        Assert.All(events, e => Assert.Equal(TestTurnId, e.TurnId));
        Assert.Equal(events.Count, events.Select(e => e.EventId).Distinct().Count());
        Assert.Equal("Hi", Assert.IsType<AgentMessageDelta>(events[4].Payload).TextDelta);
    }

    [Fact]
    public async Task EmitItemDelta_ToolCallArgumentsDeltaPayloadOnEvent()
    {
        var channel = MakeChannel();
        var item = MakeItem(ItemType.ToolCall);
        var delta = new ToolCallArgumentsDelta
        {
            ToolName = "WriteFile",
            CallId = "call_001",
            Delta = "{\"content\":\"hello\""
        };

        channel.EmitItemStarted(item);
        channel.EmitItemDelta(item, delta);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(2, events.Count);
        var payload = Assert.IsType<ToolCallArgumentsDelta>(events[1].Payload);
        Assert.Equal("toolCallArguments", payload.DeltaKind);
        Assert.Equal("WriteFile", payload.ToolName);
        Assert.Equal("call_001", payload.CallId);
    }

    [Fact]
    public async Task EmitTurnDiffUpdated_IsTurnScopedWithDiffPayload()
    {
        var channel = MakeChannel();

        channel.EmitTurnDiffUpdated("diff --git a/a.txt b/a.txt\n");
        channel.Complete();

        var evt = Assert.Single(await CollectAsync(channel));
        Assert.Equal(SessionEventType.TurnDiffUpdated, evt.EventType);
        Assert.Equal(TestTurnId, evt.TurnId);
        Assert.Null(evt.ItemId);
        Assert.Equal("diff --git a/a.txt b/a.txt\n", evt.TurnDiffUpdatedPayload?.Diff);
    }

    [Fact]
    public async Task ItemSnapshots_CarryLifecycleStatusAndAreIndependentOfTheSource()
    {
        var channel = MakeChannel();
        var completedSource = new SessionItem
        {
            Id = "item_snap_01",
            TurnId = TestTurnId,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload { ToolName = "Exec", CallId = "c1" }
        };
        var streamingSource = new SessionItem
        {
            Id = "item_snap_02",
            TurnId = TestTurnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = "done" }
        };
        var turn = MakeTurn();

        channel.EmitItemStarted(completedSource);
        channel.EmitItemCompleted(streamingSource);
        channel.EmitTurnStarted(turn);
        completedSource.Status = ItemStatus.Completed;
        streamingSource.Status = ItemStatus.Started;
        turn.Items.Add(MakeItem(ItemType.AgentMessage));
        channel.Complete();

        var events = await CollectAsync(channel);
        var started = Assert.IsType<SessionItem>(events[0].Payload);
        Assert.Equal(ItemStatus.Started, started.Status);
        Assert.Null(started.CompletedAt);
        var completed = Assert.IsType<SessionItem>(events[1].Payload);
        Assert.Equal(ItemStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Empty(Assert.IsType<SessionTurn>(events[2].Payload).Items);
    }

    private static async Task<List<SessionEvent>> CollectAsync(
        SessionEventChannel channel,
        CancellationToken ct = default)
    {
        var events = new List<SessionEvent>();
        await foreach (var evt in channel.ReadAllAsync(ct))
            events.Add(evt);
        return events;
    }
}
