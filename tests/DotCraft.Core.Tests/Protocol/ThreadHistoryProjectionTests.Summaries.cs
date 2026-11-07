using System.Text.Json;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class ThreadHistoryProjectionTests
{
    [Fact]
    public async Task Summaries_SelectPinnedCandidatesAndPairedCallsWithoutExpandingUnfinishedWork()
    {
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 1);
        var turn = thread.Turns[0];
        turn.Status = TurnStatus.Running;
        Add(turn, "reason", ItemType.ReasoningContent, new ReasoningContentPayload { Text = "private work" });
        Add(turn, "selected-call", ItemType.ToolCall, Call("chosen", "Pin", "notes"));
        Add(turn, "selected-result", ItemType.ToolResult, Result("chosen"));
        Add(turn, "wrong-namespace", ItemType.ToolCall, Call("other", "Pin", "other"));
        Add(turn, "wrong-result", ItemType.ToolResult, Result("other"));
        Add(turn, "case-sensitive", ItemType.ToolCall, Call("lower", "pin", "notes"));
        Add(turn, "file-call", ItemType.ToolCall, Call("file", "Write"));
        Add(turn, "file-result", ItemType.ToolResult, Result("file") with
        {
            StructuredContent = JsonSerializer.SerializeToNode(new { kind = "fileChange", path = "example.txt" })
        });
        Add(turn, "top-call", ItemType.ToolCall, Call("top", "Top"));
        Add(turn, "top-result", ItemType.ToolResult, Result("top"));
        Add(turn, "dynamic", ItemType.DynamicToolCall, new DynamicToolCallPayload
        {
            Namespace = "notes", ToolName = "Pin", ProviderFlatName = "notes__Pin", CallId = "dynamic"
        });
        Add(turn, "dynamic-result", ItemType.ToolResult, Result("dynamic"));
        Add(turn, "other-dynamic", ItemType.DynamicToolCall, new DynamicToolCallPayload
        {
            Namespace = "other", ToolName = "Pin", ProviderFlatName = "other__Pin", CallId = "other-dynamic"
        });
        Add(turn, "image-running", ItemType.ImageGeneration, new ImageGenerationPayload());
        Add(turn, "image-done", ItemType.ImageGeneration, new ImageGenerationPayload { Status = "completed", Result = "image" });
        Add(turn, "image-failed", ItemType.ImageGeneration, new ImageGenerationPayload { Status = "failed" });
        Add(turn, "mcp-app", ItemType.McpToolCall, Mcp("ui://example/app"));
        Add(turn, "mcp-running", ItemType.McpToolCall, Mcp("ui://example/app"), ItemStatus.Started);
        Add(turn, "mcp-other", ItemType.McpToolCall, Mcp("https://example.com"));
        Add(turn, "web", ItemType.WebSearch, new WebSearchPayload { Query = "citation" });
        Add(turn, "error", ItemType.Error, new ErrorPayload { Message = "error" });
        Add(turn, "notice", ItemType.SystemNotice, new SystemNoticePayload { Kind = "compacted" });
        Add(turn, "reply", ItemType.AgentMessage, new AgentMessagePayload { Text = "reply" });
        Add(turn, "execution", ItemType.ToolExecution, new ToolExecutionPayload { ToolName = "Pin" });
        await _store.SaveThreadAsync(thread);

        var page = await _store.ListThreadSummariesAsync(thread.Id, null, 1,
            ThreadHistorySortDirection.Descending, [new("notes", "Pin"), new(null, "Top")]);

        var summary = Assert.Single(page.Data);
        Assert.Equal(turn.Items.Count, summary.ItemCount);
        Assert.Equal(1, summary.ToolExecutionCount);
        Assert.Equal(TurnStatus.Running, summary.Turn.Status);
        Assert.Equal(
            ["item_001_001", "selected-call", "selected-result", "file-call", "file-result", "top-call", "top-result",
             "dynamic", "dynamic-result", "image-done", "image-failed", "mcp-app", "web", "error", "notice", "reply"],
            summary.Turn.Items.Select(item => item.Id));
        foreach (var item in summary.Turn.Items)
            Assert.Equal(JsonSerializer.Serialize(turn.Items.Single(original => original.Id == item.Id), SessionJsonOptions.Default),
                JsonSerializer.Serialize(item, SessionJsonOptions.Default));
    }

    [Theory]
    [InlineData(ThreadHistorySortDirection.Ascending)]
    [InlineData(ThreadHistorySortDirection.Descending)]
    public async Task Summaries_PageTurnsBeforeReadingPayloadsAndPreserveTurnCursors(ThreadHistorySortDirection direction)
    {
        var thread = CreateThread(turnCount: 4, itemsPerTurn: 1);
        foreach (var turn in thread.Turns)
        {
            Add(turn, "call", ItemType.ToolCall, Call("large", "Unselected"));
            Add(turn, "large-output", ItemType.ToolResult, Result("large") with { Result = new string('x', 128 * 1024) });
        }
        await _store.SaveThreadAsync(thread);
        var turns = await _store.ListThreadTurnsAsync(thread.Id, null, 1, direction);
        var bytes = _store.ProjectedRolloutBytes;
        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var poison = connection.CreateCommand();
            poison.CommandText = """
                UPDATE thread_items SET item_json = 'not-json'
                WHERE item_id = 'large-output' OR (item_id = 'item_002_001' AND turn_id = 'turn_002')
                """;
            poison.ExecuteNonQuery();
        }

        var summaries = await _store.ListThreadSummariesAsync(thread.Id, null, 1, direction, []);
        var summary = Assert.Single(summaries.Data);

        Assert.Equal(Assert.Single(turns.Data).Id, summary.Turn.Id);
        Assert.Equal(turns.NextCursor, summaries.NextCursor);
        Assert.Equal(turns.BackwardsCursor, summaries.BackwardsCursor);
        Assert.Equal(3, summary.ItemCount);
        Assert.Single(summary.Turn.Items);
        Assert.Equal(bytes, _store.ProjectedRolloutBytes);

        var reverse = direction == ThreadHistorySortDirection.Ascending
            ? ThreadHistorySortDirection.Descending : ThreadHistorySortDirection.Ascending;
        var backwards = await _store.ListThreadSummariesAsync(thread.Id, summaries.BackwardsCursor, 1, reverse, []);
        Assert.Equal(summary.Turn.Id, Assert.Single(backwards.Data).Turn.Id);
        var after = await _store.ListThreadSummariesAsync(thread.Id,
            summaries.BackwardsCursor!.Value with { Inclusive = false }, 1, reverse, []);
        Assert.Empty(after.Data);
    }

    [Fact]
    public async Task Summaries_RebuildOutdatedProjectionAndCorruptSelectedPayloads()
    {
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 2);
        await _store.SaveThreadAsync(thread);
        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var invalidate = connection.CreateCommand();
            invalidate.CommandText = """
                UPDATE thread_items SET summary_kind = 0;
                UPDATE threads SET attachment_index_version = 2;
                """;
            invalidate.ExecuteNonQuery();
        }

        var repaired = await _store.ListThreadSummariesAsync(thread.Id, null, 1, ThreadHistorySortDirection.Ascending, []);
        Assert.Equal(2, Assert.Single(repaired.Data).Turn.Items.Count);

        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "UPDATE thread_items SET item_json = 'not-json' WHERE item_id = 'item_001_002'";
            corrupt.ExecuteNonQuery();
        }
        var rebuilt = await _store.ListThreadSummariesAsync(thread.Id, null, 1, ThreadHistorySortDirection.Ascending, []);
        Assert.Equal("Agent 1.2", Assert.Single(rebuilt.Data).Turn.Items[1].AsAgentMessage!.Text);
    }

    [Fact]
    public async Task Summaries_TrackItemReplacementRollbackAndArchivedReads()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 1);
        var turn = thread.Turns[0];
        Add(turn, "call", ItemType.ToolCall, Call("file", "Write"));
        var result = Add(turn, "result", ItemType.ToolResult, Result("file"));
        await _store.SaveThreadAsync(thread);
        var before = await _store.ListThreadSummariesAsync(thread.Id, null, 2, ThreadHistorySortDirection.Ascending, []);
        Assert.Single(before.Data[0].Turn.Items);

        result.Payload = Result("file") with { StructuredContent = JsonSerializer.SerializeToNode(new { kind = "fileChange" }) };
        await _store.SaveTurnAsync(thread, turn);
        var changed = await _store.ListThreadSummariesAsync(thread.Id, null, 2, ThreadHistorySortDirection.Ascending, []);
        Assert.Equal(["item_001_001", "call", "result"], changed.Data[0].Turn.Items.Select(item => item.Id));

        turn.Items.Remove(result);
        await _store.SaveTurnAsync(thread, turn);
        thread.Turns.RemoveAt(1);
        await _store.RollbackThreadAsync(thread, 1);
        await _store.ArchiveStoredThreadAsync(thread.Id);
        var archived = await _store.ListThreadSummariesAsync(thread.Id, null, 2, ThreadHistorySortDirection.Descending, []);
        var summary = Assert.Single(archived.Data);
        Assert.Equal(turn.Id, summary.Turn.Id);
        Assert.Equal(2, summary.ItemCount);
        Assert.Single(summary.Turn.Items);
        Assert.Null(archived.NextCursor);
    }

    [Fact]
    public async Task Summaries_CountTurnsWithNoSelectedItems()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 0);
        Add(thread.Turns[0], "hidden", ItemType.ReasoningContent, new ReasoningContentPayload { Text = "work" });
        await _store.SaveThreadAsync(thread);

        var page = await _store.ListThreadSummariesAsync(thread.Id, null, 2, ThreadHistorySortDirection.Ascending, []);

        Assert.Equal([1, 0], page.Data.Select(summary => summary.ItemCount));
        Assert.All(page.Data, summary => Assert.Empty(summary.Turn.Items));
    }

    [Fact]
    public async Task ItemCursor_UsesDurablePositionWithoutReadingPayloadAndScopesIdentityToTurn()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 3);
        await _store.SaveThreadAsync(thread);
        var first = await _store.ListThreadItemsAsync(thread.Id, "turn_001", null, 1, ThreadHistorySortDirection.Ascending);
        var bytes = _store.ProjectedRolloutBytes;
        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var poison = connection.CreateCommand();
            poison.CommandText = "UPDATE thread_items SET item_json = 'not-json' WHERE item_id = 'item_001_001'";
            poison.ExecuteNonQuery();
        }

        var cursor = await _store.ResolveThreadItemCursorAsync(thread.Id, "turn_001", "item_001_001");

        Assert.Equal(first.NextCursor, cursor);
        Assert.False(cursor!.Value.Inclusive);
        Assert.Null(await _store.ResolveThreadItemCursorAsync(thread.Id, "turn_002", "item_001_001"));
        Assert.Null(await _store.ResolveThreadItemCursorAsync(thread.Id, "turn_001", "missing"));
        var rest = await _store.ListThreadItemsAsync(thread.Id, "turn_001", cursor, 10, ThreadHistorySortDirection.Ascending);
        Assert.Equal(["item_001_002", "item_001_003"], rest.Data.Select(entry => entry.Item.Id));
        Assert.Equal(bytes, _store.ProjectedRolloutBytes);
    }

    private static SessionItem Add(SessionTurn turn, string id, ItemType type, object payload, ItemStatus status = ItemStatus.Completed)
    {
        var item = new SessionItem
        {
            Id = id, TurnId = turn.Id, Type = type, Payload = payload, Status = status, CreatedAt = turn.StartedAt
        };
        turn.Items.Add(item);
        return item;
    }

    private static ToolCallPayload Call(string callId, string name, string? ns = null) =>
        new() { CallId = callId, ToolName = name, Namespace = ns, ProviderFlatName = ns is null ? name : $"{ns}__{name}" };

    private static ToolResultPayload Result(string callId) =>
        new() { CallId = callId, ToolName = "unrelated", ProviderFlatName = "unrelated", Result = "result", Success = true };

    private static McpToolCallPayload Mcp(string uri) =>
        new() { CallId = "mcp", ToolName = "Show", ProviderFlatName = "Show", McpAppResourceUri = uri, Status = "completed" };
}
