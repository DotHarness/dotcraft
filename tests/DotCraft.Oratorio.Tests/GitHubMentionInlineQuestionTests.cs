using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubMentionInlineQuestionTests
{
    private const string Path = "src/Auth/RefreshTokenStore.cs";

    [Fact]
    public async Task InlineReplyOnOratorioFindingJudgedFalsePositive_DismissesFindingAndResolvesThread()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.SyncPullRequestAsync();
        await PostAsync<ItemDetailResponse>(
            harness.Client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Review the pull request.", null, null));
        await WaitForItemByIdAsync(harness.Client, itemId, x => x.ReviewDrafts.Any(draft => draft.Status == ReviewDraftStatus.Published));
        var finding = (await LoadFindingsAsync(app)).First();
        Assert.NotNull(finding.RemoteThreadId);
        var rootId = Assert.IsType<long>(finding.RemoteRootCommentId);

        harness.AppServer.Outcome = FakeAppServerOutcome.DismissFindingThenReply;
        await harness.AskInlineOnGitHubAsync(9701, rootId, finding.Path, finding.Line, "@dotcraft-ai This is intentional, the old token stays valid until rotation.");
        var answered = await harness.WaitForAnsweredAsync(itemId);

        var turn = Assert.Single(answered.DiscussionTurns);
        var reply = Assert.Single(harness.GitHub.ReviewCommentReplies);
        Assert.Equal(184, reply.Number);
        Assert.Equal(rootId, reply.InReplyToId);
        Assert.Contains(GitHubWriteService.DiscussionReplyMarker(turn.DiscussionTurnId), reply.Body);
        Assert.DoesNotContain(harness.GitHub.IssueComments, x => x.Body.Contains(GitHubWriteService.DiscussionReplyMarker(turn.DiscussionTurnId)));
        var resolved = (await LoadFindingsAsync(app)).Single(x => x.DraftCommentId == finding.DraftCommentId);
        Assert.Equal(ReviewFindingResolutionState.Resolved, resolved.ResolutionState);
        Assert.Equal(ReviewFindingResolutionKind.Dismissed, resolved.ResolutionKind);
        Assert.Equal(turn.DiscussionTurnId, resolved.ResolvedViaDiscussionTurnId);
        Assert.Contains(harness.GitHub.ReviewThreadResolutions, x => x.ThreadId == finding.RemoteThreadId && x.Resolved);
        Assert.Contains(harness.GitHub.Reactions, x => x.Kind == GitHubCommentKind.ReviewComment && x.CommentId == 9701);
    }

    [Fact]
    public async Task InlineQuestionOnHumanThread_RepliesInThreadWithoutResolution()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.ReviewPullRequestAsync();
        Assert.NotEmpty(await LoadFindingsAsync(app));

        harness.AppServer.Outcome = FakeAppServerOutcome.DismissFindingThenReply;
        await harness.AskInlineOnGitHubAsync(9702, 9201, Path, 88, "@dotcraft-ai Would extracting this branch change behavior?");
        var answered = await harness.WaitForAnsweredAsync(itemId);

        var reply = Assert.Single(harness.GitHub.ReviewCommentReplies);
        Assert.Equal(9201, reply.InReplyToId);
        Assert.Empty(harness.GitHub.IssueComments);
        Assert.Empty(harness.GitHub.ReviewThreadResolutions);
        Assert.All(await LoadFindingsAsync(app), x => Assert.Equal(ReviewFindingResolutionState.Open, x.ResolutionState));
        Assert.Equal(DiscussionTurnStatus.Succeeded, Assert.Single(answered.DiscussionTurns).Status);
        var prompt = harness.AppServer.TurnPrompts[^1];
        Assert.Contains($"{Path}:88", prompt);
        Assert.Contains("Consider extracting the token validation branch.", prompt);
    }

    [Fact]
    public async Task InlineReplyRetriedAfterLostResponse_DoesNotPostTwice()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var itemId = await harness.ReviewPullRequestAsync();

        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        harness.GitHub.LoseNextReviewCommentReplyResponseCount = 1;
        await harness.AskInlineOnGitHubAsync(9703, 9201, Path, 88, "@dotcraft-ai Why is this branch here?");
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Failed);
        var turn = Assert.Single(answered.DiscussionTurns);
        var failed = Assert.Single(answered.SourceWrites, x => x.WriteId == turn.ReplySourceWriteId);

        var retried = await PostAsync<ItemDetailResponse>(harness.Client, $"/api/v1/source-writes/{failed.WriteId}/retry", new { });

        var write = Assert.Single(retried.SourceWrites, x => x.WriteId == failed.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
        Assert.Equal(harness.GitHub.ReviewCommentReplies[0].Id.ToString(), write.ExternalId);
        Assert.Single(harness.GitHub.ReviewCommentReplies);
    }

    [Fact]
    public async Task Hydration_SkipsCommentsAuthoredByOratorioApp()
    {
        const string ownComment = "Oratorio answered this earlier.";
        const string ownReview = "Oratorio review summary.";
        const string ownInline = "Oratorio inline finding.";
        const string otherBotComment = "CI passed on the latest head.";
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = harness.App;
        var at = DateTimeOffset.Parse("2026-05-02T16:00:00Z");
        var own = new GitHubUser(FakeGitHubApiClient.OwnBotLogin, "Bot");
        harness.GitHub.PullRequestIssueComments.Add(new GitHubComment(9010, ownComment, GitHubQuestionHarness.CommentUrl(9010), own, at, at));
        harness.GitHub.PullRequestIssueComments.Add(new GitHubComment(9011, otherBotComment, GitHubQuestionHarness.CommentUrl(9011), new GitHubUser("ci-helper[bot]", "Bot"), at, at));
        harness.GitHub.SourceReviews.Add(new GitHubReview(9110, ownReview, null, "COMMENTED", own, at));
        harness.GitHub.SourceReviewComments.Add(new GitHubReviewComment(9210, ownInline, GitHubQuestionHarness.ReviewCommentUrl(9210), Path, 90, 90, own, at, at));

        var itemId = await harness.ReviewPullRequestAsync();

        var detail = await harness.DetailAsync(itemId);
        var bodies = detail.Comments!.Select(x => x.Body).ToArray();
        Assert.Contains(bodies, x => x.Contains("Please use a constant-time comparison helper."));
        Assert.Contains(bodies, x => x.Contains(otherBotComment));
        Assert.Contains(bodies, x => x.Contains("Two suggestions remain."));
        Assert.DoesNotContain(bodies, x => x.Contains(ownComment) || x.Contains(ownReview) || x.Contains(ownInline));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var context = (await db.Runs.AsNoTracking().SingleAsync(x => x.ItemId == itemId)).PromptContextJson ?? "";
        Assert.Contains("Please use a constant-time comparison helper.", context);
        Assert.DoesNotContain(ownComment, context);
        Assert.DoesNotContain(ownReview, context);
        Assert.DoesNotContain(ownInline, context);
        Assert.DoesNotContain(ownComment, harness.AppServer.TurnPrompts[^1]);
    }

    private static async Task<List<OratorioReviewDraftComment>> LoadFindingsAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return await db.ReviewDraftComments.AsNoTracking()
            .Where(x => x.Status == ReviewDraftCommentStatus.Accepted)
            .OrderBy(x => x.Path)
            .ToListAsync();
    }
}
