using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class GitHubSourceApiTests
{
    [Fact]
    public async Task GitHubSync_RequiresAppAuthentication()
    {
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["Oratorio:GitHub:AppId"] = "",
            ["Oratorio:GitHub:PrivateKey"] = "",
            ["Oratorio:GitHub:PrivateKeyPath"] = ""
        });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-owner/oratorio"], sync.RepositoriesScanned);
        Assert.Equal(0, sync.IssuesImported);
        Assert.Equal(0, sync.PullRequestsImported);
        var error = Assert.Single(sync.Errors);
        Assert.Equal("example-owner/oratorio", error.Repository);
        Assert.Equal("githubAppAuthRequired", error.Code);
        Assert.Contains("GitHub App ID and private key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubSync_ImportsIssuesPullRequestsComments_AndSupportsItemIdRoutes()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-owner/oratorio"], sync.RepositoriesScanned);
        Assert.Equal(1, sync.IssuesImported);
        Assert.Equal(1, sync.PullRequestsImported);
        Assert.Equal(0, sync.CommentsImported);
        Assert.Equal(1, sync.Skipped);
        Assert.Equal(0, fakeGitHub.IssueCommentCallCount);
        Assert.Equal([GitHubListState.Open], fakeGitHub.IssueStateArguments["example-owner/oratorio"]);
        Assert.Equal([GitHubListState.Open], fakeGitHub.PullRequestStateArguments["example-owner/oratorio"]);

        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        Assert.NotNull(list);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        Assert.False(string.IsNullOrWhiteSpace(issue.ShortId));
        Assert.InRange(issue.BoardSortOrder, 0, 999);
        var pr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);
        Assert.Equal("pr:example-owner/oratorio#184", pr.ExternalId);
        Assert.Equal("feature/auth-refresh", pr.Branch);
        Assert.Equal("abc123", pr.HeadSha);
        Assert.Equal(["security", "backend"], pr.Labels);
        Assert.NotNull(pr.ItemId);

        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.SourceSnapshot);
        Assert.Equal(SourceDetailsStatus.Stale, detail.Item.SourceDetailsStatus);
        Assert.DoesNotContain(detail.Comments, x => x.Source == "github");

        detail = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { });
        Assert.Equal(SourceDetailsStatus.Current, detail.Item.SourceDetailsStatus);
        Assert.NotNull(detail.SourceSnapshot);
        var firstSnapshotId = detail.SourceSnapshot!.SnapshotId;
        var firstSnapshotSyncedAt = detail.SourceSnapshot.SyncedAt;
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "issue-comment:9001");
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "review:9101");
        Assert.Contains(detail.Comments, x => x.SourceCommentId == "review-comment:9201");
        Assert.Equal(1, fakeGitHub.IssueCommentCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCommentCallCount);
        Assert.Single(detail.Timeline, x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced);

        var bySource = await client.GetFromJsonAsync<ItemDetailResponse>(
            "/api/v1/items/by-source?source=github&externalId=pr%3Aexample-owner%2Foratorio%23184",
            JsonOptions);
        Assert.Equal(pr.ItemId, bySource?.Item.ItemId);

        clock.Advance(TimeSpan.FromMinutes(5));
        var repeatedSync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        Assert.Equal(0, repeatedSync.CommentsImported);
        var afterSecondSync = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(afterSecondSync);
        Assert.Equal(3, afterSecondSync!.Comments.Count(x => x.Source == "github"));
        Assert.Single(afterSecondSync.Timeline, x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced);
        Assert.Equal(firstSnapshotId, afterSecondSync.SourceSnapshot?.SnapshotId);
        Assert.Equal(detail.Item.LastSourceSyncAt, afterSecondSync.Item.LastSourceSyncAt);
        Assert.Equal(firstSnapshotSyncedAt, afterSecondSync.SourceSnapshot?.SyncedAt);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.Equal(2, await db.SourceSnapshots.CountAsync(x => x.ItemId == pr.ItemId));
        }

        fakeGitHub.SourceReviewComments[0] = fakeGitHub.SourceReviewComments[0] with
        {
            Body = "Extract the token validation branch and cover retry order.",
            UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z")
        };
        fakeGitHub.Issues[1] = fakeGitHub.Issues[1] with { UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z") };
        fakeGitHub.PullRequests[0] = fakeGitHub.PullRequests[0] with { UpdatedAt = DateTimeOffset.Parse("2026-05-03T00:10:00Z") };
        clock.Advance(TimeSpan.FromMinutes(5));
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var afterChangedSync = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(afterChangedSync);
        Assert.Equal(SourceDetailsStatus.Stale, afterChangedSync!.Item.SourceDetailsStatus);
        afterChangedSync = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { });
        Assert.Equal(3, afterChangedSync!.Comments.Count(x => x.Source == "github"));
        Assert.Contains(afterChangedSync.Comments, x =>
            x.SourceCommentId == "review-comment:9201" &&
            x.Body.Contains("cover retry order", StringComparison.Ordinal) &&
            x.SourceUpdatedAt == DateTimeOffset.Parse("2026-05-03T00:10:00Z"));
        Assert.Equal(2, afterChangedSync.Timeline.Count(x => x.RoundId is null && x.Kind == TimelineEventKind.SourceSynced));
        Assert.Contains(afterChangedSync.Timeline, x => x.Title == "GitHub source updated");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.True(await db.SourceSnapshots.CountAsync(x => x.ItemId == pr.ItemId) >= 2);
        }

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run mock lifecycle on a GitHub-backed item.", MockOutcome.Success, 1));
        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
    }

    [Fact]
    public async Task GitHubSyncJob_ImportsRepositoriesIndependently_WhenFirstRepositoryIsSlow()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.ListIssueDelays["example-owner/oratorio"] = TimeSpan.FromSeconds(1);
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });

        await WaitUntilAsync(async () =>
        {
            var active = await client.GetFromJsonAsync<GitHubSyncJobDto>($"/api/v1/sources/github/sync-jobs/{job.JobId}", JsonOptions);
            return active!.Repositories.Any(x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);
        });

        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);
        Assert.Equal(GitHubSyncStatus.Succeeded, completed.Status);
        Assert.Equal(["example-owner/oratorio", "example-owner/second-repo"], completed.Repositories.Select(x => x.Repository).ToArray());
    }

    [Fact]
    public async Task GitHubSyncJob_RecordsRepositoryTimeouts_AndClearsActiveJob()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.CancelingIssueRepositories.Add("example-owner/oratorio");
        fakeGitHub.IssuesByRepository["example-owner/second-repo"] = [TestGitHubIssue(3001, 7, "Second repository issue", "example-owner/second-repo")];
        fakeGitHub.PullRequestsByRepository["example-owner/second-repo"] = [];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:1"] = "example-owner/second-repo"
            });
        var client = app.CreateClient();

        var job = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });
        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);

        Assert.Equal(GitHubSyncStatus.PartialFailed, completed.Status);
        var failed = Assert.Single(completed.Repositories, x => x.Repository == "example-owner/oratorio");
        Assert.Equal(GitHubSyncRepositoryStatus.Failed, failed.Status);
        Assert.Equal(GitHubSyncRepositoryPhase.Failed, failed.Phase);
        Assert.Equal("githubSyncFailed", failed.ErrorCode);
        Assert.Contains("timeout", failed.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains(completed.Repositories, x => x.Repository == "example-owner/second-repo" && x.Status == GitHubSyncRepositoryStatus.Succeeded);

        var activeResponse = await client.GetAsync("/api/v1/sources/github/sync-jobs/active");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeBody = await activeResponse.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrWhiteSpace(activeBody) || activeBody == "null", activeBody);
    }

    [Fact]
    public async Task GitHubSyncJob_ImportsRepositoryWithOnlyPullRequestShapedIssue()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.IssuesByRepository["example-org/unity-example"] =
        [
            TestGitHubIssue(4001, 1, "Declare Unity runtime tools", "example-org/unity-example", isPullRequest: true)
        ];
        fakeGitHub.PullRequestsByRepository["example-org/unity-example"] =
        [
            TestGitHubPullRequest(5001, 1, "Declare Unity runtime tools", "example-org/unity-example")
        ];
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:GitHub:Repositories:0"] = "example-org/unity-example"
            });
        var client = app.CreateClient();

        var sync = await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        Assert.Equal(["example-org/unity-example"], sync.RepositoriesScanned);
        Assert.Equal(0, sync.IssuesImported);
        Assert.Equal(1, sync.PullRequestsImported);
        Assert.Equal(1, sync.Skipped);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items);
        Assert.Equal(ItemKind.PullRequest, pr.Kind);
        Assert.Equal("pr:example-org/unity-example#1", pr.ExternalId);
    }

    [Fact]
    public async Task GitHubSync_AutoArchivesClosedAndMergedSourceItems()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        fakeGitHub.Issues.Add(new GitHubIssue(
            1003,
            77,
            "Closed source issue",
            "Already closed upstream.",
            "closed",
            "https://github.example.test/example-owner/oratorio/issues/77",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T09:55:00Z"),
            null));
        fakeGitHub.PullRequests.Add(new GitHubPullRequest(
            2002,
            185,
            "Merged source PR",
            "Already merged upstream.",
            "closed",
            "https://github.example.test/example-owner/oratorio/pull/185",
            new GitHubUser("example-author"),
            [],
            [],
            DateTimeOffset.Parse("2026-05-01T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T11:30:00Z"),
            DateTimeOffset.Parse("2026-05-03T11:45:00Z"),
            false,
            new GitHubBranchRef("feature/merged", "def456"),
            new GitHubBranchRef("main", "base123")));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var lightJob = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { });
        await WaitForGitHubSyncJobAsync(client, lightJob.JobId);
        var lightList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        Assert.DoesNotContain(lightList!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        Assert.DoesNotContain(lightList.Items, x => x.ExternalId == "pr:example-owner/oratorio#185");
        Assert.Equal(0, fakeGitHub.IssueCommentCallCount);

        var fullJob = await PostAsync<GitHubSyncJobDto>(client, "/api/v1/sources/github/sync-jobs", new { mode = "full" });
        await WaitForGitHubSyncJobAsync(client, fullJob.JobId);

        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        var closedIssue = Assert.Single(list!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        var mergedPr = Assert.Single(list.Items, x => x.ExternalId == "pr:example-owner/oratorio#185");
        Assert.Equal(ItemState.Archived, closedIssue.State);
        Assert.Equal(SourceState.Closed, closedIssue.SourceState);
        Assert.Equal(ArchiveReason.SourceClosed, closedIssue.ArchiveReason);
        Assert.NotNull(closedIssue.SourceClosedAt);
        Assert.Equal(ItemState.Archived, mergedPr.State);
        Assert.Equal(SourceState.Merged, mergedPr.SourceState);
        Assert.Equal(ArchiveReason.SourceMerged, mergedPr.ArchiveReason);
        Assert.NotNull(mergedPr.SourceClosedAt);
        Assert.NotNull(mergedPr.SourceMergedAt);

        var closedIssueIndex = fakeGitHub.Issues.FindIndex(x => x.Number == 77);
        fakeGitHub.Issues[closedIssueIndex] = fakeGitHub.Issues[closedIssueIndex] with
        {
            State = "open",
            UpdatedAt = DateTimeOffset.UtcNow,
            ClosedAt = null
        };
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var reopenedList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var reopenedIssue = Assert.Single(reopenedList!.Items, x => x.ExternalId == "issue:example-owner/oratorio#77");
        Assert.Equal(ItemState.Discovered, reopenedIssue.State);
        Assert.Equal(SourceState.Open, reopenedIssue.SourceState);
        Assert.Null(reopenedIssue.ArchiveReason);
    }

    [Fact]
    public async Task GitHubDispatch_HydratesStaleSourceDetailsBeforeQueueing()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        Assert.Equal(SourceDetailsStatus.Stale, pr.SourceDetailsStatus);

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Dispatch after source detail hydrate.", MockOutcome.Success, 1));

        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
        Assert.Equal(SourceDetailsStatus.Current, dispatched.Item.SourceDetailsStatus);
        Assert.Contains(dispatched.Comments, x => x.SourceCommentId == "review-comment:9201");
        Assert.Equal(1, fakeGitHub.IssueCommentCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCallCount);
        Assert.Equal(1, fakeGitHub.PullRequestReviewCommentCallCount);
    }

    [Fact]
    public async Task GitHubSourceDetailsSync_FailureIsRecorded_AndBlocksDispatch()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        fakeGitHub.FailSourceDetailReads = true;

        var hydrateResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/source-details/sync", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, hydrateResponse.StatusCode);
        var hydrateError = await hydrateResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("sourceDetailsSyncFailed", hydrateError?.Error.Code);
        var failedDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.Equal(SourceDetailsStatus.Failed, failedDetail!.Item.SourceDetailsStatus);
        Assert.Equal("sourceDetailsSyncFailed", failedDetail.Item.SourceDetailsErrorCode);

        var dispatchResponse = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Dispatch should block.", MockOutcome.Success, 1),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, dispatchResponse.StatusCode);
        var dispatchError = await dispatchResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("sourceDetailsSyncFailed", dispatchError?.Error.Code);
    }

    [Fact]
    public async Task SourceBackedItem_CanBeManuallyArchivedAndReopened()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        var archived = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/archive", new { });
        Assert.Equal(ItemState.Archived, archived.Item.State);
        Assert.Equal(ArchiveReason.Manual, archived.Item.ArchiveReason);

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var afterSync = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&includeArchived=true", JsonOptions);
        var stillArchived = Assert.Single(afterSync!.Items, x => x.ExternalId == pr.ExternalId);
        Assert.Equal(ItemState.Archived, stillArchived.State);
        Assert.Equal(ArchiveReason.Manual, stillArchived.ArchiveReason);

        var reopened = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/reopen",
            new DecisionRequest("Return it to queue."));
        Assert.Equal(ItemState.Discovered, reopened.Item.State);
        Assert.Null(reopened.Item.ArchiveReason);
    }

    [Fact]
    public async Task DotCraftStatus_UsesHealthInsteadOfConfiguredAsReady()
    {
        var workspace = Directory.CreateTempSubdirectory("oratorio-status-workspace-").FullName;
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.AddSingleton<IDotCraftAppServerProcessManager>(
                    new FakeDotCraftProcessManager(connected: false, endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9100/ws", "manual")));
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = workspace
            });
        var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<DotCraftStatusResponse>("/api/v1/dotcraft/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status!.Configured);
        Assert.False(status.Connected);
        Assert.Equal("configured", status.Health);
        Assert.True(status.ManagedWorktreesEnabled);
        Assert.Equal("<repositoryWorkspace>/.craft/oratorio/worktrees", status.WorktreeRootPolicy);
        Assert.Equal(2, status.GlobalMaxActiveRuns);
        Assert.Equal(1, status.MaxActiveRunsPerRepository);
        Assert.Equal(2, status.MaxActiveRunsPerSource);
        Assert.Contains("not reachable", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DotCraftWorkspaces_ReturnsInventoryAndStatusUsesMultiWorkspaceMode()
    {
        var root = Directory.CreateTempSubdirectory("oratorio-api-workspaces-");
        var mappedWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "mapped")).FullName;
        var secondWorkspace = Directory.CreateDirectory(Path.Combine(root.FullName, "second")).FullName;
        var fakeProcess = new FakeDotCraftProcessManager(
            connected: false,
            endpoint: new DotCraftAppServerEndpoint("ws://127.0.0.1:9200/ws", "hub"));
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:Project"] = "github:github.com/example-owner/oratorio",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:0:WorkspacePath"] = mappedWorkspace,
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:Project"] = "github:github.com/example-owner/desktop-repo",
                ["Oratorio:DotCraft:RepositoryWorkspaceRoutes:1:WorkspacePath"] = secondWorkspace
            });
        var client = app.CreateClient();

        try
        {
            using var status = await client.GetFromJsonAsync<JsonDocument>("/api/v1/status", JsonOptions);
            Assert.Equal("multi", status!.RootElement.GetProperty("workspaceMode").GetString());
            Assert.True(status.RootElement.GetProperty("capabilities").GetProperty("multiWorkspaceRouting").GetBoolean());

            var workspaces = await client.GetFromJsonAsync<DotCraftWorkspacesResponse>("/api/v1/dotcraft/workspaces", JsonOptions);
            Assert.NotNull(workspaces);
            Assert.Equal(2, workspaces!.Summary.Total);
            Assert.Equal(0, workspaces.Summary.Connected);
            Assert.All(workspaces.Workspaces, workspace => Assert.False(workspace.IsDefault));
            var mapped = Assert.Single(workspaces.Workspaces, workspace => workspace.Path == mappedWorkspace);
            Assert.Equal("hub", mapped.EndpointSource);
            Assert.Equal(["github:github.com/example-owner/oratorio"], mapped.Repositories);

            await PostAsync<DotCraftStatusResponse>(client, "/api/v1/dotcraft/appserver/start", new { });
            Assert.Equal(2, fakeProcess.EnsureWorkspacePaths.Count);
            Assert.Contains(mappedWorkspace, fakeProcess.EnsureWorkspacePaths);
            Assert.Contains(secondWorkspace, fakeProcess.EnsureWorkspacePaths);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GitHubPullRequestCancelRun_CompletesReviewGateCheckAsCancelled()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("mock", "Run GitHub PR review until cancelled.", MockOutcome.Success, 30));

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/cancel-run",
            new CancelRunRequest("No longer needed."));

        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("oratorio/review", check.Name);
        Assert.Equal("abc123", check.HeadSha);
        Assert.Equal("completed", check.Status);
        Assert.Equal("cancelled", check.Conclusion);
        Assert.Equal(1, check.UpdateCount);
        Assert.Contains(cancelled.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateStart" &&
            write.Status == SourceWriteStatus.Succeeded);
        Assert.Contains(cancelled.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateRunCancelled" &&
            write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task GitHubPullRequestApprove_WritesReviewAndCheckRun()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Ship it from Oratorio."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("APPROVE", review.Event);
        Assert.Equal("Ship it from Oratorio.", review.Body);
        Assert.Equal("example-owner/oratorio", review.Repository.FullName);
        Assert.Equal(184, review.Number);

        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("oratorio/review", check.Name);
        Assert.Equal("abc123", check.HeadSha);
        Assert.Equal("completed", check.Status);
        Assert.Equal("success", check.Conclusion);
        Assert.Equal(2, check.UpdateCount);
        Assert.Equal(4, approved.SourceWrites.Count);
        Assert.All(approved.SourceWrites, write => Assert.Equal(SourceWriteStatus.Succeeded, write.Status));
    }

    [Fact]
    public async Task GitHubPullRequestRequestChanges_WritesReviewAndActionRequiredCheck()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var changed = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/request-changes",
            new DecisionRequest("Please add regression coverage."));

        Assert.Equal(ItemState.Discovered, changed.Item.State);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("REQUEST_CHANGES", review.Event);
        Assert.Contains("Please add regression coverage.", review.Body);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("action_required", check.Conclusion);
        Assert.Contains("Please add regression coverage.", check.Summary);
    }

    [Fact]
    public async Task GitHubIssueDecision_WritesIssueCommentOnly()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var issue = await CreateGitHubIssueInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.Item.ItemId}/approve",
            new DecisionRequest("Issue review accepted."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var comment = Assert.Single(fakeGitHub.IssueComments);
        Assert.Equal("example-owner/oratorio", comment.Repository.FullName);
        Assert.Equal(42, comment.Number);
        Assert.Contains("Issue review accepted.", comment.Body);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
        var write = Assert.Single(approved.SourceWrites);
        Assert.Equal(SourceWriteKind.IssueComment, write.Kind);
        Assert.Equal(SourceWriteStatus.Succeeded, write.Status);
    }

    [Fact]
    public async Task GitHubWriteFailure_IsAuditedAndRetryable()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        fakeGitHub.FailNextWriteCount = 1;
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Approve even if the first write fails."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        var failed = Assert.Single(approved.SourceWrites, write => write.Status == SourceWriteStatus.Failed);
        Assert.Equal("githubWriteFailed", failed.ErrorCode);
        Assert.Contains(approved.Timeline, x => x.Kind == TimelineEventKind.SourceWriteFailed);

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failed.WriteId}/retry", new { });
        var writeAfterRetry = Assert.Single(retried.SourceWrites, write => write.WriteId == failed.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, writeAfterRetry.Status);
        Assert.Equal(2, writeAfterRetry.AttemptCount);
    }

    [Fact]
    public async Task GitHubWritesDisabled_RecordsBlockedWriteWithoutCallingGitHub()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IOptionsMonitor<GitHubOptions>>();
            services.AddSingleton<IOptionsMonitor<GitHubOptions>>(new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = "test-private-key",
                Repositories = ["example-owner/oratorio"],
                WritesEnabled = false
            }));
        });
        var client = app.CreateClient();

        var pr = await CreateGitHubPullRequestInReviewAsync(client);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.Item.ItemId}/approve",
            new DecisionRequest("Decision should remain local."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
        Assert.All(approved.SourceWrites, write =>
        {
            Assert.Equal(SourceWriteStatus.Failed, write.Status);
            Assert.Equal("githubWritesDisabled", write.ErrorCode);
        });
    }

    [Fact]
    public async Task LocalTaskDecision_DoesNotCreateGitHubWrites()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("mock", "Run local task.", MockOutcome.Success, 1));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{reviewed.Item.ItemId}/approve",
            new DecisionRequest("Local task done."));

        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Contains(approved.Decisions, x => x.Decision == DecisionType.Approve);
        Assert.Empty(approved.SourceWrites);
        Assert.Empty(fakeGitHub.IssueComments);
        Assert.Empty(fakeGitHub.PullRequestReviews);
        Assert.Empty(fakeGitHub.CheckRuns);
    }

    [Fact]
    public async Task GitHubWebhook_RejectsInvalidSignature_AndSyncsValidPayload()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        const string payload = """{"repository":{"full_name":"example-owner/oratorio"}}""";

        var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        invalid.Headers.TryAddWithoutValidation("X-Hub-Signature-256", "sha256=bad");
        var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.Forbidden, invalidResponse.StatusCode);

        var valid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/github/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        valid.Headers.TryAddWithoutValidation("X-Hub-Signature-256", TestHelpers.Signature(payload, "test-secret"));
        var validResponse = await client.SendAsync(valid);
        validResponse.EnsureSuccessStatusCode();
        var job = await validResponse.Content.ReadFromJsonAsync<GitHubSyncJobDto>(JsonOptions);
        Assert.NotNull(job);
        Assert.Equal(GitHubSyncTrigger.Webhook, job.Trigger);
        Assert.Equal(["example-owner/oratorio"], job.Repositories.Select(x => x.Repository).ToArray());
        var completed = await WaitForGitHubSyncJobAsync(client, job.JobId);
        Assert.Equal(1, completed.PullRequestsImported);
    }

    [Fact]
    public async Task GitHubTokenProvider_ExchangesJwt_AndCachesInstallationToken()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = TestHelpers.ExportPrivateKeyPem(rsa);
        var handler = new CountingHandler();
        var provider = new GitHubTokenProvider(
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
            new StaticOptionsMonitor<GitHubOptions>(new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                InstallationProfiles =
                [
                    new GitHubInstallationProfileOptions
                    {
                        Instance = "api.github.test",
                        Owner = "dotcraft",
                        InstallationId = "98765",
                        Source = "manual"
                    }
                ],
                PrivateKey = privateKey
            }),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()),
            new GitHubInstallationResolver(
                new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test") }),
                new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
                new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector())));

        var first = await provider.GetBearerTokenAsync(new GitHubRepositoryRef("dotcraft", "oratorio"), CancellationToken.None);
        var second = await provider.GetBearerTokenAsync(new GitHubRepositoryRef("dotcraft", "second"), CancellationToken.None);

        Assert.Equal("installation-token", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Count);
        Assert.Equal("/app/installations/98765/access_tokens", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
    }

    [Fact]
    public async Task GitHubInstallationResolver_DiscoversRepositoryInstallation()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = TestHelpers.ExportPrivateKeyPem(rsa);
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = 24680 })
        });
        var resolver = new GitHubInstallationResolver(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()));

        var result = await resolver.DiscoverAsync(
            new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = privateKey
            },
            new GitHubRepositoryRef("example-owner", "oratorio"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("24680", result.InstallationId);
        Assert.Equal("api.github.test", result.Instance);
        Assert.Equal("/repos/example-owner/oratorio/installation", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
    }

    [Fact]
    public async Task GitHubInstallationResolver_ReturnsFailureForInvalidPrivateKey()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = 24680 })
        });
        var resolver = new GitHubInstallationResolver(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new FixedClock(DateTimeOffset.Parse("2026-05-04T00:00:00Z")),
            new GitHubCredentialResolver(new PassthroughConfigurationSecretProtector()));

        var result = await resolver.DiscoverAsync(
            new GitHubOptions
            {
                Endpoint = "https://api.github.test",
                AppId = "12345",
                PrivateKey = "not a pem private key"
            },
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("githubInstallationDiscoveryFailed", result.Code);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task GitHubApiClient_CreatePullRequestReview_OmitsNullOptionalCommentFields()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new { id = 200, html_url = "https://github.example/review/200" })
        });
        var client = CreateGitHubApiClient(handler);

        await client.CreatePullRequestReviewAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            "COMMENT",
            "Summary",
            "abc123",
            [new GitHubPullRequestReviewCommentWrite("README.md", "Body", 4, "RIGHT", null, null)],
            CancellationToken.None);

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var comment = document.RootElement.GetProperty("comments")[0];
        Assert.Equal("README.md", comment.GetProperty("path").GetString());
        Assert.Equal(4, comment.GetProperty("line").GetInt32());
        Assert.False(comment.TryGetProperty("start_line", out _));
        Assert.False(comment.TryGetProperty("start_side", out _));
    }

    [Fact]
    public async Task GitHubApiClient_CreatePullRequestReviewThreadReply_UsesGraphQlMutation()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"addPullRequestReviewThreadReply":{"comment":{"id":"PRRC_reply"}}}}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = CreateGitHubApiClient(handler);

        var response = await client.CreatePullRequestReviewThreadReplyAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            "PRRT_thread",
            "Resolved because the follow-up commit addressed it.",
            CancellationToken.None);

        Assert.Equal("PRRC_reply", response.ExternalId);
        Assert.Equal("/graphql", handler.LastRequestUri?.AbsolutePath);
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Contains("addPullRequestReviewThreadReply", document.RootElement.GetProperty("query").GetString(), StringComparison.Ordinal);
        var variables = document.RootElement.GetProperty("variables");
        Assert.Equal("PRRT_thread", variables.GetProperty("threadId").GetString());
        Assert.Equal("Resolved because the follow-up commit addressed it.", variables.GetProperty("body").GetString());
    }

    [Fact]
    public async Task GitHubApiClient_RepositoryCommentScan_ReadsNewestFirstAcrossAllPages()
    {
        const int total = 1050;
        var queries = new List<string>();
        var handler = new CapturingHandler(request =>
        {
            var query = request.RequestUri!.Query;
            queries.Add(query);
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(query)["page"]!);
            var comments = Enumerable.Range((page - 1) * 100, Math.Max(0, Math.Min(100, total - (page - 1) * 100)))
                .Select(index => new
                {
                    id = index + 1,
                    body = "comment",
                    html_url = $"https://github.example.test/dotcraft/oratorio/pull/1#issuecomment-{index + 1}",
                    issue_url = "https://api.github.test/repos/dotcraft/oratorio/issues/1",
                    user = new { login = "reviewer", type = "User" },
                    created_at = "2026-05-02T13:00:00Z",
                    updated_at = "2026-05-02T13:00:00Z"
                });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(comments), Encoding.UTF8, "application/json")
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"page-{page}\"");
            return response;
        });
        var client = CreateGitHubApiClient(handler);

        var result = await client.ListRepositoryIssueCommentsAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            DateTimeOffset.Parse("2026-05-02T12:58:00Z"),
            null,
            CancellationToken.None);

        Assert.False(result.NotModified);
        Assert.Equal(total, result.Items.Count);
        Assert.Equal("\"page-1\"", result.ETag);
        Assert.All(queries, query => Assert.Contains("direction=desc", query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitHubApiClient_WriteFailure_IncludesGitHubResponseBody()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"message":"Validation Failed","errors":[{"field":"comments"}]}""", Encoding.UTF8, "application/json")
        });
        var client = CreateGitHubApiClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreatePullRequestReviewAsync(
            new GitHubRepositoryRef("dotcraft", "oratorio"),
            184,
            "COMMENT",
            "Summary",
            "abc123",
            [new GitHubPullRequestReviewCommentWrite("README.md", "Body", 4, "RIGHT", null, null)],
            CancellationToken.None));

        Assert.Contains("422", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Validation Failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("comments", exception.Message, StringComparison.Ordinal);
    }
}
