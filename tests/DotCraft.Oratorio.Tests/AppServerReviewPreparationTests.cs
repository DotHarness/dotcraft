using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DotCraft.Oratorio.Tests;

public sealed class AppServerReviewPreparationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_UsesFrozenRunHead_AndRejectsMissingTarget(bool missingTarget)
    {
        var github = new FakeGitHubApiClient();
        var worktrees = new FakeWorktreeManager();
        var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.Hold);
        await using var app = CreateApp(github, appServer, worktrees);
        var client = app.CreateClient();
        var itemId = await ImportPullRequestAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var service = scope.ServiceProvider.GetRequiredService<OratorioService>();
            await service.DispatchByIdAsync(itemId,
                new DispatchRequest("appServer", "Review the queued commit.", null, null), CancellationToken.None);
            var pull = github.PullRequests[0];
            github.PullRequests[0] = pull with
            {
                UpdatedAt = pull.UpdatedAt.AddMinutes(1),
                Head = pull.Head with { Sha = "def456" },
                Base = pull.Base with { Sha = "new-base456" }
            };
            await scope.ServiceProvider.GetRequiredService<GitHubSourceService>().SyncAsync(CancellationToken.None);
            if (missingTarget)
                (await db.Runs.SingleAsync(x => x.ItemId == itemId)).TargetHeadSha = null;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var detail = await WaitForItemAsync(client, itemId,
            x => x.Item.State == (missingTarget ? ItemState.Failed : ItemState.Running));
        var run = Assert.Single(detail.Runs);
        if (missingTarget)
        {
            Assert.Equal("reviewTargetHeadUnresolved", run.ErrorCode);
            Assert.Equal(WorktreeStatus.Failed, run.WorktreeStatus);
            Assert.Empty(worktrees.PrepareRequests);
            Assert.Equal(0, appServer.StartThreadCount);
        }
        else
        {
            Assert.Equal("abc123", run.TargetHeadSha);
            Assert.Equal("abc123", run.BaseSha);
            Assert.Equal("abc123", Assert.Single(worktrees.PrepareRequests).HeadSha);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            Assert.Equal("def456", (await db.Items.SingleAsync(x => x.ItemId == itemId)).HeadSha);
            var storedRun = await db.Runs.SingleAsync(x => x.RunId == run.RunId);
            var incremental = await scope.ServiceProvider.GetRequiredService<AppServerPromptBuilder>()
                .BuildAsync(storedRun, null, run.WorktreePath!, [], run.ThreadId, CancellationToken.None);
            foreach (var (prompt, contextJson) in new[]
                     {
                         (Assert.Single(appServer.TurnPrompts), storedRun.PromptContextJson!),
                         (incremental.Prompt, incremental.ContextJson)
                     })
            {
                Assert.Contains("base123...abc123", prompt);
                Assert.DoesNotContain("def456", prompt);
                Assert.DoesNotContain("new-base456", prompt);
                using var context = JsonDocument.Parse(contextJson);
                foreach (var section in new[] { "item", "workspace", "sourceSnapshot" })
                    Assert.Equal("abc123", context.RootElement.GetProperty(section).GetProperty("headSha").GetString());
                Assert.Equal("abc123", context.RootElement.GetProperty("currentRound").GetProperty("targetHeadSha").GetString());
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchFailure_RetriesWithinRound_AndStopsAtAttemptLimit(bool recover)
    {
        var github = new FakeGitHubApiClient();
        var worktrees = new FakeWorktreeManager
        {
            Failure = new WorktreeException("reviewTargetFetchFailed", "Target commit is temporarily unavailable.")
        };
        var appServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var app = CreateApp(github, appServer, worktrees, clock);
        var client = app.CreateClient();

        await ScanAutoReviewsAsync(app);
        var itemId = await ImportPullRequestAsync(app);
        await ScanAutoReviewsAsync(app);

        var pending = await WaitForItemAsync(client, itemId,
            x => x.Runs.Count == 2 && x.Runs.Any(run => run.Attempt == 2 && run.Status == RunStatus.Queued));
        Assert.Equal("reviewTargetFetchFailed", pending.Runs.Single(x => x.Attempt == 1).ErrorCode);
        Assert.Equal(RoundStatus.Running, Assert.Single(pending.Rounds).Status);
        Assert.Equal("in_progress", Assert.Single(github.CheckRuns).Status);
        Assert.DoesNotContain(github.CheckRuns, x => x.Conclusion == "failure");
        Assert.Equal(0, appServer.StartThreadCount);

        if (recover)
            worktrees.Failure = null;
        clock.Advance(TimeSpan.FromSeconds(11));

        var completed = await WaitForItemAsync(client, itemId,
            x => x.Item.State == (recover ? ItemState.AwaitingReview : ItemState.Failed));
        Assert.Single(completed.Rounds);
        Assert.Equal(2, completed.Runs.Count);
        Assert.All(completed.Runs, run => Assert.Equal("abc123", run.TargetHeadSha));
        Assert.Equal(recover ? RunStatus.Succeeded : RunStatus.Failed,
            completed.Runs.Single(x => x.Attempt == 2).Status);
        Assert.Equal(2, worktrees.PrepareRequests.Count);
        Assert.Equal(recover ? 1 : 0, appServer.StartThreadCount);
        var check = Assert.Single(github.CheckRuns);
        if (recover)
            Assert.NotEqual("failure", check.Conclusion);
        else
            Assert.Equal("failure", check.Conclusion);

        await ScanAutoReviewsAsync(app);
        await ScanAutoReviewsAsync(app);
        using var verifyScope = app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        Assert.Equal(2, await db.Runs.CountAsync(x => x.ItemId == itemId));
    }

    private static TestOratorioApp CreateApp(
        FakeGitHubApiClient github,
        FakeAppServerClientFactory appServer,
        FakeWorktreeManager worktrees,
        MutableClock? clock = null) => new(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService<AppServerRunWorker>();
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(github);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(appServer);
            services.RemoveAll<IWorktreeManager>();
            services.AddSingleton<IWorktreeManager>(worktrees);
            if (clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            }
        }, new Dictionary<string, string?>
        {
            ["Oratorio:Automation:AutoReviewRepositories:0"] = "example-owner/oratorio",
            ["Oratorio:DotCraft:MaxRunAttempts"] = "2",
            ["Oratorio:DotCraft:RetryBackoffSeconds"] = "10",
            ["Oratorio:DotCraft:MaxRetryBackoffSeconds"] = "10"
        });

    private static async Task<string> ImportPullRequestAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GitHubSourceService>().SyncAsync(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return (await db.Items.SingleAsync(x => x.Kind == ItemKind.PullRequest)).ItemId;
    }

    private static async Task ScanAutoReviewsAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AutoReviewDispatchService>().DispatchEligibleAsync(CancellationToken.None);
    }

    private static async Task<ItemDetailResponse> WaitForItemAsync(
        HttpClient client, string itemId, Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions);
            if (latest is not null && predicate(latest))
                return latest;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Timed out waiting for {itemId}. Latest state: {latest?.Item.State}");
    }
}
