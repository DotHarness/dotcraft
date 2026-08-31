using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Protocol;

public sealed class UserCoordinationHistoryTests
{
    [Fact]
    public void AsyncAgentMessage_IsExcludedWhileToolPairAndFinalAnswerRemain()
    {
        var now = DateTimeOffset.UtcNow;
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now,
            Items =
            [
                Item(ItemType.UserMessage, new UserMessagePayload { Text = "start" }, 1),
                Item(ItemType.ToolCall, new ToolCallPayload
                {
                    ToolName = "SendUserMessageAsync",
                    ProviderFlatName = "SendUserMessageAsync",
                    CallId = "call_1",
                    Arguments = new JsonObject { ["message"] = "question" }
                }, 2),
                Item(ItemType.AgentMessage, new AgentMessagePayload
                {
                    Text = "question",
                    DeliveryMode = "async"
                }, 3),
                Item(ItemType.ToolResult, new ToolResultPayload
                {
                    ToolName = "SendUserMessageAsync",
                    ProviderFlatName = "SendUserMessageAsync",
                    CallId = "call_1",
                    Result = "{\"accepted\":true}",
                    Success = true
                }, 4),
                Item(ItemType.AgentMessage, new AgentMessagePayload { Text = "done" }, 5)
            ]
        };

        var history = ThreadStore.BuildModelVisibleHistoryFromTurn(turn);
        var text = string.Join("\n", history.SelectMany(message => message.Contents)
            .OfType<TextContent>()
            .Select(content => content.Text));

        Assert.DoesNotContain("question", text, StringComparison.Ordinal);
        Assert.Contains("done", text, StringComparison.Ordinal);
        Assert.Contains(history.SelectMany(message => message.Contents), content =>
            content is FunctionCallContent { CallId: "call_1" });
        Assert.Contains(history.SelectMany(message => message.Contents), content =>
            content is FunctionResultContent { CallId: "call_1" });
    }

    private static SessionItem Item(ItemType type, object payload, int sequence)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(sequence),
            TurnId = "turn_001",
            Type = type,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = payload
        };
    }
}
