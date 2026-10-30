using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubMentionQuestionTests
{
    private const string Question = "Why does the refresh path return the old token?\nPlease explain.";

    [Fact]
    public async Task QuestionOnReviewedIdlePullRequest_PostsOneReply()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.ReviewPullRequestAsync();
        var runsBefore = (await harness.DetailAsync(itemId)).Runs.Count;

        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await harness.AskOnGitHubAsync(9501, $"@dotcraft-ai {Question}");
        var answered = await harness.WaitForAnsweredAsync(itemId);

        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        Assert.Equal("github", turn.Origin?.Provider);
        Assert.Equal("example-reviewer", turn.Origin?.AuthorLogin);
        Assert.Equal(CommentUrl(9501), turn.Origin?.Url);
        Assert.Equal("thread-test-1", turn.ThreadId);
        Assert.Contains(answered.Comments, x => x.Purpose == CommentPurpose.DiscussionQuestion && x.Body == Question);
        Assert.Equal(runsBefore, answered.Runs.Count);
        Assert.Contains(Question, harness.AppServer.TurnPrompts[^1]);

        var reply = Assert.Single(harness.GitHub.IssueComments);
        Assert.Equal(184, reply.Number);
        Assert.Contains("Agent answer from DotCraft.", reply.Body);
        Assert.Contains(CommentUrl(9501), reply.Body);
        Assert.Contains(GitHubWriteService.DiscussionReplyMarker(turn.DiscussionTurnId), reply.Body);
        var write = Assert.Single(answered.SourceWrites, x => x.WriteId == turn.ReplySourceWriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
        Assert.NotNull(write.ExternalUrl);

        var command = await harness.LoadCommandAsync();
        Assert.Equal(GitHubCommentCommandStatus.Dispatched, command.Status);
        Assert.Equal(turn.DiscussionTurnId, command.DiscussionTurnId);
        Assert.Equal(Question, command.Question);
        Assert.Contains(harness.GitHub.Reactions, x => x.CommentId == 9501 && x.Content == "eyes");
    }

    [Fact]
    public async Task QuestionAlreadyImportedAsSourceComment_AppearsOnceInDiscussion()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var body = $"@dotcraft-ai {Question}";
        harness.GitHub.PullRequestIssueComments.Add(new GitHubComment(
            9601,
            body,
            CommentUrl(9601),
            new GitHubUser("example-reviewer", "User"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T14:00:00Z")));
        var itemId = await harness.ReviewPullRequestAsync();

        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await harness.AskOnGitHubAsync(9601, body);
        var answered = await harness.WaitForAnsweredAsync(itemId);

        var question = Assert.Single(answered.Comments, x => x.ExternalUrl == CommentUrl(9601));
        Assert.Equal(CommentPurpose.DiscussionQuestion, question.Purpose);
        Assert.Equal(question.CommentId, Assert.Single(answered.DiscussionTurns).QuestionCommentId);
    }

    [Fact]
    public async Task QuestionDuringRunningReview_IsSteeredAndReviewPublishesOnce()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitReviewDraftAfterSteer);
        await using var app = harness.App;
        var itemId = await harness.SyncPullRequestAsync();
        await PostAsync<ItemDetailResponse>(
            harness.Client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Review the pull request.", null, null));
        var running = await WaitForItemByIdAsync(harness.Client, itemId, x => x.Runs.Any(run => run.Status == RunStatus.Running && run.TurnId != null));
        var run = Assert.Single(running.Runs, x => x.Status == RunStatus.Running);

        await harness.AskOnGitHubAsync(9502, $"@dotcraft-ai {Question}");
        var answered = await harness.WaitForAnsweredAsync(itemId);
        var reviewed = await WaitForItemByIdAsync(harness.Client, itemId, x =>
            x.Item.State == ItemState.AwaitingReview &&
            x.ReviewDrafts.Any(draft => draft.Status == ReviewDraftStatus.Published));

        var steer = Assert.Single(harness.AppServer.SteerRequests);
        Assert.Equal(run.TurnId, steer.ExpectedTurnId);
        Assert.Contains(Question, steer.Prompt);
        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(run.RunId, turn.RunId);
        Assert.Equal(run.TurnId, turn.TurnId);
        Assert.Single(harness.GitHub.IssueComments);
        Assert.Single(harness.GitHub.PullRequestReviews);
        Assert.Equal(RunStatus.Succeeded, Assert.Single(reviewed.Runs).Status);
    }

    [Fact]
    public async Task LaterQuestionOnExistingThread_PreparesTheWorktreeAgain()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        var itemId = await harness.SyncPullRequestAsync();
        await harness.AskOnGitHubAsync(9511, $"@dotcraft-ai {Question}");
        await harness.WaitForAnsweredAsync(itemId);
        await WaitUntilAsync(async () =>
        {
            var detail = await harness.DetailAsync(itemId);
            return detail.DiscussionTurns.All(turn => turn.Status == DiscussionTurnStatus.Succeeded);
        });

        await harness.AskOnGitHubAsync(9512, "@dotcraft-ai And the error path?");
        var answered = await WaitForItemByIdAsync(
            harness.Client,
            itemId,
            x => x.DiscussionTurns.Count(turn => turn.Status == DiscussionTurnStatus.Succeeded) == 2);

        Assert.Equal(1, harness.AppServer.StartThreadCount);
        Assert.Equal(2, harness.Worktrees.PrepareRequests.Count);
        Assert.Equal(
            answered.DiscussionTurns.Select(turn => turn.DiscussionTurnId).Order(StringComparer.Ordinal),
            harness.Worktrees.PrepareRequests.Select(request => request.RunId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task QuestionOnNeverReviewedPullRequest_StartsReusableThreadWithoutReviewWork()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        var itemId = await harness.SyncPullRequestAsync();

        await harness.AskOnGitHubAsync(9503, $"@dotcraft-ai {Question}");
        var answered = await harness.WaitForAnsweredAsync(itemId);

        Assert.Empty(answered.Rounds);
        Assert.Empty(answered.Runs);
        Assert.Empty(answered.ReviewDrafts);
        Assert.Empty(harness.GitHub.CheckRuns);
        Assert.Empty(harness.GitHub.PullRequestReviews);
        Assert.Single(harness.GitHub.IssueComments);
        var start = Assert.Single(harness.AppServer.ThreadStartRequests);
        Assert.True((start.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.True((start.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitDiscussionReply"));
        Assert.NotNull(start.RuntimeAdditionalContext);
        Assert.Contains("abc123", harness.AppServer.TurnPrompts[^1]);
        Assert.Single(harness.Worktrees.PrepareRequests);

        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft;
        await PostAsync<ItemDetailResponse>(
            harness.Client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Review the pull request.", null, null));
        var reviewed = await WaitForItemByIdAsync(harness.Client, itemId, x => x.Item.State == ItemState.AwaitingReview);

        Assert.Equal("thread-test-1", Assert.Single(reviewed.Runs).ThreadId);
        Assert.Equal(1, harness.AppServer.StartThreadCount);
        Assert.Equal("thread-test-1", Assert.Single(harness.AppServer.ThreadResumeRequests).ThreadId);
    }

    [Fact]
    public async Task ReplyWriteRetriedAfterLostResponse_DoesNotPostTwice()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.ReviewPullRequestAsync();

        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        harness.GitHub.LoseNextIssueCommentResponseCount = 1;
        await harness.AskOnGitHubAsync(9504, $"@dotcraft-ai {Question}");
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Failed);
        var turn = Assert.Single(answered.DiscussionTurns);
        var failed = Assert.Single(answered.SourceWrites, x => x.WriteId == turn.ReplySourceWriteId);

        var retried = await PostAsync<ItemDetailResponse>(harness.Client, $"/api/v1/source-writes/{failed.WriteId}/retry", new { });

        var write = Assert.Single(retried.SourceWrites, x => x.WriteId == failed.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
        Assert.Equal("100", write.ExternalId);
        Assert.Single(harness.GitHub.IssueComments);
    }

    [Fact]
    public async Task QuestionFailingWithoutReply_PostsNothing()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.ReviewPullRequestAsync();

        harness.AppServer.Outcome = FakeAppServerOutcome.Fail;
        await harness.AskOnGitHubAsync(9505, $"@dotcraft-ai {Question}");
        var failed = await WaitForItemByIdAsync(harness.Client, itemId, x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Failed));

        Assert.Equal("discussionTurnFailed", Assert.Single(failed.DiscussionTurns).ErrorCode);
        Assert.Empty(harness.GitHub.IssueComments);
    }

    private static string CommentUrl(long id) => GitHubQuestionHarness.CommentUrl(id);
}
