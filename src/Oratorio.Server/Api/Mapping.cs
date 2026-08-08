using System.Text.Json;
using Oratorio.Server.Data;
using Oratorio.Server.Domain;
using Oratorio.Server.Sources;

namespace Oratorio.Server.Api;

public static class Mapping
{
    public static ItemSummaryDto ToSummaryDto(this OratorioItem item) =>
        new(
            item.ItemId,
            item.Source,
            item.ExternalId,
            item.Kind,
            item.Title,
            item.Repository,
            item.Assignee,
            item.Branch,
            item.ExternalUrl,
            ParseLabels(item.LabelsJson),
            item.SourceUpdatedAt,
            item.LastSourceSyncAt,
            item.IsDraft,
            item.HeadSha,
            item.SourceState,
            item.SourceDetailsStatus,
            item.SourceDetailsHydratedAt,
            item.SourceDetailsErrorCode,
            item.SourceDetailsErrorMessage,
            item.SourceClosedAt,
            item.SourceMergedAt,
            item.ArchiveReason,
            item.State,
            item.CurrentRound,
            item.CheckState,
            item.LatestSummary,
            item.CreatedAt,
            item.UpdatedAt,
            item.ParentItemId,
            item.GeneratedFromDraftId,
            item.ShortId,
            TaskStatusMapping.Project(item.State),
            item.BoardSortOrder);

    public static ItemDto ToDto(this OratorioItem item) =>
        new(
            item.ItemId,
            item.WorkspaceId,
            item.Source,
            item.ExternalId,
            item.Kind,
            item.Title,
            item.Description,
            item.Repository,
            item.Assignee,
            item.Branch,
            item.ExternalUrl,
            ParseLabels(item.LabelsJson),
            item.SourceUpdatedAt,
            item.IsDraft,
            item.HeadSha,
            item.SourceState,
            item.SourceDetailsStatus,
            item.SourceDetailsHydratedAt,
            item.SourceDetailsErrorCode,
            item.SourceDetailsErrorMessage,
            item.SourceClosedAt,
            item.SourceMergedAt,
            item.ArchiveReason,
            item.State,
            item.CurrentRound,
            item.CurrentRunId,
            item.LatestSummary,
            item.CheckState,
            item.CreatedAt,
            item.UpdatedAt,
            item.LastSourceSyncAt,
            item.ParentItemId,
            item.GeneratedFromDraftId,
            item.ShortId,
            TaskStatusMapping.Project(item.State),
            item.BoardSortOrder);

    public static RoundDto ToDto(this OratorioRound round) =>
        new(round.RoundId, round.RoundNumber, round.Status, round.Summary, round.CreatedAt, round.CompletedAt);

    public static RunDto ToDto(this OratorioRun run) =>
        new(
            run.RunId,
            run.RoundId,
            run.Attempt,
            run.Status,
            run.RunnerKind,
            run.ThreadId,
            run.TurnId,
            run.AppServerEndpoint,
            run.AppServerIdentity,
            run.StartedAt,
            run.CompletedAt,
            run.Summary,
            run.ErrorCode,
            run.ErrorMessage,
            run.ProgressPercent,
            run.StatusMessage,
            run.LastHeartbeatAt,
            run.BaseWorkspacePath,
            run.WorktreePath,
            run.WorktreeBranch,
            run.BaseRef,
            run.BaseSha,
            run.WorktreeStatus,
            run.WorktreeErrorCode,
            run.WorktreeErrorMessage,
            run.RetryCount,
            run.NextRetryAt,
            run.LeaseOwner,
            run.LeaseAcquiredAt,
            run.WorktreeCleanupAfterAt,
            run.WorktreeCleanedAt,
            run.Purpose,
            run.DispatchTrigger,
            run.TargetHeadSha,
            run.DeliveryPolicy,
            run.ImplementationTurnCount);

    public static CommentDto ToDto(this OratorioComment comment) =>
        new(
            comment.CommentId,
            comment.RoundId,
            comment.AuthorKind,
            comment.AuthorName,
            comment.Body,
            comment.Visibility,
            comment.Purpose,
            comment.CreatedAt,
            comment.Source,
            comment.SourceCommentId,
            comment.ExternalUrl,
            comment.SourceUpdatedAt);

    public static DiscussionTurnDto ToDto(this OratorioDiscussionTurn turn) =>
        new(
            turn.DiscussionTurnId,
            turn.ItemId,
            turn.RoundId,
            turn.QuestionCommentId,
            turn.ReplyCommentId,
            turn.BaseRunId,
            turn.ThreadId,
            turn.TurnId,
            turn.Status,
            turn.ErrorCode,
            turn.ErrorMessage,
            turn.CreatedAt,
            turn.UpdatedAt,
            turn.StartedAt,
            turn.CompletedAt);

    public static DecisionDto ToDto(this OratorioDecision decision) =>
        new(
            decision.DecisionId,
            decision.RoundId,
            decision.Decision,
            decision.AuthorName,
            decision.CommentId,
            decision.Body,
            decision.CreatedAt);

