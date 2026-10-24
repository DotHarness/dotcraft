using System.Net;
using System.Net.Http.Json;
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

public sealed class AppServerRunApiTests
{
    [Fact]
    public async Task AppServerDispatch_CompletesRun_AndCreatesThread()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var fakeProcess = new FakeDotCraftProcessManager();
        var fakeWorktree = new FakeWorktreeManager();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        var dispatched = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Operator wants a read-only DotCraft pass.", null, null));

        Assert.Equal(ItemState.Dispatching, dispatched.Item.State);
        Assert.Contains(dispatched.Runs, x => x.RunnerKind == "appServer" && x.Status is (RunStatus.Queued or RunStatus.Dispatching or RunStatus.Running));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("thread-test-1", run.ThreadId);
        Assert.Equal("turn-test-1", run.TurnId);
        Assert.Contains("DotCraft analysis complete", run.Summary);
        Assert.Equal(WorktreeStatus.CleanupPending, run.WorktreeStatus);
        Assert.False(string.IsNullOrWhiteSpace(run.BaseWorkspacePath));
        Assert.Equal($"local:{Path.GetFullPath(run.BaseWorkspacePath)}", run.AppServerIdentity);
        Assert.False(string.IsNullOrWhiteSpace(run.WorktreePath));
        Assert.StartsWith("oratorio/run/", run.WorktreeBranch);
        Assert.Equal("abc123", run.BaseRef);
        Assert.Equal("abc123", run.BaseSha);
        var prepareRequest = Assert.Single(fakeWorktree.PrepareRequests);
        Assert.Equal(run.TargetHeadSha, prepareRequest.HeadSha);
        Assert.Equal(run.BaseWorkspacePath, fakeProcess.EnsureWorkspacePaths.Single());
        Assert.Equal(run.BaseWorkspacePath, fakeAppServer.LastThreadStartRequest?.BaseWorkspacePath);
        Assert.Equal(run.WorktreePath, fakeAppServer.LastThreadStartRequest?.ExecutionWorkspacePath);
        Assert.Equal(CheckState.Attention, reviewed.Item.CheckState);
        Assert.Contains(reviewed.Timeline, x => x.Title == "DotCraft thread created");
        Assert.Contains(reviewed.Timeline, x => x.Title == "Agent summary captured");

