using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.GitHub;

public sealed partial class GitHubWriteService
{
    private async Task TryCaptureReviewThreadMappingAsync(OratorioSourceWriteLog write, GitHubRepositoryRef repository, CancellationToken ct)
    {
        if (write.Intent != "reviewDraftPublish" || write.Kind != SourceWriteKind.PullRequestReview || write.Number is null)
        {
            return;
        }

        try
        {
            var draftId = db.ChangeTracker.Entries<OratorioReviewDraft>()
                .Select(entry => entry.Entity)
                .FirstOrDefault(draft => draft.SourceWriteId == write.WriteId)?.DraftId
                ?? await db.ReviewDrafts.Where(draft => draft.SourceWriteId == write.WriteId).Select(draft => draft.DraftId).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(draftId))
            {
                return;
            }

            var findings = (await db.ReviewDraftComments
                    .Where(c => c.DraftId == draftId && c.Status == ReviewDraftCommentStatus.Accepted)
                    .ToListAsync(ct))
                .ToDictionary(c => c.DraftCommentId, StringComparer.Ordinal);
            if (findings.Count == 0)
            {
                return;
            }

            var threads = await client.ListPullRequestReviewThreadsAsync(repository, write.Number.Value, ct);
            foreach (var thread in threads)
            {
                foreach (var body in thread.CommentBodies)
                {
                    if (ReviewFindingMarker.Extract(body) is { } findingId && findings.TryGetValue(findingId, out var finding))
                    {
                        finding.RemoteThreadId = thread.Id;
                    }
                }
            }

            var comments = await client.ListPullRequestReviewCommentsAsync(repository, write.Number.Value, ct);
            foreach (var comment in comments.Where(x => x.InReplyToId is null))
            {
                if (ReviewFindingMarker.Extract(comment.Body) is { } findingId && findings.TryGetValue(findingId, out var finding))
                {
                    finding.RemoteRootCommentId = comment.Id;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
        }
    }
}
