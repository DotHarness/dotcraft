using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ReviewPublicationCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationRetry_PersistsPublishedReviewBeforeCheckTimeoutAndCanRetryCheck(bool blockedByWarnings)
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = blockedByWarnings ? 0 : 1 };
        await using var app = CreateApp(github, blockedByWarnings);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var draft = Assert.Single(failed.ReviewDrafts);
        var write = Assert.Single(failed.SourceWrites, write => write.WriteId == draft.SourceWriteId);

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
        ItemDetailResponse? stateBeforeCheckUpdate = null;
        github.BeforeCheckRunUpdateAsync = async (_, ct) =>
        {
            stateBeforeCheckUpdate = await client.GetFromJsonAsync<ItemDetailResponse>(
                $"/api/v1/items/id/{failed.Item.ItemId}", JsonOptions, ct);
            throw new TaskCanceledException("Synthetic GitHub check update timeout.");
        };
        var published = await PostAsync<ItemDetailResponse>(client, retryPath, new { });
        github.BeforeCheckRunUpdateAsync = null;

        var publishedWrite = AssertPublishedWrite(Assert.IsType<ItemDetailResponse>(stateBeforeCheckUpdate));
        Assert.Equal(publishedWrite.WriteId, AssertPublishedWrite(published).WriteId);
        Assert.Single(github.PullRequestReviews);
        var failedCheck = Assert.Single(published.SourceWrites, x =>
            x.Kind == SourceWriteKind.CheckRun && x.Status == SourceWriteStatus.Failed);
        Assert.Contains("review not published", check.Title, StringComparison.OrdinalIgnoreCase);

        var recovered = await PostAsync<ItemDetailResponse>(
            client, $"/api/v1/source-writes/{failedCheck.WriteId}/retry", new { });

        Assert.Equal(SourceWriteStatus.Succeeded, Assert.Single(recovered.SourceWrites, x => x.WriteId == failedCheck.WriteId).Status);
        Assert.Single(github.PullRequestReviews);
        var publishedCheck = Assert.Single(github.CheckRuns);
        Assert.Equal(checkId, publishedCheck.ExternalId);
        Assert.Equal("neutral", publishedCheck.Conclusion);
        Assert.Contains("review published", publishedCheck.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanceledPublishRequest_LeavesCheckRetryableWithoutRepublishingReview()
    {
        var github = new FakeGitHubApiClient();
        await using var app = CreateApp(github, blockedByWarnings: true);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var draft = Assert.Single(failed.ReviewDrafts);
        var itemPath = $"/api/v1/items/id/{failed.Item.ItemId}";

        var patchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var patchBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        github.BeforeCheckRunUpdateAsync = (_, ct) =>
        {
            patchStarted.SetResult();
            return patchBlocked.Task.WaitAsync(ct);
        };

        using var scope = app.Services.CreateScope();
        using var caller = new CancellationTokenSource();
        var publication = scope.ServiceProvider.GetRequiredService<ReviewDraftService>().PublishAsync(
            draft.DraftId, scope.ServiceProvider.GetRequiredService<OratorioService>(), caller.Token);
        string checkWriteId;
        try
        {
            await patchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var duringPatch = await client.GetFromJsonAsync<ItemDetailResponse>(itemPath, JsonOptions);
            AssertPublishedWrite(duringPatch!);
            checkWriteId = Assert.Single(duringPatch!.SourceWrites, write =>
                write.Kind == SourceWriteKind.CheckRun && write.Status == SourceWriteStatus.Pending).WriteId;
        }
        finally
        {
            caller.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication.WaitAsync(TimeSpan.FromSeconds(10)));
        github.BeforeCheckRunUpdateAsync = null;

        var afterCancel = await client.GetFromJsonAsync<ItemDetailResponse>(itemPath, JsonOptions);
        AssertPublishedWrite(afterCancel!);
        Assert.Equal(SourceWriteStatus.Failed, Assert.Single(afterCancel!.SourceWrites, write => write.WriteId == checkWriteId).Status);

        var recovered = await PostAsync<ItemDetailResponse>(
            client, $"/api/v1/source-writes/{checkWriteId}/retry", new { });

        Assert.Equal(SourceWriteStatus.Succeeded, Assert.Single(recovered.SourceWrites, write => write.WriteId == checkWriteId).Status);
        Assert.Single(github.PullRequestReviews);
        Assert.Contains("review published", Assert.Single(github.CheckRuns).Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovalOverlappingPublicationRetry_LeavesDecisionOnTheSameCheck()
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = 1 };
        await using var app = CreateApp(github);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var draft = Assert.Single(failed.ReviewDrafts);
        var checkId = Assert.Single(github.CheckRuns).ExternalId;
        var publicationUpdateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublicationUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        github.BeforeCheckRunUpdateAsync = (conclusion, ct) =>
        {
            if (conclusion != "neutral")
            {
                return Task.CompletedTask;
            }

            publicationUpdateStarted.TrySetResult();
            return releasePublicationUpdate.Task.WaitAsync(ct);
        };

        using var approvalScope = app.Services.CreateScope();
        var retry = PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{draft.SourceWriteId}/retry", new { });
        Task<ItemDetailResponse> approval;
        try
        {
            await publicationUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            approval = approvalScope.ServiceProvider.GetRequiredService<OratorioService>()
                .ApproveByIdAsync(failed.Item.ItemId, new DecisionRequest("Approved."), CancellationToken.None);
            var duringUpdate = await client.GetFromJsonAsync<ItemDetailResponse>(
                $"/api/v1/items/id/{failed.Item.ItemId}", JsonOptions);
            Assert.Equal(ItemState.Approved, duringUpdate!.Item.State);
            Assert.Equal("neutral", Assert.Single(github.CheckRuns).Conclusion);
        }
        finally
        {
            releasePublicationUpdate.TrySetResult();
        }

        await Task.WhenAll(retry, approval).WaitAsync(TimeSpan.FromSeconds(10));

        var approvedCheck = Assert.Single(github.CheckRuns);
        Assert.Equal(checkId, approvedCheck.ExternalId);
        Assert.Equal("success", approvedCheck.Conclusion);
    }

    [Fact]
    public async Task FailedCompletionRetry_ReportsCurrentPublicationFailureOnTheExistingCheck()
    {
        var github = new FakeGitHubApiClient();
        github.BeforeCheckRunUpdateAsync = (_, _) =>
        {
            github.BeforeCheckRunUpdateAsync = null;
            return Task.FromException(new HttpRequestException("Synthetic GitHub check update failure."));
        };

        await using var app = CreateApp(github, blockedByWarnings: true);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var failedCompletion = Assert.Single(failed.SourceWrites, x =>
            x.Kind == SourceWriteKind.CheckRun && x.Status == SourceWriteStatus.Failed);
        var check = Assert.Single(github.CheckRuns);
        var checkId = check.ExternalId;
        Assert.Equal("completed", check.Status);
        Assert.Contains("review not published", check.Title, StringComparison.OrdinalIgnoreCase);

        var retried = await PostAsync<ItemDetailResponse>(
            client, $"/api/v1/source-writes/{failedCompletion.WriteId}/retry", new { });

        Assert.Equal(SourceWriteStatus.Succeeded, Assert.Single(retried.SourceWrites, x => x.WriteId == failedCompletion.WriteId).Status);
        Assert.Empty(github.PullRequestReviews);
        check = Assert.Single(github.CheckRuns);
        Assert.Equal(checkId, check.ExternalId);
        Assert.Equal("neutral", check.Conclusion);
        Assert.Contains("review not published", check.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryAfterHeadChanges_DoesNotUpdateCheckForPreviousHead()
    {
        var github = new FakeGitHubApiClient { FailNextPullRequestReviewCount = 1 };
        await using var app = CreateApp(github);
        var client = app.CreateClient();
        var failed = await RunReviewUntilPublicationFailsAsync(client);
        var draft = Assert.Single(failed.ReviewDrafts);
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

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{draft.SourceWriteId}/retry", new { });

        Assert.Equal("def456", published.Item.HeadSha);
        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Equal(checkTitle, Assert.Single(github.CheckRuns).Title);
    }

    private static SourceWriteDto AssertPublishedWrite(ItemDetailResponse item)
    {
        var draft = Assert.Single(item.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        var write = Assert.Single(item.SourceWrites, x => x.WriteId == draft.SourceWriteId);
        Assert.Equal(SourceWriteKind.PullRequestReview, write.Kind);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
        return write;
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
            item.ReviewDrafts.Any(draft => draft.Status == ReviewDraftStatus.PublishFailed) &&
            item.SourceWrites.Any(write => write.Kind == SourceWriteKind.CheckRun &&
                write.Intent == "reviewGatePublication" && write.Status != SourceWriteStatus.Pending));
    }
}
