using System.Text.Json;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.GitHub;

public sealed record GitHubCommentWebhookReadResult(
    GitHubCommentCandidate? Candidate,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsMalformed => ErrorCode is not null;
}

public static class GitHubCommentWebhookReader
{
    public const string IssueCommentEvent = "issue_comment";
    public const string ReviewCommentEvent = "pull_request_review_comment";

    public static bool IsCommentEvent(string? eventName) =>
        string.Equals(eventName, IssueCommentEvent, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventName, ReviewCommentEvent, StringComparison.OrdinalIgnoreCase);

    public static GitHubCommentWebhookReadResult Read(string eventName, string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Malformed("githubWebhookPayloadInvalid", "GitHub comment payload was not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (!string.Equals(ReadString(root, "action"), "created", StringComparison.OrdinalIgnoreCase))
            {
                return NotApplicable();
            }

            var isReviewComment = string.Equals(eventName, ReviewCommentEvent, StringComparison.OrdinalIgnoreCase);
            int? pullRequestNumber;
            if (isReviewComment)
            {
                if (!TryGetObject(root, "pull_request", out var pullRequest))
                {
                    return NotApplicable();
                }

                pullRequestNumber = ReadInt32(pullRequest, "number");
            }
            else
            {
                if (!TryGetObject(root, "issue", out var issue) || !issue.TryGetProperty("pull_request", out _))
                {
                    return NotApplicable();
                }

                pullRequestNumber = ReadInt32(issue, "number");
            }

            if (!TryGetObject(root, "comment", out var comment))
            {
                return Malformed("githubCommentMissing", "GitHub comment payload did not include a comment.");
            }

            var commentBody = ReadString(comment, "body");
            if (commentBody is null)
            {
                return Malformed("githubCommentBodyMissing", "GitHub comment payload did not include comment.body.");
            }

            var commentId = ReadInt64(comment, "id");
            var repository = SourceProjectKey.NormalizeGitHubRepository(ReadNestedString(root, "repository", "full_name"));
            if (commentId is null || repository is null || pullRequestNumber is null or <= 0)
            {
                return Malformed(
                    "githubCommentCommandTargetInvalid",
                    "GitHub comment payload requires comment.id, an owner/name repository.full_name, and a positive pull request number.");
            }

            var createdAt = comment.TryGetProperty("created_at", out var created) &&
                created.ValueKind == JsonValueKind.String &&
                created.TryGetDateTimeOffset(out var parsedCreatedAt)
                    ? parsedCreatedAt
                    : default;
            return new GitHubCommentWebhookReadResult(new GitHubCommentCandidate(
                isReviewComment ? GitHubCommentKind.ReviewComment : GitHubCommentKind.Conversation,
                commentId.Value,
                repository,
                pullRequestNumber.Value,
                commentBody,
                ReadNestedString(comment, "user", "login") ?? ReadNestedString(root, "sender", "login") ?? "",
                ReadNestedString(comment, "user", "type") ?? ReadNestedString(root, "sender", "type"),
                ReadString(comment, "author_association") ?? "",
                ReadString(comment, "html_url"),
                createdAt,
                isReviewComment ? ReadInt64(comment, "in_reply_to_id") : null,
                isReviewComment ? ReadString(comment, "path") : null,
                isReviewComment ? ReadInt32(comment, "line") ?? ReadInt32(comment, "original_line") : null));
        }
    }

    private static GitHubCommentWebhookReadResult NotApplicable() => new(null);

    private static GitHubCommentWebhookReadResult Malformed(string errorCode, string message) =>
        new(null, errorCode, message);

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedString(JsonElement element, string objectName, string propertyName) =>
        TryGetObject(element, objectName, out var nested)
            ? ReadString(nested, propertyName)
            : null;

    private static long? ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;
}
