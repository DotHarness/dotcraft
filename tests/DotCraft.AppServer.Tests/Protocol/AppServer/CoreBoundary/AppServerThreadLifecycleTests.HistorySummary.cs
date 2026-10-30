using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Xunit;
using MethodNames = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerThreadLifecycleTests
{
    [Fact]
    public async Task ThreadTurnsList_Summary_KeepsWhatAFoldedTurnShows()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "summarize the release");
        var turn = Assert.Single(thread.Turns);
        AddItem(turn, "reasoning", ItemType.ReasoningContent, new ReasoningContentPayload { Text = "thinking" });
        AddToolPair(turn, "read", "ReadFile", null, structuredContent: null);
        AddToolPair(turn, "write", "WriteFile", null, new JsonObject { ["kind"] = "fileChange", ["changes"] = new JsonArray() });
        AddToolPair(turn, "plan", "CreatePlan", null, structuredContent: null);
        AddItem(turn, "command", ItemType.CommandExecution, new CommandExecutionPayload { Command = "dotnet test", CallId = "read" });
        AddItem(turn, "image", ItemType.ImageGeneration, new ImageGenerationPayload { CallId = "image", Status = "completed" });
        AddItem(turn, "task", ItemType.DynamicToolCall, new DynamicToolCallPayload
        {
            Namespace = "desktop", ToolName = "CreateThread", ProviderFlatName = "desktop__CreateThread", CallId = "task"
        });
        AddItem(turn, "final", ItemType.AgentMessage, new AgentMessagePayload { Text = "Done." });
        await _h.Service.SeedThreadAsync(thread);

        var none = await ListTurnsAsync(new { threadId = thread.Id });
        Assert.False(none.GetProperty("data")[0].TryGetProperty("items", out _));

        var summary = (await ListTurnsAsync(new
        {
            threadId = thread.Id,
            itemsView = "summary",
            summaryTools = new object[] { new { name = "CreatePlan" }, new { name = "CreateThread", @namespace = "desktop" } }
        })).GetProperty("data")[0];

        Assert.Equal("summary", summary.GetProperty("itemsView").GetString());
        Assert.Equal(
            ["turn_001_user", "write_call", "write_result", "plan_call", "plan_result", "image", "task", "final"],
            summary.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));
        Assert.Equal(await CountTurnItemsAsync(thread.Id, turn.Id), summary.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task ThreadTurnsList_Summary_RejectsInvalidViews()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        AddCompletedTurn(thread, "turn_001", "first");
        await _h.Service.SeedThreadAsync(thread);

        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new { threadId = thread.Id, itemsView = "full" });
        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new
        {
            threadId = thread.Id,
            summaryTools = new object[] { new { name = "CreatePlan" } }
        });
        await AssertInvalidParamsAsync(MethodNames.ThreadTurnsList, new
        {
            threadId = thread.Id,
            itemsView = "summary",
            summaryTools = Enumerable.Range(0, 33).Select(index => new { name = $"Tool{index}" }).ToArray()
        });
    }

    private async Task<int> CountTurnItemsAsync(string threadId, string turnId)
    {
        await _h.ExecuteRequestAsync(_h.BuildRequest(MethodNames.ThreadItemsList, new { threadId, turnId }));
        var doc = await _h.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(doc);
        return doc.RootElement.GetProperty("result").GetProperty("data").GetArrayLength();
    }

    private static void AddToolPair(SessionTurn turn, string callId, string toolName, string? ns, JsonNode? structuredContent)
    {
        AddItem(turn, $"{callId}_call", ItemType.ToolCall, new ToolCallPayload
        {
            Namespace = ns, ToolName = toolName, ProviderFlatName = toolName, CallId = callId
        });
        AddItem(turn, $"{callId}_result", ItemType.ToolResult, new ToolResultPayload
        {
            Namespace = ns, ToolName = toolName, ProviderFlatName = toolName, CallId = callId, Result = "ok",
            StructuredContent = structuredContent
        });
    }

    private static void AddItem(SessionTurn turn, string id, ItemType type, object payload)
    {
        var at = turn.StartedAt.AddMilliseconds(turn.Items.Count);
        turn.Items.Add(new SessionItem
        {
            Id = id,
            TurnId = turn.Id,
            Type = type,
            Status = ItemStatus.Completed,
            CreatedAt = at,
            CompletedAt = at,
            Payload = payload
        });
    }
}
