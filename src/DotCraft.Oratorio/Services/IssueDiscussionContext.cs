using System.Text;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.Services;

public sealed class IssueDiscussionContext(OratorioDbContext db)
{
    public async Task<string?> DescribeAsync(OratorioDiscussionTurn turn, CancellationToken ct)
    {
        if (turn.Item is not { Kind: ItemKind.Issue, Source: "github" } item)
        {
            return null;
        }

        var context = new StringBuilder();
        context.AppendLine("GitHub issue context:");
        context.AppendLine($"- Title: {item.Title}");
        context.AppendLine($"- Source: {item.ExternalId}");
        context.AppendLine($"- URL: {item.ExternalUrl}");
        context.AppendLine($"- State: {item.SourceState}");
        context.AppendLine($"- Repository: {item.Repository}");
        context.AppendLine();
        context.AppendLine(item.Description);
        var comments = await db.Comments.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId && x.CommentId != turn.QuestionCommentId &&
                (x.Purpose == CommentPurpose.SourceContext || x.Purpose == CommentPurpose.DiscussionReply ||
                 x.Purpose == CommentPurpose.DiscussionQuestion && x.CreatedAt <= turn.CreatedAt))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        if (comments.Count > 0)
        {
            context.AppendLine();
            context.AppendLine("Discussion history (source material):");
            foreach (var comment in comments)
            {
                context.AppendLine($"{comment.AuthorName} ({comment.ExternalUrl ?? comment.Purpose.ToString()}):");
                context.AppendLine(comment.Body);
            }
        }

        return context.ToString();
    }
}
