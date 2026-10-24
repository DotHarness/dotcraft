using System.Globalization;
using System.Text;
using System.Text.Json;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public sealed partial class GitHubWriteService
{
    public const string DiscussionReplyIntent = "discussionReply";

    public static string DiscussionReplyMarker(string discussionTurnId) =>
        $"<!-- oratorio:discussion-reply:{discussionTurnId} -->";

    public async Task RecordDiscussionReplyAsync(OratorioDiscussionTurn turn, string reply, CancellationToken ct)
    {
        if (turn.Item is null || !TryResolveGitHubTarget(turn.Item, out var repository, out var number))
        {
            return;
        }

        var marker = DiscussionReplyMarker(turn.DiscussionTurnId);
        var threadRoot = turn.OriginCommentKind == GitHubCommentKind.ReviewComment ? turn.OriginThreadRootCommentId : null;
        var request = new Dictionary<string, object?>
        {
            ["body"] = ComposeDiscussionReply(turn, reply, marker, inThread: threadRoot is not null),
            ["marker"] = marker,
            ["discussionTurnId"] = turn.DiscussionTurnId
        };
        if (threadRoot is not null)
        {
            request["inReplyTo"] = threadRoot.Value;
        }

        var now = clock.UtcNow;
        var write = new OratorioSourceWriteLog
        {
            ItemId = turn.ItemId,
            RoundId = turn.RoundId,
            Source = "github",
            Kind = threadRoot is null ? SourceWriteKind.IssueComment : SourceWriteKind.PullRequestReviewCommentReply,
            Intent = DiscussionReplyIntent,
            Status = SourceWriteStatus.Pending,
            Repository = repository.FullName,
            Number = number,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.SourceWriteLogs.Add(write);
        turn.ReplySourceWriteId = write.WriteId;
        AddTimeline(write, TimelineEventKind.SourceWriteQueued, "GitHub write queued", null);
        await TryExecuteAsync(write, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<GitHubWriteResponse> CreateIssueCommentAsync(GitHubRepositoryRef repository, OratorioSourceWriteLog write, CancellationToken ct)
    {
        var marker = ReadOptionalString(write.RequestJson, "marker");
        if (!string.IsNullOrWhiteSpace(marker) && write.AttemptCount > 1)
        {
            var comments = await client.ListIssueCommentsAsync(repository, write.Number!.Value, ct);
            var published = comments.FirstOrDefault(x => x.Body.Contains(marker, StringComparison.Ordinal));
            if (published is not null)
            {
                return FoundByMarker(published.Id, published.HtmlUrl);
            }
        }

        return await client.CreateIssueCommentAsync(repository, write.Number!.Value, ReadString(write.RequestJson, "body"), ct);
    }

    private async Task<GitHubWriteResponse> CreateReviewCommentReplyAsync(GitHubRepositoryRef repository, OratorioSourceWriteLog write, CancellationToken ct)
    {
        var marker = ReadString(write.RequestJson, "marker");
        if (write.AttemptCount > 1)
        {
            var comments = await client.ListPullRequestReviewCommentsAsync(repository, write.Number!.Value, ct);
            var published = comments.FirstOrDefault(x => x.Body.Contains(marker, StringComparison.Ordinal));
            if (published is not null)
            {
                return FoundByMarker(published.Id, published.HtmlUrl);
            }
        }

        using var request = JsonDocument.Parse(write.RequestJson);
        var inReplyTo = request.RootElement.GetProperty("inReplyTo").GetInt64();
        return await client.CreatePullRequestReviewCommentReplyAsync(
            repository,
            write.Number!.Value,
            inReplyTo,
            ReadString(write.RequestJson, "body"),
            ct);
    }

    private static GitHubWriteResponse FoundByMarker(long id, string? htmlUrl) =>
        new(
            id.ToString(CultureInfo.InvariantCulture),
            htmlUrl,
            JsonSerializer.Serialize(new { id, html_url = htmlUrl, foundByMarker = true }, JsonOptions));

    private static string ComposeDiscussionReply(OratorioDiscussionTurn turn, string reply, string marker, bool inThread)
    {
        var body = new StringBuilder();
        body.AppendLine(inThread || string.IsNullOrWhiteSpace(turn.OriginUrl)
            ? $"Replying to @{turn.OriginAuthorLogin}"
            : $"Replying to @{turn.OriginAuthorLogin}: {turn.OriginUrl}");
        body.AppendLine();
        body.AppendLine(reply.Trim());
        body.AppendLine();
        body.Append(marker);
        return body.ToString();
    }
}
