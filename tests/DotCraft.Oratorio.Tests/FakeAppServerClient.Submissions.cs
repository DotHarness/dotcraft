using System.Text.Json;

namespace DotCraft.Oratorio.Tests;

internal sealed partial class FakeAppServerClient
{
    private async Task SubmitImplementationDraftAsync(string threadId, string turnId)
    {
        if (_dynamicToolHandler is not null)
        {
            var arguments = JsonSerializer.SerializeToElement(new
            {
                summary = "Implemented the requested issue workflow.",
                tests = new[] { "dotnet test passed" },
                risks = Array.Empty<string>(),
                changedFiles = new[] { "src/Implementation.cs" },
                proposedCommitMessage = "Implement issue workflow",
                proposedPrTitle = "Implement issue workflow",
                proposedPrBody = "Implements the requested workflow."
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var toolThreadId = useMismatchedToolThreadId() ? "thread-mismatch" : threadId;
            var result = await _dynamicToolHandler(CreateCall(toolThreadId, turnId, "call-implementation-1", "oratorio_run", "SubmitImplementationDraft", arguments), CancellationToken.None);
            captureToolResult(result);
        }
    }

    private async Task ReplyOnSteerAsync(string threadId, string turnId)
    {
        var prompt = await _steered.Task;
        await SubmitDiscussionReplyAsync(threadId, turnId, "call-discussion-1", ExtractDiscussionTurnId(prompt, null));
        if (outcome == FakeAppServerOutcome.SubmitReviewDraftAfterSteer)
        {
            await SubmitSummaryOnlyDraftAsync(threadId, turnId, "call-review-1", "Review summary after answering a question.");
        }
        else if (outcome == FakeAppServerOutcome.SubmitImplementationDraftAfterSteer)
        {
            await SubmitImplementationDraftAsync(threadId, turnId);
        }

        _notifications.Writer.TryWrite(Notification("item/agentMessage/delta", new { delta = "DotCraft analysis complete. " }));
        _notifications.Writer.TryWrite(Notification("turn/completed", new { threadId, turnId }));
        _notifications.Writer.TryComplete();
    }
}
