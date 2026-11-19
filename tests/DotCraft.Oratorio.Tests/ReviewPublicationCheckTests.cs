using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ReviewPublicationCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoPublicationWithoutReview_ReportsFailureAndClearsNoticeAfterRetry(bool blockedByWarnings)
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = blockedByWarnings ? 0 : 1 };
        await using var app = CreateApp(github, blockedByWarnings);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var draft = Assert.Single(failed.ReviewDrafts);
        var write = Assert.Single(failed.SourceWrites, write => write.Intent == "reviewDraftPublish");

        Assert.Equal(blockedByWarnings ? "reviewDraftWarnings" : "githubWriteFailed", write.ErrorCode);
        Assert.Empty(github.PullRequestReviews);
        Assert.Equal(RunStatus.Succeeded, Assert.Single(failed.Runs).Status);
        var check = Assert.Single(github.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
        var checkId = check.ExternalId;
        Assert.Contains("review not published", check.Title, StringComparison.OrdinalIgnoreCase);

        var retryPath = blockedByWarnings
            ? $"/api/v1/review-drafts/{draft.DraftId}/publish"
            : $"/api/v1/source-writes/{write.WriteId}/retry";
        var published = await PostAsync<ItemDetailResponse>(client, retryPath, new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Single(github.PullRequestReviews);
        var publishedCheck = Assert.Single(github.CheckRuns);
        Assert.Equal(checkId, publishedCheck.ExternalId);
        Assert.Equal("neutral", publishedCheck.Conclusion);
        Assert.Contains("review published", publishedCheck.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryAfterApproval_DoesNotOverwriteDecisionCheck()
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = 1 };
        await using var app = CreateApp(github);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var write = Assert.Single(failed.SourceWrites, write => write.Intent == "reviewDraftPublish");
        var check = Assert.Single(github.CheckRuns);

        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{failed.Item.ItemId}/approve", new DecisionRequest("Approved."));
        Assert.Equal("success", check.Conclusion);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{write.WriteId}/retry", new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Equal(ItemState.Approved, published.Item.State);
        Assert.Equal("success", Assert.Single(github.CheckRuns).Conclusion);
    }

    [Fact]
    public async Task RetryAfterHeadChanges_DoesNotUpdateCheckForPreviousHead()
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = 1 };
        await using var app = CreateApp(github);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var write = Assert.Single(failed.SourceWrites, write => write.Intent == "reviewDraftPublish");
        var checkTitle = Assert.Single(github.CheckRuns).Title;
        var pull = Assert.Single(github.PullRequests);
        github.PullRequests[0] = pull with
        {
            Head = pull.Head with { Sha = "def456" },
            UpdatedAt = pull.UpdatedAt.AddMinutes(1)
        };
        using (var scope = app.Services.CreateScope())
        {
            var sync = await scope.ServiceProvider.GetRequiredService<GitHubSourceService>().SyncAsync(CancellationToken.None);
            Assert.Empty(sync.Errors);
        }

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{write.WriteId}/retry", new { });

        Assert.Equal("def456", published.Item.HeadSha);
        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Equal(checkTitle, Assert.Single(github.CheckRuns).Title);
    }

    private static TestOratorioApp CreateApp(FakeGitHubApiClient github, bool blockedByWarnings = false)
    {
        var appServer = new FakeAppServerClientFactory(blockedByWarnings
            ? FakeAppServerOutcome.SubmitNoOpReviewDraft
            : FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        return new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(github);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(appServer);
        });
    }

    private static async Task<ItemDetailResponse> RunReviewUntilPublicationFailsAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review the pull request.", null, null));
        return await WaitForItemByIdAsync(client, pr.ItemId!, item =>
            item.ReviewDrafts.Any(draft => draft.Status == ReviewDraftStatus.PublishFailed));
    }
}
