using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oratorio.Server.Api;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Services;

/// <summary>
/// Drives the Implementation Follow-up loop (design spec §3, §5.5): when an originating
/// issue/local task that already delivered a generated pull request accrues new unresolved
/// published review findings or new human PR review comments, re-activate the originating item
/// and queue a bounded implementation follow-up run that pushes follow-up commits to the same PR.
/// The generated PR itself stays a read-only review target.
/// </summary>
public sealed class ImplementationFollowUpDispatchService(
    OratorioDbContext db,
    OratorioService oratorio,
    IOptionsMonitor<OratorioAutomationOptions> options,
    IClock clock,
    BoardEventHub boardEvents,
    ILogger<ImplementationFollowUpDispatchService> logger)
{
    public async Task DispatchEligibleAsync(CancellationToken ct)
    {
        var policy = options.CurrentValue;
        if (!policy.AutoFollowUpEnabled)
        {
            return;
        }

        var candidates = await db.Items.AsNoTracking()
            .Where(x =>
                x.State == ItemState.AwaitingReview &&
                (x.Kind == ItemKind.Issue || x.Kind == ItemKind.LocalTask))
            .OrderBy(x => x.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var item in candidates)
        {
            try
            {
                await EvaluateItemAsync(item, policy, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Implementation follow-up evaluation failed for {ItemId}.", item.ItemId);
            }
        }
    }

    private async Task EvaluateItemAsync(OratorioItem item, OratorioAutomationOptions policy, CancellationToken ct)
    {
        var generatedPr = await db.Items.AsNoTracking()
            .Where(x =>
                x.ParentItemId == item.ItemId &&
                x.Kind == ItemKind.PullRequest &&
                (x.Source == "github" || x.Source == "gitlab"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (generatedPr is null)
        {
            return;
        }

        if (!policy.CanAutoFollowUpRepository(generatedPr.Repository))
        {
            return;
        }

        if (generatedPr.SourceState != SourceState.Open || generatedPr.State == ItemState.Archived)
        {
            return;
        }

        var openFindingIds = await db.ReviewDraftComments.AsNoTracking()
            .Where(c =>
                c.Draft!.ItemId == generatedPr.ItemId &&
                c.Draft.Status == ReviewDraftStatus.Published &&
                c.Status == ReviewDraftCommentStatus.Accepted &&
                c.ResolutionState == ReviewFindingResolutionState.Open)
            .OrderBy(c => c.DraftCommentId)
            .Select(c => c.DraftCommentId)
            .ToListAsync(ct);
        var findingsKey = BuildFindingsKey(openFindingIds);
        var latestCommentAt = await LatestHumanCommentAtAsync(generatedPr.ItemId, ct);

        var state = await db.ImplementationFollowUpItemStates
            .FirstOrDefaultAsync(x => x.OriginatingItemId == item.ItemId, ct);

        var hasOpenFindings = openFindingIds.Count > 0;
        var newFindings = hasOpenFindings && !string.Equals(findingsKey, state?.LastObservedFindingsKey, StringComparison.Ordinal);
        var newComments = latestCommentAt is not null &&
            (state?.LastObservedCommentAt is null || latestCommentAt > state.LastObservedCommentAt);

        if (!newFindings && !newComments)
        {
            return;
        }

        var now = clock.UtcNow;
        var roundCount = state?.FollowUpRoundCount ?? 0;
        if (roundCount >= policy.EffectiveMaxFollowUpRounds)
        {
            RecordCapReached(ref state, item, generatedPr, findingsKey, latestCommentAt, policy.EffectiveMaxFollowUpRounds, now);
            await db.SaveChangesAsync(ct);
            return;
        }

        var note = BuildFollowUpNote(openFindingIds.Count, newComments, generatedPr);
        try
        {
            var detail = await oratorio.ReactivateForImplementationFollowUpByIdAsync(item.ItemId, note, ct);
            var queuedRun = detail.Runs
                .OrderByDescending(x => x.StartedAt ?? x.CompletedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault(x => x.RunnerKind == "appServer" && x.Purpose == RunPurpose.Implementation);

            state = Upsert(state, item, now);
            state.GeneratedPrItemId = generatedPr.ItemId;
            state.Repository = generatedPr.Repository ?? "";
            state.LastObservedFindingsKey = findingsKey;
            state.LastObservedCommentAt = latestCommentAt;
            state.LastQueuedHeadSha = generatedPr.HeadSha;
            state.LastQueuedRunId = queuedRun?.RunId;
            state.FollowUpRoundCount = roundCount + 1;
            state.LastErrorCode = null;
            state.LastErrorMessage = null;
            state.LastErrorAt = null;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        catch (OratorioApiException ex) when (ex.Code is "activeRunExists" or "invalidTransition" or "sourceDetailsRequired" or "implementationUnsupportedItem")
        {
            logger.LogDebug("Skipped implementation follow-up for {ItemId}: {Code}.", item.ItemId, ex.Code);
            RecordError(ref state, item, generatedPr, ex.Code, ex.Message, now);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<DateTimeOffset?> LatestHumanCommentAtAsync(string prItemId, CancellationToken ct)
    {
        var comments = await db.Comments.AsNoTracking()
            .Where(c =>
                c.ItemId == prItemId &&
                c.Purpose == CommentPurpose.SourceContext &&
                c.SourceCommentId != null)
            .Select(c => new { c.SourceUpdatedAt, c.CreatedAt })
            .ToListAsync(ct);
        return comments.Count == 0
            ? null
            : comments.Max(c => c.SourceUpdatedAt ?? c.CreatedAt);
    }

    private OratorioImplementationFollowUpItemState Upsert(OratorioImplementationFollowUpItemState? state, OratorioItem item, DateTimeOffset now)
    {
        if (state is not null)
        {
            return state;
        }

        state = new OratorioImplementationFollowUpItemState
        {
            OriginatingItemId = item.ItemId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ImplementationFollowUpItemStates.Add(state);
        return state;
    }

    private void RecordCapReached(
        ref OratorioImplementationFollowUpItemState? state,
        OratorioItem item,
        OratorioItem generatedPr,
        string findingsKey,
        DateTimeOffset? latestCommentAt,
        int maxRounds,
        DateTimeOffset now)
    {
        state = Upsert(state, item, now);
        state.GeneratedPrItemId = generatedPr.ItemId;
        state.Repository = generatedPr.Repository ?? "";
        state.LastObservedFindingsKey = findingsKey;
        state.LastObservedCommentAt = latestCommentAt;
        var changed = !string.Equals(state.LastErrorCode, "followUpCapReached", StringComparison.Ordinal);
        state.LastErrorCode = "followUpCapReached";
        state.LastErrorMessage = $"Implementation follow-up stopped after reaching the maximum of {maxRounds} rounds.";
        state.LastErrorAt = now;
        state.UpdatedAt = now;
        if (changed)
        {
            AddSkipTimeline(item, "Implementation follow-up capped", state.LastErrorMessage, "followUpCapReached", now);
        }
    }

    private void RecordError(
        ref OratorioImplementationFollowUpItemState? state,
        OratorioItem item,
        OratorioItem generatedPr,
        string code,
        string message,
        DateTimeOffset now)
    {
        state = Upsert(state, item, now);
        state.GeneratedPrItemId = generatedPr.ItemId;
        state.Repository = generatedPr.Repository ?? "";
        var changed = !string.Equals(state.LastErrorCode, code, StringComparison.Ordinal) ||
            !string.Equals(state.LastErrorMessage, message, StringComparison.Ordinal);
        state.LastErrorCode = code;
        state.LastErrorMessage = message;
        state.LastErrorAt = now;
        state.UpdatedAt = now;
        if (changed)
        {
            AddSkipTimeline(item, "Implementation follow-up skipped", message, code, now);
        }
    }

    private void AddSkipTimeline(OratorioItem item, string title, string body, string code, DateTimeOffset now)
    {
        db.TimelineEvents.Add(new OratorioTimelineEvent
        {
            ItemId = item.ItemId,
            Kind = TimelineEventKind.CheckUpdated,
            ActorKind = ActorKind.System,
            ActorName = "oratorio/follow-up",
            Title = title,
            Body = body,
            MetadataJson = JsonSerializer.Serialize(new { code }),
            CreatedAt = now
        });
        boardEvents.PublishTaskUpdated(item, now);
    }

    private static string BuildFindingsKey(IReadOnlyList<string> orderedFindingIds) =>
        orderedFindingIds.Count == 0 ? "" : string.Join('\n', orderedFindingIds);

    private static string BuildFollowUpNote(int openFindingCount, bool newComments, OratorioItem generatedPr)
    {
        var parts = new List<string>();
        if (openFindingCount > 0)
        {
            parts.Add(openFindingCount == 1
                ? "1 open review finding"
                : $"{openFindingCount} open review findings");
        }

        if (newComments)
        {
            parts.Add("new human review comments");
        }

        var reason = parts.Count == 0 ? "new review feedback" : string.Join(" and ", parts);
        var target = generatedPr.ExternalUrl ?? generatedPr.ExternalId;
        return $"Oratorio queued an implementation follow-up to address {reason} on the generated pull request {target}. Reuse the existing PR branch and push follow-up commits; resolve findings you fix.";
    }
}
