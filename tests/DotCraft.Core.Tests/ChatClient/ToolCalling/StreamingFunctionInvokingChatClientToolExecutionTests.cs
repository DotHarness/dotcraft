using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Theory]
    [InlineData("structured-error", "failed", false, "rejected")]
    [InlineData("exception-result", "failed", false, "rejected")]
    [InlineData("success", "completed", true, null)]
    [InlineData("cancelled", "cancelled", false, "cancelled")]
    public async Task GetStreamingResponseAsync_RecordsToolExecutionOutcome(
        string outcome, string expectedStatus, bool expectedSuccess, string? expectedError)
    {
        var inner = new RoundTripFakeChatClient();
        using var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "unused", name: "GetStatus")],
            FunctionInvoker = (context, _) => ValueTask.FromResult<object?>(outcome switch
            {
                "structured-error" => StreamingFunctionInvokingChatClient.CreateToolFailureResult(
                    context.CallContent.CallId, "rejected", "tool_execution_failed"),
                "exception-result" => new FunctionResultContent(context.CallContent.CallId, "rejected")
                {
                    Exception = new InvalidOperationException("rejected")
                },
                "cancelled" => throw new OperationCanceledException("cancelled"),
                _ => new FunctionResultContent(context.CallContent.CallId, "ok")
            })
        };
        var turn = new SessionTurn { Id = "turn_1", ThreadId = "thread_1", StartedAt = DateTimeOffset.UtcNow };
        var completed = new List<SessionItem>();
        using var scope = ToolExecutionRuntimeScope.Set(new ToolExecutionRuntimeContext
        {
            ThreadId = turn.ThreadId,
            TurnId = turn.Id,
            Turn = turn,
            NextItemSequence = () => turn.Items.Count + 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = completed.Add,
            SupportsToolExecutionLifecycle = true
        });
        RegisterToolExecution(turn, "item_1", "call-1", "GetStatus");

        if (outcome == "cancelled")
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));
        }
        else
        {
            var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));
            var result = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>());
            Assert.Equal(expectedSuccess ? "ok" : "rejected", result.Result);
            Assert.Equal(outcome == "structured-error" ? "tool_execution_failed" : null,
                StreamingFunctionInvokingChatClient.GetToolResultErrorCode(result));
        }

        var payload = Assert.IsType<ToolExecutionPayload>(Assert.Single(completed).Payload);
        Assert.Equal(expectedStatus, payload.Status);
        Assert.Equal(expectedSuccess, payload.Success);
        Assert.Equal(expectedError, payload.ErrorMessage);
    }
}
