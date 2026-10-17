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
    public async Task NoDrafts_ReportsNone()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "No drafts");

        Assert.Equal(LatestResult.None, (await GetDetailAsync(client, itemId)).Item.LatestResult);
    }

    [Fact]
    public async Task CleanReview_ReportsNoIssues()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "Clean review");
        await SeedAsync(app, itemId, (db, round, run) =>
        {
            db.ReviewDrafts.Add(ReviewDraft(itemId, round, run, Start, majorCount: 0, minorCount: 0));
            db.ReviewDrafts.Add(ReviewDraft(itemId, round, run, Start.AddMinutes(1), majorCount: 2, minorCount: 0, ReviewDraftStatus.Discarded));
        });

        Assert.Equal(LatestResult.NoIssues, (await GetDetailAsync(client, itemId)).Item.LatestResult);
    }

    [Fact]
    public async Task ReviewWithFindings_ReportsFindings()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "Review with findings");
        await SeedAsync(app, itemId, (db, round, run) =>
            db.ReviewDrafts.Add(ReviewDraft(itemId, round, run, Start, majorCount: 0, minorCount: 1)));

        Assert.Equal(LatestResult.Findings, (await GetDetailAsync(client, itemId)).Item.LatestResult);
    }

    [Fact]
    public async Task ImplementationDraft_ReportsChangesReady()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "Implementation draft");
        await SeedAsync(app, itemId, (db, round, run) =>
        {
            db.ReviewDrafts.Add(ReviewDraft(itemId, round, run, Start, majorCount: 1, minorCount: 0));
            db.ImplementationDrafts.Add(ImplementationDraft(itemId, round, run, Start.AddMinutes(1), ImplementationDraftStatus.Draft));
        });

        Assert.Equal(LatestResult.ChangesReady, (await GetDetailAsync(client, itemId)).Item.LatestResult);
    }

    [Fact]
    public async Task DeliveredImplementation_ReportsDelivered()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "Delivered implementation");
        await SeedAsync(app, itemId, (db, round, run) =>
            db.ImplementationDrafts.Add(ImplementationDraft(itemId, round, run, Start, ImplementationDraftStatus.Delivered)));

        Assert.Equal(LatestResult.Delivered, (await GetDetailAsync(client, itemId)).Item.LatestResult);
    }

    [Fact]
    public async Task PendingFollowUp_ReportsFollowUpsOverLaterResults()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var itemId = await CreateLocalTaskAsync(client, "Pending follow-up");
        await SeedAsync(app, itemId, (db, round, run) =>
        {
            db.FollowUpDrafts.Add(FollowUpDraft(itemId, round, run, Start, FollowUpDraftStatus.Draft));
            db.ImplementationDrafts.Add(ImplementationDraft(itemId, round, run, Start.AddMinutes(1), ImplementationDraftStatus.Delivered));
        });

        Assert.Equal(LatestResult.FollowUps, (await GetDetailAsync(client, itemId)).Item.LatestResult);
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
