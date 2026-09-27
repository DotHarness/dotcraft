using System.Text.Json;
using DotCraft.AppServer;
using Xunit;
using MethodNames = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Fact]
    public async Task ThreadTurnsList_BackwardsCursor_IncludesAnchorInEitherDirection()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        AddCompletedTurn(thread, "turn_004", "fourth");
        AddCompletedTurn(thread, "turn_005", "fifth");
        await _h.Service.SeedThreadAsync(thread);

        var newest = await ListTurnsAsync(new { threadId = thread.Id, limit = 2, sortDirection = "descending" });
        var older = await ListTurnsAsync(new
        {
            threadId = thread.Id,
            limit = 2,
            cursor = newest.GetProperty("nextCursor").GetString(),
            sortDirection = "descending"
        });
        Assert.Equal(["turn_003", "turn_002"], TurnIds(older));
        var anchor = older.GetProperty("backwardsCursor").GetString();

        var towardOldest = await ListTurnsAsync(new { threadId = thread.Id, cursor = anchor, sortDirection = "descending" });
        Assert.Equal(["turn_003", "turn_002", "turn_001"], TurnIds(towardOldest));
        var towardNewest = await ListTurnsAsync(new { threadId = thread.Id, cursor = anchor, sortDirection = "ascending" });
        Assert.Equal(["turn_003", "turn_004", "turn_005"], TurnIds(towardNewest));
    }

    [Fact]
    public async Task ThreadTurnsList_BackwardsCursor_RejectsOtherThreadItemScopeAndBoundNextCursor()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        await _h.Service.SeedThreadAsync(thread);
        var other = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(other, "turn_other", "other");
        await _h.Service.SeedThreadAsync(other);

        var otherPage = await ListTurnsAsync(new { threadId = other.Id });
        var otherAnchor = otherPage.GetProperty("backwardsCursor").GetString();
        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new { threadId = thread.Id, cursor = otherAnchor });

        var page = await ListTurnsAsync(new { threadId = thread.Id, limit = 1, sortDirection = "descending" });
        var anchor = page.GetProperty("backwardsCursor").GetString();
        await AssertInvalidParamsAsync(MethodNames.ThreadItemsList, new { threadId = thread.Id, cursor = anchor });

        var fromAnchor = await ListTurnsAsync(new { threadId = thread.Id, cursor = anchor, limit = 1, sortDirection = "descending" });
        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new
        {
            threadId = thread.Id,
            cursor = fromAnchor.GetProperty("nextCursor").GetString(),
            sortDirection = "ascending"
        });
    }

    [Fact]
    public async Task ThreadTurnsList_BackwardsCursorAfterRollback_ContinuesFromSurvivingTurns()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        await _h.Service.SeedThreadAsync(thread);

        var page = await ListTurnsAsync(new { threadId = thread.Id, limit = 1, sortDirection = "descending" });
        Assert.Equal(["turn_003"], TurnIds(page));
        var anchor = page.GetProperty("backwardsCursor").GetString();

        await _h.ExecuteRequestAsync(_h.BuildRequest(MethodNames.ThreadRollback, new { threadId = thread.Id, numTurns = 1 }));
        CoreAppServerTestHarness.AssertIsSuccessResponse(await _h.Transport.ReadNextSentAsync());

        var towardOldest = await ListTurnsAsync(new { threadId = thread.Id, cursor = anchor, sortDirection = "descending" });
        Assert.Equal(["turn_002", "turn_001"], TurnIds(towardOldest));
        var towardNewest = await ListTurnsAsync(new { threadId = thread.Id, cursor = anchor, sortDirection = "ascending" });
        Assert.Empty(TurnIds(towardNewest));
        Assert.False(towardNewest.TryGetProperty("backwardsCursor", out _));
    }

    private async Task<JsonElement> ListTurnsAsync(object parameters)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(MethodNames.ThreadTurnsList, parameters));
        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        return doc.RootElement.GetProperty("result").Clone();
    }

    private async Task AssertInvalidParamsAsync(string method, object parameters)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(method, parameters));
        CoreAppServerTestHarness.AssertIsErrorResponse(
            await _h.Transport.ReadNextSentAsync(), AppServerErrors.InvalidParamsCode);
    }

    private static string[] TurnIds(JsonElement result) =>
        result.GetProperty("data").EnumerateArray().Select(turn => turn.GetProperty("id").GetString()!).ToArray();
}
