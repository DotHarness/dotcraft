using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public sealed partial class GitHubWriteService
{
    private const string ReviewGatePublicationFailedIntent = "reviewGatePublicationFailed";
    private const string ReviewGatePublicationRecoveredIntent = "reviewGatePublicationRecovered";

    internal async Task RecordReviewGatePublicationAsync(OratorioSourceWriteLog publication, bool automatic, CancellationToken ct)
    {
        var failed = publication.Status == SourceWriteStatus.Failed;
        if (publication.Source != "github" || publication.HeadSha is null || (failed && !automatic))
        {
            return;
        }

        var checkWrites = db.SourceWriteLogs.AsNoTracking()
            .Where(write => write.ItemId == publication.ItemId &&
                write.RoundId == publication.RoundId &&
                write.HeadSha == publication.HeadSha &&
                write.Source == "github" &&
                write.Kind == SourceWriteKind.CheckRun);
        if ((!failed && !await checkWrites.AnyAsync(write => write.Intent == ReviewGatePublicationFailedIntent, ct)) ||
            !await IsCurrentReviewGateAsync(publication, ct))
        {
            return;
        }

        var checkRunId = await checkWrites
            .Where(write => write.Intent == ReviewGateCompleteIntent && write.Status == SourceWriteStatus.Succeeded)
            .Select(write => write.ExternalId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(checkRunId))
        {
            return;
        }

        var now = clock.UtcNow;
        var request = new
        {
            name = CheckRunName,
            status = "completed",
            conclusion = "neutral",
            title = failed ? "Oratorio analysis completed: review not published" : "Oratorio review published",
            summary = failed
                ? "Oratorio completed the review analysis, but automatic publication failed or was blocked. The draft is available in Oratorio to review or retry."
                : "Oratorio completed the review analysis and published the review. Merge control remains with GitHub branch protection and repository collaborators.",
            checkRunId
        };
        var write = new OratorioSourceWriteLog
        {
            ItemId = publication.ItemId,
            RoundId = publication.RoundId,
            Source = "github",
            Kind = SourceWriteKind.CheckRun,
            Intent = failed ? ReviewGatePublicationFailedIntent : ReviewGatePublicationRecoveredIntent,
            Status = SourceWriteStatus.Pending,
            Repository = publication.Repository,
            Number = publication.Number,
            HeadSha = publication.HeadSha,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.SourceWriteLogs.Add(write);
        AddTimeline(write, TimelineEventKind.SourceWriteQueued, "GitHub write queued", null);
        await TryExecuteAsync(write, ct);
    }

    private async Task<bool> CanRetryReviewGatePublicationAsync(OratorioSourceWriteLog write, CancellationToken ct)
    {
        if (write.Intent is not (ReviewGatePublicationFailedIntent or ReviewGatePublicationRecoveredIntent))
        {
            return true;
        }

        return await IsCurrentReviewGateAsync(write, ct) &&
            (write.Intent != ReviewGatePublicationFailedIntent ||
                !await db.ReviewDrafts.AnyAsync(draft =>
                    draft.RoundId == write.RoundId && draft.Status == ReviewDraftStatus.Published, ct));
    }

    private Task<bool> IsCurrentReviewGateAsync(OratorioSourceWriteLog write, CancellationToken ct) =>
        db.Rounds.AnyAsync(round =>
            round.RoundId == write.RoundId &&
            round.Item!.State == ItemState.AwaitingReview &&
            !round.Item.IsDraft &&
            round.Item.CurrentRound == round.RoundNumber &&
            round.Item.HeadSha == write.HeadSha, ct);
}
