using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public sealed partial class GitHubWriteService
{
    private const string ReviewGatePublicationIntent = "reviewGatePublication";
    private static readonly SemaphoreSlim[] CheckWriteGates = Enumerable.Range(0, 32)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    internal async Task RecordReviewGatePublicationAsync(OratorioSourceWriteLog publication, CancellationToken ct)
    {
        if (publication.HeadSha is null || !await IsCurrentReviewGateAsync(publication, ct))
        {
            return;
        }

        var checkRunId = await FindLatestReviewGateCheckRunIdAsync(publication.ItemId, publication.RoundId!, publication.HeadSha, ct);
        if (checkRunId is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var write = new OratorioSourceWriteLog
        {
            ItemId = publication.ItemId,
            RoundId = publication.RoundId,
            Source = "github",
            Kind = SourceWriteKind.CheckRun,
            Intent = ReviewGatePublicationIntent,
            Status = SourceWriteStatus.Pending,
            Repository = publication.Repository,
            Number = publication.Number,
            HeadSha = publication.HeadSha,
            RequestJson = JsonSerializer.Serialize(new { name = CheckRunName, status = "completed", conclusion = "neutral", checkRunId }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.SourceWriteLogs.Add(write);
        AddTimeline(write, TimelineEventKind.SourceWriteQueued, "GitHub write queued", null);
        await TryExecuteAsync(write, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<GitHubWriteResponse?> ExecuteCheckRunAsync(GitHubRepositoryRef repository, OratorioSourceWriteLog write, CancellationToken ct)
    {
        var gate = CheckWriteGates[(uint)HashCode.Combine(write.ItemId, write.RoundId) % (uint)CheckWriteGates.Length];
        await gate.WaitAsync(ct);
        try
        {
            var request = JsonNode.Parse(write.RequestJson)!.AsObject();
            var checkRunId = request["checkRunId"]?.GetValue<string>();

            if (IsReviewGateAnalysisUpdate(write))
            {
                if ((write.Intent == ReviewGatePublicationIntent || write.AttemptCount > 1) &&
                    !await IsCurrentReviewGateAsync(write, ct))
                {
                    return null;
                }

                checkRunId ??= await FindLatestReviewGateCheckRunIdAsync(write.ItemId, write.RoundId!, write.HeadSha, ct);
                var outcome = await db.ReviewDrafts.AsNoTracking()
                    .Where(draft => draft.RoundId == write.RoundId && draft.SourceWrite!.HeadSha == write.HeadSha)
                    .OrderByDescending(draft => draft.CreatedAt)
                    .Select(draft => (SourceWriteStatus?)draft.SourceWrite!.Status)
                    .FirstOrDefaultAsync(ct);

                var (title, summary) = outcome switch
                {
                    SourceWriteStatus.Failed => (
                        "Oratorio analysis completed: review not published",
                        "Oratorio completed the review analysis, but the review was not published. See Oratorio for details."),
                    SourceWriteStatus.Succeeded => (
                        "Oratorio review published",
                        "Oratorio completed the review analysis and published the review. Merge control remains with GitHub branch protection and repository collaborators."),
                    _ => (
                        "Oratorio review completed",
                        "Oratorio completed the review analysis. Merge control has returned to GitHub branch protection and repository collaborators.")
                };
                request["title"] = title;
                request["summary"] = summary;
                if (checkRunId is not null)
                {
                    request["checkRunId"] = checkRunId;
                }

                write.RequestJson = request.ToJsonString(JsonOptions);
            }

            var checkStatus = request["status"]?.GetValue<string>() ?? "completed";
            var conclusion = request["conclusion"]?.GetValue<string>();
            var checkTitle = request["title"]?.GetValue<string>() ?? "Oratorio review";
            var checkSummary = request["summary"]?.GetValue<string>() ?? throw new JsonException("Missing required property 'summary'.");
            return string.IsNullOrWhiteSpace(checkRunId)
                ? await client.CreateCheckRunAsync(repository, CheckRunName, write.HeadSha!, checkStatus, conclusion, checkTitle, checkSummary, ct)
                : await client.UpdateCheckRunAsync(repository, checkRunId, checkStatus, conclusion, checkTitle, checkSummary, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsReviewGateAnalysisUpdate(OratorioSourceWriteLog write) =>
        write.Intent is ReviewGateCompleteIntent or ReviewGatePublicationIntent;

    private Task<bool> IsCurrentReviewGateAsync(OratorioSourceWriteLog write, CancellationToken ct) =>
        db.Rounds.AnyAsync(round =>
            round.RoundId == write.RoundId &&
            round.Item!.State == ItemState.AwaitingReview &&
            !round.Item.IsDraft &&
            round.Item.CurrentRound == round.RoundNumber &&
            round.Item.HeadSha == write.HeadSha, ct);
}
