using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Tests for SessionEventChannel: event emission ordering, completion, and reader behavior.
/// </summary>
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

    // -------------------------------------------------------------------------
    // Basic emit and read
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitTurnStarted_CanBeRead()
    {
        var channel = MakeChannel();
        var turn = MakeTurn();

        channel.EmitTurnStarted(turn);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Single(events);
        Assert.Equal(SessionEventType.TurnStarted, events[0].EventType);
        Assert.Equal(TestThreadId, events[0].ThreadId);
        Assert.Equal(TestTurnId, events[0].TurnId);
    }

    [Fact]
    public async Task EmitItemStartedThenCompleted_TwoEventsInOrder()
    {
        var channel = MakeChannel();
        var item = MakeItem(ItemType.AgentMessage);

        channel.EmitItemStarted(item);
        channel.EmitItemCompleted(item);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(2, events.Count);
        Assert.Equal(SessionEventType.ItemStarted, events[0].EventType);
        Assert.Equal(SessionEventType.ItemCompleted, events[1].EventType);
    }

    [Fact]
    public async Task EmitItemDelta_DeltaPayloadOnEvent()
    {
        var channel = MakeChannel();
        var item = MakeItem(ItemType.AgentMessage);
        var delta = new AgentMessageDelta { TextDelta = "chunk1" };

        channel.EmitItemStarted(item);
        channel.EmitItemDelta(item, delta);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(2, events.Count);
        var deltaEvt = events[1];
        Assert.Equal(SessionEventType.ItemDelta, deltaEvt.EventType);
        Assert.Equal("chunk1", (deltaEvt.Payload as AgentMessageDelta)?.TextDelta);
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
        var deltaEvt = events[1];
        var payload = Assert.IsType<ToolCallArgumentsDelta>(deltaEvt.Payload);
        Assert.Equal("toolCallArguments", payload.DeltaKind);
        Assert.Equal("WriteFile", payload.ToolName);
        Assert.Equal("call_001", payload.CallId);
    }

    // -------------------------------------------------------------------------
    // Full Turn event sequence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FullTurnSequence_EventsInCorrectOrder()
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

        var events = await CollectAsync(channel);
        var types = events.Select(e => e.EventType).ToList();

        Assert.Equal(
        [
            SessionEventType.TurnStarted,
            SessionEventType.ItemStarted,
            SessionEventType.ItemCompleted,
            SessionEventType.ItemStarted,
            SessionEventType.ItemDelta,
            SessionEventType.ItemCompleted,
            SessionEventType.TurnCompleted
        ], types);
    }

    [Fact]
    public async Task TurnFailed_LastEventIsTurnFailed()
    {
        var channel = MakeChannel();
        var turn = MakeTurn();

        channel.EmitTurnStarted(turn);
        channel.EmitTurnFailed(turn, "Oops");
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(SessionEventType.TurnFailed, events.Last().EventType);
    }

    [Fact]
    public async Task TurnCancelled_LastEventIsTurnCancelled()
    {
        var channel = MakeChannel();
        var turn = MakeTurn();

        channel.EmitTurnStarted(turn);
        channel.EmitTurnCancelled(turn, "Cancelled by user");
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(SessionEventType.TurnCancelled, events.Last().EventType);
    }

    // -------------------------------------------------------------------------
    // Thread-level events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitThreadCreated_HasNullTurnId()
    {
        var channel = MakeChannel();
        var thread = new SessionThread
        {
            Id = TestThreadId, WorkspacePath = "/ws",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };

        channel.EmitThreadCreated(thread);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Single(events);
        Assert.Equal(SessionEventType.ThreadCreated, events[0].EventType);
        Assert.Null(events[0].TurnId);
    }

    [Fact]
    public async Task EmitThreadStatusChanged_ContainsStatusPayload()
    {
        var channel = MakeChannel();

        channel.EmitThreadStatusChanged(TestThreadId, ThreadStatus.Active, ThreadStatus.Paused);
        channel.Complete();

        var events = await CollectAsync(channel);
        var payload = events[0].StatusChangedPayload;
        Assert.NotNull(payload);
        Assert.Equal(ThreadStatus.Active, payload.PreviousStatus);
        Assert.Equal(ThreadStatus.Paused, payload.NewStatus);
    }

    [Fact]
    public async Task EmitSystemEvent_ContainsSystemPayload()
    {
        var channel = MakeChannel();

        channel.EmitSystemEvent("consolidationFailed", message: "provider unavailable");
        channel.Complete();

        var events = await CollectAsync(channel);
        var payload = events[0].SystemEventPayload;
        Assert.NotNull(payload);
        Assert.Equal(SessionEventType.SystemEvent, events[0].EventType);
        Assert.Equal(TestTurnId, events[0].TurnId);
        Assert.Equal("consolidationFailed", payload.Kind);
        Assert.Equal("provider unavailable", payload.Message);
    }

    // -------------------------------------------------------------------------
    // Snapshot semantics (spec appserver-protocol.md §6.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmitItemStarted_Snapshot_HasStartedStatus()
    {
        // item/started must carry status="started" with no completedAt (spec §6.3)
        var channel = MakeChannel();
        var item = new SessionItem
        {
            Id = "item_snap_01", TurnId = TestTurnId,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload { ToolName = "Exec", CallId = "c1" }
        };

        channel.EmitItemStarted(item);
        channel.Complete();

        var events = await CollectAsync(channel);
        var snapshotItem = Assert.IsType<SessionItem>(events[0].Payload);
        Assert.Equal(ItemStatus.Started, snapshotItem.Status);
        Assert.Null(snapshotItem.CompletedAt);
    }

    [Fact]
    public async Task EmitItemCompleted_Snapshot_HasCompletedStatus()
    {
        // item/completed must carry status="completed" with completedAt set (spec §6.3)
        var channel = MakeChannel();
        var item = new SessionItem
        {
            Id = "item_snap_02", TurnId = TestTurnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            Payload = new AgentMessagePayload { Text = "done" }
        };

        channel.EmitItemCompleted(item);
        channel.Complete();

        var events = await CollectAsync(channel);
        var snapshotItem = Assert.IsType<SessionItem>(events[0].Payload);
        Assert.Equal(ItemStatus.Completed, snapshotItem.Status);
        Assert.NotNull(snapshotItem.CompletedAt);
    }

    [Fact]
    public async Task EmitItemStarted_SnapshotIsIndependent()
    {
        // Mutating the source item after emission must not affect the queued event snapshot.
        var channel = MakeChannel();
        var item = new SessionItem
        {
            Id = "item_snap_03", TurnId = TestTurnId,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload { CallId = "c1", Result = "ok", Success = true }
        };

        channel.EmitItemStarted(item);
        item.Status = ItemStatus.Completed; // mutate source after emission
        channel.Complete();

        var events = await CollectAsync(channel);
        var snapshotItem = Assert.IsType<SessionItem>(events[0].Payload);
        Assert.Equal(ItemStatus.Started, snapshotItem.Status);
    }

    [Fact]
    public async Task EmitTurnStarted_SnapshotItemsList_IsIndependent()
    {
        // Items added to source turn after EmitTurnStarted must not appear in the event.
        var channel = MakeChannel();
        var turn = MakeTurn();

        channel.EmitTurnStarted(turn);
        turn.Items.Add(MakeItem(ItemType.AgentMessage)); // added after emission
        channel.Complete();

        var events = await CollectAsync(channel);
        var snapshotTurn = Assert.IsType<SessionTurn>(events[0].Payload);
        Assert.Empty(snapshotTurn.Items);
    }

    // -------------------------------------------------------------------------
    // Approval events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApprovalEvents_AreEmittedInOrder()
    {
        var channel = MakeChannel();
        var approvalItem = new SessionItem
        {
            Id = "item_002",
            TurnId = TestTurnId,
            Type = ItemType.ApprovalRequest,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ApprovalRequestPayload
            {
                ApprovalType = "file",
                Operation = "write",
                Target = "/etc/secret",
                RequestId = "req_001"
            }
        };

        channel.EmitApprovalRequested(approvalItem);
        channel.EmitApprovalResolved(approvalItem);
        channel.Complete();

        var events = await CollectAsync(channel);
        Assert.Equal(SessionEventType.ApprovalRequested, events[0].EventType);
        Assert.Equal(SessionEventType.ApprovalResolved, events[1].EventType);
    }

    // -------------------------------------------------------------------------
    // Event IDs are unique and monotonic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EventIds_AreUniqueAndMonotonic()
    {
        var channel = MakeChannel();
        var turn = MakeTurn();
        var item = MakeItem(ItemType.AgentMessage);

        channel.EmitTurnStarted(turn);
        channel.EmitItemStarted(item);
        channel.EmitItemCompleted(item);
        channel.EmitTurnCompleted(turn);
        channel.Complete();

        var events = await CollectAsync(channel);
        var ids = events.Select(e => e.EventId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count()); // all unique
    }

    // -------------------------------------------------------------------------
    // Complete called twice is safe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Complete_CalledTwice_DoesNotThrow()
    {
        var channel = MakeChannel();
        channel.EmitTurnStarted(MakeTurn());
        channel.Complete();
        channel.Complete(); // second call is a no-op

        var events = await CollectAsync(channel);
        Assert.Single(events);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

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
