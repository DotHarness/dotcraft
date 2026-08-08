using System.Text.Json;
using Oratorio.Server.Domain;

namespace Oratorio.Server.Sources;

/// <summary>
/// Maps provider-specific source write payload kinds to source-neutral audit categories.
/// </summary>
public static class SourceWriteCanonicalKinds
{
    public const string SourceComment = "sourceComment";
    public const string ReviewSummary = "reviewSummary";
    public const string InlineReviewDiscussion = "inlineReviewDiscussion";
    public const string ExternalStatus = "externalStatus";
    public const string LocalCommit = "localCommit";
    public const string BranchPush = "branchPush";
    public const string ReviewTargetCreation = "reviewTargetCreation";
    public const string ProviderApproval = "providerApproval";
    public const string ResolveReviewThread = "resolveReviewThread";

    public static string From(SourceWriteKind kind, string? requestJson = null) =>
        kind switch
        {
            SourceWriteKind.IssueComment => SourceComment,
            SourceWriteKind.PullRequestReview => HasInlineReviewComments(requestJson) ? InlineReviewDiscussion : ReviewSummary,
            SourceWriteKind.CheckRun => ExternalStatus,
            SourceWriteKind.LocalCommit => LocalCommit,
            SourceWriteKind.BranchPush => BranchPush,
            SourceWriteKind.PullRequestCreation => ReviewTargetCreation,
            SourceWriteKind.MergeRequestNote => ReviewSummary,
            SourceWriteKind.MergeRequestDiscussion => InlineReviewDiscussion,
            SourceWriteKind.CommitStatus => ExternalStatus,
            SourceWriteKind.MergeRequestCreation => ReviewTargetCreation,
            SourceWriteKind.ResolveReviewThread => ResolveReviewThread,
            _ => kind.ToString()
        };

    private static bool HasInlineReviewComments(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson);
            return document.RootElement.TryGetProperty("comments", out var comments) &&
                comments.ValueKind == JsonValueKind.Array &&
                comments.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
