using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ReviewDraftApiTests
{
    [Fact]
    public async Task AppServerReviewDraftTool_KeepsOnlyLatestDraftPerRun()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftTwice);
        await using var app = CreateReviewApp(fakeGitHub, fakeAppServer);
        var client = app.CreateClient();
        var pr = await DispatchReviewAsync(client);

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Any(d => d.Status == ReviewDraftStatus.Published));
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(2, fakeAppServer.ToolResults.Count(x => x.Success));
        Assert.Single(reviewed.ReviewDrafts, x => x.Status == ReviewDraftStatus.Published);
        Assert.Single(reviewed.ReviewDrafts, x => x.Status == ReviewDraftStatus.Discarded);
        Assert.Single(fakeGitHub.PullRequestReviews);
    }

    [Fact]
    public async Task AppServerReviewRun_WaitsForBackgroundWork_BeforeRequiringDraft()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraftAfterBackgroundWork);
        await using var app = CreateReviewApp(fakeGitHub, fakeAppServer);
        var client = app.CreateClient();
        ItemSummaryDto? pr = null;
        fakeAppServer.WakeTurnStarted = turnId => WaitForItemByIdAsync(client, pr!.ItemId!, x => x.Runs.Any(r => r.TurnId == turnId));
        pr = await DispatchReviewAsync(client);

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Any(d => d.Status == ReviewDraftStatus.Published));
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("turn-wake-1", run.TurnId);
        Assert.Equal(["thread-test-1"], fakeAppServer.TurnThreadIds);
        Assert.Single(reviewed.ReviewDrafts);
        Assert.Single(fakeGitHub.PullRequestReviews);
    }

    [Fact]
    public async Task AppServerReviewRun_RequiresDraftAfterTurnEndsWithoutBackgroundWork()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.CompleteTurnWithoutDraftStreamOpen);
        await using var app = CreateReviewApp(fakeGitHub, fakeAppServer);
        var client = app.CreateClient();
        var pr = await DispatchReviewAsync(client);

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.All(failed.Runs.Where(x => x.RunnerKind == "appServer"), x => Assert.Equal("reviewDraftRequired", x.ErrorCode));
        Assert.Empty(failed.ReviewDrafts);
    }

    [Fact]
    public async Task AppServerReviewRun_StallDuringWakeTurn_InterruptsWakeTurn()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-16T09:00:00Z"));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.HoldWakeTurnAfterBackgroundWork);
        await using var app = CreateReviewApp(new FakeGitHubApiClient(), fakeAppServer, clock);
        var client = app.CreateClient();
        var pr = await DispatchReviewAsync(client);

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Runs.Any(run => run.TurnId == "turn-wake-1"));
        clock.Advance(TimeSpan.FromSeconds(6));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal("appServerStalled", run.ErrorCode);
        Assert.Equal([("thread-test-1", "turn-wake-1")], fakeAppServer.InterruptedTurns);
    }

    [Fact]
    public async Task AppServerReviewRun_StallWhileWaitingForBackgroundWork_ConfirmsTermination()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-16T09:00:00Z"));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.HoldBackgroundWork);
        await using var app = CreateReviewApp(new FakeGitHubApiClient(), fakeAppServer, clock);
        var client = app.CreateClient();
        var pr = await DispatchReviewAsync(client);

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Runs.Any(run => run.StatusMessage == "Waiting for background tasks to finish."));
        clock.Advance(TimeSpan.FromSeconds(6));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal("appServerStalled", run.ErrorCode);
        Assert.Empty(fakeAppServer.InterruptedTurns);
    }

    private static TestOratorioApp CreateReviewApp(FakeGitHubApiClient fakeGitHub, FakeAppServerClientFactory fakeAppServer, MutableClock? clock = null) =>
        new(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                if (clock is not null)
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(clock);
                }
            },
            clock is null ? null : new Dictionary<string, string?> { ["Oratorio:DotCraft:StallTimeoutSeconds"] = "5", ["Oratorio:DotCraft:MaxRunAttempts"] = "1" });

    private static async Task<ItemSummaryDto> DispatchReviewAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        return pr;
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsMismatchedReusedThreadBinding()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraft)
        {
            UseMismatchedToolThreadId = true
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(reviewed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidRunBinding", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsSuggestionOldTextNotFound()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSuggestionTextNotFoundReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a review draft with unmatched oldText.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("reviewDraftSuggestionTextNotFound", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_RejectsAmbiguousSuggestionOldText()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with
        {
            Patch = """
@@ -84,6 +84,7 @@ public sealed class RefreshTokenStore
     public string Refresh(string token)
     {
+        return refreshed;
+        return refreshed;
     }
 }
"""
        };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitAmbiguousSuggestionTextReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a review draft with ambiguous oldText.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("reviewDraftSuggestionTextAmbiguous", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_ReturnsCommentableRangesForSyntheticBadAnchor()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles.Clear();
        fakeGitHub.PullRequestFiles.Add(new GitHubChangedFile(
            "src/Example/SyntheticFileSearcher.cs",
            "modified",
            0,
            0,
            0,
            FakeGitHubApiClient.SyntheticFileSearcherPatch));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSyntheticBadAnchorReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a synthetic review anchor regression.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var toolResult = Assert.Single(fakeAppServer.ToolResults);

        Assert.Equal("reviewDraftRequired", Assert.Single(failed.Runs, x => x.RunnerKind == "appServer").ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.False(toolResult.Success);
        Assert.Equal("reviewDraftAnchorNotCommentable", toolResult.ErrorCode);
        using var structured = JsonDocument.Parse(JsonSerializer.Serialize(toolResult.StructuredContent, JsonOptions));
        var invalidComment = Assert.Single(structured.RootElement.GetProperty("error").GetProperty("details").GetProperty("invalidComments").EnumerateArray());
        Assert.Equal("Synthetic anchor drift", invalidComment.GetProperty("title").GetString());
        Assert.Equal("src/Example/SyntheticFileSearcher.cs", invalidComment.GetProperty("path").GetString());
        Assert.Equal(129, invalidComment.GetProperty("line").GetInt32());
        Assert.Equal("RIGHT", invalidComment.GetProperty("side").GetString());
        Assert.Equal("lineNotCommentable", invalidComment.GetProperty("reason").GetString());
        Assert.Equal("255-277", invalidComment.GetProperty("rightCommentableRanges").GetString());
    }

    [Fact]
    public async Task AppServerReviewDraftTool_AllowsRetryAfterAnchorFailure()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles.Clear();
        fakeGitHub.PullRequestFiles.Add(new GitHubChangedFile(
            "src/Example/SyntheticFileSearcher.cs",
            "modified",
            0,
            0,
            0,
            FakeGitHubApiClient.SyntheticFileSearcherPatch));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitRetryAnchorReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a retryable synthetic review anchor regression.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.ReviewDrafts.Count == 1 && x.ReviewDrafts[0].Status != ReviewDraftStatus.Draft);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Collection(
            fakeAppServer.ToolResults,
            first =>
            {
                Assert.False(first.Success);
                Assert.Equal("reviewDraftAnchorNotCommentable", first.ErrorCode);
            },
            second => Assert.True(second.Success));
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        Assert.Equal(1, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
        var comment = Assert.Single(draft.Comments);
        Assert.Equal(ReviewDraftCommentStatus.Accepted, comment.Status);
        Assert.Equal("src/Example/SyntheticFileSearcher.cs", comment.Path);
        Assert.Equal(255, comment.Line);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_ReturnsFailedToolResultWhenSourceDiffRequestFails()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailPullRequestFileReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal("reviewDraftRequired", run.ErrorCode);
        Assert.Empty(failed.ReviewDrafts);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("upstreamSourceRequestFailed", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_UsesLocalDiffWhenGitHubFilePatchIsMissing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with { Patch = null };
        var fakeLocalDiff = new FakeReviewLocalDiffProvider(new Dictionary<string, string>
        {
            ["src/Auth/RefreshTokenStore.cs"] = FakeGitHubApiClient.RefreshTokenStorePatch
        });
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IReviewLocalDiffProvider>();
            services.AddSingleton<IReviewLocalDiffProvider>(fakeLocalDiff);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Contains("src/Auth/RefreshTokenStore.cs", fakeLocalDiff.RequestedPaths);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
    }

    [Fact]
    public async Task AppServerReviewDraftTool_SkipsOnlyAffectedCommentWhenPatchAndFallbackAreMissing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.PullRequestFiles[0] = fakeGitHub.PullRequestFiles[0] with { Patch = null };
        var fakeLocalDiff = new FakeReviewLocalDiffProvider();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IReviewLocalDiffProvider>();
            services.AddSingleton<IReviewLocalDiffProvider>(fakeLocalDiff);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Contains("src/Auth/RefreshTokenStore.cs", fakeLocalDiff.RequestedPaths);
        Assert.Equal(1, draft.AcceptedCount);
        Assert.Equal(2, draft.WarningCount);
        Assert.Equal(0, draft.MajorCount);
        Assert.Equal(1, draft.MinorCount);
        Assert.Equal("Found 1 issue.", draft.SummaryBody);
        Assert.Contains(draft.Warnings, warning => warning.Contains("src/Auth/RefreshTokenStore.cs", StringComparison.Ordinal));
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDraftSuggestionCountMismatch", StringComparison.Ordinal));
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Accepted && comment.Path == "src/Auth/JwtMiddleware.cs");
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Skipped && comment.Path == "src/Auth/RefreshTokenStore.cs");
    }

    [Fact]
    public async Task ReviewFindingResolution_CapturesThreadIdsAndResolvesGitHubReviewThread()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.ReviewDrafts.Count == 1 && x.ReviewDrafts[0].Status == ReviewDraftStatus.Published);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        string findingId;
        string threadId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var comments = await db.ReviewDraftComments
                .Where(c => c.DraftId == draft.DraftId && c.Status == ReviewDraftCommentStatus.Accepted)
                .ToListAsync();
            Assert.NotEmpty(comments);
            Assert.All(comments, c => Assert.False(string.IsNullOrWhiteSpace(c.RemoteThreadId)));
            findingId = comments[0].DraftCommentId;
            threadId = comments[0].RemoteThreadId!;
        }

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{findingId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null));
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{findingId}/reopen",
            new { });
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && !x.Resolved);
    }

    [Fact]
    public async Task AutoReviewPublish_PublishesValidDraftAsCommentReviewWithInlineSuggestions()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Kind == SourceWriteKind.PullRequestReview &&
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Succeeded));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        Assert.Equal(ItemState.AwaitingReview, reviewed.Item.State);
        Assert.Equal(0, fakeGitHub.FullDiffCallCount);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);
        Assert.Equal(1, draft.MajorCount);
        Assert.Equal(1, draft.MinorCount);
        var suggestion = Assert.Single(draft.Comments, comment => comment.SuggestionReplacement is not null);
        Assert.Equal("        return refreshed;", suggestion.SuggestionOriginal);
        Assert.Equal("        return token;", suggestion.SuggestionReplacement);
        Assert.Equal(87, suggestion.Line);
        Assert.Null(suggestion.StartLine);
        Assert.Equal("RIGHT", suggestion.Side);
        Assert.Null(suggestion.StartSide);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("Found 2 issues.", review.Body);
        Assert.Equal("abc123", review.CommitId);
        Assert.Equal(2, review.Comments.Count);
        Assert.Contains(review.Comments, comment => comment.Path == "src/Auth/RefreshTokenStore.cs" && comment.Line == 87 && comment.Side == "RIGHT");
        Assert.DoesNotContain(review.Comments, comment => comment.Side == "LEFT");
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) && comment.Body.Contains("```suggestion", StringComparison.Ordinal) && comment.Body.Contains("return token;", StringComparison.Ordinal));
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🟡 Validate middleware setup**", StringComparison.Ordinal) && comment.Path == "src/Auth/JwtMiddleware.cs" && !comment.Body.Contains("```suggestion", StringComparison.Ordinal));
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestReview && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);

        var publishAgain = await client.PostAsJsonAsync($"/api/v1/review-drafts/{draft.DraftId}/publish", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, publishAgain.StatusCode);
    }

    [Fact]
    public async Task AutoReviewPublish_BlocksDraftsWithWarningsAsFailedSourceWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitNoOpReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Failed &&
                write.ErrorCode == "reviewDraftWarnings"));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, draft.Status);
        Assert.Equal(2, draft.WarningCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Equal(0, draft.SuggestionCount);
        Assert.Contains(draft.Comments, comment => comment.Status == ReviewDraftCommentStatus.Skipped && comment.Warning?.Contains("reviewDraftNoOpSuggestion", StringComparison.Ordinal) == true);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("reviewDraftWarnings", write.ErrorCode);
    }

    [Fact]
    public async Task AutoReviewPublish_BlocksStaleHeadAsFailedSourceWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var fakeWorktree = new FakeWorktreeManager { BaseShaOverride = "older-sha" };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.SourceWrites.Any(write => write.ErrorCode == "stalePullRequestHead"));

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("stalePullRequestHead", write.ErrorCode);

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{reviewed.ReviewDrafts[0].DraftId}/publish", new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(retried.ReviewDrafts).Status);
        Assert.Single(fakeGitHub.PullRequestReviews);
    }

    [Fact]
    public async Task ReviewDraftPublishRetry_PreservesInlineSuggestionsAndMarksDraftPublished()
    {
        var fakeGitHub = new FakeGitHubApiClient { FailNextPullRequestReviewCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare structured PR review suggestions.", null, null));
        var failedPublish = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.ReviewDrafts.Count == 1 && x.ReviewDrafts[0].Status == ReviewDraftStatus.PublishFailed);

        var failedDraft = Assert.Single(failedPublish.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, failedDraft.Status);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        var failedWrite = Assert.Single(failedPublish.SourceWrites, write => write.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, failedWrite.Status);
        using (var document = JsonDocument.Parse(failedWrite.RequestJson))
        {
            var comments = document.RootElement.GetProperty("comments");
            Assert.Contains(
                comments.EnumerateArray(),
                comment => (comment.GetProperty("body").GetString() ?? "").Contains("```suggestion", StringComparison.Ordinal) &&
                    (comment.GetProperty("body").GetString() ?? "").Contains("return token;", StringComparison.Ordinal));
        }

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Equal(2, retriedWrite.AttemptCount);
        var publishedDraft = Assert.Single(retried.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, publishedDraft.Status);
        Assert.NotNull(publishedDraft.PublishedAt);
        Assert.Equal(failedWrite.WriteId, publishedDraft.SourceWriteId);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal(2, review.Comments.Count);
        Assert.Contains(review.Comments, comment => comment.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) && comment.Body.Contains("```suggestion", StringComparison.Ordinal) && comment.Body.Contains("return token;", StringComparison.Ordinal));
    }
}
