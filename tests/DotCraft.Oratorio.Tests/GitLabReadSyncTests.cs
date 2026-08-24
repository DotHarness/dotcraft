using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitLab;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Tests;

public sealed class GitLabReadSyncTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-19T09:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task GitLabProviderStatus_ReportsReadCapableAndNoWrites()
    {
        await using var app = new TestOratorioApp(settings: GitLabSettings());
        var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<SourceProviderStatusDto>("/api/v1/sources/gitlab/status", JsonOptions);

        Assert.NotNull(status);
        Assert.Equal("gitlab", status!.Provider);
        Assert.True(status.Configured);
        Assert.Equal("token", status.AuthenticationState);
        Assert.True(status.ReadCapability.Available);
        Assert.False(status.WriteCapability.Available);
        Assert.Equal("disabled", status.WriteCapability.State);
        Assert.True(status.WebhookCapability.Available);
        var project = Assert.Single(status.Projects);
        Assert.Equal("group/subgroup/project", project.ProjectPath);
        Assert.Equal("gitlab:gitlab.example.test/group/subgroup/project", project.Key);
        Assert.True(project.ReadCapability?.Available);
        Assert.Equal("available", project.ReadCapability?.State);
        Assert.True(project.WebhookCapability?.Available);
    }

    [Fact]
    public async Task GitLabProviderStatus_ReportsPartialWhenOnlySomeProjectsHaveProfiles()
    {
        var settings = GitLabSettings();
        settings["Oratorio:GitLab:Projects:1"] = "group/missing/project";
        await using var app = new TestOratorioApp(settings: settings);
        var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<SourceProviderStatusDto>("/api/v1/sources/gitlab/status", JsonOptions);

        Assert.NotNull(status);
        Assert.Equal("partial", status!.AuthenticationState);
        Assert.True(status.ReadCapability.Available);
        Assert.Equal("partial", status.ReadCapability.State);
        Assert.True(status.WebhookCapability.Available);
        Assert.Equal("partial", status.WebhookCapability.State);
        Assert.Equal(2, status.Projects.Count);
        var configured = Assert.Single(status.Projects, project => project.ProjectPath == "group/subgroup/project");
        var missing = Assert.Single(status.Projects, project => project.ProjectPath == "group/missing/project");
        Assert.True(configured.ReadCapability?.Available);
        Assert.Equal("credentialsMissing", missing.ReadCapability?.State);
        Assert.False(missing.ReadCapability?.Available);
        Assert.Equal("unconfigured", missing.WebhookCapability?.State);
    }

    [Fact]
    public async Task GitLabSyncJob_ImportsIssuesAndMergeRequests_WithStableCanonicalKeys()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        await using var app = GitLabApp(fakeGitLab);
        var client = app.CreateClient();

        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        var completed = await WaitForSourceSyncJobAsync(client, job.JobId);

        Assert.Equal(SourceSyncStatus.Succeeded, completed.Status);
        Assert.Equal(1, completed.IssuesImported);
        Assert.Equal(1, completed.ReviewTargetsImported);
        var run = Assert.Single(completed.Projects);
        Assert.Equal("gitlab:gitlab.example.test/group/subgroup/project", run.SourceProjectKey);
        Assert.Equal("group/subgroup/project", run.ProjectPath);

        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab", JsonOptions);
        Assert.NotNull(list);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        var mr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);

        Assert.Equal("issue:gitlab.example.test/group/subgroup/project#12", issue.ExternalId);
        Assert.Equal("gitlab:gitlab.example.test/group/subgroup/project", issue.Repository);
        Assert.Equal(["backend", "m2"], issue.Labels);
        Assert.Equal(ItemState.Discovered, issue.State);
        Assert.Equal(BoardTaskStatus.Todo, issue.TaskStatus);
        Assert.False(string.IsNullOrWhiteSpace(issue.ShortId));
        Assert.InRange(issue.BoardSortOrder, 0, 999);
        Assert.Equal("mr:gitlab.example.test/group/subgroup/project!7", mr.ExternalId);
        Assert.Equal("feature/gitlab-read", mr.Branch);
        Assert.Equal("head-sha-1", mr.HeadSha);
        Assert.False(mr.IsDraft);
        Assert.Equal(SourceDetailsStatus.Stale, mr.SourceDetailsStatus);
    }

    [Fact]
    public async Task GitLabMergeRequestReview_RetriesMissingDraft_AndReusesFailedThread()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var workspace = Path.Combine(Path.GetTempPath(), "oratorio-test-workspace");
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock, SystemClock>();
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                services.RemoveAll<IOptionsMonitor<DotCraftOptions>>();
                services.AddSingleton<IOptionsMonitor<DotCraftOptions>>(new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
                {
                    RepositoryWorkspaceRoutes =
                    [
                        new() { Project = "gitlab:gitlab.example.test/group/subgroup/project", WorkspacePath = workspace }
                    ],
                    MaxRunAttempts = 2,
                    RetryBackoffSeconds = 1,
                    MaxRetryBackoffSeconds = 1
                }));
            });
        var client = app.CreateClient();

        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mergeRequest = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mergeRequest.ItemId}/dispatch",
            new DispatchRequest("appServer", "Retry a missing merge request review draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            mergeRequest.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1 && x.Runs.Count == 2);
        var runs = reviewed.Runs.OrderBy(x => x.Attempt).ToArray();

        Assert.Equal("gitlab", reviewed.Item.Source);
        Assert.Equal("reviewDraftRequired", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Failed, runs[0].Status);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Equal("thread-test-1", runs[0].ThreadId);
        Assert.Equal("thread-test-1", runs[1].ThreadId);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(1, fakeAppServer.ThreadResumeAttemptCount);
    }

    [Fact]
    public async Task GitLabSyncJob_FailsOnlyProjectMissingProfileToken()
    {
        var settings = GitLabSettings();
        settings["Oratorio:GitLab:Projects:1"] = "group/missing/project";
        var requests = new List<Uri>();
        var handler = new CapturingHandler(request =>
        {
            requests.Add(request.RequestUri!);
            Assert.True(request.Headers.TryGetValues("PRIVATE-TOKEN", out var tokenValues));
            Assert.Equal("gitlab-token", Assert.Single(tokenValues));
            Assert.DoesNotContain("group%2Fmissing%2Fproject", request.RequestUri!.AbsoluteUri);

            var uri = request.RequestUri!.AbsoluteUri;
            var body = uri.Contains("/issues", StringComparison.Ordinal) ||
                uri.Contains("/merge_requests", StringComparison.Ordinal)
                ? "[]"
                : """{"id":99,"path_with_namespace":"group/subgroup/project","web_url":"https://gitlab.example.test/group/subgroup/project","default_branch":"main"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new StaticHttpClientFactory(new HttpClient(handler)));
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClock(Now));
            },
            settings);
        var client = app.CreateClient();

        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        var completed = await WaitForSourceSyncJobAsync(client, job.JobId);

        Assert.Equal(SourceSyncStatus.PartialFailed, completed.Status);
        Assert.Equal(2, completed.ProjectsTotal);
        Assert.Equal(1, completed.ProjectsFailed);
        var succeeded = Assert.Single(completed.Projects, project => project.ProjectPath == "group/subgroup/project");
        Assert.Equal(SourceSyncProjectStatus.Succeeded, succeeded.Status);
        var failed = Assert.Single(completed.Projects, project => project.ProjectPath == "group/missing/project");
        Assert.Equal(SourceSyncProjectStatus.Failed, failed.Status);
        Assert.Equal("gitlabProjectProfileTokenMissing", failed.ErrorCode);
        Assert.All(requests, request => Assert.Contains("group%2Fsubgroup%2Fproject", request.AbsoluteUri));
    }

    [Fact]
    public async Task GitLabSyncJob_RecordsProjectTimeouts_AndClearsActiveJob()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        fakeGitLab.CancelingIssueProjects.Add("group/subgroup/project");
        await using var app = GitLabApp(fakeGitLab);
        var client = app.CreateClient();

        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        var completed = await WaitForSourceSyncJobAsync(client, job.JobId);

        Assert.Equal(SourceSyncStatus.Failed, completed.Status);
        Assert.Equal(1, completed.ProjectsFailed);
        var failed = Assert.Single(completed.Projects);
        Assert.Equal(SourceSyncProjectStatus.Failed, failed.Status);
        Assert.Equal(SourceSyncProjectPhase.Failed, failed.Phase);
        Assert.Equal("gitLabSyncFailed", failed.ErrorCode);
        Assert.Contains("timeout", failed.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);

        var activeResponse = await client.GetAsync("/api/v1/sources/sync-jobs/active?provider=gitlab");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeBody = await activeResponse.Content.ReadAsStringAsync();
        Assert.True(string.IsNullOrWhiteSpace(activeBody) || activeBody == "null", activeBody);
    }

    [Fact]
    public async Task AutoReview_CanonicalGitHubAndGitLabAllowlistQueuesBothProviders()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(new OratorioAutomationOptions
        {
            AutoReviewRepositories =
            [
                "github:github.com/example-owner/oratorio",
                "gitlab:gitlab.example.test/group/subgroup/project"
            ]
        });
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
                services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
                services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
            });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var gitLabJob = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, gitLabJob.JobId);
        var gitHubList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github&kind=pullRequest", JsonOptions);
        var gitLabList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var gitHubPullRequest = Assert.Single(gitHubList!.Items);
        var gitLabMergeRequest = Assert.Single(gitLabList!.Items);

        await DispatchAutoReviewAsync(app);

        var reviewedGitHub = await WaitForItemByIdAsync(
            client,
            gitHubPullRequest.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Any(run => run.DispatchTrigger == RunDispatchTrigger.AutoReview));
        var reviewedGitLab = await WaitForItemByIdAsync(
            client,
            gitLabMergeRequest.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Any(run => run.DispatchTrigger == RunDispatchTrigger.AutoReview));
        Assert.Contains(reviewedGitHub.Runs, run => run.DispatchTrigger == RunDispatchTrigger.AutoReview && run.TargetHeadSha == "abc123");
        Assert.Contains(reviewedGitLab.Runs, run => run.DispatchTrigger == RunDispatchTrigger.AutoReview && run.TargetHeadSha == "head-sha-1");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var itemStates = await db.AutoReviewItemStates.AsNoTracking().ToListAsync();
        Assert.Contains(itemStates, state => state.ItemId == gitHubPullRequest.ItemId && state.Repository == "github:github.com/example-owner/oratorio");
        Assert.Contains(itemStates, state => state.ItemId == gitLabMergeRequest.ItemId && state.Repository == "gitlab:gitlab.example.test/group/subgroup/project");
    }

    [Fact]
    public async Task GitLabReviewRun_AllowsSummaryOnlyDraft()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Review this GitLab MR and submit a clean draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(0, draft.MajorCount);
        Assert.Equal(0, draft.MinorCount);
        Assert.Equal(0, draft.SuggestionCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Empty(draft.Comments);
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
    }

    [Fact]
    public async Task GitLabReviewRun_SummaryOnlyDraftDoesNotReadDiffs()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailMergeRequestDiffReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit a clean GitLab MR review draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(0, fakeGitLab.MergeRequestDiffReadCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Empty(draft.Comments);
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public async Task GitLabReviewRun_StoresSkippedInlineCommentsWhenDiffReadFails()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailMergeRequestDiffReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit GitLab MR review comments.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(1, fakeGitLab.MergeRequestDiffReadCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Equal(4, draft.WarningCount);
        Assert.All(draft.Comments, comment =>
        {
            Assert.Equal(ReviewDraftCommentStatus.Skipped, comment.Status);
            Assert.Contains("diff validation is unavailable", comment.Warning, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDiffUnavailable", StringComparison.Ordinal));
        Assert.Contains(draft.Warnings, warning => warning.Contains("reviewDraftSuggestionCountMismatch", StringComparison.Ordinal));
        Assert.True(fakeAppServer.LastToolResult?.Success);
    }

    [Fact]
    public async Task GitLabReviewRun_UsesLocalDiffFallbackWhenDiffReadFails()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailMergeRequestDiffReads = true };
        var localDiffs = new FakeReviewLocalDiffProvider(fakeGitLab.Diffs.ToDictionary(diff => diff.NewPath, diff => diff.Diff!));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(),
            services =>
            {
                services.RemoveAll<IReviewLocalDiffProvider>();
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IReviewLocalDiffProvider>(localDiffs);
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Submit GitLab MR review comments.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        Assert.Equal(1, fakeGitLab.MergeRequestDiffReadCount);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(1, draft.SuggestionCount);
        Assert.Equal(0, draft.WarningCount);
        Assert.Empty(draft.Warnings);
        Assert.All(draft.Comments, comment => Assert.Equal(ReviewDraftCommentStatus.Accepted, comment.Status));
        Assert.Contains("src/Auth/RefreshTokenStore.cs", localDiffs.RequestedPaths);
        Assert.Contains("src/Auth/JwtMiddleware.cs", localDiffs.RequestedPaths);
    }

    [Fact]
    public async Task GitLabDetailsHydrate_ImportsNotesAndDiscussionsAsSourceContext()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        await using var app = GitLabApp(fakeGitLab);
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        var detail = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{mr.ItemId}/source-details/sync", new { });

        Assert.Equal(SourceDetailsStatus.Current, detail.Item.SourceDetailsStatus);
        Assert.Contains(detail.Comments!, x => x.Source == "gitlab" && x.Purpose == CommentPurpose.SourceContext && x.SourceCommentId!.StartsWith("mr-note:", StringComparison.Ordinal));
        Assert.Contains(detail.Comments!, x => x.Source == "gitlab" && x.Purpose == CommentPurpose.SourceContext && x.SourceCommentId!.StartsWith("mr-discussion-note:", StringComparison.Ordinal));
        Assert.NotNull(detail.SourceSnapshot);
        Assert.Contains("diff_refs", detail.SourceSnapshot!.PayloadJson ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitLabLifecycle_ArchivesClosedItemsAndReopensOnlySourceArchivedItems()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        await using var app = GitLabApp(fakeGitLab);
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);

        fakeGitLab.Issues[0] = fakeGitLab.Issues[0] with
        {
            State = "closed",
            ClosedAt = Now.AddMinutes(5),
            UpdatedAt = Now.AddMinutes(5)
        };
        fakeGitLab.MergeRequests[0] = fakeGitLab.MergeRequests[0] with
        {
            State = "merged",
            MergedAt = Now.AddMinutes(6),
            UpdatedAt = Now.AddMinutes(6)
        };
        job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Full);
        await WaitForSourceSyncJobAsync(client, job.JobId);

        var closedList = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&includeArchived=true", JsonOptions);
        var issue = Assert.Single(closedList!.Items, x => x.Kind == ItemKind.Issue);
        var mr = Assert.Single(closedList.Items, x => x.Kind == ItemKind.PullRequest);
        Assert.Equal(ItemState.Archived, issue.State);
        Assert.Equal(ArchiveReason.SourceClosed, issue.ArchiveReason);
        Assert.Equal(ItemState.Archived, mr.State);
        Assert.Equal(ArchiveReason.SourceMerged, mr.ArchiveReason);

        fakeGitLab.Issues[0] = fakeGitLab.Issues[0] with
        {
            State = "opened",
            ClosedAt = null,
            UpdatedAt = Now.AddMinutes(10)
        };
        job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Full);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var reopened = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&includeArchived=true", JsonOptions);
        Assert.Equal(ItemState.Discovered, Assert.Single(reopened!.Items, x => x.Kind == ItemKind.Issue).State);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var manual = db.Items.Single(x => x.Source == "gitlab" && x.Kind == ItemKind.Issue);
            manual.State = ItemState.Archived;
            manual.ArchiveReason = ArchiveReason.Manual;
            await db.SaveChangesAsync();
        }

        fakeGitLab.Issues[0] = fakeGitLab.Issues[0] with { UpdatedAt = Now.AddMinutes(15) };
        job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Full);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var manuallyArchived = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&includeArchived=true", JsonOptions);
        var finalIssue = Assert.Single(manuallyArchived!.Items, x => x.Kind == ItemKind.Issue);
        Assert.Equal(ItemState.Archived, finalIssue.State);
        Assert.Equal(ArchiveReason.Manual, finalIssue.ArchiveReason);
    }

    [Fact]
    public async Task GitLabWebhook_VerifiesSecretAndStandardWebhookSigningToken()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        await using var app = GitLabApp(fakeGitLab);
        var client = app.CreateClient();
        var body = """{"project":{"path_with_namespace":"group/subgroup/project"}}""";

        var secretRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/gitlab/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        secretRequest.Headers.TryAddWithoutValidation("X-Gitlab-Token", "gitlab-webhook-secret");
        var secretResponse = await client.SendAsync(secretRequest);
        secretResponse.EnsureSuccessStatusCode();
        var secretJob = await secretResponse.Content.ReadFromJsonAsync<SourceSyncJobDto>(JsonOptions);
        Assert.Equal("gitlab", secretJob!.Provider);

        await WaitForSourceSyncJobAsync(client, secretJob.JobId);
        var signatureBody = """{"project":{"path_with_namespace":"group/subgroup/project"}}""";
        var webhookId = "msg-test-1";
        var timestamp = Now.ToUnixTimeSeconds().ToString();
        var signature = StandardWebhookSignature("whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("signing-key")), webhookId, timestamp, signatureBody);
        var signedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/gitlab/webhook")
        {
            Content = new StringContent(signatureBody, Encoding.UTF8, "application/json")
        };
        signedRequest.Headers.TryAddWithoutValidation("webhook-id", webhookId);
        signedRequest.Headers.TryAddWithoutValidation("webhook-timestamp", timestamp);
        signedRequest.Headers.TryAddWithoutValidation("webhook-signature", signature);
        var signedResponse = await client.SendAsync(signedRequest);

        signedResponse.EnsureSuccessStatusCode();
        var signedJob = await signedResponse.Content.ReadFromJsonAsync<SourceSyncJobDto>(JsonOptions);
        Assert.Equal("gitlab", signedJob!.Provider);
    }

    [Fact]
    public async Task GitLabWebhook_RejectsConfiguredProjectWhenProfileSecretMissing()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var settings = GitLabSettings();
        settings["Oratorio:GitLab:ProjectProfiles:0:ProjectPath"] = "group/other/project";
        await using var app = GitLabApp(fakeGitLab, settings);
        var client = app.CreateClient();
        var body = """{"project":{"path_with_namespace":"group/subgroup/project"}}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sources/gitlab/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Gitlab-Token", "gitlab-webhook-secret");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GitLabDecisionWrites_CreateIssueNotesMergeRequestNotesAndCommitStatuses()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        await using var app = GitLabApp(fakeGitLab, GitLabSettings(writesEnabled: true));
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        var mr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);

        await RecordGitLabDecisionWriteAsync(app, issue.ItemId!, DecisionType.Approve, "Ship it.");
        var issueNote = Assert.Single(fakeGitLab.IssueNotesCreated);
        Assert.Equal(12, issueNote.Iid);
        Assert.Contains("Ship it.", issueNote.Body);

        await RecordGitLabDecisionWriteAsync(app, mr.ItemId!, DecisionType.RequestChanges, "Please fix this.");
        var mrNote = Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(7, mrNote.Iid);
        Assert.Contains("requested changes", mrNote.Body, StringComparison.OrdinalIgnoreCase);
        var status = Assert.Single(fakeGitLab.CommitStatusesCreated);
        Assert.Equal("head-sha-1", status.Sha);
        Assert.Equal("failed", status.State);
        Assert.Equal(GitLabWriteService.CommitStatusName, status.Name);
    }

    [Fact]
    public async Task GitLabDecisionWrites_FailWhenTargetProjectProfileTokenMissing()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var settings = GitLabSettings(writesEnabled: true);
        settings["Oratorio:GitLab:ProjectProfiles:0:ProjectPath"] = "group/other/project";
        await using var app = GitLabApp(fakeGitLab, settings);
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=issue", JsonOptions);
        var issue = Assert.Single(list!.Items);

        await RecordGitLabDecisionWriteAsync(app, issue.ItemId!, DecisionType.Approve, "Ship it.");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var write = Assert.Single(await db.SourceWriteLogs.AsNoTracking().Where(x => x.ItemId == issue.ItemId).ToListAsync());
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("gitlabProjectProfileTokenMissing", write.ErrorCode);
        Assert.Empty(fakeGitLab.IssueNotesCreated);
    }

    [Fact]
    public async Task GitLabReviewDraftPublish_CreatesSummaryNoteAndInlineDiscussions()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(2, draft.AcceptedCount);
        Assert.Equal(0, draft.WarningCount);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Contains(published.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestDiscussion && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);
        var summaryNote = Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(7, summaryNote.Iid);
        Assert.Equal("Found 2 issues.", summaryNote.Body);
        Assert.Equal(2, fakeGitLab.MergeRequestDiscussionsCreated.Count);
        Assert.Contains(fakeGitLab.MergeRequestDiscussionsCreated, discussion =>
            discussion.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) &&
            discussion.Position.NewPath == "src/Auth/RefreshTokenStore.cs" &&
            discussion.Position.NewLine == 88 &&
            discussion.Position.OldLine is null &&
            discussion.Position.HeadSha == "head-sha-1" &&
            discussion.Body.Contains("```suggestion", StringComparison.Ordinal) &&
            !discussion.Body.Contains("```suggestion:", StringComparison.Ordinal));
        Assert.Contains(fakeGitLab.MergeRequestDiscussionsCreated, discussion =>
            discussion.Body.StartsWith("**🟡 Validate middleware setup**", StringComparison.Ordinal) &&
            discussion.Position.NewPath == "src/Auth/JwtMiddleware.cs" &&
            discussion.Position.NewLine == 22 &&
            !discussion.Body.Contains("```suggestion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitLabReviewDraftPublish_UsesOldAndNewLinesForContextLineSuggestions()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitContextLineReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        var suggestion = Assert.Single(draft.Comments);
        Assert.Equal(89, suggestion.Line);
        Assert.Equal("RIGHT", suggestion.Side);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        var discussion = Assert.Single(fakeGitLab.MergeRequestDiscussionsCreated);
        Assert.Equal(87, discussion.Position.OldLine);
        Assert.Equal(89, discussion.Position.NewLine);
        Assert.Contains("```suggestion", discussion.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("```suggestion:", discussion.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitLabReviewDraftPublish_MissingCurrentPositionCreatesFailedSourceWrite()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        fakeGitLab.Diffs.Clear();

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(published.ReviewDrafts).Status);
        Assert.Empty(fakeGitLab.MergeRequestNotesCreated);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
        var write = Assert.Single(published.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("gitlabDiffAnchorMissing", write.ErrorCode);
    }

    [Fact]
    public async Task GitLabReviewFindingResolution_CapturesThreadIdsAndPropagatesToDiscussions()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var settings = GitLabSettings(writesEnabled: true);
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });
        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);

        // Step B: each accepted finding captured its GitLab discussion id at publish.
        string firstFindingId;
        string firstThreadId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var comments = await db.ReviewDraftComments
                .Where(c => c.DraftId == draft.DraftId && c.Status == ReviewDraftCommentStatus.Accepted)
                .ToListAsync();
            Assert.Equal(2, comments.Count);
            Assert.All(comments, c => Assert.False(string.IsNullOrWhiteSpace(c.RemoteThreadId)));
            var first = comments[0];
            firstFindingId = first.DraftCommentId;
            firstThreadId = first.RemoteThreadId!;
        }

        // Step C: resolving propagates to the matching GitLab discussion.
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{firstFindingId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Addressed."));
        Assert.Contains(fakeGitLab.DiscussionResolutions, x => x.DiscussionId == firstThreadId && x.Resolved);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}/comments/{firstFindingId}/reopen",
            new { });
        Assert.Contains(fakeGitLab.DiscussionResolutions, x => x.DiscussionId == firstThreadId && !x.Resolved);
    }

    [Fact]
    public async Task GitLabReviewDraftPublish_UsesOffsetAwareSuggestionFenceForMultiLineSuggestions()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitMultiLineReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        var suggestion = Assert.Single(draft.Comments);
        Assert.Equal("        return token;\n        return refreshed;", suggestion.SuggestionOriginal);
        Assert.Equal("        return refreshed;\n        return token;", suggestion.SuggestionReplacement);
        Assert.Equal(87, suggestion.StartLine);
        Assert.Equal(88, suggestion.Line);
        Assert.Equal("RIGHT", suggestion.StartSide);
        Assert.Equal("RIGHT", suggestion.Side);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        var discussion = Assert.Single(fakeGitLab.MergeRequestDiscussionsCreated);
        Assert.Equal(88, discussion.Position.NewLine);
        Assert.Null(discussion.Position.OldLine);
        Assert.StartsWith("**🔴 Refresh order is reversed**", discussion.Body, StringComparison.Ordinal);
        Assert.Contains("```suggestion:-1+0", discussion.Body);
    }

    [Fact]
    public async Task GitLabReviewDraftPublish_SummaryOnlyCreatesNoteWithoutReadingDiffs()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailMergeRequestDiffReads = true };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a clean GitLab MR review.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        var published = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        Assert.Equal(0, fakeGitLab.MergeRequestDiffReadCount);
        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(published.ReviewDrafts).Status);
        Assert.Contains(published.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestNote && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);
        var summaryNote = Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(7, summaryNote.Iid);
        Assert.Equal("No issues found.", summaryNote.Body);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
    }

    [Fact]
    public async Task GitLabReviewDraftPublishRetry_PreservesInlineSuggestionsAndMarksDraftPublished()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailNextMergeRequestNoteCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var draft = Assert.Single(reviewed.ReviewDrafts);

        var acceptedComment = draft.Comments.First(x => x.Status == ReviewDraftCommentStatus.Accepted && x.SuggestionReplacement is not null);
        _ = await PatchAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draft.DraftId}",
            new ReviewDraftUpdateRequest(
                "Retryable GitLab review summary.",
                [new ReviewDraftCommentUpdateRequest(acceptedComment.DraftCommentId, "Retry inline body.", "return retryToken;")]));

        var failedPublish = await PostAsync<ItemDetailResponse>(client, $"/api/v1/review-drafts/{draft.DraftId}/publish", new { });

        var failedDraft = Assert.Single(failedPublish.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, failedDraft.Status);
        Assert.Empty(fakeGitLab.MergeRequestNotesCreated);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
        var failedWrite = Assert.Single(failedPublish.SourceWrites, write => write.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, failedWrite.Status);
        using (var document = JsonDocument.Parse(failedWrite.RequestJson))
        {
            var comments = document.RootElement.GetProperty("comments");
            Assert.Contains(
                comments.EnumerateArray(),
                comment => (comment.GetProperty("body").GetString() ?? "").Contains("```suggestion", StringComparison.Ordinal) &&
                    (comment.GetProperty("body").GetString() ?? "").Contains("return retryToken;", StringComparison.Ordinal));
        }

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Equal(2, retriedWrite.AttemptCount);
        var publishedDraft = Assert.Single(retried.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, publishedDraft.Status);
        Assert.NotNull(publishedDraft.PublishedAt);
        Assert.Equal(failedWrite.WriteId, publishedDraft.SourceWriteId);
        Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(2, fakeGitLab.MergeRequestDiscussionsCreated.Count);
        Assert.Contains(fakeGitLab.MergeRequestDiscussionsCreated, discussion =>
            discussion.Body.StartsWith("**🔴 Missing refresh guard**", StringComparison.Ordinal) &&
            discussion.Body.Contains("```suggestion", StringComparison.Ordinal) &&
            discussion.Body.Contains("return retryToken;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GitLabAutoReviewPublish_PublishesValidDraftAsMergeRequestDiscussions()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var settings = GitLabSettings(writesEnabled: true);
        settings["Oratorio:Automation:AutoReviewPublishEnabled"] = "true";
        settings["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "gitlab:gitlab.example.test/group/subgroup/project";
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            mr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Kind == SourceWriteKind.MergeRequestDiscussion &&
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Succeeded));

        Assert.Equal(ReviewDraftStatus.Published, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Equal(ItemState.AwaitingReview, reviewed.Item.State);
        Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(2, fakeGitLab.MergeRequestDiscussionsCreated.Count);
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestDiscussion && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task GitLabAutoReviewPublish_UsesLocalDiffFallbackWhenDiffReadFails()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailMergeRequestDiffReads = true };
        var localDiffs = new FakeReviewLocalDiffProvider(fakeGitLab.Diffs.ToDictionary(diff => diff.NewPath, diff => diff.Diff!));
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var settings = GitLabSettings(writesEnabled: true);
        settings["Oratorio:Automation:AutoReviewPublishEnabled"] = "true";
        settings["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "gitlab:gitlab.example.test/group/subgroup/project";
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IReviewLocalDiffProvider>();
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IReviewLocalDiffProvider>(localDiffs);
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            mr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Kind == SourceWriteKind.MergeRequestDiscussion &&
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Succeeded));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.Published, draft.Status);
        Assert.Equal(0, draft.WarningCount);
        Assert.Equal(2, fakeGitLab.MergeRequestDiffReadCount);
        Assert.Single(fakeGitLab.MergeRequestNotesCreated);
        Assert.Equal(2, fakeGitLab.MergeRequestDiscussionsCreated.Count);
        Assert.All(fakeGitLab.MergeRequestDiscussionsCreated, discussion =>
        {
            Assert.Equal(discussion.Position.OldPath, discussion.Position.NewPath);
            Assert.NotNull(discussion.Position.NewLine);
        });
        Assert.Contains(reviewed.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestDiscussion && write.Intent == "reviewDraftPublish" && write.Status == SourceWriteStatus.Succeeded);
    }

    [Fact]
    public async Task GitLabAutoReviewPublish_BlocksDraftsWithWarningsAsFailedSourceWrite()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitNoOpReviewDraft);
        var settings = GitLabSettings(writesEnabled: true);
        settings["Oratorio:Automation:AutoReviewPublishEnabled"] = "true";
        settings["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "gitlab:gitlab.example.test/group/subgroup/project";
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare a no-op GitLab MR review suggestion.", null, null));
        var reviewed = await WaitForItemByIdAsync(
            client,
            mr.ItemId!,
            x => x.SourceWrites.Any(write =>
                write.Intent == "reviewDraftPublish" &&
                write.Status == SourceWriteStatus.Failed &&
                write.ErrorCode == "reviewDraftWarnings"));

        var draft = Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(ReviewDraftStatus.PublishFailed, draft.Status);
        Assert.Equal(2, draft.WarningCount);
        Assert.Empty(fakeGitLab.MergeRequestNotesCreated);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("reviewDraftWarnings", write.ErrorCode);
    }

    [Fact]
    public async Task GitLabAutoReviewPublish_BlocksStaleHeadAsFailedSourceWrite()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var fakeWorktree = new FakeWorktreeManager { BaseShaOverride = "older-sha" };
        var settings = GitLabSettings(writesEnabled: true);
        settings["Oratorio:Automation:AutoReviewPublishEnabled"] = "true";
        settings["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "gitlab:gitlab.example.test/group/subgroup/project";
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IWorktreeManager>();
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IWorktreeManager>(fakeWorktree);
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.SourceWrites.Any(write => write.ErrorCode == "stalePullRequestHead"));

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Empty(fakeGitLab.MergeRequestNotesCreated);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("stalePullRequestHead", write.ErrorCode);
    }

    [Fact]
    public async Task GitLabAutoReviewPublish_BlocksDisabledGitLabWritesAsFailedSourceWrite()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitCleanReviewDraft);
        var settings = GitLabSettings(writesEnabled: false);
        settings["Oratorio:Automation:AutoReviewPublishEnabled"] = "true";
        settings["Oratorio:Automation:AutoReviewPublishRepositories:0"] = "gitlab:gitlab.example.test/group/subgroup/project";
        await using var app = GitLabApp(
            fakeGitLab,
            settings,
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=pullRequest", JsonOptions);
        var mr = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{mr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Prepare GitLab MR review suggestions.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, mr.ItemId!, x => x.SourceWrites.Any(write => write.Intent == "reviewDraftPublish" && write.ErrorCode == "gitlabWritesDisabled"));

        Assert.Equal(ReviewDraftStatus.PublishFailed, Assert.Single(reviewed.ReviewDrafts).Status);
        Assert.Empty(fakeGitLab.MergeRequestNotesCreated);
        Assert.Empty(fakeGitLab.MergeRequestDiscussionsCreated);
        var write = Assert.Single(reviewed.SourceWrites, x => x.Intent == "reviewDraftPublish");
        Assert.Equal(SourceWriteStatus.Failed, write.Status);
        Assert.Equal("gitlabWritesDisabled", write.ErrorCode);
    }

    [Fact]
    public async Task GitLabImplementationDraft_AutoPrCreatesGeneratedMergeRequestItem()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IGitDeliveryClient>();
                services.AddSingleton<IGitDeliveryClient>(fakeGit);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=issue", JsonOptions);
        var issue = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the GitLab issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var delivered = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.ImplementationDrafts.Any(draft => draft.Status == ImplementationDraftStatus.Delivered));
        var draft = Assert.Single(delivered.ImplementationDrafts);

        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Equal("https://gitlab.example.test/group/subgroup/project/-/merge_requests/17", draft.PullRequestUrl);
        Assert.Equal("gitlab", Assert.Single(fakeGit.Pushes).Project.Provider);
        Assert.Equal("group/subgroup/project", Assert.Single(fakeGit.Pushes).Project.ProjectPath);
        var create = Assert.Single(fakeGitLab.MergeRequestsCreated);
        Assert.Equal("main", create.TargetBranch);
        Assert.Contains("Refs #12", create.Description);
        Assert.Contains(delivered.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestCreation && write.Status == SourceWriteStatus.Succeeded);

        var generated = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{draft.PullRequestItemId}", JsonOptions);
        Assert.NotNull(generated);
        Assert.Equal("gitlab", generated!.Item.Source);
        Assert.Equal(ItemKind.PullRequest, generated.Item.Kind);
        Assert.Equal(issue.ItemId, generated.Item.ParentItemId);
        Assert.Equal(draft.DraftId, generated.Item.GeneratedFromDraftId);
    }

    [Fact]
    public async Task GitLabImplementationDraft_SourceWriteRetryReusesCommitAndBranchPush()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailNextMergeRequestCreateCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IGitDeliveryClient>();
                services.AddSingleton<IGitDeliveryClient>(fakeGit);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=issue", JsonOptions);
        var issue = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the GitLab issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var failed = await WaitForItemByIdAsync(
            client,
            issue.ItemId!,
            x => x.ImplementationDrafts.Any(draft => draft.Status == ImplementationDraftStatus.DeliveryFailed) &&
                x.SourceWrites.Any(write => write.Kind == SourceWriteKind.MergeRequestCreation && write.Status == SourceWriteStatus.Failed));
        var failedWrite = Assert.Single(failed.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestCreation);
        Assert.Equal("deliveryFailed", failedWrite.ErrorCode);
        Assert.Equal("commit-sha-123", Assert.Single(failed.ImplementationDrafts).CommitSha);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Empty(fakeGitLab.MergeRequestsCreated);

        fakeGit.EmptyDiff = true;
        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failedWrite.WriteId}/retry", new { });

        var draft = Assert.Single(retried.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Equal("commit-sha-123", draft.CommitSha);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        var create = Assert.Single(fakeGitLab.MergeRequestsCreated);
        Assert.Equal("main", create.TargetBranch);
        var retriedWrite = Assert.Single(retried.SourceWrites, write => write.WriteId == failedWrite.WriteId);
        Assert.Equal(SourceWriteStatus.Succeeded, retriedWrite.Status);
        Assert.Null(retriedWrite.ErrorCode);
        Assert.DoesNotContain(retried.SourceWrites, write => write.ErrorCode == "invalidGitLabTarget");
    }

    [Fact]
    public async Task GitLabImplementationDraft_DeliverRetryAfterMergeRequestFailureDoesNotRequireDiff()
    {
        var fakeGitLab = new FakeGitLabApiClient { FailNextMergeRequestCreateCount = 1 };
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IGitDeliveryClient>();
                services.AddSingleton<IGitDeliveryClient>(fakeGit);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=issue", JsonOptions);
        var issue = Assert.Single(list!.Items);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the GitLab issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var failed = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.ImplementationDrafts.Any(draft => draft.Status == ImplementationDraftStatus.DeliveryFailed));
        var failedDraft = Assert.Single(failed.ImplementationDrafts);
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);

        fakeGit.EmptyDiff = true;
        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{failedDraft.DraftId}/deliver", new { });

        var draft = Assert.Single(delivered.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.DoesNotContain(delivered.SourceWrites, write => write.ErrorCode == "emptyDiff");
        Assert.Single(fakeGit.CommitMessages);
        Assert.Single(fakeGit.Pushes);
        Assert.Single(fakeGitLab.MergeRequestsCreated);
    }

    [Fact]
    public async Task GitLabImplementationDraft_RetryReusesSucceededMergeRequestWrite()
    {
        var fakeGitLab = new FakeGitLabApiClient();
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = GitLabApp(
            fakeGitLab,
            GitLabSettings(writesEnabled: true),
            services =>
            {
                services.RemoveAll<IGitDeliveryClient>();
                services.AddSingleton<IGitDeliveryClient>(fakeGit);
            });
        var client = app.CreateClient();
        var job = await EnqueueGitLabSyncAsync(client, SourceSyncMode.Incremental);
        await WaitForSourceSyncJobAsync(client, job.JobId);
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=gitlab&kind=issue", JsonOptions);
        var issue = Assert.Single(list!.Items);
        var draftId = await SeedFailedImplementationDraftWithSucceededMrWriteAsync(app, issue.ItemId!);

        var delivered = await PostAsync<ItemDetailResponse>(client, $"/api/v1/implementation-drafts/{draftId}/deliver", new { });

        var draft = Assert.Single(delivered.ImplementationDrafts);
        Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status);
        Assert.Empty(fakeGitLab.MergeRequestsCreated);
        Assert.Contains(delivered.SourceWrites, write => write.Kind == SourceWriteKind.MergeRequestCreation && write.Status == SourceWriteStatus.Succeeded);
        var generated = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{draft.PullRequestItemId}", JsonOptions);
        Assert.NotNull(generated);
        Assert.Equal("mr:gitlab.example.test/group/subgroup/project!17", generated!.Item.ExternalId);
    }

    [Fact]
    public async Task GitLabApiClient_FollowsPaginationAndSendsPrivateToken()
    {
        var requests = new List<Uri>();
        var handler = new CapturingHandler(request =>
        {
            requests.Add(request.RequestUri!);
            Assert.True(request.Headers.TryGetValues("PRIVATE-TOKEN", out var tokenValues));
            Assert.Equal("project-token", Assert.Single(tokenValues));
            var page = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal) ? "2" : "1";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(page == "1"
                    ? """[{"id":1,"iid":1,"title":"First","state":"opened","labels":[],"assignees":[],"created_at":"2026-05-19T09:00:00Z","updated_at":"2026-05-19T09:00:00Z"}]"""
                    : """[{"id":2,"iid":2,"title":"Second","state":"opened","labels":[],"assignees":[],"created_at":"2026-05-19T09:01:00Z","updated_at":"2026-05-19T09:01:00Z"}]""",
                    Encoding.UTF8,
                    "application/json")
            };
            if (page == "1")
            {
                response.Headers.TryAddWithoutValidation("X-Next-Page", "2");
            }

            return response;
        });
        var httpClient = new HttpClient(handler);
        var api = new GitLabApiClient(
            new StaticHttpClientFactory(httpClient),
            new StaticOptionsMonitor<GitLabOptions>(new GitLabOptions
            {
                Endpoint = "https://gitlab.example.test",
                ProjectProfiles =
                [
                    new GitLabProjectProfileOptions
                    {
                        Instance = "gitlab.example.test",
                        ProjectPath = "group/subgroup/project",
                        TokenKind = "projectAccessToken",
                        Token = "project-token"
                    },
                    new GitLabProjectProfileOptions
                    {
                        Instance = "gitlab.example.test",
                        ProjectPath = "group/other/project",
                        TokenKind = "projectAccessToken",
                        Token = "other-project-token"
                    }
                ]
            }),
            new GitLabCredentialResolver(new PassthroughConfigurationSecretProtector()));

        var issues = await api.ListIssuesAsync(new GitLabProjectRef("group/subgroup/project"), GitLabListState.Opened, null, CancellationToken.None);

        Assert.Equal([1, 2], issues.Select(x => x.Iid).ToArray());
        Assert.Equal(2, requests.Count);
        Assert.StartsWith("https://gitlab.example.test/api/v4/", requests[0].AbsoluteUri);
        Assert.Contains("%2F", requests[0].AbsoluteUri);
        var ex = await Assert.ThrowsAsync<GitLabCredentialException>(() =>
            api.ListIssuesAsync(new GitLabProjectRef("group/missing/project"), GitLabListState.Opened, null, CancellationToken.None));
        Assert.Equal("gitlabProjectProfileTokenMissing", ex.Code);
        Assert.Equal(2, requests.Count);
    }

    private static TestOratorioApp GitLabApp(
        FakeGitLabApiClient fakeGitLab,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null) =>
        new(
            services =>
            {
                var defaultWorkspace = Path.Combine(Path.GetTempPath(), "oratorio-test-workspace");
                Directory.CreateDirectory(defaultWorkspace);
                services.RemoveAll<IGitLabApiClient>();
                services.RemoveAll<IClock>();
                services.RemoveAll<IOptionsMonitor<DotCraftOptions>>();
                services.AddSingleton<IClock>(new FixedClock(Now));
                services.AddSingleton<IGitLabApiClient>(fakeGitLab);
                services.AddSingleton<IOptionsMonitor<DotCraftOptions>>(new StaticOptionsMonitor<DotCraftOptions>(new DotCraftOptions
                {
                    RepositoryWorkspaceRoutes =
                    [
                        new() { Project = "github:github.com/example-owner/oratorio", WorkspacePath = defaultWorkspace },
                        new() { Project = "gitlab:gitlab.example.test/group/subgroup/project", WorkspacePath = defaultWorkspace }
                    ],
                    MaxRunAttempts = 1,
                    RetryBackoffSeconds = 1,
                    MaxRetryBackoffSeconds = 1
                }));
                configureServices?.Invoke(services);
            },
            settings ?? GitLabSettings());

    private static Dictionary<string, string?> GitLabSettings(bool writesEnabled = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Oratorio:GitLab:Enabled"] = "true",
            ["Oratorio:GitLab:WritesEnabled"] = writesEnabled ? "true" : "false",
            ["Oratorio:GitLab:Endpoint"] = "https://gitlab.example.test",
            ["Oratorio:GitLab:Projects:0"] = "group/subgroup/project"
        };

        settings["Oratorio:GitLab:ProjectProfiles:0:Instance"] = "gitlab.example.test";
        settings["Oratorio:GitLab:ProjectProfiles:0:ProjectPath"] = "group/subgroup/project";
        settings["Oratorio:GitLab:ProjectProfiles:0:TokenKind"] = "projectAccessToken";
        settings["Oratorio:GitLab:ProjectProfiles:0:Token"] = "gitlab-token";
        settings["Oratorio:GitLab:ProjectProfiles:0:WebhookSecret"] = "gitlab-webhook-secret";
        settings["Oratorio:GitLab:ProjectProfiles:0:WebhookSigningToken"] = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("signing-key"));

        return settings;
    }

    private static async Task<SourceSyncJobDto> EnqueueGitLabSyncAsync(HttpClient client, SourceSyncMode mode)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/sources/gitlab/sync-jobs",
            new SourceSyncJobRequest("gitlab", mode),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SourceSyncJobDto>(JsonOptions)
            ?? throw new InvalidOperationException("GitLab sync job response was empty.");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object payload)
    {
        var response = await client.PostAsJsonAsync(path, payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException("Response was empty.");
    }

    private static async Task<T> PatchAsync<T>(HttpClient client, string path, object payload)
    {
        var response = await client.PatchAsJsonAsync(path, payload, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException("Response was empty.");
    }

    private static async Task<SourceSyncJobDto> WaitForSourceSyncJobAsync(HttpClient client, string jobId)
    {
        SourceSyncJobDto? latest = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<SourceSyncJobDto>($"/api/v1/sources/sync-jobs/{jobId}?provider=gitlab", JsonOptions);
            if (latest is not null && latest.Status is not (SourceSyncStatus.Queued or SourceSyncStatus.Running))
            {
                return latest;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for GitLab sync job {jobId}. Latest status: {latest?.Status}");
    }

    private static async Task DispatchAutoReviewAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AutoReviewDispatchService>();
        await dispatcher.DispatchEligibleAsync(CancellationToken.None);
    }

    private static async Task<ItemDetailResponse> WaitForItemByIdAsync(HttpClient client, string itemId, Func<ItemDetailResponse, bool> predicate)
    {
        ItemDetailResponse? latest = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(100);
        }

        var runs = latest is null
            ? "(none)"
            : string.Join("; ", latest.Runs.Select(run => $"{run.Status}/{run.ErrorCode ?? "-"}:{run.StatusMessage}"));
        throw new TimeoutException($"Timed out waiting for item {itemId}. Latest state: {latest?.Item.State}; runs: {runs}");
    }

    private static async Task RecordGitLabDecisionWriteAsync(TestOratorioApp app, string itemId, DecisionType decisionType, string body)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var item = db.Items.Single(x => x.ItemId == itemId);
        var round = new OratorioRound
        {
            ItemId = item.ItemId,
            RoundNumber = item.CurrentRound + 1,
            Status = RoundStatus.Open,
            CreatedAt = Now
        };
        item.CurrentRound = round.RoundNumber;
        db.Rounds.Add(round);
        var decision = new OratorioDecision
        {
            ItemId = item.ItemId,
            Round = round,
            Decision = decisionType,
            Body = body,
            CreatedAt = Now
        };
        db.Decisions.Add(decision);
        await db.SaveChangesAsync();

        var writes = scope.ServiceProvider.GetRequiredService<GitLabWriteService>();
        await writes.RecordDecisionWritesAsync(decision.DecisionId, CancellationToken.None);
    }

    private static async Task<string> SeedFailedImplementationDraftWithSucceededMrWriteAsync(TestOratorioApp app, string itemId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var item = db.Items.Single(x => x.ItemId == itemId);
        var workspace = Path.Combine(Path.GetTempPath(), "oratorio-test-workspace");
        Directory.CreateDirectory(workspace);
        var round = new OratorioRound
        {
            ItemId = item.ItemId,
            RoundNumber = item.CurrentRound + 1,
            Status = RoundStatus.AwaitingReview,
            CreatedAt = Now
        };
        item.CurrentRound = round.RoundNumber;
        db.Rounds.Add(round);
        var run = new OratorioRun
        {
            ItemId = item.ItemId,
            Round = round,
            Attempt = 1,
            Status = RunStatus.Succeeded,
            RunnerKind = "appServer",
            StartedAt = Now,
            CompletedAt = Now,
            Purpose = RunPurpose.Implementation,
            DeliveryPolicy = DeliveryPolicy.ManualDelivery,
            BaseWorkspacePath = workspace,
            WorktreePath = workspace,
            WorktreeBranch = "oratorio/run/retry",
            WorktreeStatus = WorktreeStatus.Ready
        };
        db.Runs.Add(run);
        var draft = new OratorioImplementationDraft
        {
            ItemId = item.ItemId,
            Round = round,
            Run = run,
            Status = ImplementationDraftStatus.DeliveryFailed,
            DeliveryPolicy = DeliveryPolicy.ManualDelivery,
            Summary = "Retry failed GitLab delivery.",
            ProposedCommitMessage = "Implement retryable GitLab issue",
            ProposedPrTitle = "Implement retryable GitLab issue",
            ProposedPrBody = "Implements a retryable GitLab issue.",
            ErrorCode = "generatedMrLinkFailed",
            ErrorMessage = "Synthetic link failure after MR creation.",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        db.ImplementationDrafts.Add(draft);
        var created = new GitLabMergeRequestCreateResponse(
            701,
            17,
            "Implement retryable GitLab issue",
            "Implements a retryable GitLab issue.",
            "opened",
            "https://gitlab.example.test/group/subgroup/project/-/merge_requests/17",
            "oratorio/run/retry",
            "main",
            "commit-sha-existing",
            new GitLabDiffRefs("base-sha", "commit-sha-existing", "start-sha"));
        db.SourceWriteLogs.Add(new OratorioSourceWriteLog
        {
            ItemId = item.ItemId,
            Round = round,
            Source = "gitlab",
            Kind = SourceWriteKind.MergeRequestCreation,
            Intent = "implementationMergeRequestCreate",
            Status = SourceWriteStatus.Succeeded,
            Repository = "gitlab:gitlab.example.test/group/subgroup/project",
            ExternalId = "mr:gitlab.example.test/group/subgroup/project!17",
            ExternalUrl = created.WebUrl,
            RequestJson = "{}",
            ResponseJson = JsonSerializer.Serialize(created, JsonOptions),
            AttemptCount = 1,
            CreatedAt = Now,
            UpdatedAt = Now,
            CompletedAt = Now
        });
        await db.SaveChangesAsync();
        return draft.DraftId;
    }

    private static string StandardWebhookSignature(string signingToken, string webhookId, string timestamp, string body)
    {
        var rawKey = Convert.FromBase64String(signingToken["whsec_".Length..]);
        var message = $"{webhookId}.{timestamp}.{body}";
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(rawKey, Encoding.UTF8.GetBytes(message)));
    }

    private sealed class FakeGitLabApiClient : IGitLabApiClient
    {
        private const string ProjectPath = "group/subgroup/project";

        public List<GitLabIssue> Issues { get; } =
        [
            new(
                101,
                12,
                "Backfill GitLab issue import",
                "Bring GitLab issues onto the board.",
                "opened",
                "https://gitlab.example.test/group/subgroup/project/-/issues/12",
                new GitLabUser(1, "example-author", "Example Author", null),
                ["backend", "m2"],
                [new GitLabUser(2, "example-assignee", "Example Assignee", null)],
                DateTimeOffset.Parse("2026-05-19T08:00:00Z"),
                DateTimeOffset.Parse("2026-05-19T08:30:00Z"),
                null)
        ];

        public List<GitLabMergeRequest> MergeRequests { get; } =
        [
            new(
                201,
                7,
                "Read GitLab merge requests",
                "Import merge requests as review targets.",
                "opened",
                "https://gitlab.example.test/group/subgroup/project/-/merge_requests/7",
                new GitLabUser(1, "example-author", "Example Author", null),
                ["review"],
                [new GitLabUser(2, "example-assignee", "Example Assignee", null)],
                [new GitLabUser(3, "example-reviewer", "Example Reviewer", null)],
                DateTimeOffset.Parse("2026-05-19T08:05:00Z"),
                DateTimeOffset.Parse("2026-05-19T08:35:00Z"),
                null,
                null,
                false,
                "feature/gitlab-read",
                "main",
                "head-sha-1",
                new GitLabDiffRefs("base-sha", "head-sha-1", "start-sha"),
                "mergeable",
                "can_be_merged")
        ];

        public List<GitLabMergeRequestDiff> Diffs { get; } =
        [
            new(
                "src/Auth/RefreshTokenStore.cs",
                "src/Auth/RefreshTokenStore.cs",
                "@@ -86,3 +86,4 @@\n line86\n+        return token;\n+        return refreshed;\n line89\n",
                false,
                false,
                false),
            new(
                "src/Auth/JwtMiddleware.cs",
                "src/Auth/JwtMiddleware.cs",
                "@@ -20,4 +20,4 @@\n line20\n line21\n-await next();\n+await validateThenNext();\n line23\n",
                false,
                false,
                false)
        ];

        public List<(int Iid, string Body)> IssueNotesCreated { get; } = [];
        public List<(int Iid, string Body)> MergeRequestNotesCreated { get; } = [];
        public List<(int Iid, string Body, GitLabMergeRequestPosition Position)> MergeRequestDiscussionsCreated { get; } = [];
        public List<(string Sha, string State, string Name, string Description)> CommitStatusesCreated { get; } = [];
        public List<(string Title, string SourceBranch, string TargetBranch, string Description)> MergeRequestsCreated { get; } = [];
        public int FailNextMergeRequestCreateCount { get; set; }
        public int FailNextMergeRequestNoteCount { get; set; }
        public bool FailMergeRequestDiffReads { get; set; }
        public int MergeRequestDiffReadCount { get; private set; }
        public HashSet<string> CancelingIssueProjects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<GitLabProject> GetProjectAsync(GitLabProjectRef project, CancellationToken ct) =>
            Task.FromResult(new GitLabProject(99, ProjectPath, "https://gitlab.example.test/group/subgroup/project", "main"));

        public Task<IReadOnlyList<GitLabIssue>> ListIssuesAsync(GitLabProjectRef project, GitLabListState state, DateTimeOffset? updatedAfter, CancellationToken ct)
        {
            if (CancelingIssueProjects.Contains(project.ProjectPath))
            {
                throw new TaskCanceledException($"Synthetic GitLab list timeout for {project.ProjectPath}.");
            }

            return Task.FromResult<IReadOnlyList<GitLabIssue>>(Issues
                .Where(issue => MatchesState(issue.State, state))
                .Where(issue => updatedAfter is null || issue.UpdatedAt >= updatedAfter)
                .ToArray());
        }

        public Task<IReadOnlyList<GitLabMergeRequest>> ListMergeRequestsAsync(GitLabProjectRef project, GitLabListState state, DateTimeOffset? updatedAfter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitLabMergeRequest>>(MergeRequests
                .Where(mergeRequest => MatchesState(mergeRequest.State, state))
                .Where(mergeRequest => updatedAfter is null || mergeRequest.UpdatedAt >= updatedAfter)
                .ToArray());

        public Task<GitLabMergeRequest> GetMergeRequestAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
            Task.FromResult(MergeRequests.Single(x => x.Iid == iid));

        public Task<IReadOnlyList<GitLabMergeRequestDiff>> ListMergeRequestDiffsAsync(GitLabProjectRef project, int iid, CancellationToken ct)
        {
            MergeRequestDiffReadCount++;
            if (FailMergeRequestDiffReads)
            {
                throw new HttpRequestException("Synthetic GitLab diff read failure.", null, HttpStatusCode.InternalServerError);
            }

            return Task.FromResult<IReadOnlyList<GitLabMergeRequestDiff>>(Diffs);
        }

        public Task<IReadOnlyList<GitLabNote>> ListIssueNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitLabNote>>(
            [
                new(301, "Issue note imported from GitLab.", new GitLabUser(4, "reviewer", "Reviewer", null), Now, Now, false, null, iid, "https://gitlab.example.test/group/subgroup/project/-/issues/12#note_301")
            ]);

        public Task<IReadOnlyList<GitLabNote>> ListMergeRequestNotesAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitLabNote>>(
            [
                new(401, "Please keep subgroup paths encoded.", new GitLabUser(4, "reviewer", "Reviewer", null), Now, Now, false, null, iid, "https://gitlab.example.test/group/subgroup/project/-/merge_requests/7#note_401")
            ]);

        public Task<IReadOnlyList<GitLabDiscussion>> ListMergeRequestDiscussionsAsync(GitLabProjectRef project, int iid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GitLabDiscussion>>(
            [
                new(
                    "discussion-1",
                    false,
                    [
                        new(
                            501,
                            "Inline discussion imported from GitLab.",
                            new GitLabUser(5, "inline-reviewer", "Inline Reviewer", null),
                            Now,
                            Now,
                            false,
                            "DiffNote",
                            "https://gitlab.example.test/group/subgroup/project/-/merge_requests/7#note_501",
                            new GitLabPosition("text", null, "src/GitLabImport.cs", null, 42))
                    ])
            ]);

        public Task<GitLabWriteResponse> CreateIssueNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct)
        {
            IssueNotesCreated.Add((iid, body));
            return Task.FromResult(new GitLabWriteResponse("issue-note-write", $"https://gitlab.example.test/{ProjectPath}/-/issues/{iid}#note_write", "{}"));
        }

        public Task<GitLabWriteResponse> CreateMergeRequestNoteAsync(GitLabProjectRef project, int iid, string body, CancellationToken ct)
        {
            if (FailNextMergeRequestNoteCount > 0)
            {
                FailNextMergeRequestNoteCount--;
                throw new HttpRequestException("Synthetic GitLab merge request note failure.");
            }

            MergeRequestNotesCreated.Add((iid, body));
            return Task.FromResult(new GitLabWriteResponse("mr-note-write", $"https://gitlab.example.test/{ProjectPath}/-/merge_requests/{iid}#note_write", "{}"));
        }

        public List<(string DiscussionId, bool Resolved)> DiscussionResolutions { get; } = [];

        private int _discussionCounter;

        public Task<GitLabWriteResponse> CreateMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string body, GitLabMergeRequestPosition position, CancellationToken ct)
        {
            MergeRequestDiscussionsCreated.Add((iid, body, position));
            var discussionId = $"discussion-{++_discussionCounter}";
            return Task.FromResult(new GitLabWriteResponse(discussionId, $"https://gitlab.example.test/{ProjectPath}/-/merge_requests/{iid}#note_{discussionId}", "{}"));
        }

        public Task<GitLabWriteResponse> ResolveMergeRequestDiscussionAsync(GitLabProjectRef project, int iid, string discussionId, bool resolved, CancellationToken ct)
        {
            DiscussionResolutions.Add((discussionId, resolved));
            return Task.FromResult(new GitLabWriteResponse(discussionId, null, "{}"));
        }

        public Task<GitLabWriteResponse> SetCommitStatusAsync(GitLabProjectRef project, string sha, string state, string name, string description, string? targetUrl, CancellationToken ct)
        {
            CommitStatusesCreated.Add((sha, state, name, description));
            return Task.FromResult(new GitLabWriteResponse("status-write", null, "{}"));
        }

        public Task<GitLabMergeRequestCreateResponse> CreateMergeRequestAsync(GitLabProjectRef project, string title, string sourceBranch, string targetBranch, string description, bool draft, CancellationToken ct)
        {
            if (FailNextMergeRequestCreateCount > 0)
            {
                FailNextMergeRequestCreateCount--;
                throw new HttpRequestException("Synthetic GitLab merge request creation failure.");
            }

            MergeRequestsCreated.Add((title, sourceBranch, targetBranch, description));
            return Task.FromResult(new GitLabMergeRequestCreateResponse(
                701,
                17,
                title,
                description,
                "opened",
                $"https://gitlab.example.test/{ProjectPath}/-/merge_requests/17",
                sourceBranch,
                targetBranch,
                "commit-sha-123",
                new GitLabDiffRefs("base-sha", "commit-sha-123", "start-sha")));
        }

        private static bool MatchesState(string state, GitLabListState filter) =>
            filter == GitLabListState.All ||
            filter == GitLabListState.Opened && state.Equals("opened", StringComparison.OrdinalIgnoreCase) ||
            filter == GitLabListState.Closed && state.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
            filter == GitLabListState.Merged && state.Equals("merged", StringComparison.OrdinalIgnoreCase);
    }
}
