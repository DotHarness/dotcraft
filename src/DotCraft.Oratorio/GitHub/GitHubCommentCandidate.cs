using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.GitHub;

public sealed record GitHubCommentCandidate(
    GitHubCommentKind Kind,
    long CommentId,
    string Repository,
    int PullRequestNumber,
    string Body,
    string AuthorLogin,
    string? AuthorType,
    string AuthorAssociation,
    string? HtmlUrl,
    DateTimeOffset CreatedAt,
    long? InReplyToId = null,
    string? Path = null,
    int? Line = null)
{
    public string SourceCommentId => SourceCommentIdFor(Kind, CommentId);

    public bool IsBotAuthor => string.Equals(AuthorType, "Bot", StringComparison.OrdinalIgnoreCase);

    public static string SourceCommentIdFor(GitHubCommentKind kind, long commentId) =>
        kind == GitHubCommentKind.Conversation
            ? $"issue-comment:{commentId}"
            : $"review-comment:{commentId}";

    public static GitHubCommentCandidate? FromConversationComment(string repository, GitHubComment comment)
    {
        if (!comment.HtmlUrl.Contains("/pull/", StringComparison.OrdinalIgnoreCase) ||
            TryReadTrailingNumber(comment.IssueUrl) is not { } number)
        {
            return null;
        }

        return new GitHubCommentCandidate(
            GitHubCommentKind.Conversation,
            comment.Id,
            repository,
            number,
            comment.Body,
            comment.User?.Login ?? "",
            comment.User?.Type,
            comment.AuthorAssociation ?? "",
            comment.HtmlUrl,
            comment.CreatedAt);
    }

    public static GitHubCommentCandidate? FromReviewComment(string repository, GitHubReviewComment comment)
    {
        if (TryReadTrailingNumber(comment.PullRequestUrl) is not { } number)
        {
            return null;
        }

        return new GitHubCommentCandidate(
            GitHubCommentKind.ReviewComment,
            comment.Id,
            repository,
            number,
            comment.Body,
            comment.User?.Login ?? "",
            comment.User?.Type,
            comment.AuthorAssociation ?? "",
            comment.HtmlUrl,
            comment.CreatedAt,
            comment.InReplyToId,
            comment.Path,
            comment.Line ?? comment.OriginalLine);
    }

    private static int? TryReadTrailingNumber(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var slash = url.TrimEnd('/').LastIndexOf('/');
        return slash >= 0 && int.TryParse(url.TrimEnd('/')[(slash + 1)..], out var number) && number > 0
            ? number
            : null;
    }
}
