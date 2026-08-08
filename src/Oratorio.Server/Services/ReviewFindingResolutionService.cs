using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.GitHub;
using Oratorio.Server.GitLab;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Services;

/// <summary>
/// Resolves and reopens published review findings (design spec §5.7). Shared by the
/// <c>ResolveReviewFinding</c> dynamic tool (review runs and discussion turns) and the
/// operator resolve/reopen API. When the finding maps to a known source review thread, it also
/// enqueues and executes a <c>ResolveReviewThread</c> write through provider write controls.
/// </summary>
public sealed class ReviewFindingResolutionService(
    OratorioDbContext db,
    IClock clock,
    BoardEventHub boardEvents,
    GitHubWriteService gitHubWrites,
    GitLabWriteService gitLabWrites)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResolveReviewFindingResponse> ResolveForRunAsync(string runId, ResolveReviewFindingRequest request, CancellationToken ct)
    {
        var itemId = await db.Runs.AsNoTracking()
            .Where(x => x.RunId == runId)
            .Select(x => x.ItemId)
            .FirstOrDefaultAsync(ct)
            ?? throw OratorioApiException.Conflict("runNotFound", "The requested run does not exist.", new Dictionary<string, object?> { ["runId"] = runId });

        var comment = await LoadResolvableByItemAsync(itemId, request.FindingId, ct);
        await ApplyResolveAsync(comment, request, AuthorKind.Agent, runId, null, ct);
        return ToResponse(comment);
    }

    public async Task<ResolveReviewFindingResponse> ResolveForDiscussionAsync(string discussionTurnId, ResolveReviewFindingRequest request, CancellationToken ct)
    {
        var itemId = await db.DiscussionTurns.AsNoTracking()
            .Where(x => x.DiscussionTurnId == discussionTurnId)
            .Select(x => x.ItemId)
            .FirstOrDefaultAsync(ct)
            ?? throw OratorioApiException.Conflict("discussionTurnNotFound", "The requested Agent Discussion Turn does not exist.", new Dictionary<string, object?> { ["discussionTurnId"] = discussionTurnId });

        var comment = await LoadResolvableByItemAsync(itemId, request.FindingId, ct);
        await ApplyResolveAsync(comment, request, AuthorKind.Agent, null, discussionTurnId, ct);
        return ToResponse(comment);
    }

    /// <summary>Operator resolve via API. Returns the owning item id so the caller can reload detail.</summary>
    public async Task<string> ResolveByOperatorAsync(string draftId, string findingId, string resolutionKind, string? note, CancellationToken ct)
    {
        var comment = await LoadResolvableByDraftAsync(draftId, findingId, ct);
        await ApplyResolveAsync(comment, new ResolveReviewFindingRequest(findingId, resolutionKind, note), AuthorKind.Operator, null, null, ct);
        return comment.Draft!.ItemId;
    }

    /// <summary>Operator reopen via API. Returns the owning item id so the caller can reload detail.</summary>
    public async Task<string> ReopenByOperatorAsync(string draftId, string findingId, CancellationToken ct)
    {
        var comment = await LoadResolvableByDraftAsync(draftId, findingId, ct);
        if (comment.ResolutionState == ReviewFindingResolutionState.Open)
        {
            return comment.Draft!.ItemId;
        }

        var now = clock.UtcNow;
        comment.ResolutionState = ReviewFindingResolutionState.Open;
        comment.ResolutionKind = null;
        comment.ResolvedByKind = null;
        comment.ResolutionNote = null;
        comment.ResolvedAt = null;
        comment.ResolvedInRunId = null;
        comment.ResolvedViaDiscussionTurnId = null;

        var item = comment.Draft!.Item!;
        item.UpdatedAt = now;
        AddTimeline(item, comment.Draft.RoundId, TimelineEventKind.ReviewFindingReopened, ActorKind.Operator, "operator", "Review finding reopened", comment.Title, now);
        await EnqueueRemoteResolveAsync(comment, resolved: false, now, ct);
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(item, now);
        return item.ItemId;
    }

    private async Task ApplyResolveAsync(
        OratorioReviewDraftComment comment,
        ResolveReviewFindingRequest request,
        AuthorKind actor,
        string? runId,
        string? discussionTurnId,
        CancellationToken ct)
    {
        var kind = ParseKind(request.ResolutionKind);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (comment.ResolutionState == ReviewFindingResolutionState.Resolved &&
            comment.ResolutionKind == kind &&
            string.Equals(comment.ResolutionNote, note, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(comment.RemoteResolveWriteId))
            {
                var remoteNow = clock.UtcNow;
                if (await EnqueueRemoteResolveAsync(comment, resolved: true, remoteNow, ct))
                {
                    await db.SaveChangesAsync(ct);
                    boardEvents.PublishTaskUpdated(comment.Draft!.Item!, remoteNow);
                }
            }

            return;
        }

        var now = clock.UtcNow;
        comment.ResolutionState = ReviewFindingResolutionState.Resolved;
        comment.ResolutionKind = kind;
        comment.ResolvedByKind = actor;
        comment.ResolutionNote = note;
        comment.ResolvedAt = now;
        comment.ResolvedInRunId = runId;
        comment.ResolvedViaDiscussionTurnId = discussionTurnId;

        var item = comment.Draft!.Item!;
        item.UpdatedAt = now;
        var actorKind = actor == AuthorKind.Agent ? ActorKind.Agent : ActorKind.Operator;
        var actorName = actor == AuthorKind.Agent ? "DotCraft" : "operator";
        AddTimeline(item, comment.Draft.RoundId, TimelineEventKind.ReviewFindingResolved, actorKind, actorName, $"Review finding resolved ({KindLabel(kind)})", comment.Title, now);

        await EnqueueRemoteResolveAsync(comment, resolved: true, now, ct);
        await db.SaveChangesAsync(ct);
        boardEvents.PublishTaskUpdated(item, now);
    }

    /// <summary>
    /// Propagates resolution to the source review thread (design spec §5.7, Step C). No-op unless
    /// the finding has a captured remote thread id. Provider write settings decide whether the
    /// queued source write succeeds or records a retryable failure.
    /// </summary>
    private async Task<bool> EnqueueRemoteResolveAsync(OratorioReviewDraftComment comment, bool resolved, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(comment.RemoteThreadId))
        {
            return false;
        }

        var draft = comment.Draft!;
        if (string.IsNullOrWhiteSpace(draft.SourceWriteId))
        {
            return false;
        }

        var publishWrite = await db.SourceWriteLogs.AsNoTracking().FirstOrDefaultAsync(x => x.WriteId == draft.SourceWriteId, ct);
        if (publishWrite is null || string.IsNullOrWhiteSpace(publishWrite.Repository) || publishWrite.Number is null)
        {
            return false;
        }

        var write = new OratorioSourceWriteLog
        {
            ItemId = draft.ItemId,
            RoundId = draft.RoundId,
            Source = publishWrite.Source,
            Kind = SourceWriteKind.ResolveReviewThread,
            Intent = "reviewFindingResolve",
            Status = SourceWriteStatus.Pending,
            Repository = publishWrite.Repository,
            Number = publishWrite.Number,
            HeadSha = draft.Item!.HeadSha,
            CreatedAt = now,
            UpdatedAt = now
        };
        write.RequestJson = JsonSerializer.Serialize(BuildRemoteResolveRequest(comment, write.WriteId, publishWrite.Source, resolved), JsonOptions);
        db.SourceWriteLogs.Add(write);
        comment.RemoteResolveWriteId = write.WriteId;

        if (publishWrite.Source == "gitlab")
        {
            await gitLabWrites.ExecuteAsync(write, ct);
        }
        else
        {
            await gitHubWrites.ExecuteAsync(write, ct);
        }

        return true;
    }

    private async Task<OratorioReviewDraftComment> LoadResolvableByItemAsync(string itemId, string findingId, CancellationToken ct)
    {
        var comment = await LoadCommentAsync(findingId, ct);
        if (comment is null || comment.Draft is null || comment.Draft.ItemId != itemId)
        {
            throw NotFound(findingId, new Dictionary<string, object?> { ["findingId"] = findingId, ["itemId"] = itemId });
        }

        EnsureResolvable(comment);
        return comment;
    }

    private async Task<OratorioReviewDraftComment> LoadResolvableByDraftAsync(string draftId, string findingId, CancellationToken ct)
    {
        var comment = await LoadCommentAsync(findingId, ct);
        if (comment is null || comment.Draft is null || comment.Draft.DraftId != draftId)
        {
            throw NotFound(findingId, new Dictionary<string, object?> { ["findingId"] = findingId, ["draftId"] = draftId });
        }

        EnsureResolvable(comment);
        return comment;
    }

    private Task<OratorioReviewDraftComment?> LoadCommentAsync(string findingId, CancellationToken ct) =>
        db.ReviewDraftComments
            .Include(x => x.Draft!)
            .ThenInclude(d => d.Item)
            .FirstOrDefaultAsync(x => x.DraftCommentId == findingId, ct);

    private static void EnsureResolvable(OratorioReviewDraftComment comment)
    {
        if (comment.Draft!.Status != ReviewDraftStatus.Published)
        {
            throw OratorioApiException.Conflict(
                "reviewFindingNotResolvable",
                "Only findings from a published review draft can be resolved.",
                new Dictionary<string, object?> { ["findingId"] = comment.DraftCommentId, ["draftStatus"] = comment.Draft.Status });
        }

        if (comment.Status != ReviewDraftCommentStatus.Accepted)
        {
            throw OratorioApiException.Conflict(
                "reviewFindingNotResolvable",
                "Only accepted, published findings can be resolved.",
                new Dictionary<string, object?> { ["findingId"] = comment.DraftCommentId, ["commentStatus"] = comment.Status });
        }
    }

    private static OratorioApiException NotFound(string findingId, Dictionary<string, object?> details) =>
        OratorioApiException.Conflict("reviewFindingNotFound", "The requested review finding does not exist in this scope.", details);

    private static ReviewFindingResolutionKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "fixed" => ReviewFindingResolutionKind.Fixed,
        "dismissed" => ReviewFindingResolutionKind.Dismissed,
        _ => throw OratorioApiException.Validation(
            "resolutionKind must be 'fixed' or 'dismissed'.",
            new Dictionary<string, object?> { ["resolutionKind"] = value })
    };

    private static string KindLabel(ReviewFindingResolutionKind kind) =>
        kind == ReviewFindingResolutionKind.Fixed ? "fixed" : "dismissed";

    private static Dictionary<string, object?> BuildRemoteResolveRequest(
        OratorioReviewDraftComment comment,
        string writeId,
        string source,
        bool resolved)
    {
        var request = new Dictionary<string, object?>
        {
            ["threadId"] = comment.RemoteThreadId,
            ["resolved"] = resolved
        };
        if (resolved &&
            string.Equals(source, "github", StringComparison.OrdinalIgnoreCase) &&
            comment.ResolutionKind is { } kind &&
            !string.IsNullOrWhiteSpace(comment.ResolutionNote))
        {
            var marker = $"oratorio-resolution:{writeId}";
            request["replyMarker"] = marker;
            request["replyBody"] = BuildResolutionReplyBody(kind, comment.ResolutionNote, marker);
        }

        return request;
    }

    private static string BuildResolutionReplyBody(ReviewFindingResolutionKind kind, string note, string marker) =>
        $"Oratorio resolved this finding as **{KindLabel(kind)}**.\n\nReason: {note.Trim()}\n\n<!-- {marker} -->";

    private static ResolveReviewFindingResponse ToResponse(OratorioReviewDraftComment comment) =>
        new(comment.DraftCommentId, comment.ResolutionState.ToString(), comment.ResolutionKind?.ToString());

    private static void AddTimeline(
        OratorioItem item,
        string? roundId,
        TimelineEventKind kind,
        ActorKind actorKind,
        string actorName,
        string title,
        string? body,
        DateTimeOffset createdAt)
    {
        item.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            RoundId = roundId,
            Kind = kind,
            ActorKind = actorKind,
            ActorName = actorName,
            Title = title,
            Body = body,
            CreatedAt = createdAt
        });
    }
}