        Assert.NotNull(fakeAppServer.LastThreadStartRequest?.RuntimeAdditionalContext);
        Assert.True((fakeAppServer.LastThreadStartRequest?.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
    }

    [Fact]
    public async Task AppServerDispatch_ContinuesWhenDrawerItemPublishFails()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.DrawerItemPublishFailure);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-drawer-publish-failure");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-drawer-publish-failure/dispatch",
            new DispatchRequest("appServer", "Complete despite drawer publish failure.", null, null));

        var reviewed = await WaitForItemAsync(client, "task:test-drawer-publish-failure", x => x.Item.State == ItemState.AwaitingReview);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotEqual("appServerFailed", run.ErrorCode);
        Assert.Contains("DotCraft analysis complete", run.Summary);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task CancelRun_InterruptsActiveAppServerTurn_AndKeepsRunCancelled()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-cancel-appserver");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-appserver/dispatch",
            new DispatchRequest("appServer", "Hold until cancellation.", null, null));

        var running = await WaitForItemAsync(client, "task:test-cancel-appserver", x => x.Item.State == ItemState.Running);
        var runId = running.Item.CurrentRunId!;

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-cancel-appserver/cancel-run",
            new CancelRunRequest("Stop this AppServer run."));

        Assert.Equal(ItemState.Discovered, cancelled.Item.State);
        Assert.Null(cancelled.Item.CurrentRunId);
        Assert.Contains(cancelled.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");

        await Task.Delay(300);
        var afterCancelledNotification = await client.GetFromJsonAsync<ItemDetailResponse>("/api/v1/items/local/task:test-cancel-appserver", JsonOptions);
        Assert.Equal(ItemState.Discovered, afterCancelledNotification!.Item.State);
        Assert.Contains(afterCancelledNotification.Runs, x => x.RunId == runId && x.Status == RunStatus.Cancelled);
        Assert.DoesNotContain(afterCancelledNotification.Runs, x => x.RunId == runId && x.Status == RunStatus.Failed);
    }

    [Fact]
    public async Task CancelRun_RemainsAuthoritativeWhenReviewDraftAlreadyExists()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
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
            new DispatchRequest("appServer", "Hold after a Review Draft is stored.", null, null));
        var running = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Running);
        var run = Assert.Single(running.Runs, x => x.RunId == running.Item.CurrentRunId);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ReviewDrafts.Add(new OratorioReviewDraft
            {
                ItemId = pr.ItemId!,
                RoundId = run.RoundId,
                RunId = run.RunId,
                Status = ReviewDraftStatus.Draft,
                SummaryBody = "No issues found.",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var cancelled = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/cancel-run",
            new CancelRunRequest("Operator cancellation wins."));
        await Task.Delay(300);
        var afterNotification = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);

        Assert.Equal(ItemState.Discovered, cancelled.Item.State);
        Assert.Equal(RunStatus.Cancelled, Assert.Single(afterNotification!.Runs, x => x.RunId == run.RunId).Status);
        Assert.Equal("operatorCancelled", Assert.Single(afterNotification.Runs, x => x.RunId == run.RunId).ErrorCode);
        Assert.Single(afterNotification.ReviewDrafts);
        Assert.DoesNotContain(afterNotification.Timeline, x => x.Title == "Review draft retained after AppServer termination");
        Assert.Equal("cancelled", Assert.Single(fakeGitHub.CheckRuns).Conclusion);
    }

    [Fact]
    public async Task AppServerDispatch_RunTimeoutInterruptsTurn_AndKeepsRunTimedOut()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.TimeoutAfterTurnStarted);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "1"
            });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-timeout-interrupt");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-timeout-interrupt/dispatch",
            new DispatchRequest("appServer", "Timeout and interrupt this DotCraft run.", null, null));

        var timedOut = await WaitForItemAsync(client, "task:test-appserver-timeout-interrupt", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(timedOut.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Equal("appServerTimedOut", run.ErrorCode);
        Assert.Contains("interrupted and cancelled", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
        Assert.DoesNotContain(timedOut.Runs, x => x.ErrorCode == "appServerTurnCancelled");
    }

    [Fact]
    public async Task AppServerDispatch_RunTimeoutCompletesWhenInterruptHasNoTerminalNotification()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.TimeoutAfterTurnStartedWithoutTerminal);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-appserver-timeout-no-terminal");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-appserver-timeout-no-terminal/dispatch",
            new DispatchRequest("appServer", "Timeout and simulate missing terminal notification.", null, null));

        var timedOut = await WaitForItemAsync(client, "task:test-appserver-timeout-no-terminal", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(timedOut.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.TimedOut, run.Status);
        Assert.Equal("appServerTerminationUnconfirmed", run.ErrorCode);
        Assert.Contains("no terminal notification", run.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fakeAppServer.InterruptedTurns, x => x.ThreadId == "thread-test-1" && x.TurnId == "turn-test-1");
        Assert.Single(fakeAppServer.TurnPrompts);
    }

    [Fact]
    public async Task AppServerDispatch_MissingBaseWorkspaceFailsWithoutStartingThread()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        var fakeProcess = new FakeDotCraftProcessManager();
        var fakeWorktree = new FakeWorktreeManager
        {
            Failure = new WorktreeException("baseWorkspaceMissing", "Base workspace is missing.")
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager>(fakeProcess);
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            services.AddSingleton<IWorktreeManager>(fakeWorktree);
        });
        var client = app.CreateClient();

        await CreateItemAsync(client, "task:test-missing-base-workspace");
        await PostAsync<ItemDetailResponse>(
            client,
            "/api/v1/items/local/task:test-missing-base-workspace/dispatch",
            new DispatchRequest("appServer", "Try AppServer with no configured checkout.", null, null));

        var failed = await WaitForItemAsync(client, "task:test-missing-base-workspace", x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("baseWorkspaceMissing", run.ErrorCode);
        Assert.Equal(WorktreeStatus.Failed, run.WorktreeStatus);
        Assert.Equal("baseWorkspaceMissing", run.WorktreeErrorCode);
        Assert.Empty(fakeProcess.EnsureWorkspacePaths);
        Assert.Equal(0, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerScheduler_RespectsPerRepositoryConcurrencyLimit()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var first = await CreateLocalTaskAsync(client, "AppServer hold one", repository: "example-owner/oratorio");
        var second = await CreateLocalTaskAsync(client, "AppServer hold two", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{first.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Hold the first AppServer run.", null, null));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{second.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "This should wait for the repository slot.", null, null));

        await WaitUntilAsync(() => fakeAppServer.StartThreadCount == 1);
        await Task.Delay(400);

        var firstDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{first.Item.ItemId}", JsonOptions);
        var secondDetail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{second.Item.ItemId}", JsonOptions);
        var activeRuns = firstDetail!.Runs.Concat(secondDetail!.Runs)
            .Where(x => x.RunnerKind == "appServer" && x.Status is RunStatus.Dispatching or RunStatus.Running)
            .ToList();
        var queuedRuns = firstDetail.Runs.Concat(secondDetail.Runs)
            .Where(x => x.RunnerKind == "appServer" && x.Status == RunStatus.Queued)
            .ToList();
        Assert.Single(activeRuns);
        Assert.Single(queuedRuns);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerRedispatch_ReusesThreadAndRebindsTools()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
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
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/request-changes",
            new DecisionRequest("Please address the refresh-token race before another review."));

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Follow up on requested changes.", null, null));

        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        var resume = Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Equal("thread-test-1", resume.ThreadId);
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitReviewDraft"));
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitFollowUpDraft"));
        Assert.NotNull(resume.RuntimeAdditionalContext);

        var secondRun = Assert.Single(
            (await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions))!.Runs,
            x => x.RunnerKind == "appServer" && x.Attempt == 1 && x.RoundId != null && x.ThreadId == "thread-test-1" && x.Status == RunStatus.Succeeded && x.Summary?.Contains("DotCraft analysis complete") == true && x.TurnId == "turn-test-2");
        Assert.Equal("thread-test-1", secondRun.ThreadId);
        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.Contains(detail!.Timeline, x => x.Title == "DotCraft thread reused" && x.Body?.Contains("thread-test-1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PullRequestReReview_QueuesNewRoundWithoutGitHubWrite_AndUsesLatestHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
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
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var queued = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/rereview", new { });
        Assert.Equal(ItemState.Dispatching, queued.Item.State);
        Assert.Equal(2, queued.Item.CurrentRound);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 2 && x.Status == RoundStatus.Running);
        Assert.Contains(queued.Decisions, x => x.Decision == DecisionType.ReReview && x.Body?.Contains("abc123 to def456", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(queued.SourceWrites, x => x.DecisionId is not null);
        Assert.Contains(queued.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "def456");
        Assert.Empty(queued.DiscussionTurns);

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        Assert.Contains(reviewed.Runs, x => x.Status == RunStatus.Succeeded && x.BaseSha == "def456");
    }

    [Fact]
    public async Task PullRequestReReview_ApprovedPullRequest_CreatesNewRoundWithoutExtraGitHubWrite()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
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
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1 && x.ReviewDrafts[0].Status == ReviewDraftStatus.Published);

        var approved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/approve",
            new DecisionRequest("Looks good."));
        Assert.Equal(ItemState.Approved, approved.Item.State);
        Assert.Equal(5, approved.SourceWrites.Count);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "fed789", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var queued = await PostAsync<ItemDetailResponse>(client, $"/api/v1/items/id/{pr.ItemId}/rereview", new { });
        Assert.Equal(ItemState.Dispatching, queued.Item.State);
        Assert.Equal(2, queued.Item.CurrentRound);
        Assert.Contains(queued.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Equal(6, queued.SourceWrites.Count);
        Assert.Contains(queued.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "fed789");
        Assert.Equal(2, fakeGitHub.PullRequestReviews.Count);
        Assert.Equal(2, fakeGitHub.CheckRuns.Count);
    }

    [Fact]
    public async Task PullRequestReReview_RejectsWhenHeadHasNotChanged()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
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
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        var response = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("noNewPullRequestHead", error?.Error.Code);
    }

    [Fact]
    public async Task PullRequestReReview_RequiresGitHubPullRequestCompletedReviewRun_AndInactiveItem()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
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
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);
        var pr = Assert.Single(list.Items, x => x.Kind == ItemKind.PullRequest);

        var issueResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{issue.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, issueResponse.StatusCode);
        var issueError = await issueResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("invalidReReviewTarget", issueError?.Error.Code);

        var missingRunResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, missingRunResponse.StatusCode);
        var missingRunError = await missingRunResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("reviewRunRequired", missingRunError?.Error.Code);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Keep active.", null, null));
        await WaitUntilAsync(() => fakeAppServer.StartThreadCount == 1);

        var activeResponse = await client.PostAsJsonAsync($"/api/v1/items/id/{pr.ItemId}/rereview", new { }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, activeResponse.StatusCode);
        var activeError = await activeResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("activeRunExists", activeError?.Error.Code);
    }

    [Fact]
    public async Task AppServerDispatch_FailsWhenRuntimeAdditionalContextUnsupported()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft)
        {
            SupportsRuntimeAdditionalContext = false
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
            new DispatchRequest("appServer", "Runtime context is required.", null, null));

        var failed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.Failed);
        var run = Assert.Single(failed.Runs, x => x.RunnerKind == "appServer");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("runtimeAdditionalContextUnsupported", run.ErrorCode);
        Assert.Equal(0, fakeAppServer.StartThreadCount);
    }

    [Fact]
    public async Task AppServerDispatch_DoesNotReuseIncompatibleThreadContext()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
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
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        var first = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        var firstRun = Assert.Single(first.Runs, x => x.RunnerKind == "appServer");
        await MarkRunAsIncompatibleThreadContextAsync(app, firstRun.RunId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/request-changes",
            new DecisionRequest("Run another review from incompatible context."));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Follow up from incompatible context.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        var secondRun = reviewed.Runs.Single(x => x.RunnerKind == "appServer" && x.RoundId != firstRun.RoundId);
        Assert.Equal("thread-test-2", secondRun.ThreadId);
        Assert.Contains(reviewed.Timeline, x => x.Title == "DotCraft thread created" && x.Body?.Contains("No compatible compact AppServer thread", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AgentDiscussionTurn_RecordsReplyWithoutChangingTaskLifecycle()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Discuss a completed run", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));

        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);
        var baseRun = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");
        var originalState = reviewed.Item.State;
        var originalCurrentRunId = reviewed.Item.CurrentRunId;
        var originalCheckState = reviewed.Item.CheckState;

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        var asked = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Why did you call out the refresh-token path?", reviewed.Item.CurrentRound, null));

        Assert.Contains(asked.Comments, x => x.Purpose == CommentPurpose.DiscussionQuestion && x.Body == "Why did you call out the refresh-token path?");
        Assert.Single(asked.DiscussionTurns);

        var answered = await WaitForItemByIdAsync(
            client,
            task.Item.ItemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded)
                && x.Comments.Any(comment => comment.Purpose == CommentPurpose.DiscussionReply));
        var turn = Assert.Single(answered.DiscussionTurns);

        Assert.Equal(DiscussionTurnStatus.Succeeded, turn.Status);
        Assert.Null(turn.RunId);
        Assert.Equal(baseRun.ThreadId, turn.ThreadId);
        Assert.Equal("turn-test-2", turn.TurnId);
        Assert.NotNull(turn.ReplyCommentId);
        Assert.Equal(originalState, answered.Item.State);
        Assert.Equal(originalCurrentRunId, answered.Item.CurrentRunId);
        Assert.Equal(originalCheckState, answered.Item.CheckState);
        Assert.Contains(answered.Comments, x => x.Purpose == CommentPurpose.DiscussionReply && x.AuthorKind == AuthorKind.Agent && x.Body == "Agent answer from DotCraft.");
        Assert.Contains(answered.Timeline, x => x.Title == "Agent answered discussion question");
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-1"], fakeAppServer.TurnThreadIds);
        var resume = Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Equal("thread-test-1", resume.ThreadId);
        Assert.True((resume.DynamicTools ?? []).ContainsTool("oratorio_run", "SubmitDiscussionReply"));
        Assert.NotNull(resume.RuntimeAdditionalContext);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.True(fakeAppServer.LastToolResult!.Success);
    }

    [Fact]
    public async Task AgentDiscussionTurn_RequiresCompatibleCompletedThread()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var task = await CreateLocalTaskAsync(client, "Ask too early", repository: "example-owner/oratorio");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Can you answer without a base thread?", null, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.Equal("noCompatibleDiscussionThread", error?.Error.Code);
    }

    [Fact]
    public async Task AgentDiscussionTurn_SteersSecondQuestionIntoRunningDiscussionTurn()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Questions share a running turn", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);

        fakeAppServer.Outcome = FakeAppServerOutcome.ReplyOnSteer;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("First question?", reviewed.Item.CurrentRound, null));
        var first = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Running && turn.TurnId != null));
        var firstTurn = Assert.Single(first.DiscussionTurns);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Second question?", reviewed.Item.CurrentRound, null));
        var answered = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Succeeded));

        var steer = Assert.Single(fakeAppServer.SteerRequests);
        Assert.Equal(firstTurn.TurnId, steer.ExpectedTurnId);
        var second = Assert.Single(answered.DiscussionTurns, turn => turn.DiscussionTurnId != firstTurn.DiscussionTurnId);
        Assert.Equal(DiscussionTurnStatus.Succeeded, second.Status);
        Assert.Equal(firstTurn.TurnId, second.TurnId);
        Assert.Single(fakeAppServer.ThreadResumeRequests);
    }

    [Fact]
    public async Task AgentDiscussionTurn_MismatchedToolBindingFailsStably()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success)
        {
            UseMismatchedToolThreadId = true
        };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });
        var client = app.CreateClient();

        var task = await CreateLocalTaskAsync(client, "Reject mismatched discussion reply", repository: "example-owner/oratorio");
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial DotCraft analysis.", null, null));
        var reviewed = await WaitForItemByIdAsync(client, task.Item.ItemId, x => x.Item.State == ItemState.AwaitingReview);

        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{task.Item.ItemId}/discussion-turns",
            new DiscussionTurnRequest("Please answer from the wrong thread.", reviewed.Item.CurrentRound, null));

        var failed = await WaitForItemByIdAsync(
            client,
            task.Item.ItemId,
            x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Failed));
        var turn = Assert.Single(failed.DiscussionTurns);

        Assert.Equal(DiscussionTurnStatus.Failed, turn.Status);
        Assert.Null(turn.ReplyCommentId);
        Assert.Equal("discussionReplyMissing", turn.ErrorCode);
        Assert.NotNull(fakeAppServer.LastToolResult);
        Assert.False(fakeAppServer.LastToolResult!.Success);
        Assert.Equal("InvalidDiscussionTurnBinding", fakeAppServer.LastToolResult.ErrorCode);
    }

    [Fact]
    public async Task PullRequestReviewRun_RetriesFailedTurn_AndReusesFailedThread()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Fail);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2",
                ["Oratorio:DotCraft:RetryBackoffSeconds"] = "1",
                ["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "1"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Retry a missing review draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1 && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        var runs = reviewed.Runs.Where(x => x.RunnerKind == "appServer").OrderBy(x => x.Attempt).ToArray();

        Assert.Equal(RunStatus.Failed, runs[0].Status);
        Assert.Equal("appServerTurnFailed", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Equal(1, runs[1].RetryCount);
        Assert.Equal("thread-test-1", runs[0].ThreadId);
        Assert.Equal("thread-test-1", runs[1].ThreadId);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Single(fakeAppServer.ThreadResumeRequests);
        Assert.Contains(reviewed.Timeline, x => x.Title == "Retry scheduled");
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
    }

    [Fact]
    public async Task PullRequestReviewRun_UsesPersistedDraftWhenAppServerDisconnectsAfterSubmission()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraftThenDisconnect);
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
            new DispatchRequest("appServer", "Keep the submitted draft if the turn fails.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Null(run.ErrorCode);
        Assert.Equal("No issues found.", run.Summary);
        Assert.Single(reviewed.ReviewDrafts);
        Assert.Equal(1, fakeAppServer.StartThreadCount);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
        Assert.Contains(reviewed.Timeline, x =>
            x.Title == "Review draft retained after AppServer termination" &&
            x.Body!.Contains("disconnected before the run completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PullRequestReviewRun_StartsFreshThreadWhenFailedThreadResumeIsRejected()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft)
        {
            FailThreadResume = true
        };
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Fall back when thread resume is rejected.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);

        Assert.Equal(1, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Empty(fakeAppServer.ThreadResumeRequests);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);
        Assert.Contains(reviewed.Timeline, x =>
            x.Title == "DotCraft thread created" &&
            x.Body!.Contains("could not be resumed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PullRequestReviewRun_StartsFreshThreadAfterInfrastructureFailure()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Disconnect);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = new TestOratorioApp(
            services =>
            {
                services.RemoveAll<IGitHubApiClient>();
                services.AddSingleton<IGitHubApiClient>(fakeGitHub);
                services.RemoveAll<IDotCraftAppServerProcessManager>();
                services.RemoveAll<IDotCraftAppServerClientFactory>();
                services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
                services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Use a fresh thread after infrastructure failure.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        var runs = reviewed.Runs.Where(x => x.RunnerKind == "appServer").OrderBy(x => x.Attempt).ToArray();

        Assert.Equal("appServerDisconnected", runs[0].ErrorCode);
        Assert.Equal(RunStatus.Succeeded, runs[1].Status);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(0, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);
    }

    [Fact]
    public async Task PullRequestReviewRun_AllowsSummaryOnlyReviewDraft()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
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
            new DispatchRequest("appServer", "Submit a clean structured draft.", null, null));

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.ReviewDrafts.Count == 1 && x.ReviewDrafts[0].Status == ReviewDraftStatus.Published);
        var draft = Assert.Single(reviewed.ReviewDrafts);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(0, draft.MajorCount);
        Assert.Equal(0, draft.MinorCount);
        Assert.Equal(0, draft.SuggestionCount);
        Assert.Equal(0, draft.AcceptedCount);
        Assert.Empty(draft.Comments);
        Assert.Equal("No issues found.", draft.SummaryBody);
        var check = Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("completed", check.Status);
        Assert.Equal("neutral", check.Conclusion);
        Assert.Equal(1, check.UpdateCount);
        Assert.Contains(reviewed.SourceWrites, write =>
            write.Kind == SourceWriteKind.CheckRun &&
            write.Intent == "reviewGateComplete" &&
            write.Status == SourceWriteStatus.Succeeded);
        var review = Assert.Single(fakeGitHub.PullRequestReviews);
        Assert.Equal("COMMENT", review.Event);
        Assert.Equal("No issues found.", review.Body);
        Assert.Empty(review.Comments);
    }
}
