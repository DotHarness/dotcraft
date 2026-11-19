using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.Oratorio.Api;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class ReviewFindingResolutionTests
{
    private const long RootCommentId = 5_000_000_001L;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task OperatorResolve_ThenReopen_UpdatesFindingState()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-operator");

        var resolved = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Addressed in follow-up commit."));

        var draftDto = Assert.Single(resolved.ReviewDrafts);
        var commentDto = Assert.Single(draftDto.Comments);
        Assert.Equal(ReviewFindingResolutionState.Resolved, commentDto.ResolutionState);
        Assert.Equal(ReviewFindingResolutionKind.Fixed, commentDto.ResolutionKind);
        Assert.Equal(AuthorKind.Operator, commentDto.ResolvedByKind);
        Assert.Equal("Addressed in follow-up commit.", commentDto.ResolutionNote);
        Assert.NotNull(commentDto.ResolvedAt);
        Assert.Equal(1, draftDto.ResolvedCount);

        var reopened = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/reopen",
            new { });

        var reopenedComment = Assert.Single(Assert.Single(reopened.ReviewDrafts).Comments);
        Assert.Equal(ReviewFindingResolutionState.Open, reopenedComment.ResolutionState);
        Assert.Null(reopenedComment.ResolutionKind);
        Assert.Null(reopenedComment.ResolvedByKind);
        Assert.Null(reopenedComment.ResolvedAt);
        Assert.Equal(0, Assert.Single(reopened.ReviewDrafts).ResolvedCount);
    }

    [Fact]
    public async Task AgentToolResolve_RecordsRunProvenance()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var (_, commentId, _, runId) = await SeedPublishedFindingAsync(app, client, "task:resolve-tool");

        using var scope = app.Services.CreateScope();
        var resolution = scope.ServiceProvider.GetRequiredService<ReviewFindingResolutionService>();
        var response = await resolution.ResolveForRunAsync(
            runId,
            new ResolveReviewFindingRequest(commentId, "dismissed", "Agreed non-issue."),
            CancellationToken.None);

        Assert.Equal("Resolved", response.ResolutionState);
        Assert.Equal("Dismissed", response.ResolutionKind);

        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var stored = await db.ReviewDraftComments.AsNoTracking().FirstAsync(x => x.DraftCommentId == commentId);
        Assert.Equal(ReviewFindingResolutionState.Resolved, stored.ResolutionState);
        Assert.Equal(ReviewFindingResolutionKind.Dismissed, stored.ResolutionKind);
        Assert.Equal(AuthorKind.Agent, stored.ResolvedByKind);
        Assert.Equal(runId, stored.ResolvedInRunId);
    }

    [Fact]
    public async Task Resolve_RejectsUnknownAndUnpublishedFindings()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-rejected");

        var unknown = await client.PostAsJsonAsync(
            $"/api/v1/review-drafts/{draftId}/comments/does-not-exist/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        Assert.Equal("reviewFindingNotFound", await ReadErrorCodeAsync(unknown));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var draft = await db.ReviewDrafts.FirstAsync(x => x.DraftId == draftId);
            draft.Status = ReviewDraftStatus.Draft;
            draft.PublishedAt = null;
            await db.SaveChangesAsync();
        }

        var unpublished = await client.PostAsJsonAsync(
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, unpublished.StatusCode);
        Assert.Equal("reviewFindingNotResolvable", await ReadErrorCodeAsync(unpublished));
    }

    [Fact]
    public async Task Resolve_AlreadyResolvedFindingWithMissingRemoteWrite_QueuesRemoteResolve()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-remote-backfill");
        const string threadId = "thread-remote-backfill";

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var draft = await db.ReviewDrafts.FirstAsync(x => x.DraftId == draftId);
            var comment = await db.ReviewDraftComments.FirstAsync(x => x.DraftCommentId == commentId);
            var now = DateTimeOffset.UtcNow;
            var publishWrite = new OratorioSourceWriteLog
            {
                ItemId = draft.ItemId,
                RoundId = draft.RoundId,
                Source = "github",
                Kind = SourceWriteKind.PullRequestReview,
                Intent = "reviewDraftPublish",
                Status = SourceWriteStatus.Succeeded,
                Repository = "example-owner/oratorio",
                Number = 42,
                HeadSha = "head-sha",
                RequestJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now
            };

            db.SourceWriteLogs.Add(publishWrite);
            draft.SourceWriteId = publishWrite.WriteId;
            comment.ResolutionState = ReviewFindingResolutionState.Resolved;
            comment.ResolutionKind = ReviewFindingResolutionKind.Fixed;
            comment.ResolvedByKind = AuthorKind.Agent;
            comment.ResolutionNote = "Already fixed.";
            comment.ResolvedAt = now;
            comment.RemoteThreadId = threadId;
            comment.RemoteResolveWriteId = null;
            await db.SaveChangesAsync();
        }

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Already fixed."));

        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var stored = await verifyDb.ReviewDraftComments.AsNoTracking().FirstAsync(x => x.DraftCommentId == commentId);
        Assert.False(string.IsNullOrWhiteSpace(stored.RemoteResolveWriteId));
        Assert.Equal(1, await verifyDb.SourceWriteLogs.CountAsync(x => x.Kind == SourceWriteKind.ResolveReviewThread && x.Intent == "reviewFindingResolve"));
    }

    [Theory]
    [InlineData(RootCommentId)]
    [InlineData(null)]
    public async Task Resolve_WithNote_ResolvesThreadAndRepliesWhenRootIsKnown(long? rootCommentId)
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-reply");
        const string threadId = "thread-42-0";
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId, rootCommentId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Addressed in follow-up commit."));

        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
        if (rootCommentId is null)
        {
            Assert.Empty(fakeGitHub.ReviewCommentReplies);
            return;
        }

        var reply = Assert.Single(fakeGitHub.ReviewCommentReplies);
        Assert.Equal(42, reply.Number);
        Assert.Equal(rootCommentId.Value, reply.InReplyToId);
        Assert.Contains("Reason: Addressed in follow-up commit.", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveWithoutNoteAndReopen_DoNotPostGitHubThreadReply()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-no-reply");
        const string threadId = "thread-42-0";
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId, rootCommentId: RootCommentId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/reopen",
            new { });

        Assert.Empty(fakeGitHub.ReviewCommentReplies);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && !x.Resolved);
    }

    [Fact]
    public async Task RetryResolveWrite_WhenReplyResponseWasLost_SkipsDuplicateReply()
    {
        var fakeGitHub = new FakeGitHubApiClient { LoseNextReviewCommentReplyResponseCount = 1 };
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-retry-dedupe");
        const string threadId = "thread-42-0";
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId, rootCommentId: RootCommentId);

        var detail = await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Addressed in follow-up commit."));

        var failed = Assert.Single(detail.SourceWrites, write => write.Kind == SourceWriteKind.ResolveReviewThread);
        Assert.Equal(SourceWriteStatus.Failed, failed.Status);
        Assert.Single(fakeGitHub.ReviewCommentReplies);
        Assert.Empty(fakeGitHub.ReviewThreadResolutions);

        var retried = await PostAsync<ItemDetailResponse>(client, $"/api/v1/source-writes/{failed.WriteId}/retry", new { });

        Assert.Equal(SourceWriteStatus.Succeeded, Assert.Single(retried.SourceWrites, write => write.WriteId == failed.WriteId).Status);
        Assert.Single(fakeGitHub.ReviewCommentReplies);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
    }

    private static Task<(string DraftId, string CommentId, string ItemId, string RunId)> SeedPublishedFindingAsync(
        TestOratorioApp app,
        HttpClient client,
        string externalId) =>
        SeedFindingAsync(app, client, externalId, ReviewDraftStatus.Published);

    private static async Task<(string DraftId, string CommentId, string ItemId, string RunId)> SeedFindingAsync(
        TestOratorioApp app,
        HttpClient client,
        string externalId,
        ReviewDraftStatus draftStatus)
    {
        await CreateItemAsync(client, externalId);
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/items/local/{externalId}/dispatch",
            new DispatchRequest("mock", "Seed run", MockOutcome.Success, 1));
        await WaitForItemAsync(client, externalId, x => x.Item.State == ItemState.AwaitingReview);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var item = await db.Items.FirstAsync(x => x.Source == "local" && x.ExternalId == externalId);
        var run = await db.Runs.Where(x => x.ItemId == item.ItemId).OrderByDescending(x => x.Attempt).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        var draft = new OratorioReviewDraft
        {
            ItemId = item.ItemId,
            RoundId = run.RoundId,
            RunId = run.RunId,
            Status = draftStatus,
            SummaryBody = "Found 1 issue.",
            MajorCount = 0,
            MinorCount = 1,
            SuggestionCount = 0,
            WarningsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = draftStatus == ReviewDraftStatus.Published ? now : null
        };
        var comment = new OratorioReviewDraftComment
        {
            DraftId = draft.DraftId,
            Severity = "YELLOW",
            Title = "Handle an empty input",
            Body = "An empty input leaves the result unset.",
            Path = "example.txt",
            Line = 3,
            Side = "RIGHT",
            CommentOnlyReason = "investigateOnly",
            Status = ReviewDraftCommentStatus.Accepted
        };
        db.ReviewDrafts.Add(draft);
        db.ReviewDraftComments.Add(comment);
        await db.SaveChangesAsync();
        return (draft.DraftId, comment.DraftCommentId, item.ItemId, run.RunId);
    }

    private static async Task AttachRemoteThreadAsync(TestOratorioApp app, string draftId, string commentId, string threadId, long? rootCommentId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var draft = await db.ReviewDrafts.FirstAsync(x => x.DraftId == draftId);
        var comment = await db.ReviewDraftComments.FirstAsync(x => x.DraftCommentId == commentId);
        var now = DateTimeOffset.UtcNow;
        var publishWrite = new OratorioSourceWriteLog
        {
            ItemId = draft.ItemId,
            RoundId = draft.RoundId,
            Source = "github",
            Kind = SourceWriteKind.PullRequestReview,
            Intent = "reviewDraftPublish",
            Status = SourceWriteStatus.Succeeded,
            Repository = "example-owner/oratorio",
            Number = 42,
            HeadSha = "head-sha",
            RequestJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = now
        };

        db.SourceWriteLogs.Add(publishWrite);
        draft.SourceWriteId = publishWrite.WriteId;
        comment.RemoteThreadId = threadId;
        comment.RemoteRootCommentId = rootCommentId;
        await db.SaveChangesAsync();
    }

    private static async Task CreateItemAsync(HttpClient client, string externalId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/items",
            new CreateItemRequest(
                "local",
                externalId,
                ItemKind.PullRequest,
                "Resolution test item",
                "A test item.",
                "example-owner/oratorio",
                "operator",
                "test/oratorio"),
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<ItemDetailResponse> WaitForItemAsync(HttpClient client, string externalId, Func<ItemDetailResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var detail = await GetAsync<ItemDetailResponse>(client, $"/api/v1/items/local/{externalId}");
            if (predicate(detail))
            {
                return detail;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for item {externalId}.");
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"POST {path} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}
