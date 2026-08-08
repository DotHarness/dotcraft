using System.Text.Json.Serialization;
using Oratorio.Server.Domain;

namespace Oratorio.Server.GitLab;

public enum GitLabListState
{
    Opened,
    Closed,
    Merged,
    All
}

public sealed record GitLabProjectRef(string Path)
{
    public string ProjectPath => Path;
    public string EncodedPath => Uri.EscapeDataString(Path);

    public static bool TryParse(string? value, out GitLabProjectRef project)
    {
        project = new GitLabProjectRef("");
        var normalized = Oratorio.Server.Sources.SourceProjectKey.NormalizeGitLabProjectPath(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        project = new GitLabProjectRef(normalized);
        return true;
    }
}

public sealed record GitLabProject(
    long Id,
    string PathWithNamespace,
    string? WebUrl,
    string? DefaultBranch = null);

public sealed record GitLabIssue(
    long Id,
    int Iid,
    string Title,
    string? Description,
    string State,
    string? WebUrl,
    GitLabUser? Author,
    IReadOnlyList<string>? Labels,
    IReadOnlyList<GitLabUser>? Assignees,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

public sealed record GitLabMergeRequest(
    long Id,
    int Iid,
    string Title,
    string? Description,
    string State,
    string? WebUrl,
    GitLabUser? Author,
    IReadOnlyList<string>? Labels,
    IReadOnlyList<GitLabUser>? Assignees,
    IReadOnlyList<GitLabUser>? Reviewers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? MergedAt,
    bool Draft,
    string? SourceBranch,
    string? TargetBranch,
    string? Sha,
    GitLabDiffRefs? DiffRefs,
    string? DetailedMergeStatus,
    string? MergeStatus);

public sealed record GitLabUser(
    long? Id,
    string? Username,
    string? Name,
    string? WebUrl);

public sealed record GitLabDiffRefs(
    string? BaseSha,
    string? HeadSha,
    string? StartSha);

public sealed record GitLabNote(
    long Id,
    string Body,
    GitLabUser? Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool System,
    string? Type,
    [property: JsonPropertyName("noteable_iid")] int? NoteableIid,
    string? Url);

public sealed record GitLabDiscussion(
    string Id,
    bool IndividualNote,
    IReadOnlyList<GitLabDiscussionNote> Notes);

public sealed record GitLabDiscussionNote(
    long Id,
    string Body,
    GitLabUser? Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool System,
    string? Type,
    string? Url,
    GitLabPosition? Position);

public sealed record GitLabPosition(
    string? PositionType,
    string? OldPath,
    string? NewPath,
    int? OldLine,
    int? NewLine);

public sealed record GitLabMergeRequestDiff(
    string OldPath,
    string NewPath,
    string? Diff,
    bool NewFile,
    bool RenamedFile,
    bool DeletedFile,
    bool? Collapsed = null,
    bool? TooLarge = null);

public sealed record GitLabWriteResponse(string ExternalId, string? ExternalUrl, string ResponseJson);

public sealed record GitLabMergeRequestPosition(
    string BaseSha,
    string HeadSha,
    string StartSha,
    string OldPath,
    string NewPath,
    int? OldLine,
    int? NewLine);

public sealed record GitLabMergeRequestDiscussionCommentWrite(
    string Body,
    GitLabMergeRequestPosition Position,
    string? FindingId = null);

public sealed record GitLabMergeRequestCreateResponse(
    long Id,
    int Iid,
    string Title,
    string? Description,
    string State,
    string? WebUrl,
    string? SourceBranch,
    string? TargetBranch,
    string? Sha,
    GitLabDiffRefs? DiffRefs);

public sealed record GitLabSyncItem(
    string ProjectPath,
    string ProjectKey,
    string ExternalId,
    string Kind,
    int Iid,
    string Title,
    string? Description,
    string? WebUrl,
    string? Assignee,
    string? Branch,
    string? TargetBranch,
    IReadOnlyList<string> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDraft,
    string? HeadSha,
    SourceState SourceState,
    DateTimeOffset? SourceClosedAt,
    DateTimeOffset? SourceMergedAt,
    string PayloadJson,
    IReadOnlyList<GitLabImportedComment> Comments);

public sealed record GitLabImportedComment(
    string SourceCommentId,
    string AuthorName,
    string Body,
    string? Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GitLabProjectSyncResult(
    int IssuesDiscovered,
    int MergeRequestsDiscovered,
    int Issues,
    int MergeRequests,
    int Comments,
    int Skipped);

public interface IGitLabProjectSyncProgress
{
    Task SetPhaseAsync(SourceSyncProjectPhase phase, CancellationToken ct);
    Task SetDiscoveredAsync(int issues, int mergeRequests, int skipped, CancellationToken ct);
    Task SetImportedAsync(int issues, int mergeRequests, int comments, int skipped, CancellationToken ct);
}
