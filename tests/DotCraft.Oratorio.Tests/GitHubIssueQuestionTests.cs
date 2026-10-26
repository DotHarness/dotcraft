using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubIssueQuestionTests
{
    [Theory]
    [InlineData("oratorio")]
    [InlineData("pull")]
    public async Task ScannedIssueQuestion_AnswersWithoutWebhook(string repositoryName)
    {
        var repository = $"example-owner/{repositoryName}";
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply,
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:WebhookSecret"] = "",
                ["Oratorio:GitHub:Repositories:0"] = repository,
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = $"github:github.com/{repository}"
            });
        await using var app = harness.App;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GitHubCommentScanService>().ScanAsync(CancellationToken.None);
        }

        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        harness.GitHub.RepositoryIssueComments[repository] =
        [
            new GitHubComment(9670, "@dotcraft-ai Explain the issue", $"https://github.example.test/{repository}/issues/42#issuecomment-9670",
                new GitHubUser("reviewer", "User"), now, now, "MEMBER",
                $"https://api.github.example.test/repos/{repository}/issues/42")
        ];
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GitHubCommentScanService>().ScanAsync(CancellationToken.None);
        }

        var itemId = await WaitForIssueAsync(harness);
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Succeeded);
        Assert.Single(answered.DiscussionTurns);
        Assert.Equal(42, Assert.Single(harness.GitHub.IssueComments).Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuestionOnImplementationThread_KeepsWorkBranchAndRunContract(bool active)
    {
        var harness = new GitHubQuestionHarness(active
            ? FakeAppServerOutcome.SubmitImplementationDraftAfterSteer
            : FakeAppServerOutcome.SubmitImplementationDraft);
        await using var app = harness.App;
        await harness.SyncPullRequestAsync();
        var itemId = await WaitForIssueAsync(harness);
        await PostAsync<ItemDetailResponse>(harness.Client, $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Implement the issue", null, null, "implementation", DeliveryPolicy.ManualDelivery));
        var before = await WaitForItemByIdAsync(harness.Client, itemId, x => active
            ? x.Runs.Any(r => r.Status == RunStatus.Running && r.TurnId != null)
            : x.Runs.Any(r => r.Status == RunStatus.Succeeded));
        var run = Assert.Single(before.Runs);
        harness.AppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;

        using var response = await harness.AskOnIssueAsync(9671, "@dotcraft-ai Explain your approach");
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Succeeded);
        var finished = await WaitForItemByIdAsync(harness.Client, itemId, x => x.Runs.Any(r => r.Status == RunStatus.Succeeded));

        Assert.Equal(run.ThreadId, Assert.Single(answered.DiscussionTurns).ThreadId);
        Assert.Equal(run.RunId, Assert.Single(finished.Runs).RunId);
        Assert.Single(finished.Rounds);
        Assert.Single(finished.ImplementationDrafts);
        Assert.Single(harness.Worktrees.PrepareRequests);
        Assert.Equal(0, harness.GitHub.GetRepositoryCallCount);
        Assert.Empty(harness.GitHub.PullRequestCreates);
        if (active)
        {
            Assert.Equal(run.TurnId, Assert.Single(harness.AppServer.SteerRequests).ExpectedTurnId);
        }
        else
        {
            Assert.Empty(harness.AppServer.SteerRequests);
        }
    }

    [Fact]
    public async Task FirstQuestion_ImportsIssueAndAnswersWithoutDispatchingWork()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        var before = await harness.Client.GetFromJsonAsync<ItemListResponse>("/api/v1/items", JsonOptions);
        Assert.Empty(before!.Items);
        harness.GitHub.DefaultBranch = "trunk";
        harness.Worktrees.BaseShaOverride = "latest-trunk-sha";

        using var response = await harness.AskOnIssueAsync(9701, "@dotcraft-ai Why is this change needed?");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var itemId = await WaitForIssueAsync(harness);
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Succeeded);

        Assert.Equal(ItemKind.Issue, answered.Item.Kind);
        Assert.Equal(ItemState.Discovered, answered.Item.State);
        Assert.Equal(CheckState.NotConfigured, answered.Item.CheckState);
        Assert.Empty(answered.Rounds);
        Assert.Empty(answered.Runs);
        Assert.Empty(answered.ReviewDrafts);
        Assert.Empty(answered.ImplementationDrafts);
        Assert.Empty(harness.GitHub.CheckRuns);
        Assert.Empty(harness.GitHub.PullRequestCreates);
        Assert.Empty(harness.GitHub.PullRequestReviews);
        var turn = Assert.Single(answered.DiscussionTurns);
        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        var reply = Assert.Single(harness.GitHub.IssueComments);
        Assert.Equal(42, reply.Number);
        Assert.Contains("/issues/42#issuecomment-9701", reply.Body);
        Assert.Contains(GitHubWriteService.DiscussionReplyMarker(turn.DiscussionTurnId), reply.Body);
        Assert.Contains(harness.GitHub.Reactions, x => x.CommentId == 9701 && x.Content == "eyes");
        var command = await harness.LoadCommandAsync();
        Assert.Equal(ItemKind.Issue, command.TargetKind);
        Assert.Equal(42, command.TargetNumber);
        var request = Assert.Single(harness.Worktrees.PrepareRequests);
        Assert.Equal("trunk", request.FetchBaseBranch);
        Assert.Null(request.HeadSha);
        Assert.Equal(1, harness.GitHub.GetRepositoryCallCount);
        Assert.False(harness.AppServer.LastThreadStartRequest!.DynamicTools!.ContainsTool("oratorio_run", "SubmitImplementationDraft"));
        Assert.Contains(harness.GitHub.Issues[0].Title, harness.AppServer.TurnPrompts[0]);
        Assert.Contains(harness.GitHub.Issues[0].Body!, harness.AppServer.TurnPrompts[0]);
        Assert.Contains(harness.GitHub.IssueSourceComments[0].Body, harness.AppServer.TurnPrompts[0]);
        Assert.Contains("latest-trunk-sha", harness.AppServer.TurnPrompts[0]);
        Assert.Contains(harness.AppServer.LastThreadStartRequest.ExecutionWorkspacePath, harness.AppServer.TurnPrompts[0]);
    }

    [Fact]
    public async Task LaterQuestion_ReusesThreadAndRefreshesDefaultBranchAndDiscussion()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        using var first = await harness.AskOnIssueAsync(9702, "@dotcraft-ai Explain the issue");
        var itemId = await WaitForIssueAsync(harness);
        await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Succeeded);
        await WaitUntilAsync(() => app.Services.GetRequiredService<DiscussionThreadSessions>().Find("thread-test-1") is null);
        harness.Worktrees.BaseShaOverride = "new-main-head";
        harness.GitHub.Issues[0] = harness.GitHub.Issues[0] with { Body = "Updated issue requirements." };
        harness.GitHub.IssueSourceComments.Add(harness.GitHub.IssueSourceComments[0] with
        {
            Id = 9799, Body = "Additional human context.", HtmlUrl = "https://github.example.test/comment/9799"
        });

        using var second = await harness.AskOnIssueAsync(9703, "@dotcraft-ai What about the updated requirements?");
        var answered = await WaitForItemByIdAsync(harness.Client, itemId,
            x => x.DiscussionTurns.Count(t => t.Status == DiscussionTurnStatus.Succeeded) == 2 &&
                x.SourceWrites.Count(w => w.Status == SourceWriteStatus.Succeeded) == 2);

        Assert.Equal(1, harness.AppServer.StartThreadCount);
        Assert.Equal(2, harness.Worktrees.PrepareRequests.Count);
        Assert.Equal(2, harness.GitHub.GetRepositoryCallCount);
        Assert.All(answered.DiscussionTurns, x => Assert.Equal("thread-test-1", x.ThreadId));
        Assert.Contains("Updated issue requirements.", harness.AppServer.TurnPrompts[^1]);
        Assert.Contains("Additional human context.", harness.AppServer.TurnPrompts[^1]);
        Assert.Contains("new-main-head", harness.AppServer.TurnPrompts[^1]);
        Assert.Contains("Agent answer from DotCraft.", harness.AppServer.TurnPrompts[^1]);
        Assert.Empty(answered.Runs);
    }

    [Theory]
    [InlineData("@dotcraft-ai /review")]
    [InlineData("@dotcraft-ai review")]
    public async Task IssueReviewCommand_IsRejectedWithoutAcknowledgement(string body)
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        using var response = await harness.AskOnIssueAsync(9710, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = await harness.LoadCommandAsync();
        Assert.Equal(GitHubCommentCommandStatus.Rejected, command.Status);
        Assert.Equal("githubCommentCommandUnsupportedForIssue", command.ErrorCode);
        Assert.Empty(harness.GitHub.Reactions);
        Assert.Empty(harness.GitHub.IssueComments);
        Assert.Empty(harness.Worktrees.PrepareRequests);
    }

    [Fact]
    public async Task ClosedIssue_IsRejectedBeforeAcknowledgement()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        harness.GitHub.Issues[0] = harness.GitHub.Issues[0] with { State = "closed", ClosedAt = DateTimeOffset.UtcNow };
        using var response = await harness.AskOnIssueAsync(9711, "@dotcraft-ai Explain this");
        await WaitUntilAsync(async () => (await harness.LoadCommandAsync()).Status == GitHubCommentCommandStatus.Rejected);
        Assert.Equal("githubCommentCommandIssueNotOpen", (await harness.LoadCommandAsync()).ErrorCode);
        Assert.Empty(harness.GitHub.Reactions);
        Assert.Empty(harness.GitHub.IssueComments);
        Assert.Empty(harness.Worktrees.PrepareRequests);
    }

    [Theory]
    [InlineData("managedDisabled", "issueDiscussionManagedWorktreeRequired")]
    [InlineData("repositoryFailure", "discussionTurnFailed")]
    [InlineData("fetchFailure", "issueDiscussionBaseFetchFailed")]
    public async Task WorkspaceFailure_DoesNotFallBackOrReply(string failure, string code)
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply,
            new Dictionary<string, string?> { ["Oratorio:DotCraft:ManagedWorktreesEnabled"] = (failure != "managedDisabled").ToString() });
        await using var app = harness.App;
        harness.GitHub.FailRepositoryRead = failure == "repositoryFailure";
        if (failure == "fetchFailure")
        {
            harness.Worktrees.Failure = new WorktreeException(code, "Synthetic fetch failure.");
        }

        using var response = await harness.AskOnIssueAsync(9713, "@dotcraft-ai Explain");
        var itemId = await WaitForIssueAsync(harness);
        var failed = await WaitForItemByIdAsync(harness.Client, itemId,
            x => x.DiscussionTurns.Any(t => t.Status == DiscussionTurnStatus.Failed));
        Assert.Equal(code, Assert.Single(failed.DiscussionTurns).ErrorCode);
        Assert.Empty(harness.GitHub.IssueComments);
        Assert.Equal(0, harness.AppServer.StartThreadCount);
    }

    [Fact]
    public async Task SourceReadFailure_RetriesWithoutDuplicatingQuestion()
    {
        var harness = new GitHubQuestionHarness(FakeAppServerOutcome.SubmitDiscussionReply);
        await using var app = harness.App;
        harness.GitHub.FailNextGetIssueCount = 1;
        using var response = await harness.AskOnIssueAsync(9716, "@dotcraft-ai Explain");
        var itemId = await WaitForIssueAsync(harness);
        var answered = await harness.WaitForReplyWriteAsync(itemId, SourceWriteStatus.Succeeded);
        Assert.Single(answered.DiscussionTurns);
        Assert.Equal(2, (await harness.LoadCommandAsync()).AttemptCount);
        Assert.Single(harness.GitHub.IssueComments);
    }

    private static async Task<string> WaitForIssueAsync(GitHubQuestionHarness harness)
    {
        string? id = null;
        await WaitUntilAsync(async () =>
        {
            var list = await harness.Client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
            id = list!.Items.FirstOrDefault(x => x.Kind == ItemKind.Issue)?.ItemId;
            return id is not null;
        });
        return id!;
    }
}
