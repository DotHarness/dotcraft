using System.Text.Json.Serialization;

namespace Oratorio.Server.GitHub;

public enum GitHubListState
{
    Open,
    Closed,
    All
}

public sealed record GitHubRepositoryRef(string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";

    public static bool TryParse(string value, out GitHubRepositoryRef repository)
    {
        repository = new GitHubRepositoryRef("", "");
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        repository = new GitHubRepositoryRef(parts[0], parts[1]);
        return true;
    }
}

public sealed record GitHubIssue(
    long Id,
    int Number,
    string Title,
    string? Body,
    string State,
    string HtmlUrl,
    GitHubUser? User,
    IReadOnlyList<GitHubLabel> Labels,
    IReadOnlyList<GitHubUser> Assignees,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    GitHubPullMarker? PullRequest);

public sealed record GitHubPullRequest(
    long Id,
    int Number,
    string Title,
    string? Body,
    string State,
    string HtmlUrl,
    GitHubUser? User,
    IReadOnlyList<GitHubLabel> Labels,
    IReadOnlyList<GitHubUser> Assignees,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? MergedAt,
    bool Draft,
    GitHubBranchRef Head,
    GitHubBranchRef Base);

public sealed record GitHubComment(
    long Id,
    string Body,
    string HtmlUrl,
    GitHubUser? User,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GitHubReview(
    long Id,
    string? Body,
    string? HtmlUrl,
    string State,
    GitHubUser? User,
    DateTimeOffset SubmittedAt);

public sealed record GitHubReviewComment(
    long Id,
    string Body,
    string HtmlUrl,
    string Path,
    int? Line,
    int? OriginalLine,
    GitHubUser? User,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GitHubUser(string Login);

public sealed record GitHubLabel(string Name);

public sealed record GitHubPullMarker([property: JsonPropertyName("html_url")] string? HtmlUrl);

public sealed record GitHubBranchRef(string Ref, string Sha);

public sealed record GitHubInstallationTokenResponse(string Token, DateTimeOffset ExpiresAt);

public sealed record GitHubWriteResponse(string ExternalId, string? ExternalUrl, string ResponseJson);

public sealed record GitHubReviewThread(string Id, bool IsResolved, IReadOnlyList<string> CommentBodies);

public sealed record GitHubPullRequestCreateResponse(
    long Id,
    int Number,
    string HtmlUrl,
    string Title,
    GitHubBranchRef Head,
    GitHubBranchRef Base);

public sealed record GitHubChangedFile(
    string Filename,
    string Status,
    int Additions,
    int Deletions,
    int Changes = 0,
    string? Patch = null,
    [property: JsonPropertyName("previous_filename")] string? PreviousFilename = null);

public sealed record GitHubPullRequestReviewCommentWrite(
    string Path,
    string Body,
    int Line,
    string Side,
    int? StartLine,
    string? StartSide);

public sealed record GitHubSyncItem(
    string Repository,
    string ExternalId,
    string Kind,
    int Number,
    string Title,
    string? Body,
    string? HtmlUrl,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDraft,
    string? HeadSha,
    Oratorio.Server.Domain.SourceState SourceState,
    DateTimeOffset? SourceClosedAt,
    DateTimeOffset? SourceMergedAt,
    string PayloadJson,
    IReadOnlyList<GitHubImportedComment> Comments);

public sealed record GitHubImportedComment(
    string SourceCommentId,
    string AuthorName,
    string Body,
    string? HtmlUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GitHubRepositorySyncResult(
    int IssuesDiscovered,
    int PullRequestsDiscovered,
    int Issues,
    int PullRequests,
    int Comments,
    int Skipped);

public interface IGitHubRepositorySyncProgress
{
    Task SetPhaseAsync(Oratorio.Server.Domain.GitHubSyncRepositoryPhase phase, CancellationToken ct);
    Task SetDiscoveredAsync(int issues, int pullRequests, int skipped, CancellationToken ct);
    Task SetImportedAsync(int issues, int pullRequests, int comments, int skipped, CancellationToken ct);
}
