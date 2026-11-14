using System.Text.Json;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Fact]
    public async Task Resume_InitializesInclusiveTurnAndReusableItemSnapshots_WithScopedContinuation()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var empty = await ReadHistoryRpcAsync("thread/resume", new { threadId = thread.Id });
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("turnsBackwardsCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("itemsBackwardsCursor").ValueKind);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        foreach (var turn in thread.Turns)
            AddHistoryAnswer(turn);
        await _h.Service.SeedThreadAsync(thread);
        var resumed = await ReadHistoryRpcAsync("thread/resume", new { threadId = thread.Id });
        AddCompletedTurn(thread, "turn_003", "after resume");
        AddHistoryAnswer(thread.Turns[^1]);
        thread.Turns[0].Items.Add(new()
        {
            Id = "late_answer", TurnId = "turn_001", Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed, CreatedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = "after resume" }
        });
        await _h.Service.SeedThreadAsync(thread);
        var turns = await ReadHistoryRpcAsync("thread/turns/list",
            new { threadId = thread.Id, cursor = resumed.GetProperty("turnsBackwardsCursor").GetString(), limit = 1 });
        Assert.Equal("turn_002", turns.GetProperty("data")[0].GetProperty("id").GetString());
        var olderTurns = await ReadHistoryRpcAsync("thread/turns/list",
            new { threadId = thread.Id, cursor = turns.GetProperty("nextCursor").GetString(), limit = 1 });
        Assert.Equal("turn_001", Assert.Single(olderTurns.GetProperty("data").EnumerateArray()).GetProperty("id").GetString());

        var anchor = resumed.GetProperty("itemsBackwardsCursor").GetString();
        foreach (var turnId in new[] { "turn_001", "turn_002" })
        {
            var first = await ReadHistoryRpcAsync("thread/items/list", new { threadId = thread.Id, turnId, cursor = anchor, limit = 1 });
            var entry = Assert.Single(first.GetProperty("data").EnumerateArray());
            Assert.Equal(turnId, entry.GetProperty("turnId").GetString());
            Assert.Equal(turnId + "_answer", entry.GetProperty("item").GetProperty("id").GetString());
            var next = first.GetProperty("nextCursor").GetString();
            Assert.NotNull(next);
            var older = await ReadHistoryRpcAsync("thread/items/list", new { threadId = thread.Id, turnId, cursor = next, limit = 1 });
            Assert.Single(older.GetProperty("data").EnumerateArray());
            Assert.NotEqual(first.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString(),
                older.GetProperty("data")[0].GetProperty("item").GetProperty("id").GetString());
            await AssertInvalidParamsAsync("thread/items/list",
                new { threadId = thread.Id, turnId = turnId == "turn_001" ? "turn_002" : "turn_001", cursor = next });
            await AssertInvalidParamsAsync("thread/items/list",
                new { threadId = thread.Id, turnId, cursor = next, sortDirection = "ascending" });
        }
        await AssertInvalidParamsAsync("thread/items/list",
            new { threadId = thread.Id, turnId = "turn_001", cursor = anchor, sortDirection = "ascending" });
        var other = await _h.Service.CreateThreadAsync(_h.Identity);
        await AssertInvalidParamsAsync("thread/items/list", new { threadId = other.Id, cursor = anchor });
    }

    [Fact]
    public async Task Resume_EphemeralThreadReturnsNullPersistedHistoryAnchors()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity, ephemeral: true);
        AddCompletedTurn(thread, "turn_001", "in memory");
        var resumed = await ReadHistoryRpcAsync("thread/resume", new { threadId = thread.Id });
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("turnsBackwardsCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("itemsBackwardsCursor").ValueKind);
    }

    [Fact]
    public async Task TimelineRpc_ReturnsRealOrderedBoundariesAndTypedItems_AndScopesCursor()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        foreach (var turn in thread.Turns)
            AddHistoryAnswer(turn);
        await _h.Service.SeedThreadAsync(thread);
        var first = await ReadHistoryRpcAsync("thread/timeline/list", new { threadId = thread.Id, limit = 3 });
        var entries = first.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(["item", "item", "turnCompleted"], entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Equal(entries.Select(entry => entry.GetProperty("position").GetInt64()).Order(),
            entries.Select(entry => entry.GetProperty("position").GetInt64()));
        Assert.All(entries, entry => Assert.Equal("turn_002", entry.GetProperty("turnId").GetString()));
        Assert.Equal("final_answer", entries[1].GetProperty("item").GetProperty("payload").GetProperty("phase").GetString());
        Assert.False(entries[2].TryGetProperty("item", out _));
        Assert.Equal("completed", entries[2].GetProperty("status").GetString());
        Assert.Equal(thread.Turns[1].StartedAt, entries[2].GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(thread.Turns[1].CompletedAt, entries[2].GetProperty("completedAt").GetDateTimeOffset());
        var next = first.GetProperty("nextCursor").GetString();
        var older = await ReadHistoryRpcAsync("thread/timeline/list", new { threadId = thread.Id, limit = 3, cursor = next });
        Assert.All(older.GetProperty("data").EnumerateArray(),
            entry => Assert.True(entry.GetProperty("position").GetInt64() < entries[0].GetProperty("position").GetInt64()));
        Assert.Contains(older.GetProperty("data").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "turnStarted");
        var other = await _h.Service.CreateThreadAsync(_h.Identity);
        await AssertInvalidParamsAsync("thread/timeline/list", new { threadId = other.Id, cursor = next });
        await AssertInvalidParamsAsync("thread/items/list", new { threadId = thread.Id, cursor = next });
        var largePage = await ReadHistoryRpcAsync("thread/timeline/list", new { threadId = thread.Id, limit = 500 });
        Assert.Equal(8, largePage.GetProperty("data").GetArrayLength());
        await AssertInvalidParamsAsync("thread/timeline/list", new { threadId = thread.Id, limit = 501 });
        await AssertInvalidParamsAsync("thread/items/list", new { threadId = thread.Id, limit = 500 });
        await AssertInvalidParamsAsync("thread/turns/list", new { threadId = thread.Id, limit = 500 });
    }

    private static void AddHistoryAnswer(SessionTurn turn) =>
        turn.Items.Add(new()
        {
            Id = turn.Id + "_answer", TurnId = turn.Id, Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed, CreatedAt = turn.StartedAt,
            Payload = new AgentMessagePayload { Text = "answer" }
        });

    private async Task<JsonElement> ReadHistoryRpcAsync(string method, object parameters)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(method, parameters));
        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }
}
