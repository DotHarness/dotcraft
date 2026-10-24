using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using static DotCraft.Oratorio.Tests.OratorioApiTestSupport;

namespace DotCraft.Oratorio.Tests;

public sealed class ThreadStateInputTests
{
    private const string FindingPath = "src/Auth/RefreshToken.cs";

    [Fact]
    public async Task NewThread_RecordsFullState_AndUnchangedStateAddsNothing()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(new FakeGitHubApiClient(), fakeAppServer);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);

        var record = await LoadThreadStateAsync(app, "thread-test-1");
        Assert.Equal(itemId, record.ItemId);
        Assert.Equal("abc123", record.HeadSha);
        Assert.Equal("base123", record.BaseSha);
        Assert.Equal(SourceState.Open, record.SourceState);
        Assert.False(record.IsDraft);

        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 1);

        var discussionInput = fakeAppServer.TurnPrompts[^1];
        Assert.DoesNotContain("abc123", discussionInput);
        Assert.DoesNotContain("base123", discussionInput);
    }

    [Fact]
    public async Task HeadMoved_NextInputNamesOldAndNewHead()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = CreateApp(fakeGitHub, fakeAppServer, clock);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 1);

        var discussionInput = fakeAppServer.TurnPrompts[^1];
        Assert.Contains("abc123", discussionInput);
        Assert.Contains("def456", discussionInput);
        Assert.Equal("def456", (await LoadThreadStateAsync(app, "thread-test-1")).HeadSha);
    }

    [Fact]
    public async Task ResolvedFinding_ReportedNoLongerOpenOnce()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(new FakeGitHubApiClient(), fakeAppServer);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);
        var (draftId, findingId) = await SeedOpenFindingAsync(app, itemId);

        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 1);
        Assert.Contains(findingId, fakeAppServer.TurnPrompts[^1]);
        Assert.Contains(FindingPath, fakeAppServer.TurnPrompts[^1]);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{findingId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null));
        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 2);
        Assert.Contains(findingId, fakeAppServer.TurnPrompts[^1]);
        Assert.DoesNotContain(FindingPath, fakeAppServer.TurnPrompts[^1]);
        Assert.DoesNotContain(findingId, (await LoadThreadStateAsync(app, "thread-test-1")).OpenFindingsJson);

        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 3);
        Assert.DoesNotContain(findingId, fakeAppServer.TurnPrompts[^1]);
    }

    [Fact]
    public async Task RejectedTurnStart_NextInputRepeatsTheChange()
    {
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        await using var app = CreateApp(new FakeGitHubApiClient(), fakeAppServer);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);
        var (_, findingId) = await SeedOpenFindingAsync(app, itemId);

        fakeAppServer.RejectTurnStarts = 1;
        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Is the refresh path safe?", null, null));
        await WaitForItemByIdAsync(client, itemId, x => x.DiscussionTurns.Any(turn => turn.Status == DiscussionTurnStatus.Failed));
        Assert.Contains(findingId, fakeAppServer.TurnPrompts[^1]);
        Assert.DoesNotContain(findingId, (await LoadThreadStateAsync(app, "thread-test-1")).OpenFindingsJson);

        await AskAsync(client, fakeAppServer, itemId, expectedTurns: 1);
        Assert.Contains(findingId, fakeAppServer.TurnPrompts[^1]);
        Assert.Contains(findingId, (await LoadThreadStateAsync(app, "thread-test-1")).OpenFindingsJson);
    }

    [Fact]
    public async Task ReplacedThread_IsToldFullStateWithoutChanges()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        var fakeAppServer = new FakeAppServerClientFactory(FakeAppServerOutcome.SubmitSummaryOnlyReviewDraft);
        var clock = new MutableClock(DateTimeOffset.Parse("2026-05-03T00:00:00Z"));
        await using var app = CreateApp(fakeGitHub, fakeAppServer, clock);
        var client = app.CreateClient();
        var itemId = await ReviewPullRequestAsync(client);

        clock.Advance(TimeSpan.FromMinutes(5));
        MoveDefaultPullRequestHead(fakeGitHub, "def456", clock.UtcNow);
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        fakeAppServer.FailThreadResume = true;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/request-changes",
            new DecisionRequest("Please look again."));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/dispatch",
            new DispatchRequest("appServer", "Review the new head.", null, null));
        await WaitForItemByIdAsync(client, itemId, x => x.Item.State == ItemState.AwaitingReview && x.Item.CurrentRound == 2);

        Assert.Equal(["thread-test-1", "thread-test-2"], fakeAppServer.TurnThreadIds);
        Assert.Contains("def456", fakeAppServer.TurnPrompts[^1]);
        Assert.DoesNotContain("abc123", fakeAppServer.TurnPrompts[^1]);
        Assert.Equal("def456", (await LoadThreadStateAsync(app, "thread-test-2")).HeadSha);
    }

    private static TestOratorioApp CreateApp(FakeGitHubApiClient fakeGitHub, FakeAppServerClientFactory fakeAppServer, IClock? clock = null) =>
        new(services =>
        {
            if (clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(clock);
            }

            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
            services.RemoveAll<IDotCraftAppServerProcessManager>();
            services.RemoveAll<IDotCraftAppServerClientFactory>();
            services.AddSingleton<IDotCraftAppServerProcessManager, FakeDotCraftProcessManager>();
            services.AddSingleton<IDotCraftAppServerClientFactory>(fakeAppServer);
        });

    private static async Task<string> ReviewPullRequestAsync(HttpClient client)
    {
        await PostAsync<GitHubSyncResponse>(client, "/api/v1/sources/github/sync", new { });
        var list = await client.GetFromJsonAsync<ItemListResponse>("/api/v1/items?source=github", JsonOptions);
        var pr = Assert.Single(list!.Items, x => x.Kind == ItemKind.PullRequest);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{pr.ItemId}/dispatch",
            new DispatchRequest("appServer", "Initial read-only DotCraft pass.", null, null));
        await WaitForItemByIdAsync(client, pr.ItemId!, x => x.Item.State == ItemState.AwaitingReview);
        return pr.ItemId!;
    }

    private static async Task AskAsync(HttpClient client, FakeAppServerClientFactory fakeAppServer, string itemId, int expectedTurns)
    {
        fakeAppServer.Outcome = FakeAppServerOutcome.SubmitDiscussionReply;
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/id/{itemId}/discussion-turns",
            new DiscussionTurnRequest("Is the refresh path safe?", null, null));
        await WaitForItemByIdAsync(
            client,
            itemId,
            x => x.DiscussionTurns.Count(turn => turn.Status == DiscussionTurnStatus.Succeeded) == expectedTurns);
    }

    private static async Task<(string DraftId, string FindingId)> SeedOpenFindingAsync(TestOratorioApp app, string itemId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var run = await db.Runs.Where(x => x.ItemId == itemId).OrderByDescending(x => x.Attempt).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        var draft = new OratorioReviewDraft
        {
            ItemId = itemId,
            RoundId = run.RoundId,
            RunId = run.RunId,
            Status = ReviewDraftStatus.Published,
            SummaryBody = "Found 1 issue.",
            MinorCount = 1,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now
        };
        var finding = new OratorioReviewDraftComment
        {
            DraftId = draft.DraftId,
            Severity = "YELLOW",
            Title = "Refresh token can be reused after rotation",
            Body = "The old token stays valid until the next sync.",
            Path = FindingPath,
            Line = 42,
            CommentOnlyReason = "investigateOnly",
            Status = ReviewDraftCommentStatus.Accepted
        };
        db.ReviewDrafts.Add(draft);
        db.ReviewDraftComments.Add(finding);
        await db.SaveChangesAsync();
        return (draft.DraftId, finding.DraftCommentId);
    }

    private static async Task<OratorioThreadState> LoadThreadStateAsync(TestOratorioApp app, string threadId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        return await db.ThreadStates.AsNoTracking().SingleAsync(x => x.ThreadId == threadId);
    }
}
