using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.Tests;

public sealed class LatestResultTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-01T10:00:00Z");

    [Fact]
    public async Task LatestResult_PrefersPendingFollowUpsThenNewestNonDiscardedDraft()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var noDrafts = await CreateLocalTaskAsync(client, "No drafts");
        var cleanReview = await CreateLocalTaskAsync(client, "Clean review");
        var findings = await CreateLocalTaskAsync(client, "Review with findings");
        var changesReady = await CreateLocalTaskAsync(client, "Implementation draft");
        var delivered = await CreateLocalTaskAsync(client, "Delivered implementation");
        var followUps = await CreateLocalTaskAsync(client, "Pending follow-up");
        await SeedAsync(app, cleanReview, (db, round, run) =>
        {
            db.ReviewDrafts.Add(ReviewDraft(cleanReview, round, run, Start, majorCount: 0, minorCount: 0));
            db.ReviewDrafts.Add(ReviewDraft(cleanReview, round, run, Start.AddMinutes(1), majorCount: 2, minorCount: 0, ReviewDraftStatus.Discarded));
        });
        await SeedAsync(app, findings, (db, round, run) =>
            db.ReviewDrafts.Add(ReviewDraft(findings, round, run, Start, majorCount: 0, minorCount: 1)));
        await SeedAsync(app, changesReady, (db, round, run) =>
        {
            db.ReviewDrafts.Add(ReviewDraft(changesReady, round, run, Start, majorCount: 1, minorCount: 0));
            db.ImplementationDrafts.Add(ImplementationDraft(changesReady, round, run, Start.AddMinutes(1), ImplementationDraftStatus.Draft));
        });
        await SeedAsync(app, delivered, (db, round, run) =>
            db.ImplementationDrafts.Add(ImplementationDraft(delivered, round, run, Start, ImplementationDraftStatus.Delivered)));
        await SeedAsync(app, followUps, (db, round, run) =>
        {
            db.FollowUpDrafts.Add(FollowUpDraft(followUps, round, run, Start, FollowUpDraftStatus.Draft));
            db.ImplementationDrafts.Add(ImplementationDraft(followUps, round, run, Start.AddMinutes(1), ImplementationDraftStatus.Delivered));
        });

        Assert.Equal(LatestResult.None, (await GetDetailAsync(client, noDrafts)).Item.LatestResult);
        Assert.Equal(LatestResult.NoIssues, (await GetDetailAsync(client, cleanReview)).Item.LatestResult);
        Assert.Equal(LatestResult.Findings, (await GetDetailAsync(client, findings)).Item.LatestResult);
        Assert.Equal(LatestResult.ChangesReady, (await GetDetailAsync(client, changesReady)).Item.LatestResult);
        Assert.Equal(LatestResult.Delivered, (await GetDetailAsync(client, delivered)).Item.LatestResult);
        Assert.Equal(LatestResult.FollowUps, (await GetDetailAsync(client, followUps)).Item.LatestResult);
    }

    [Fact]
    public async Task ItemAndTaskLists_CarryLatestResult()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var plainId = await CreateLocalTaskAsync(client, "Plain task");
        var reviewedId = await CreateLocalTaskAsync(client, "Reviewed task");
        await SeedAsync(app, reviewedId, (db, round, run) =>
        {
            db.ReviewDrafts.Add(ReviewDraft(reviewedId, round, run, Start, majorCount: 1, minorCount: 1));
            db.FollowUpDrafts.Add(FollowUpDraft(reviewedId, round, run, Start, FollowUpDraftStatus.Created));
        });

        foreach (var (path, property) in new[] { ("/api/v1/items", "items"), ("/api/v1/tasks", "tasks") })
        {
            using var document = JsonDocument.Parse(await client.GetStringAsync(path));
            var results = document.RootElement.GetProperty(property).EnumerateArray()
                .ToDictionary(x => x.GetProperty("itemId").GetString()!, x => x.GetProperty("latestResult").GetString());
            Assert.Equal("none", results[plainId]);
            Assert.Equal("findings", results[reviewedId]);
        }
    }

    private static async Task<string> CreateLocalTaskAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/local-tasks",
            new CreateLocalTaskRequest(title, "Latest result fixture.", "example-owner/oratorio", "operator", "local/latest", []),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<ItemDetailResponse>(JsonOptions);
        return detail!.Item.ItemId;
    }

    private static async Task<ItemDetailResponse> GetDetailAsync(HttpClient client, string itemId) =>
        (await client.GetFromJsonAsync<ItemDetailResponse>($"/api/v1/items/id/{itemId}", JsonOptions))!;

    private static async Task SeedAsync(TestOratorioApp app, string itemId, Action<OratorioDbContext, OratorioRound, OratorioRun> seed)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var round = new OratorioRound { ItemId = itemId, RoundNumber = 1, Status = RoundStatus.AwaitingReview, CreatedAt = Start, CompletedAt = Start };
        var run = new OratorioRun
        {
            ItemId = itemId,
            RoundId = round.RoundId,
            Attempt = 1,
            Status = RunStatus.Succeeded,
            RunnerKind = "appServer",
            Purpose = RunPurpose.ReviewAnalysis,
            StartedAt = Start,
            CompletedAt = Start
        };
        db.Rounds.Add(round);
        db.Runs.Add(run);
        seed(db, round, run);
        await db.SaveChangesAsync();
    }

    private static OratorioReviewDraft ReviewDraft(
        string itemId,
        OratorioRound round,
        OratorioRun run,
        DateTimeOffset createdAt,
        int majorCount,
        int minorCount,
        ReviewDraftStatus status = ReviewDraftStatus.Published) =>
        new()
        {
            ItemId = itemId,
            RoundId = round.RoundId,
            RunId = run.RunId,
            Status = status,
            SummaryBody = "Review summary.",
            MajorCount = majorCount,
            MinorCount = minorCount,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static OratorioImplementationDraft ImplementationDraft(
        string itemId,
        OratorioRound round,
        OratorioRun run,
        DateTimeOffset createdAt,
        ImplementationDraftStatus status) =>
        new()
        {
            ItemId = itemId,
            RoundId = round.RoundId,
            RunId = run.RunId,
            Status = status,
            Summary = "Implementation summary.",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static OratorioFollowUpDraft FollowUpDraft(
        string itemId,
        OratorioRound round,
        OratorioRun run,
        DateTimeOffset createdAt,
        FollowUpDraftStatus status) =>
        new()
        {
            ItemId = itemId,
            RoundId = round.RoundId,
            RunId = run.RunId,
            Status = status,
            Title = "Follow-up",
            Body = "Follow-up body.",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
}
