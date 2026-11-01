using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class ThreadHistoryProjectionTests
{
    [Fact]
    public async Task MessageOccurrences_PageWithinOneItemWithoutOverlapsOrSplitSurrogates()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 2);
        var text = "😀" + new string('x', 79) + "Aa" + new string('y', 79) + "😀|aaaa|AA";
        thread.Turns[0].Items[0].Payload = new UserMessagePayload { Text = text };
        thread.Turns[0].Items[1].Payload = new AgentMessagePayload { Text = "aa" };
        thread.Turns[1].Items[0].Payload = new UserMessagePayload { Text = "later AA" };
        await _store.SaveThreadAsync(thread);

        var hits = new List<ThreadMessageOccurrence>();
        var positions = new List<ThreadMessageSearchCursor>();
        ThreadMessageSearchCursor? cursor = null;
        do
        {
            var page = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "aa", cursor, 1);
            hits.Add(Assert.Single(page.Data));
            cursor = page.NextCursor;
            if (cursor is { } position)
                positions.Add(position);
            Assert.True(hits.Count <= 6);
        } while (cursor is not null);

        Assert.Equal(
            ["item_001_001", "item_001_001", "item_001_001", "item_001_001", "item_001_002", "item_002_001"],
            hits.Select(hit => hit.ItemId));
        Assert.Equal(["Aa", "aa", "aa", "AA", "aa", "AA"],
            hits.Select(hit => hit.Snippet[hit.SnippetMatchStart..hit.SnippetMatchEnd]));
        Assert.Equal([83, 167, 169, 172], positions.Take(4).Select(position => position.TextOffset));
        Assert.Single(positions.Take(4).Select(position => position.ItemOrdinal).Distinct());
        Assert.StartsWith("😀", hits[0].Snippet);
        Assert.EndsWith("😀", hits[0].Snippet);
        Assert.All(hits, hit =>
        {
            Assert.False(char.IsLowSurrogate(hit.Snippet[0]));
            Assert.False(char.IsHighSurrogate(hit.Snippet[^1]));
        });

        var emojiFirst = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "😀", null, 1);
        Assert.Equal(2, emojiFirst.NextCursor!.Value.TextOffset);
        var emojiLast = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "😀", emojiFirst.NextCursor, 1);
        var emojiHit = Assert.Single(emojiLast.Data);
        Assert.Equal("😀", emojiHit.Snippet[emojiHit.SnippetMatchStart..emojiHit.SnippetMatchEnd]);
        Assert.Null(emojiLast.NextCursor);

        foreach (var direction in new[] { ThreadHistorySortDirection.Ascending, ThreadHistorySortDirection.Descending })
        {
            var turns = await _store.ListThreadTurnsAsync(
                thread.Id, new ThreadHistoryCursor(hits[^1].TurnOrdinal, Inclusive: true), 10, direction);
            Assert.Equal(hits[^1].TurnId, turns.Data[0].Id);
        }
    }

    [Fact]
    public async Task MessageOccurrences_SearchOnlyVisibleMessageTextAndTreatQueryLiterally()
    {
        const string term = "[a.*]";
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 5);
        var turn = thread.Turns[0];
        turn.Items[0].Payload = new UserMessagePayload
        {
            Text = "aZZ",
            MaterializedInputParts = [new SessionInputPart { Text = term }]
        };
        turn.Items[1].Payload = new AgentMessagePayload { Text = "visible [A.*]" };
        turn.Items[2].Type = ItemType.ReasoningContent;
        turn.Items[2].Payload = new ReasoningContentPayload { Text = term };
        turn.Items[3].Type = ItemType.CommandExecution;
        turn.Items[3].Payload = new CommandExecutionPayload { Command = term, AggregatedOutput = term };
        turn.Items[4].Type = ItemType.ToolExecution;
        turn.Items[4].Payload = new ToolExecutionPayload { ToolName = term, ResultPreview = term };
        await _store.SaveThreadAsync(thread);

        var page = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, term, null, 20);

        var hit = Assert.Single(page.Data);
        Assert.Equal(turn.Items[1].Id, hit.ItemId);
        Assert.Equal("[A.*]", hit.Snippet[hit.SnippetMatchStart..hit.SnippetMatchEnd]);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task MessageOccurrences_UseCurrentUpdatesAndExcludeRolledBackTurns()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 2);
        thread.Turns[0].Items[0].Payload = new UserMessagePayload { Text = "old token" };
        thread.Turns[0].Items[1].Payload = new AgentMessagePayload { Text = "old token" };
        thread.Turns[1].Items[0].Payload = new UserMessagePayload { Text = "old token" };
        await _store.SaveThreadAsync(thread);
        var first = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "old", null, 2);
        Assert.NotNull(first.NextCursor);

        thread.Turns[0].Items[0].Payload = new UserMessagePayload { Text = "current token" };
        await _store.SaveTurnAsync(thread, thread.Turns[0]);
        var updated = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "token", null, 20);
        Assert.Equal(["item_001_001", "item_001_002", "item_002_001"], updated.Data.Select(hit => hit.ItemId));
        Assert.Equal("current token", updated.Data[0].Snippet);

        thread.Turns.RemoveAt(1);
        await _store.RollbackThreadAsync(thread, 1);
        var remaining = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "old", null, 20);
        Assert.Equal("item_001_002", Assert.Single(remaining.Data).ItemId);
        var continuation = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "old", first.NextCursor, 20);
        Assert.Empty(continuation.Data);
        Assert.Null(continuation.NextCursor);
        var exact = await _store.ReadThreadItemsAsync(thread.Id, "turn_001", ["item_001_001"]);
        Assert.Equal("current token", Assert.Single(exact).Item.AsUserMessage!.Text);
        Assert.Empty(await _store.ReadThreadItemsAsync(thread.Id, "turn_002", ["item_002_001"]));
    }

    [Fact]
    public async Task ExactItems_FilterByTurnAndIdsWithoutHydratingUnrequestedItems()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 3);
        await _store.SaveThreadAsync(thread);
        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText = """
                UPDATE thread_items SET item_json = 'not-json'
                WHERE thread_id = $thread_id AND turn_id = 'turn_001' AND item_id = 'item_001_002'
                """;
            corrupt.Parameters.AddWithValue("$thread_id", thread.Id);
            corrupt.ExecuteNonQuery();
        }

        var items = await _store.ReadThreadItemsAsync(
            thread.Id, "turn_001", ["item_001_003", "item_002_001", "missing", "item_001_001", "item_001_003"]);

        Assert.Equal(["item_001_001", "item_001_003"], items.Select(entry => entry.Item.Id));
        Assert.All(items, entry => Assert.Equal("turn_001", entry.TurnId));

        var repaired = await _store.SearchThreadMessageOccurrencesAsync(thread.Id, "Agent 1.2", null, 20);
        Assert.Equal("item_001_002", Assert.Single(repaired.Data).ItemId);
    }
}
