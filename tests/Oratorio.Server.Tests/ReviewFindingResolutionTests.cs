using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.Services;

namespace Oratorio.Server.Tests;

public sealed class ReviewFindingResolutionTests
{
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
    public async Task Resolve_UnknownFinding_Returns409NotFound()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var (draftId, _, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-missing");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/review-drafts/{draftId}/comments/does-not-exist/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reviewFindingNotFound", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Resolve_UnpublishedDraftFinding_Returns409NotResolvable()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var (draftId, commentId) = await SeedDraftFindingAsync(app, client, "task:resolve-unpublished");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reviewFindingNotResolvable", await ReadErrorCodeAsync(response));
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

    [Fact]
    public async Task Resolve_WithNote_PostsGitHubThreadReplyBeforeResolving()
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
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", "Addressed in follow-up commit."));

        var reply = Assert.Single(fakeGitHub.ReviewThreadReplies);
        Assert.Equal(threadId, reply.ThreadId);
        Assert.Contains("Oratorio resolved this finding as **fixed**.", reply.Body, StringComparison.Ordinal);
        Assert.Contains("Reason: Addressed in follow-up commit.", reply.Body, StringComparison.Ordinal);
        Assert.Contains("<!-- oratorio-resolution:", reply.Body, StringComparison.Ordinal);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
        Assert.True(
            fakeGitHub.WriteOperations.IndexOf($"reply:{threadId}") <
            fakeGitHub.WriteOperations.IndexOf($"resolve:{threadId}:True"));
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
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId);

        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/resolve",
            new ResolveReviewFindingOperatorRequest("fixed", null));
        await PostAsync<ItemDetailResponse>(
            client,
            $"/api/v1/review-drafts/{draftId}/comments/{commentId}/reopen",
            new { });

        Assert.Empty(fakeGitHub.ReviewThreadReplies);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && !x.Resolved);
    }

    [Fact]
    public async Task RetryResolveWrite_WhenReplyMarkerAlreadyExists_SkipsDuplicateReply()
    {
        var fakeGitHub = new FakeGitHubApiClient();
        await using var app = new TestOratorioApp(services =>
        {
            services.RemoveAll<IGitHubApiClient>();
            services.AddSingleton<IGitHubApiClient>(fakeGitHub);
        });
        var client = app.CreateClient();
        var (draftId, commentId, _, _) = await SeedPublishedFindingAsync(app, client, "task:resolve-retry-dedupe");
        const string threadId = "thread-42-0";
        const string marker = "oratorio-resolution:retry-write";
        await AttachRemoteThreadAsync(app, draftId, commentId, threadId);
        fakeGitHub.PullRequestReviews.Add(new GitHubPullRequestReviewWrite(
            new GitHubRepositoryRef("example-owner", "oratorio"),
            42,
            "COMMENT",
            "Summary",
            "head-sha",
            [new GitHubPullRequestReviewCommentWrite("README.md", "Original finding body", 1, "RIGHT", null, null)]));
        fakeGitHub.ReviewThreadReplies.Add(new GitHubReviewThreadReplyWrite(
            new GitHubRepositoryRef("example-owner", "oratorio"),
            threadId,
            $"Already posted.\n\n<!-- {marker} -->"));

        string writeId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OratorioDbContext>();
            var draft = await db.ReviewDrafts.FirstAsync(x => x.DraftId == draftId);
            var now = DateTimeOffset.UtcNow;
            var write = new OratorioSourceWriteLog
            {
                ItemId = draft.ItemId,
                RoundId = draft.RoundId,
                Source = "github",
                Kind = SourceWriteKind.ResolveReviewThread,
                Intent = "reviewFindingResolve",
                Status = SourceWriteStatus.Failed,
                Repository = "example-owner/oratorio",
                Number = 42,
                HeadSha = "head-sha",
                RequestJson = JsonSerializer.Serialize(new
                {
                    threadId,
                    resolved = true,
                    replyBody = $"Oratorio resolved this finding as **fixed**.\n\nReason: Addressed.\n\n<!-- {marker} -->",
                    replyMarker = marker
                }, JsonOptions),
                ErrorCode = "githubWriteFailed",
                ErrorMessage = "Synthetic partial failure.",
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now
            };
            db.SourceWriteLogs.Add(write);
            var comment = await db.ReviewDraftComments.FirstAsync(x => x.DraftCommentId == commentId);
            comment.RemoteResolveWriteId = write.WriteId;
            await db.SaveChangesAsync();
            writeId = write.WriteId;
        }

        using (var retryScope = app.Services.CreateScope())
        {
            var service = retryScope.ServiceProvider.GetRequiredService<GitHubWriteService>();
            await service.RetryAsync(writeId, CancellationToken.None);
        }

        var reply = Assert.Single(fakeGitHub.ReviewThreadReplies);
        Assert.Contains(marker, reply.Body, StringComparison.Ordinal);
        Assert.Contains(fakeGitHub.ReviewThreadResolutions, x => x.ThreadId == threadId && x.Resolved);
        Assert.DoesNotContain(fakeGitHub.WriteOperations, op => op == $"reply:{threadId}");

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OratorioDbContext>();
        var storedWrite = await verifyDb.SourceWriteLogs.AsNoTracking().FirstAsync(x => x.WriteId == writeId);
        Assert.Equal(SourceWriteStatus.Succeeded, storedWrite.Status);
        Assert.Contains("\"replySkipped\":true", storedWrite.ResponseJson, StringComparison.Ordinal);
    }

    private static async Task<(string DraftId, string CommentId, string ItemId, string RunId)> SeedPublishedFindingAsync(
        TestOratorioApp app,
        HttpClient client,
        string externalId) =>
        await SeedFindingAsync(app, client, externalId, ReviewDraftStatus.Published);

    private static async Task<(string DraftId, string CommentId)> SeedDraftFindingAsync(
        TestOratorioApp app,
        HttpClient client,
        string externalId)
    {
        var (draftId, commentId, _, _) = await SeedFindingAsync(app, client, externalId, ReviewDraftStatus.Draft);
        return (draftId, commentId);
    }

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
            Title = "Preserve the removed workspace sample redirect",
            Body = "The redirect removal changes the published sample URL and may leave existing readers without a working path.",
            Path = "docs/.vitepress/config.mts",
            Line = 241,
            Side = "RIGHT",
            CommentOnlyReason = "investigateOnly",
            Status = ReviewDraftCommentStatus.Accepted
        };
        db.ReviewDrafts.Add(draft);
        db.ReviewDraftComments.Add(comment);
        await db.SaveChangesAsync();
        return (draft.DraftId, comment.DraftCommentId, item.ItemId, run.RunId);
    }

    private static async Task AttachRemoteThreadAsync(TestOratorioApp app, string draftId, string commentId, string threadId)
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

            await Task.Delay(100);
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
