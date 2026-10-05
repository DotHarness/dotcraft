using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_DrainsRunningInputAfterPreSamplingCompaction()
    {
        var inner = new RoundTripFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "tool ok", name: "GetStatus")]
        };
        var prepared = new List<List<ChatMessage>>();
        using var sampling = StreamingSamplingRuntimeScope.Set((messages, _, _) =>
        {
            prepared.Add(messages.ToList());
            return Task.FromResult(prepared.Count == 2
                ? new StreamingSamplingPreparation([new ChatMessage(ChatRole.User, "summary")], false, true)
                : new StreamingSamplingPreparation(messages, false, false));
        });
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) => boundary == StreamingGuidanceBoundary.AfterTools
                ? RunningInput(new ChatMessage(ChatRole.User, "steer"))
                : NoRunningInput()
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        Assert.DoesNotContain(prepared[1], message => message.Text == "steer");
        Assert.Equal(["user:summary", "user:steer"], inner.Calls[1].Select(message => $"{message.Role}:{message.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AnswerBoundaryEndsWithoutSamplingWhenDrainAdmitsNothing()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var preparations = 0;
        var boundaries = new List<StreamingGuidanceBoundary>();
        using var sampling = StreamingSamplingRuntimeScope.Set((messages, _, _) =>
        {
            preparations++;
            return Task.FromResult(new StreamingSamplingPreparation(messages, false, false));
        });
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) =>
            {
                boundaries.Add(boundary);
                return NoRunningInput();
            },
            HasPendingGuidanceAsync = _ => Task.FromResult(true)
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Single(inner.Calls);
        Assert.Equal(2, preparations);
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AnswerBoundary], boundaries);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_StreamReissueDoesNotDrainRunningInput()
    {
        var inner = new ScriptedStreamChatClient(
            ([ToolCall("call-1", "GetWeather")], null),
            ([Text("partial")], new IOException("connection reset")),
            ([Text("done")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1))
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "sunny", name: "GetWeather")]
        };
        var boundaries = new List<StreamingGuidanceBoundary>();
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) =>
            {
                boundaries.Add(boundary);
                return boundary == StreamingGuidanceBoundary.AfterTools
                    ? RunningInput(new ChatMessage(ChatRole.User, "steer"))
                    : NoRunningInput();
            }
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather?")]));

        Assert.Equal(3, inner.Requests.Count);
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AfterTools], boundaries);
        Assert.Single(inner.Requests[2], message => message.Text == "steer");
    }
}
