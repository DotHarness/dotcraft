using System.Net.Http.Json;
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

public sealed class AutoReviewApiTests
{
    [Theory]
    [InlineData("example-owner/oratorio", "example-owner/oratorio")]
    [InlineData("github:github.com/example-owner/oratorio", "github:github.com/example-owner/oratorio")]
    public async Task AutoReview_BaselinesExistingPullRequestsWhenRepositoryIsEnabled(string configuredRepository, string stateRepository)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(new OratorioAutomationOptions());
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);

        automation.Set(new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await DispatchAutoReviewAsync(app);

        var detail = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);
        Assert.NotNull(detail);
        Assert.Empty(detail!.Runs);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var itemState = await db.AutoReviewItemStates.AsNoTracking().SingleAsync(x => x.ItemId == pr.ItemId);
        Assert.Equal(stateRepository, itemState.Repository);
        Assert.Equal("abc123", itemState.LastObservedHeadSha);
        Assert.Equal("abc123", itemState.LastQueuedHeadSha);
    }

    [Fact]
    public async Task AutoReview_QueuesNewPullRequestsAfterRepositoryBaseline()
    {
        const string configuredRepository = "github:github.com/example-owner/oratorio";
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await using var app = new TestOratorioApp(services =>
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
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.ReviewDrafts.Count == 1);
        var run = Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer");

        Assert.Equal(RunDispatchTrigger.AutoReview, run.DispatchTrigger);
        Assert.Equal("abc123", run.TargetHeadSha);
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "abc123");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateComplete" && x.HeadSha == "abc123");
        Assert.DoesNotContain(reviewed.SourceWrites, x => x.DecisionId is not null);
    }

    [Fact]
    public async Task AutoReview_QueuesNewPullRequestBehindManyMergedPullRequests()
    {
        const string configuredRepository = "github:github.com/example-owner/oratorio";
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
        await using var app = new TestOratorioApp(services =>
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
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var mergedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            for (var number = 1; number <= 201; number++)
            {
                db.Items.Add(new OratorioItem
                {
                    Source = "github",
                    ExternalId = $"example-owner/oratorio#{number}",
                    Kind = ItemKind.PullRequest,
                    Title = $"Merged change {number}",
                    Repository = "example-owner/oratorio",
                    HeadSha = $"merged{number}",
                    SourceState = SourceState.Merged,
                    State = ItemState.Archived,
                    CreatedAt = mergedAt,
                    UpdatedAt = mergedAt.AddMinutes(number)
                });
            }

            await db.SaveChangesAsync();
        }

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        string prItemId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            prItemId = await db.Items.Where(x => x.Kind == ItemKind.PullRequest && x.SourceState == SourceState.Open).Select(x => x.ItemId).SingleAsync();
        }

        await DispatchAutoReviewAsync(app);

        var reviewed = await WaitForItemByIdAsync(client, prItemId, x => x.Runs.Any(run => run.RunnerKind == "appServer"));
        Assert.Equal(RunDispatchTrigger.AutoReview, Assert.Single(reviewed.Runs, x => x.RunnerKind == "appServer").DispatchTrigger);
    }

    [Fact]
    public async Task AutoReview_StopsAfterReviewRetryAttemptsAreExhaustedForSameHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Success);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = ["example-owner/oratorio"] });
        await using var app = new TestOratorioApp(
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
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2"
            });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);

        var failed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.Failed && x.Runs.Count(run => run.RunnerKind == "appServer") == 2);
        Assert.All(failed.Runs.Where(x => x.RunnerKind == "appServer"), x => Assert.Equal("reviewDraftRequired", x.ErrorCode));

        await DispatchAutoReviewAsync(app);
        await Task.Delay(1200);
        await DispatchAutoReviewAsync(app);
        var afterRescan = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{pr.ItemId}", JsonOptions);

        Assert.Equal(ItemState.Failed, afterRescan!.Item.State);
        Assert.Equal(2, afterRescan.Runs.Count(x => x.RunnerKind == "appServer"));
        Assert.Single(fakeGitHub.CheckRuns);
        Assert.Equal("failure", fakeGitHub.CheckRuns[0].Conclusion);
    }

    [Fact]
    public async Task AutoReview_SupersedesQueuedRetryWhenHeadChanges()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.Success);
        fakeAppServer.ScriptedOutcomes.Enqueue(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = ["example-owner/oratorio"] });
        await using var app = new TestOratorioApp(
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
            },
            new Dictionary<string, string?>
            {
                ["Oratorio:DotCraft:MaxRunAttempts"] = "2",
                ["Oratorio:DotCraft:RetryBackoffSeconds"] = "2",
                ["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "2"
            });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);
        await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Runs.Any(run => run.Attempt == 2 && run.Status == RunStatus.Queued));

        MoveDefaultPullRequestHead(fakeGitHub, "def456", DateTimeOffset.UtcNow.AddMinutes(1));
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2 && x.ReviewDrafts.Count == 1);
        var oldRetry = Assert.Single(reviewed.Runs, x => x.RoundId == reviewed.Rounds.Single(r => r.RoundNumber == 1).RoundId && x.Attempt == 2);
        var newHeadRun = Assert.Single(reviewed.Runs, x => x.RoundId == reviewed.Rounds.Single(r => r.RoundNumber == 2).RoundId);

        Assert.Equal(RunStatus.Cancelled, oldRetry.Status);
        Assert.Equal("reviewRetrySuperseded", oldRetry.ErrorCode);
        Assert.Equal(RoundStatus.Superseded, reviewed.Rounds.Single(x => x.RoundNumber == 1).Status);
        Assert.Equal(1, newHeadRun.Attempt);
        Assert.Equal("def456", newHeadRun.TargetHeadSha);
        Assert.Equal(RunStatus.Succeeded, newHeadRun.Status);
        Assert.Equal(2, fakeAppServer.StartThreadCount);
        Assert.Equal(0, fakeAppServer.ThreadResumeAttemptCount);
        Assert.Contains(reviewed.SourceWrites, x => x.Intent == "reviewGateRunCancelled" && x.HeadSha == "abc123");
        Assert.Contains(reviewed.SourceWrites, x => x.Intent == "reviewGateComplete" && x.HeadSha == "def456");
    }

    [Fact]
    public async Task AutoReview_ReReviewsWhenPullRequestHeadChanges()
    {
        const string configuredRepository = "example-owner/oratorio";
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        var automation = new MutableOptionsMonitor<OratorioAutomationOptions>(
            new OratorioAutomationOptions { AutoReviewRepositories = [configuredRepository] });
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
            services.RemoveAll<IOptionsMonitor<OratorioAutomationOptions>>();
            services.AddSingleton<IOptionsMonitor<OratorioAutomationOptions>>(automation);
        });
        var client = app.CreateClient();

        await DispatchAutoReviewAsync(app);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await DispatchAutoReviewAsync(app);
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        await DispatchAutoReviewAsync(app);

        var reviewed = await WaitForItemByIdAsync(
            client,
            pr.ItemId!,
            x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);
        var run = reviewed.Runs
            .Where(x => x.RunnerKind == "appServer")
            .OrderByDescending(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
            .First();

        Assert.Equal(RunDispatchTrigger.AutoReview, run.DispatchTrigger);
        Assert.Equal("def456", run.TargetHeadSha);
        Assert.Contains(reviewed.Rounds, x => x.RoundNumber == 1 && x.Status == RoundStatus.Superseded);
        Assert.Contains(reviewed.Decisions, x => x.Decision == DecisionType.ReReview && x.AuthorName == "oratorio/auto-review");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateStart" && x.HeadSha == "def456");
        Assert.Contains(reviewed.SourceWrites, x => x.Kind == SourceWriteKind.CheckRun && x.Intent == "reviewGateComplete" && x.HeadSha == "def456");
        Assert.DoesNotContain(reviewed.SourceWrites, x => x.DecisionId is not null);
    }
}
