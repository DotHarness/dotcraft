using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class NativeSubAgentForkMaterializationTests
{
    [Fact]
    public void BuildHistory_RetainsStableConversationAndDropsTransientAssistantTraffic()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(new ChatRole("developer"), "developer"),
            new(ChatRole.User, "task"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("reasoning"),
                new TextContent("final answer")
            ]),
            new(ChatRole.Assistant,
            [
                new TextContent("calling a tool"),
                new FunctionCallContent("call-1", "ReadFile")
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "contents")])
        };

        var forked = SessionService.BuildNativeSubAgentForkHistory(history);

        Assert.Collection(
            forked,
            message => Assert.Equal(ChatRole.System, message.Role),
            message => Assert.Equal(new ChatRole("developer"), message.Role),
            message => Assert.Equal(ChatRole.User, message.Role),
            message =>
            {
                Assert.Equal(ChatRole.Assistant, message.Role);
                var text = Assert.IsType<TextContent>(Assert.Single(message.Contents));
                Assert.Equal("final answer", text.Text);
            });
        Assert.DoesNotContain(forked.SelectMany(static message => message.Contents),
            static content => content is TextReasoningContent or FunctionCallContent or FunctionResultContent);
    }
}
