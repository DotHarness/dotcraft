using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStreamingResponseAsync_CompletesWhenEmptyProviderResponseStopsNormally(bool usageOnly)
    {
        AIContent[] contents = usageOnly
            ? [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 0 })]
            : [];
        var terminal = new ChatResponseUpdate(ChatRole.Assistant, contents)
        {
            FinishReason = ChatFinishReason.Stop
        };
        var client = new StreamingFunctionInvokingChatClient(new FixedUpdatesFakeChatClient(terminal));

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(ChatFinishReason.Stop, Assert.Single(updates).FinishReason);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStreamingResponseAsync_PreservesTerminalContentFilter(bool afterTool)
    {
        var terminal = new ChatResponseUpdate(ChatRole.Assistant,
            [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 0 })])
        {
            FinishReason = ChatFinishReason.ContentFilter
        };
        var inner = afterTool
            ? new ScriptedStreamChatClient(([ToolCall("call-1", "GetStatus")], null), ([terminal], null))
            : new ScriptedStreamChatClient(([terminal], null));
        using var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "tool ok", name: "GetStatus")]
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(ChatFinishReason.ContentFilter, updates.Last().FinishReason);
        var usage = Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
        Assert.Equal(10, usage.Details.InputTokenCount);
        Assert.Equal(afterTool ? 2 : 1, inner.Requests.Count);
        if (afterTool)
            Assert.Equal("call-1", Assert.Single(inner.Requests[1].SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()).CallId);
    }

    [Theory]
    [InlineData("provider error")]
    [InlineData("")]
    public async Task GetStreamingResponseAsync_ThrowsWhenEmptyStopContainsProviderError(string message)
    {
        var terminal = new ChatResponseUpdate(ChatRole.Assistant, [new ErrorContent(message)])
        {
            FinishReason = ChatFinishReason.Stop
        };
        var client = new StreamingFunctionInvokingChatClient(new FixedUpdatesFakeChatClient(terminal));

        await Assert.ThrowsAsync<EmptyProviderResponseException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));
    }

    [Theory]
    [InlineData("empty", "empty streaming response")]
    [InlineData("usage", "assistant content")]
    [InlineData("error", "provider returned an empty error response")]
    public async Task GetStreamingResponseAsync_ThrowsEmptyResponse_WhenProviderEmitsNoAssistantContent(
        string kind,
        string expectedMessage)
    {
        AIContent[] contents = kind switch
        {
            "usage" => [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 0 })],
            "error" => [new ErrorContent("provider returned an empty error response")],
            _ => []
        };
        var client = new StreamingFunctionInvokingChatClient(
            new FixedUpdatesFakeChatClient(new ChatResponseUpdate(ChatRole.Assistant, contents)));

        var ex = await Assert.ThrowsAsync<EmptyProviderResponseException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