    public static TimelineEventDto ToDto(this OratorioTimelineEvent timelineEvent) =>
        new(
            timelineEvent.EventId,
            timelineEvent.RoundId,
            timelineEvent.RunId,
            timelineEvent.Kind,
            timelineEvent.ActorKind,
            timelineEvent.ActorName,
            timelineEvent.Title,
            timelineEvent.Body,
            timelineEvent.MetadataJson,
            timelineEvent.CreatedAt);

    public static SourceWriteDto ToDto(this OratorioSourceWriteLog write) =>
        new(
            write.WriteId,
            write.ItemId,
            write.RoundId,
            write.DecisionId,
            write.Source,
            write.Kind,
            SourceWriteCanonicalKinds.From(write.Kind, write.RequestJson),
            write.Intent,
            write.Status,
            write.Repository,
            write.Number,
            write.HeadSha,
            write.RequestJson,
            write.ResponseJson,
            write.ExternalId,
            write.ExternalUrl,
            write.AttemptCount,
            write.ErrorCode,
            write.ErrorMessage,
            write.CreatedAt,
            write.UpdatedAt,
            write.CompletedAt);

    public static SourceSnapshotDto ToDto(this OratorioSourceSnapshot snapshot) =>
        new(
            snapshot.SnapshotId,
            snapshot.Source,
            snapshot.ExternalId,
            snapshot.Repository,
            snapshot.HeadSha,
            snapshot.SourceUpdatedAt,
            snapshot.PayloadJson,
            snapshot.SyncedAt);

    public static ReviewDraftDto ToDto(this OratorioReviewDraft draft)
    {
        var warnings = ParseWarnings(draft.WarningsJson);
        var comments = draft.Comments
            .OrderBy(x => x.Status == Domain.ReviewDraftCommentStatus.Accepted ? 0 : 1)
            .ThenBy(x => x.Path)
            .ThenBy(x => x.Line)
            .Select(x => x.ToDto())
            .ToList();
        return new ReviewDraftDto(
            draft.DraftId,
            draft.ItemId,
            draft.RoundId,
            draft.RunId,
            draft.Status,
            draft.SummaryBody,
            draft.MajorCount,
            draft.MinorCount,
            draft.SuggestionCount,
            warnings,
            comments.Count(x => x.Status == Domain.ReviewDraftCommentStatus.Accepted),
            warnings.Count,
            comments.Count(x => x.Status == Domain.ReviewDraftCommentStatus.Accepted && x.ResolutionState == Domain.ReviewFindingResolutionState.Resolved),
            draft.CreatedAt,
            draft.UpdatedAt,
            draft.PublishedAt,
            draft.SourceWriteId,
            comments);
    }

    public static ReviewDraftCommentDto ToDto(this OratorioReviewDraftComment comment) =>
        new(
            comment.DraftCommentId,
            comment.Severity,
            comment.Title,
            comment.Body,
            comment.Path,
            comment.Line,
            comment.Side,
            comment.StartLine,
            comment.StartSide,
            comment.SuggestionOriginal,
            comment.SuggestionReplacement,
            comment.CommentOnlyReason,
            comment.Status,
            comment.Warning,
            comment.ResolutionState,
            comment.ResolutionKind,
            comment.ResolvedByKind,
            comment.ResolutionNote,
            comment.ResolvedAt);

    public static ImplementationDraftDto ToDto(this OratorioImplementationDraft draft) =>
        new(
            draft.DraftId,
            draft.ItemId,
            draft.RoundId,
            draft.RunId,
            draft.Status,
            draft.DeliveryPolicy,
            draft.Summary,
            ParseStringList(draft.TestsJson),
            ParseStringList(draft.RisksJson),
            ParseStringList(draft.ChangedFilesJson),
            draft.ProposedCommitMessage,
            draft.ProposedPrTitle,
            draft.ProposedPrBody,
            draft.BranchName,
            draft.CommitSha,
            draft.PullRequestItemId,
            draft.PullRequestUrl,
            draft.SourceWriteId,
            draft.ErrorCode,
            draft.ErrorMessage,
            draft.CreatedAt,
            draft.UpdatedAt,
            draft.DeliveredAt);

    public static FollowUpDraftDto ToDto(this OratorioFollowUpDraft draft) =>
        new(
            draft.DraftId,
            draft.ItemId,
            draft.RoundId,
            draft.RunId,
            draft.Status,
            draft.Title,
            draft.Body,
            draft.Rationale,
            draft.Repository,
            draft.Assignee,
            draft.Branch,
            ParseStringList(draft.LabelsJson ?? "[]"),
            draft.CreatedItemId,
            draft.CreatedAt,
            draft.UpdatedAt,
            draft.ResolvedAt);

    private static IReadOnlyList<string> ParseLabels(string? labelsJson)
    {
        if (string.IsNullOrWhiteSpace(labelsJson))
        {
            return [];
        }

        try
        {
                return JsonSerializer.Deserialize<IReadOnlyList<string>>(labelsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseWarnings(string warningsJson)
    {
        return ParseStringList(warningsJson);
    }

    private static IReadOnlyList<string> ParseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
