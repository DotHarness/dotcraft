using DotCraft.Sessions;
using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerActiveItemRecoveryTests
{
    [Fact]
    public async Task MetadataResumeCarriesOnlyOrderedActiveBaselinesAndCallReadSelectsOneOperation()
    {
        using var harness = new CoreAppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running, Items =
            [
                new SessionItem { Id = "old", TurnId = "turn_001", Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed, Payload = new AgentMessagePayload { Text = "completed history" } },
                new SessionItem { Id = "call", TurnId = "turn_001", Type = ItemType.ToolCall,
                    Status = ItemStatus.Started, Payload = new ToolCallPayload { CallId = "exec", ToolName = "Exec" } },
                new SessionItem { Id = "command", TurnId = "turn_001", Type = ItemType.CommandExecution,
                    Status = ItemStatus.Streaming, Payload = new CommandExecutionPayload { CallId = "exec", Command = "echo ok", AggregatedOutput = "base" } },
                new SessionItem { Id = "answer", TurnId = "turn_001", Type = ItemType.AgentMessage,
                    Status = ItemStatus.Streaming, Payload = new AgentMessagePayload { Text = "prefix" } }
            ]
        });
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadResume, new { threadId = thread.Id }));
        using var response = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var current = response.RootElement.GetProperty("result").GetProperty("currentState");
        Assert.Equal(["call", "command", "answer"], current.GetProperty("activeItems").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()));
        Assert.DoesNotContain("completed history", current.GetRawText());
        Assert.False(response.RootElement.GetProperty("result").TryGetProperty("initialTurnsPage", out _));

        harness.Service.PublishThreadEvent(new SessionEvent
        {
            ThreadId = thread.Id, TurnId = "turn_001", ItemId = "command", EventType = SessionEventType.ItemDelta,
            Payload = new CommandExecutionOutputDelta { TextDelta = "next" }
        });
        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_001", callIds = new[] { "exec" } }));
        using var delta = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(Methods.CommandOutputDelta, delta.RootElement.GetProperty("method").GetString());
        using var read = await harness.Transport.ReadNextSentAsync();
        CoreAppServerTestHarness.AssertIsSuccessResponse(read);
        var items = read.RootElement.GetProperty("result").GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["call", "command"], items.Select(entry => entry.GetProperty("item").GetProperty("id").GetString()));
        Assert.Equal("basenext", items[1].GetProperty("item").GetProperty("payload").GetProperty("aggregatedOutput").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.ThreadItemsRead,
            new { threadId = thread.Id, turnId = "turn_001", itemIds = new[] { "answer" }, callIds = new[] { "exec" } }));
        using var invalid = await harness.Transport.ReadNextSentAsync();
        Assert.True(invalid.RootElement.TryGetProperty("error", out _));
    }
}
