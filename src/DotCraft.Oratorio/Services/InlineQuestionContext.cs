using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.GitHub;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Services;

public sealed class InlineQuestionContext(
    OratorioDbContext db,
    IGitHubApiClient client,
    ILogger<InlineQuestionContext> logger)
{
    private const int MaxThreadComments = 10;
    private const int MaxCommentLength = 600;

    public async Task<string?> DescribeAsync(OratorioDiscussionTurn turn, CancellationToken ct)
    {
        if (turn.OriginCommentKind != GitHubCommentKind.ReviewComment ||
            turn.OriginThreadRootCommentId is not { } rootId ||
            turn.Item is null)
        {
            return null;
        }

        var section = new StringBuilder();
        section.AppendLine($"Inline review comment on {Location(turn.OriginPath, turn.OriginLine)}.");
        var finding = await db.ReviewDraftComments.AsNoTracking()
            .Where(c =>
                c.RemoteRootCommentId == rootId &&
                c.Draft!.ItemId == turn.ItemId &&
                c.Draft.Status == ReviewDraftStatus.Published &&
                c.Status == ReviewDraftCommentStatus.Accepted)
            .FirstOrDefaultAsync(ct);
        if (finding is not null)
        {
            AppendFinding(section, finding);
            return section.ToString();
        }

        var thread = await LoadHumanThreadCommentsAsync(turn, rootId, ct);
        if (thread.Count > 0)
        {
            section.AppendLine("Earlier comments in this review thread:");
            foreach (var comment in thread)
            {
                section.AppendLine($"- @{comment.User?.Login}: {Shorten(comment.Body)}");
            }
        }

        return section.ToString();
    }

    private static void AppendFinding(StringBuilder section, OratorioReviewDraftComment finding)
    {
        var state = finding.ResolutionState == ReviewFindingResolutionState.Open
            ? "open"
            : $"resolved as {finding.ResolutionKind?.ToString().ToLowerInvariant()}";
        section.AppendLine($"The thread is Oratorio finding findingId `{finding.DraftCommentId}` ({state}): {finding.Severity} {finding.Title} ({Location(finding.Path, finding.Line)})");
        section.AppendLine(finding.Body.Trim());
        if (finding.ResolutionState == ReviewFindingResolutionState.Open)
        {
            section.AppendLine($"If the discussion concludes this finding is a non-issue or already handled, resolve it as `dismissed` with {OratorioDynamicToolCatalog.ResolveReviewFindingId}. Reply either way.");
        }
    }

    private async Task<IReadOnlyList<GitHubReviewComment>> LoadHumanThreadCommentsAsync(OratorioDiscussionTurn turn, long rootId, CancellationToken ct)
    {
        if (!GitHubWriteService.TryResolveGitHubTarget(turn.Item!, out var repository, out var number))
        {
            return [];
        }

        try
        {
            var comments = await client.ListPullRequestReviewCommentsAsync(repository, number, ct);
            return comments
                .Where(x =>
                    (x.Id == rootId || x.InReplyToId == rootId) &&
                    x.Id != turn.OriginCommentId &&
                    !string.Equals(x.User?.Type, "Bot", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.CreatedAt)
                .TakeLast(MaxThreadComments)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Review thread comments for Agent Discussion Turn {DiscussionTurnId} could not be read.", turn.DiscussionTurnId);
            return [];
        }
    }

    private static string Location(string? path, int? line) =>
        line is null ? path ?? "an unknown file" : $"{path}:{line}";

    private static string Shorten(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= MaxCommentLength ? text : text[..MaxCommentLength] + "...";
    }
}
