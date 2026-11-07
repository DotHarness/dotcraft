using System.Text.Json;
using DotCraft.Sessions;
using Xunit;
using MethodNames = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ThreadResume_InitialPage_ReservesLiveSlotWithoutSkippingDurableTurns(int limit)
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddCompletedTurn(thread, "turn_003", "third");
        await _h.Service.SeedThreadAsync(thread);
        AddCompletedTurn(thread, "turn_live", "running");
        thread.Turns[^1].Status = TurnStatus.Running;
        thread.Turns[^1].CompletedAt = null;

        var restored = await ResumePageAsync(thread.Id, new { limit, itemsView = "none" });
        var page = restored.GetProperty("initialTurnsPage");
        Assert.Equal(limit, page.GetProperty("data").GetArrayLength());
        Assert.Equal("turn_live", TurnIds(page)[0]);
        Assert.All(page.GetProperty("data").EnumerateArray(),
            turn => Assert.False(turn.TryGetProperty("items", out _)));
        Assert.Equal("turn_live", restored.GetProperty("currentState").GetProperty("turnId").GetString());

        var ids = TurnIds(page).ToList();
        var cursor = page.GetProperty("nextCursor").GetString();
        while (cursor is not null)
        {
            var older = await ListTurnsAsync(new { threadId = thread.Id, cursor, limit, itemsView = "none" });
            ids.AddRange(TurnIds(older));
            cursor = older.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        }
        Assert.Equal(["turn_live", "turn_003", "turn_002", "turn_001"], ids);
    }

    [Fact]
    public async Task ThreadResume_InitialSummary_DefaultsToCandidatesAndFiltersCapabilityCounts()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        var turn = thread.Turns[^1];
        AddToolPair(turn, "read", "ReadFile", null, null);
        AddItem(turn, "execution", ItemType.ToolExecution, new ToolExecutionPayload { CallId = "read", ToolName = "ReadFile" });
        AddItem(turn, "reply", ItemType.AgentMessage, new AgentMessagePayload { Text = "done" });
        await _h.Service.SeedThreadAsync(thread);

        var restored = await ResumePageAsync(thread.Id, new { limit = 1 });
        var projected = Assert.Single(restored.GetProperty("initialTurnsPage").GetProperty("data").EnumerateArray());
        Assert.Equal("summary", projected.GetProperty("itemsView").GetString());
        Assert.Equal(["turn_001_user", "reply"],
            projected.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(4, projected.GetProperty("itemCount").GetInt32());
        Assert.Equal(4, restored.GetProperty("currentState").GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task ThreadResume_InitialFull_OnlyHydratesSelectedPageAndReplacesDurableHead()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        AddCompletedTurn(thread, "turn_002", "second");
        AddToolPair(thread.Turns[^1], "read", "ReadFile", null, null);
        await _h.Service.SeedThreadAsync(thread);

        var restored = await ResumePageAsync(thread.Id, new { limit = 1, itemsView = "full" });
        var page = restored.GetProperty("initialTurnsPage");
        var projected = Assert.Single(page.GetProperty("data").EnumerateArray());
        Assert.Equal("turn_002", projected.GetProperty("id").GetString());
        Assert.Equal("full", projected.GetProperty("itemsView").GetString());
        Assert.Equal(["turn_002_user", "read_call", "read_result"],
            projected.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        var older = await ListTurnsAsync(new { threadId = thread.Id, cursor = page.GetProperty("nextCursor").GetString() });
        Assert.Equal(["turn_001"], TurnIds(older));
    }

    private async Task<JsonElement> ResumePageAsync(string threadId, object initialTurnsPage)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(MethodNames.ThreadResume, new { threadId, initialTurnsPage }));
        var response = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }
}
