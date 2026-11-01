using System.Text.Json;
using DotCraft.Sessions;
using Xunit;
using MethodNames = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Fact]
    public async Task MessageSearchCanNavigateToTheTurnAndReadTheExactItem()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_older", "older text");
        AddCompletedTurn(thread, "turn_match", "head 😀 needle middle NEEDLE tail");
        var turn = thread.Turns[1];
        AddItem(turn, "thought", ItemType.ReasoningContent, new ReasoningContentPayload { Text = "needle" });
        AddItem(turn, "answer", ItemType.AgentMessage, new AgentMessagePayload { Text = "reply needle" });
        await _h.Service.SeedThreadAsync(thread);

        var first = await ReadRetrievalAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = " needle ", limit = 2 });
        var hits = first.GetProperty("data");
        Assert.Equal(2, hits.GetArrayLength());
        Assert.All(hits.EnumerateArray(), hit =>
        {
            Assert.Equal("turn_match_user", hit.GetProperty("itemId").GetString());
            var range = hit.GetProperty("snippetMatchRange");
            var start = range.GetProperty("start").GetInt32();
            var end = range.GetProperty("end").GetInt32();
            Assert.Equal("needle", hit.GetProperty("snippet").GetString()![start..end].ToLowerInvariant());
        });

        var turns = await ListTurnsAsync(new
        {
            threadId = thread.Id,
            cursor = hits[0].GetProperty("turnCursor").GetString(),
            sortDirection = "ascending",
            limit = 1
        });
        Assert.Equal(["turn_match"], TurnIds(turns));
        var second = await ReadRetrievalAsync(MethodNames.ThreadSearchOccurrences, new
        {
            threadId = thread.Id,
            searchTerm = "needle",
            cursor = first.GetProperty("nextCursor").GetString(),
            limit = 2
        });
        Assert.Equal("answer", Assert.Single(second.GetProperty("data").EnumerateArray()).GetProperty("itemId").GetString());
        Assert.False(second.TryGetProperty("nextCursor", out _));

        var exact = await ReadRetrievalAsync(MethodNames.ThreadItemsRead, new
        {
            threadId = thread.Id, turnId = turn.Id,
            itemIds = new[] { "answer", "missing", "turn_match_user", "answer" }
        });
        var items = exact.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["turn_match_user", "answer"], items.Select(entry => entry.GetProperty("item").GetProperty("id").GetString()));
        Assert.Equal("head 😀 needle middle NEEDLE tail",
            items[0].GetProperty("item").GetProperty("payload").GetProperty("text").GetString());
    }

    [Fact]
    public async Task SearchCursorRejectsOtherQueriesThreadsAndMalformedTokens()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_match", "needle needle");
        await _h.Service.SeedThreadAsync(thread);
        var other = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(other, "turn_other", "needle needle");
        await _h.Service.SeedThreadAsync(other);
        var page = await ReadRetrievalAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = "needle", limit = 1 });
        var cursor = page.GetProperty("nextCursor").GetString();

        await AssertInvalidParamsAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = other.Id, searchTerm = "needle", cursor });
        await AssertInvalidParamsAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = "different", cursor });
        await AssertInvalidParamsAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = "needle", cursor = "invalid!" });
        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new { threadId = thread.Id, cursor });
        await AssertInvalidParamsAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = " ", limit = 1 });
        await AssertInvalidParamsAsync(MethodNames.ThreadSearchOccurrences,
            new { threadId = thread.Id, searchTerm = "needle", limit = 101 });
    }

    [Fact]
    public async Task ExactItemReadsValidateSelectionAndRemainTurnScoped()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_first", "first");
        AddCompletedTurn(thread, "turn_second", "second");
        await _h.Service.SeedThreadAsync(thread);

        await AssertInvalidParamsAsync(MethodNames.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_first", itemIds = Array.Empty<string>() });
        await AssertInvalidParamsAsync(MethodNames.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_first", itemIds = new[] { " " } });
        await AssertInvalidParamsAsync(MethodNames.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_first", itemIds = Enumerable.Repeat("item", 101).ToArray() });
        var wrongTurn = await ReadRetrievalAsync(MethodNames.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_first", itemIds = new[] { "turn_second_user" } });
        Assert.Empty(wrongTurn.GetProperty("data").EnumerateArray());
    }

    private async Task<JsonElement> ReadRetrievalAsync(string method, object parameters)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(method, parameters));
        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }
}
