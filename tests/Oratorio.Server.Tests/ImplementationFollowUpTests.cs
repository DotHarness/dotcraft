using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.DotCraft;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.Services;

namespace Oratorio.Server.Tests;

public sealed class ImplementationFollowUpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string Repository = "example-owner/oratorio";

    [Fact]
    public async Task FollowUp_ReactivatesOriginatingItem_WhenGeneratedPrHasOpenFinding()
    {
        await using var app = CreateApp(followUpEnabled: true);
        var (issueId, _) = await SeedDeliveredIssueWithFindingAsync(app);

        await DispatchFollowUpAsync(app);

        var run = await WaitForFollowUpRunAsync(app, issueId);
        Assert.Equal(RunPurpose.Implementation, run.Purpose);
        Assert.Equal(DeliveryPolicy.AutoPr, run.DeliveryPolicy);

        var state = await GetFollowUpStateAsync(app, issueId);
        Assert.NotNull(state);
        Assert.Equal(1, state!.FollowUpRoundCount);
        Assert.False(string.IsNullOrEmpty(state.LastObservedFindingsKey));
    }

    [Fact]
    public async Task FollowUp_DoesNotReactivate_WhenRepositoryNotAllowlisted()
    {
        await using var app = CreateApp(followUpEnabled: false);
        var (issueId, _) = await SeedDeliveredIssueWithFindingAsync(app);

        await DispatchFollowUpAsync(app);

        Assert.False(await HasFollowUpRunAsync(app, issueId));
        Assert.Null(await GetFollowUpStateAsync(app, issueId));
    }

    [Fact]
    public async Task FollowUp_DoesNotReactivate_WhenFindingsAlreadyResolved()
    {
        await using var app = CreateApp(followUpEnabled: true);
        var (issueId, _) = await SeedDeliveredIssueWithFindingAsync(app, resolution: ReviewFindingResolutionState.Resolved);

        await DispatchFollowUpAsync(app);

        Assert.False(await HasFollowUpRunAsync(app, issueId));
    }

    [Fact]
    public async Task FollowUp_StopsAndRecordsCap_WhenRoundLimitReached()
    {
        await using var app = CreateApp(followUpEnabled: true, maxFollowUpRounds: 1);
        var (issueId, _) = await SeedDeliveredIssueWithFindingAsync(app, followUpRoundCount: 1);

        await DispatchFollowUpAsync(app);

        Assert.False(await HasFollowUpRunAsync(app, issueId));
        var state = await GetFollowUpStateAsync(app, issueId);
        Assert.NotNull(state);
        Assert.Equal("followUpCapReached", state!.LastErrorCode);
        Assert.Equal(1, state.FollowUpRoundCount);
    }

    [Fact]
    public async Task FollowUp_DeliversToExistingPullRequest_WithoutCreatingASecondOne()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var fakeGit = new FakeGitDeliveryClient();
        await using var app = CreateApp(followUpEnabled: true, gitHub: fakeGitHub, appServer: fakeAppServer, git: fakeGit);
        var client = app.CreateClient();

        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var issue = Assert.Single(list!.Items, x => x.Kind == ItemKind.Issue);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{issue.ItemId}/dispatch",
            new DispatchRequest("appServer", "Implement the synced issue.", null, null, "implementation", DeliveryPolicy.AutoPr));
        var delivered = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 1);
        var generatedPrItemId = Assert.Single(delivered.ImplementationDrafts).PullRequestItemId;
        Assert.NotNull(generatedPrItemId);
        Assert.Single(fakeGitHub.PullRequestCreates);

        await SeedPublishedFindingAsync(app, generatedPrItemId!);

        await DispatchFollowUpAsync(app);

        var followedUp = await WaitForItemByIdAsync(client, issue.ItemId!, x => x.Item.State == ItemState.AwaitingReview && x.ImplementationDrafts.Count == 2);
        Assert.All(followedUp.ImplementationDrafts, draft => Assert.Equal(ImplementationDraftStatus.Delivered, draft.Status));
        Assert.Single(fakeGitHub.PullRequestCreates); // no second pull request opened
        Assert.True(fakeGit.CommitMessages.Count >= 2);
        Assert.Contains(followedUp.SourceWrites, write => write.Kind == SourceWriteKind.PullRequestUpdate && write.Intent == "implementationBranchUpdate" && write.Status == SourceWriteStatus.Succeeded);

        // The follow-up round must base its worktree on the existing PR branch head (B6),
        // not reset to the repository base, so prior delivered commits are retained.
        var worktree = (FakeWorktreeManager)app.Services.GetRequiredService<IWorktreeManager>();
        Assert.Contains(worktree.PrepareRequests, request => !string.IsNullOrWhiteSpace(request.StackOntoBranch));
    }

    [Fact]
    public async Task FollowUp_PromptIncludesGeneratedPrFindingsAndHumanComments()
    {
        await using var app = CreateApp(followUpEnabled: true);
        var seedNow = DateTimeOffset.UtcNow;
        var (issueId, _) = await SeedDeliveredIssueWithFindingAsync(
            app,
            suggestionReplacement: "return token ?? throw new InvalidOperationException();",
            humanCommentAt: seedNow.AddMinutes(5));

        var runId = await SeedFollowUpRunAsync(app, issueId);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var builder = scope.ServiceProvider.GetRequiredService<AppServerPromptBuilder>();
        var run = await db.Runs.FirstAsync(x => x.RunId == runId);
        var prompt = await builder.BuildAsync(run, "Address the review feedback.", "/workspace/sample", ["oratorio_run.SubmitImplementationDraft"], incremental: false, CancellationToken.None);

        Assert.Contains("Review feedback on the generated pull request", prompt.Prompt);
        Assert.Contains("Guard against null token", prompt.Prompt);
        Assert.Contains("return token ?? throw new InvalidOperationException();", prompt.Prompt);
        Assert.Contains("Please also handle timeouts.", prompt.Prompt);
    }

    private static TestOratorioApp CreateApp(
        bool followUpEnabled,
        int? maxFollowUpRounds = null,
        FakeGitHubApiClient? gitHub = null,
        FakeAppServerClientFactory? appServer = null,
        FakeGitDeliveryClient? git = null)
    {
        var resolvedGitHub = gitHub ?? new FakeGitHubApiClient();
        var resolvedAppServer = appServer ?? new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitImplementationDraft);
        var resolvedGit = git ?? new FakeGitDeliveryClient();
        var settings = new Dictionary<string, string?>();
        if (followUpEnabled)
        {
            settings["Oratorio:Automation:AutoFollowUpEnabled"] = "true";
            settings["Oratorio:Automation:AutoFollowUpRepositories:0"] = Repository;
        }

        if (maxFollowUpRounds is not null)
        {
            settings["Oratorio:Automation:MaxFollowUpRounds"] = maxFollowUpRounds.Value.ToString();
        }

        return new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(resolvedGitHub);
            services.RemoveAll<IGitDeliveryClient>();
            services.AddSingleton<IGitDeliveryClient>(resolvedGit);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(resolvedAppServer);
        }, settings);
    }

    private static async Task DispatchFollowUpAsync(TestOratorioApp app)
    {
        using var scope = app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ImplementationFollowUpDispatchService>();
        await dispatcher.DispatchEligibleAsync(CancellationToken.None);
    }

    private static async Task<(string IssueId, string PrId)> SeedDeliveredIssueWithFindingAsync(
        TestOratorioApp app,
        ReviewFindingResolutionState resolution = ReviewFindingResolutionState.Open,
        int followUpRoundCount = 0,
        string? suggestionReplacement = null,
        DateTimeOffset? humanCommentAt = null)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var now = DateTimeOffset.UtcNow;

        var issue = new OratorioItem
        {
            Source = "github",
            ExternalId = "issue:example-owner/oratorio#7",
            Kind = ItemKind.Issue,
            Title = "Add retry handling",
            Repository = Repository,
            State = ItemState.AwaitingReview,
            CheckState = CheckState.Attention,
            SourceState = SourceState.Open,
            SourceDetailsStatus = SourceDetailsStatus.Current,
            CurrentRound = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Items.Add(issue);
        var issueRound = new OratorioRound { ItemId = issue.ItemId, RoundNumber = 1, Status = RoundStatus.AwaitingReview, CreatedAt = now, CompletedAt = now };
        db.Rounds.Add(issueRound);
        db.Runs.Add(new OratorioRun
        {
            ItemId = issue.ItemId,
            RoundId = issueRound.RoundId,
            Attempt = 1,
            Status = RunStatus.Succeeded,
            RunnerKind = "appServer",
            Purpose = RunPurpose.Implementation,
            StartedAt = now,
            CompletedAt = now
        });

        var pr = new OratorioItem
        {
            Source = "github",
            ExternalId = "pr:example-owner/oratorio#501",
            Kind = ItemKind.PullRequest,
            Title = "Add retry handling",
            Repository = Repository,
            Branch = "oratorio/run/abc123",
            HeadSha = "head-1",
            ExternalUrl = "https://github.example.test/example-owner/oratorio/pull/501",
            State = ItemState.Discovered,
            CheckState = CheckState.Attention,
            SourceState = SourceState.Open,
            ParentItemId = issue.ItemId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Items.Add(pr);

        await SeedPublishedFindingCoreAsync(db, pr.ItemId, resolution, suggestionReplacement, now);

        if (humanCommentAt is not null)
        {
            db.Comments.Add(new OratorioComment
            {
                ItemId = pr.ItemId,
                AuthorKind = AuthorKind.Source,
                AuthorName = "human-reviewer",
                Body = "Please also handle timeouts.",
                Visibility = CommentVisibility.Source,
                Purpose = CommentPurpose.SourceContext,
                Source = "github",
                SourceCommentId = "review-comment-1",
                ExternalUrl = "https://github.example.test/example-owner/oratorio/pull/501#discussion_r1",
                CreatedAt = humanCommentAt.Value,
                SourceUpdatedAt = humanCommentAt.Value
            });
        }

        if (followUpRoundCount > 0)
        {
            db.ImplementationFollowUpItemStates.Add(new OratorioImplementationFollowUpItemState
            {
                OriginatingItemId = issue.ItemId,
                GeneratedPrItemId = pr.ItemId,
                Repository = Repository,
                FollowUpRoundCount = followUpRoundCount,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return (issue.ItemId, pr.ItemId);
    }

    private static async Task SeedPublishedFindingAsync(TestOratorioApp app, string prItemId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        await SeedPublishedFindingCoreAsync(db, prItemId, ReviewFindingResolutionState.Open, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task SeedPublishedFindingCoreAsync(
        OratorioDbContext db,
        string prItemId,
        ReviewFindingResolutionState resolution,
        string? suggestionReplacement,
        DateTimeOffset now)
    {
        var round = new OratorioRound { ItemId = prItemId, RoundNumber = 1, Status = RoundStatus.AwaitingReview, CreatedAt = now, CompletedAt = now };
        db.Rounds.Add(round);
        var run = new OratorioRun
        {
            ItemId = prItemId,
            RoundId = round.RoundId,
            Attempt = 1,
            Status = RunStatus.Succeeded,
            RunnerKind = "appServer",
            Purpose = RunPurpose.ReviewAnalysis,
            StartedAt = now,
            CompletedAt = now
        };
        db.Runs.Add(run);
        var draft = new OratorioReviewDraft
        {
            ItemId = prItemId,
            RoundId = round.RoundId,
            RunId = run.RunId,
            Status = ReviewDraftStatus.Published,
            SummaryBody = "Found 1 issue.",
            MajorCount = 1,
            MinorCount = 0,
            SuggestionCount = suggestionReplacement is null ? 0 : 1,
            WarningsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
        db.ReviewDrafts.Add(draft);
        db.ReviewDraftComments.Add(new OratorioReviewDraftComment
        {
            DraftId = draft.DraftId,
            Severity = "RED",
            Title = "Guard against null token",
            Body = "The installation token can be null here.",
            Path = "src/Implementation.cs",
            Line = 12,
            Side = "RIGHT",
            SuggestionReplacement = suggestionReplacement,
            CommentOnlyReason = suggestionReplacement is null ? "investigateOnly" : null,
            Status = ReviewDraftCommentStatus.Accepted,
            ResolutionState = resolution
        });
        await Task.CompletedTask;
    }

    private static async Task<string> SeedFollowUpRunAsync(TestOratorioApp app, string issueId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var now = DateTimeOffset.UtcNow;
        var item = await db.Items.FirstAsync(x => x.ItemId == issueId);
        item.CurrentRound = 2;
        var round = new OratorioRound { ItemId = issueId, RoundNumber = 2, Status = RoundStatus.Running, CreatedAt = now };
        db.Rounds.Add(round);
        var run = new OratorioRun
        {
            ItemId = issueId,
            RoundId = round.RoundId,
            Attempt = 1,
            Status = RunStatus.Queued,
            RunnerKind = "appServer",
            Purpose = RunPurpose.Implementation,
            DispatchTrigger = RunDispatchTrigger.AutoFollowUp,
            DeliveryPolicy = DeliveryPolicy.AutoPr,
            StartedAt = now,
            LastHeartbeatAt = now
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run.RunId;
    }

    private static async Task<OratorioImplementationFollowUpItemState?> GetFollowUpStateAsync(TestOratorioApp app, string issueId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return await db.ImplementationFollowUpItemStates.AsNoTracking().FirstOrDefaultAsync(x => x.OriginatingItemId == issueId);
    }

    private static async Task<bool> HasFollowUpRunAsync(TestOratorioApp app, string issueId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return await db.Runs.AsNoTracking().AnyAsync(x => x.ItemId == issueId && x.DispatchTrigger == RunDispatchTrigger.AutoFollowUp);
    }

    private static async Task<OratorioRun> WaitForFollowUpRunAsync(TestOratorioApp app, string issueId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
                var run = await db.Runs.AsNoTracking()
                    .Where(x => x.ItemId == issueId && x.DispatchTrigger == RunDispatchTrigger.AutoFollowUp)
                    .OrderByDescending(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
                    .FirstOrDefaultAsync();
                if (run is not null)
                {
                    return run;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for an implementation follow-up run.");
    }

    private async Task<T> PostAsync<T>(System.Net.Http.HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        Assert.NotNull(value);
        return value!;
    }

    private static async Task<ItemDetailResponse> WaitForItemByIdAsync(
        System.Net.Http.HttpClient client,
        string itemId,
        Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        ItemDetailResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions);
            if (latest is not null && predicate(latest))
            {
                return latest;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Timed out waiting for {itemId}. Latest state: {latest?.Item.State}");
    }
}
