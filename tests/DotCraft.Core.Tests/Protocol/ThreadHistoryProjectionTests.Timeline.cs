using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class ThreadHistoryProjectionTests
{
    [Fact]
    public async Task Timeline_PagesForwardWithinNewestPage_AndKeepsLateAppendAfterActualCompletion()
    {
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 7);
        await _store.SaveThreadAsync(thread);
        var before = await _store.ListThreadTimelineAsync(thread.Id, null, 100);
        var completion = Assert.Single(before.Data, entry => entry.Kind == ThreadTimelineEntryKind.TurnCompleted);
        var firstItemPosition = before.Data.First(entry => entry.Kind == ThreadTimelineEntryKind.Item).Position;
        var final = thread.Turns[0].Items[^1];
        var late = new SessionItem
        {
            Id = "late_result", TurnId = final.TurnId, Type = ItemType.ToolResult, Status = ItemStatus.Completed,
            CreatedAt = final.CreatedAt.AddMinutes(1),
            Payload = new ToolResultPayload { CallId = "background", Success = true, Result = "done" }
        };
        thread.Turns[0].Items.Add(late);
        await _store.AppendThreadChangesAsync(thread, [RolloutChangeRecords.ItemAppended(final.TurnId, late)], false);
        var latest = await _store.ListThreadTimelineAsync(thread.Id, null, 2);
        Assert.Equal([ThreadTimelineEntryKind.TurnCompleted, ThreadTimelineEntryKind.Item], latest.Data.Select(entry => entry.Kind));
        Assert.Equal(completion.Position, latest.Data[0].Position);
        Assert.True(latest.Data[1].Position > completion.Position);
        Assert.Equal("late_result", latest.Data[1].Item!.Id);

        var pages = new List<IReadOnlyList<ThreadTimelineEntry>> { latest.Data };
        var cursor = latest.NextCursor;
        while (cursor is not null)
        {
            var page = await _store.ListThreadTimelineAsync(thread.Id, cursor, 2);
            Assert.InRange(page.Data.Count, 1, 2);
            Assert.Equal(page.Data.OrderBy(entry => entry.Position).Select(entry => entry.Position),
                page.Data.Select(entry => entry.Position));
            pages.Add(page.Data);
            cursor = page.NextCursor;
        }
        var chronological = pages.AsEnumerable().Reverse().SelectMany(page => page).ToArray();
        Assert.Equal(10, chronological.Length);
        Assert.Equal(10, chronological.Select(entry => (entry.Position, entry.Kind, entry.TurnId, entry.Item?.Id)).Distinct().Count());
        Assert.Equal(ThreadTimelineEntryKind.TurnStarted, chronological[0].Kind);

        // A later full Turn snapshot must retain both the real completion position and the phase resolved there.
        await _store.SaveTurnAsync(thread, thread.Turns[0]);
        var replaced = await _store.ListThreadTimelineAsync(thread.Id, null, 100);
        Assert.Equal(completion.Position, Assert.Single(replaced.Data, entry => entry.Kind == ThreadTimelineEntryKind.TurnCompleted).Position);
        Assert.Equal(firstItemPosition, replaced.Data.First(entry => entry.Kind == ThreadTimelineEntryKind.Item).Position);
        Assert.Equal("final_answer", replaced.Data.Single(entry => entry.Item?.Id == final.Id).Item!.AsAgentMessage!.Phase);
    }

    [Fact]
    public async Task Bootstrap_IsInclusiveAndSharedAcrossTurnFilters_AndRetainsSnapshotBound()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 3);
        await _store.SaveThreadAsync(thread);
        var bootstrap = await _store.ReadThreadHistoryBootstrapAsync(thread.Id);
        Assert.NotNull(bootstrap.TurnsCursor);
        Assert.NotNull(bootstrap.ItemsCursor);
        var turns = await _store.ListThreadTurnsAsync(thread.Id, bootstrap.TurnsCursor, 1, ThreadHistorySortDirection.Descending);
        Assert.Equal("turn_002", Assert.Single(turns.Data).Id);
        foreach (var turn in thread.Turns)
        {
            var first = await _store.ListThreadItemsAsync(thread.Id, turn.Id, bootstrap.ItemsCursor, 1, ThreadHistorySortDirection.Descending);
            Assert.Equal(turn.Items[^1].Id, Assert.Single(first.Data).Item.Id);
            Assert.Equal(bootstrap.ItemsCursor!.Value.SnapshotOrdinal, first.NextCursor!.Value.SnapshotOrdinal);
            var older = await _store.ListThreadItemsAsync(thread.Id, turn.Id, first.NextCursor, 10, ThreadHistorySortDirection.Descending);
            Assert.Equal(turn.Items.Take(2).Reverse().Select(item => item.Id), older.Data.Select(entry => entry.Item.Id));
        }

        var late = new SessionItem
        {
            Id = "after_snapshot", TurnId = "turn_001", Type = ItemType.AgentMessage, Status = ItemStatus.Completed,
            Payload = new AgentMessagePayload { Text = "new identity", Phase = "final_answer" }
        };
        await _store.AppendThreadChangesAsync(thread, [RolloutChangeRecords.ItemAppended(late.TurnId, late)], false);
        var frozen = await _store.ListThreadItemsAsync(thread.Id, "turn_001", bootstrap.ItemsCursor, 100, ThreadHistorySortDirection.Descending);
        Assert.DoesNotContain(frozen.Data, entry => entry.Item.Id == late.Id);
        var current = await _store.ListThreadItemsAsync(thread.Id, "turn_001", null, 100, ThreadHistorySortDirection.Descending);
        Assert.Equal(late.Id, current.Data[0].Item.Id);
    }

    [Fact]
    public async Task Bootstrap_OnlyEmptyHistoryIsNull_AndRunningHeadHasNoCompletion()
    {
        var thread = CreateThread(turnCount: 0, itemsPerTurn: 0);
        await _store.SaveThreadAsync(thread);
        var empty = await _store.ReadThreadHistoryBootstrapAsync(thread.Id);
        Assert.Null(empty.TurnsCursor);
        Assert.Null(empty.ItemsCursor);

        var turn = new SessionTurn
        {
            Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running, StartedAt = thread.CreatedAt,
            Items = [new() { Id = "input", TurnId = "turn_001", Type = ItemType.UserMessage,
                Status = ItemStatus.Completed, Payload = new UserMessagePayload { Text = "start" } }]
        };
        thread.Turns.Add(turn);
        await _store.SaveTurnAsync(thread, turn);
        var head = await _store.ReadThreadHistoryBootstrapAsync(thread.Id);
        Assert.NotNull(head.TurnsCursor);
        Assert.NotNull(head.ItemsCursor);
        var page = await _store.ListThreadTimelineAsync(thread.Id, null, 100);
        Assert.Equal([ThreadTimelineEntryKind.TurnStarted, ThreadTimelineEntryKind.Item], page.Data.Select(entry => entry.Kind));
        var start = Assert.Single(page.Data, entry => entry.Kind == ThreadTimelineEntryKind.TurnStarted);
        Assert.Equal(TurnStatus.Running, start.Turn!.Status);
    }

    [Fact]
    public async Task HistoricalPhase_RebuildUsesCanonicalBoundariesAndSeparatesAsyncDelivery()
    {
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 1);
        var turn = thread.Turns[0];
        turn.Items.AddRange([
            new() { Id = "progress", TurnId = turn.Id, Type = ItemType.AgentMessage, Status = ItemStatus.Completed,
                Payload = new AgentMessagePayload { Text = "working" } },
            new() { Id = "call", TurnId = turn.Id, Type = ItemType.ToolCall, Status = ItemStatus.Completed,
                Payload = new ToolCallPayload { CallId = "operation", ToolName = "ReadFile" } },
            new() { Id = "answer", TurnId = turn.Id, Type = ItemType.AgentMessage, Status = ItemStatus.Completed,
                Payload = new AgentMessagePayload { Text = "answer" } },
            new() { Id = "late-result", TurnId = turn.Id, Type = ItemType.ToolResult, Status = ItemStatus.Completed,
                Payload = new ToolResultPayload { CallId = "operation", Success = true, Result = "done" } },
            new() { Id = "async", TurnId = turn.Id, Type = ItemType.AgentMessage, Status = ItemStatus.Completed,
                Payload = new AgentMessagePayload { Text = "delivered", DeliveryMode = "async" } }
        ]);
        await _store.SaveThreadAsync(thread);
        using (var database = new WorkspaceStateDatabase(_root))
        using (var connection = database.OpenConnection())
        using (var stale = connection.CreateCommand())
        {
            stale.CommandText = "UPDATE threads SET attachment_index_version = 4 WHERE thread_id = $thread";
            stale.Parameters.AddWithValue("$thread", thread.Id);
            stale.ExecuteNonQuery();
        }
        var rebuilt = await _store.ListThreadItemsAsync(thread.Id, turn.Id, null, 100, ThreadHistorySortDirection.Ascending);
        Assert.Equal("commentary", rebuilt.Data.Single(entry => entry.Item.Id == "progress").Item.AsAgentMessage!.Phase);
        Assert.Equal("final_answer", rebuilt.Data.Single(entry => entry.Item.Id == "answer").Item.AsAgentMessage!.Phase);
        var delivered = rebuilt.Data.Single(entry => entry.Item.Id == "async").Item.AsAgentMessage!;
        Assert.Equal("final_answer", delivered.Phase);
        Assert.Equal("async", delivered.DeliveryMode);
    }

    [Fact]
    public async Task Timeline_DoesNotReadUnselectedPayloads_AndRollbackDoesNotResurrectBoundaries()
    {
        var thread = CreateThread(turnCount: 3, itemsPerTurn: 3);
        await _store.SaveThreadAsync(thread);
        using (var database = new WorkspaceStateDatabase(_root))
        using (var connection = database.OpenConnection())
        using (var corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = "UPDATE thread_items SET item_json = 'not-json' WHERE thread_id = $thread AND turn_id = 'turn_001'";
            corrupt.Parameters.AddWithValue("$thread", thread.Id);
            corrupt.ExecuteNonQuery();
        }
        var first = await _store.ListThreadTimelineAsync(thread.Id, null, 2);
        Assert.Equal(2, first.Data.Count);
        Assert.All(first.Data, entry => Assert.Equal("turn_003", entry.TurnId));
        thread.Turns.RemoveAt(2);
        await _store.RollbackThreadAsync(thread, 1);
        var older = await _store.ListThreadTimelineAsync(thread.Id, first.NextCursor, 2);
        Assert.All(older.Data, entry => Assert.Equal("turn_002", entry.TurnId));
        Assert.Equal(2, older.Data.Count);
    }
}
