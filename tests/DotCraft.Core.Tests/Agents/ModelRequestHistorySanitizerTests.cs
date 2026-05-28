using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class ModelRequestHistorySanitizerTests
{
    [Fact]
    public void Sanitize_WhenAssistantToolCallIsFollowedByUser_InsertsSyntheticToolResult()
    {
        var call = new FunctionCallContent("call-1", "Exec", new Dictionary<string, object?>());
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "start"),
            new(ChatRole.Assistant, (IList<AIContent>)[call]),
            new(ChatRole.User, "continue")
        };

        var repaired = ModelRequestHistorySanitizer.Sanitize(messages);

        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User], repaired.Select(message => message.Role).ToArray());
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(repaired[2].Contents));
        Assert.Equal("call-1", result.CallId);
        Assert.Contains("repaired an incomplete historical tool call", result.Result?.ToString());
    }

    [Fact]
    public void Sanitize_WhenToolMessageMissesOneResult_AddsOnlyMissingResult()
    {
        var call1 = new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>());
        var call2 = new FunctionCallContent("call-2", "Exec", new Dictionary<string, object?>());
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, (IList<AIContent>)[call1, call2]),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call-1", "ok")])
        };

        var repaired = ModelRequestHistorySanitizer.Sanitize(messages);

        Assert.Equal(ChatRole.Tool, repaired[1].Role);
        var results = repaired[1].Contents.OfType<FunctionResultContent>().ToArray();
        Assert.Equal(["call-1", "call-2"], results.Select(result => result.CallId).ToArray());
        Assert.Equal("ok", results[0].Result);
        Assert.Contains("repaired an incomplete historical tool call", results[1].Result?.ToString());
    }

    [Fact]
    public void Sanitize_WhenToolResultHasNoPendingCall_PreservesExistingBridgeMessage()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "start"),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call-orphan", "old")]),
            new(ChatRole.Assistant, "done")
        };

        var repaired = ModelRequestHistorySanitizer.Sanitize(messages);

        Assert.Same(messages, repaired);
    }

    [Fact]
    public void Sanitize_WhenHistoryAlreadyPaired_ReturnsOriginalList()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, (IList<AIContent>)[
                new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?>())
            ]),
            new(ChatRole.Tool, (IList<AIContent>)[new FunctionResultContent("call-1", "ok")])
        };

        var repaired = ModelRequestHistorySanitizer.Sanitize(messages);

        Assert.Same(messages, repaired);
    }
}
