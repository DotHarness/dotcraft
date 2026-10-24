using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class NativeSubAgentForkMaterializationTests
{
    [Fact]
    public void BuildHistory_KeepsAssistantReasoningAndToolTrafficInOrder()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(new ChatRole("developer"), "developer"),
            new(ChatRole.User, "task"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("reasoning"),
                new TextContent("calling a tool"),
                new FunctionCallContent("call-1", "ReadFile")
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")]),
            new(ChatRole.User, "follow-up")
        };

        var forked = SessionService.BuildNativeSubAgentForkHistory(history);

        Assert.Equal(history.Select(static message => message.Role), forked.Select(static message => message.Role));
        Assert.Equal(
            history.SelectMany(static message => message.Contents).Select(static content => content.GetType()),
            forked.SelectMany(static message => message.Contents).Select(static content => content.GetType()));
    }

    [Fact]
    public void BuildHistory_RepairsCallsWithoutResults()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "task"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-spawn", "SpawnAgent")])
        };

        var forked = SessionService.BuildNativeSubAgentForkHistory(history);

        var result = Assert.IsType<FunctionResultContent>(Assert.Single(forked[^1].Contents));
        Assert.Equal(ChatRole.Tool, forked[^1].Role);
        Assert.Equal("call-spawn", result.CallId);
    }

    [Fact]
    public void BuildHistory_PreservesInheritedItemsVerbatim()
    {
        var request = new ChatMessage(ChatRole.User, "task\n<system-reminder>\nrendered context\n</system-reminder>")
        {
            MessageId = "msg_parent_first"
        };
        request.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            ["openai.responses.item_id"] = "msg_parent_first"
        };

        var forked = SessionService.BuildNativeSubAgentForkHistory([request]);

        // Byte equality with the parent's request item is what lets the child share the cached
        // prefix, so neither the rendered context block nor the provider item id may be rebuilt.
        var inherited = Assert.Single(forked);
        Assert.Equal(request.Text, inherited.Text);
        Assert.Equal("msg_parent_first", inherited.MessageId);
        Assert.True(inherited.AdditionalProperties!.TryGetValue("openai.responses.item_id", out var itemId));
        Assert.Equal("msg_parent_first", itemId);
    }
}
