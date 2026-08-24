using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.Data;

public sealed class OratorioItem
{
    public string ItemId { get; set; } = Guid.NewGuid().ToString("n");
    public string WorkspaceId { get; set; } = "default";
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public ItemKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Repository { get; set; }
    public string? Assignee { get; set; }
    public string? Branch { get; set; }
    public string? ExternalUrl { get; set; }
    public string? LabelsJson { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public bool IsDraft { get; set; }
    public string? HeadSha { get; set; }
    public SourceState SourceState { get; set; } = SourceState.Unknown;
    public SourceDetailsStatus SourceDetailsStatus { get; set; } = SourceDetailsStatus.NotRequired;
    public DateTimeOffset? SourceDetailsHydratedAt { get; set; }
    public string? SourceDetailsErrorCode { get; set; }
    public string? SourceDetailsErrorMessage { get; set; }
    public DateTimeOffset? SourceClosedAt { get; set; }
    public DateTimeOffset? SourceMergedAt { get; set; }
    public ArchiveReason? ArchiveReason { get; set; }
    public ItemState State { get; set; } = ItemState.Discovered;
    public int CurrentRound { get; set; }
    public string? CurrentRunId { get; set; }
    public string? LatestSummary { get; set; }
    public CheckState CheckState { get; set; } = CheckState.NotConfigured;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSourceSyncAt { get; set; }
    public string? ParentItemId { get; set; }
    public string? GeneratedFromDraftId { get; set; }
    public int? ShortIdInteger { get; set; }
    public string? ShortId { get; set; }
    public long BoardSortOrder { get; set; }

    public List<OratorioRound> Rounds { get; set; } = [];
    public List<OratorioRun> Runs { get; set; } = [];
    public List<OratorioComment> Comments { get; set; } = [];
    public List<OratorioDecision> Decisions { get; set; } = [];
    public List<OratorioTimelineEvent> TimelineEvents { get; set; } = [];
    public List<OratorioSourceSnapshot> SourceSnapshots { get; set; } = [];
    public List<OratorioReviewDraft> ReviewDrafts { get; set; } = [];
    public List<OratorioImplementationDraft> ImplementationDrafts { get; set; } = [];
    public List<OratorioFollowUpDraft> FollowUpDrafts { get; set; } = [];
    public List<OratorioDiscussionTurn> DiscussionTurns { get; set; } = [];
    public OratorioItem? ParentItem { get; set; }
    public List<OratorioItem> ChildItems { get; set; } = [];
}

public sealed class OratorioTaskShortIdCounter
{
    public string WorkspaceId { get; set; } = "default";
    public string Prefix { get; set; } = "";
    public int NextValue { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OratorioRound
{
    public string RoundId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public int RoundNumber { get; set; }
    public RoundStatus Status { get; set; } = RoundStatus.Open;
    public string? PromptContextJson { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioItem? Item { get; set; }
}

public sealed class OratorioRun
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public int Attempt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Queued;
    public string RunnerKind { get; set; } = "mock";
    public string? ThreadId { get; set; }
    public string? TurnId { get; set; }
    public string? AppServerEndpoint { get; set; }
    public string? AppServerIdentity { get; set; }
    public string? PromptContextJson { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Summary { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int ProgressPercent { get; set; }
    public string? StatusMessage { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? BaseWorkspacePath { get; set; }
    public string? WorktreePath { get; set; }
    public string? WorktreeBranch { get; set; }
    public string? BaseRef { get; set; }
    public string? BaseSha { get; set; }
    public WorktreeStatus WorktreeStatus { get; set; } = WorktreeStatus.NotRequired;
    public string? WorktreeErrorCode { get; set; }
    public string? WorktreeErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseAcquiredAt { get; set; }
    public DateTimeOffset? WorktreeCleanupAfterAt { get; set; }
    public DateTimeOffset? WorktreeCleanedAt { get; set; }
    public MockOutcome MockOutcome { get; set; } = MockOutcome.Success;
    public int MockDurationSeconds { get; set; } = 8;
    public RunPurpose Purpose { get; set; } = RunPurpose.ReviewAnalysis;
    public RunDispatchTrigger DispatchTrigger { get; set; } = RunDispatchTrigger.Manual;
    public string? TargetHeadSha { get; set; }
    public DeliveryPolicy DeliveryPolicy { get; set; } = DeliveryPolicy.ManualDelivery;
    public int ImplementationTurnCount { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
}

public sealed class OratorioAutoReviewRepositoryState
{
    public string Repository { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset? InitializedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OratorioGitHubCommentCommand
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString("n");
    public string? DeliveryId { get; set; }
    public string SourceCommentId { get; set; } = "";
    public string Repository { get; set; } = "";
    public int PullRequestNumber { get; set; }
    public string ActorLogin { get; set; } = "";
    public string AuthorAssociation { get; set; } = "";
    public GitHubCommentCommandKind CommandKind { get; set; } = GitHubCommentCommandKind.Review;
    public string? Focus { get; set; }
    public GitHubCommentCommandStatus Status { get; set; } = GitHubCommentCommandStatus.Queued;
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? RunId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioRun? Run { get; set; }
}

public sealed class OratorioAutoReviewItemState
{
    public string ItemId { get; set; } = "";
    public string Repository { get; set; } = "";
    public string? LastObservedHeadSha { get; set; }
    public string? LastQueuedHeadSha { get; set; }
    public string? LastQueuedRunId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRun? LastQueuedRun { get; set; }
}

public sealed class OratorioImplementationFollowUpItemState
{
    public string OriginatingItemId { get; set; } = "";
    public string? GeneratedPrItemId { get; set; }
    public string Repository { get; set; } = "";
    public string? LastObservedFindingsKey { get; set; }
    public DateTimeOffset? LastObservedCommentAt { get; set; }
    public string? LastQueuedHeadSha { get; set; }
    public string? LastQueuedRoundId { get; set; }
    public string? LastQueuedRunId { get; set; }
    public int FollowUpRoundCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public OratorioItem? OriginatingItem { get; set; }
    public OratorioItem? GeneratedPrItem { get; set; }
    public OratorioRun? LastQueuedRun { get; set; }
}

public sealed class OratorioComment
{
    public string CommentId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string? RoundId { get; set; }
    public AuthorKind AuthorKind { get; set; } = AuthorKind.Operator;
    public string AuthorName { get; set; } = "operator";
    public string Body { get; set; } = "";
    public CommentVisibility Visibility { get; set; } = CommentVisibility.Internal;
    public CommentPurpose Purpose { get; set; } = CommentPurpose.Feedback;
    public DateTimeOffset CreatedAt { get; set; }
    public string? Source { get; set; }
    public string? SourceCommentId { get; set; }
    public string? ExternalUrl { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
}

public sealed class OratorioDiscussionTurn
{
    public string DiscussionTurnId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string? RoundId { get; set; }
    public string QuestionCommentId { get; set; } = "";
    public string? ReplyCommentId { get; set; }
    public string BaseRunId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public string? TurnId { get; set; }
    public string? ModelId { get; set; }
    public string? AppServerEndpoint { get; set; }
    public DiscussionTurnStatus Status { get; set; } = DiscussionTurnStatus.Pending;
    public string? PromptContextJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioComment? QuestionComment { get; set; }
    public OratorioComment? ReplyComment { get; set; }
    public OratorioRun? BaseRun { get; set; }
}

public sealed class OratorioSourceSnapshot
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string Source { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? Repository { get; set; }
    public string? HeadSha { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset SyncedAt { get; set; }

    public OratorioItem? Item { get; set; }
}

public sealed class OratorioSourceWriteLog
{
    public string WriteId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string? RoundId { get; set; }
    public string? DecisionId { get; set; }
    public string Source { get; set; } = "";
    public SourceWriteKind Kind { get; set; }
    public string Intent { get; set; } = "";
    public SourceWriteStatus Status { get; set; } = SourceWriteStatus.Pending;
    public string? Repository { get; set; }
    public int? Number { get; set; }
    public string? HeadSha { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string? ResponseJson { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioDecision? Decision { get; set; }
}

public sealed class OratorioReviewDraft
{
    public string DraftId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string RunId { get; set; } = "";
    public ReviewDraftStatus Status { get; set; } = ReviewDraftStatus.Draft;
    public string SummaryBody { get; set; } = "";
    public int MajorCount { get; set; }
    public int MinorCount { get; set; }
    public int SuggestionCount { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? SourceWriteId { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioRun? Run { get; set; }
    public OratorioSourceWriteLog? SourceWrite { get; set; }
    public List<OratorioReviewDraftComment> Comments { get; set; } = [];
}

public sealed class OratorioReviewDraftComment
{
    public string DraftCommentId { get; set; } = Guid.NewGuid().ToString("n");
    public string DraftId { get; set; } = "";
    public string Severity { get; set; } = "YELLOW";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public string Side { get; set; } = "RIGHT";
    public int? StartLine { get; set; }
    public string? StartSide { get; set; }
    public string? SuggestionOriginal { get; set; }
    public string? SuggestionReplacement { get; set; }
    public string? CommentOnlyReason { get; set; }
    public ReviewDraftCommentStatus Status { get; set; } = ReviewDraftCommentStatus.Accepted;
    public string? Warning { get; set; }
    public ReviewFindingResolutionState ResolutionState { get; set; } = ReviewFindingResolutionState.Open;
    public ReviewFindingResolutionKind? ResolutionKind { get; set; }
    public AuthorKind? ResolvedByKind { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedInRunId { get; set; }
    public string? ResolvedViaDiscussionTurnId { get; set; }
    public string? RemoteThreadId { get; set; }
    public string? RemoteResolveWriteId { get; set; }

    public OratorioReviewDraft? Draft { get; set; }
}

public sealed class OratorioImplementationDraft
{
    public string DraftId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string RunId { get; set; } = "";
    public ImplementationDraftStatus Status { get; set; } = ImplementationDraftStatus.Draft;
    public DeliveryPolicy DeliveryPolicy { get; set; } = DeliveryPolicy.ManualDelivery;
    public string Summary { get; set; } = "";
    public string TestsJson { get; set; } = "[]";
    public string RisksJson { get; set; } = "[]";
    public string ChangedFilesJson { get; set; } = "[]";
    public string ProposedCommitMessage { get; set; } = "";
    public string ProposedPrTitle { get; set; } = "";
    public string ProposedPrBody { get; set; } = "";
    public string? BranchName { get; set; }
    public string? CommitSha { get; set; }
    public string? PullRequestItemId { get; set; }
    public string? PullRequestUrl { get; set; }
    public string? SourceWriteId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioRun? Run { get; set; }
    public OratorioItem? PullRequestItem { get; set; }
    public OratorioSourceWriteLog? SourceWrite { get; set; }
}

public sealed class OratorioFollowUpDraft
{
    public string DraftId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string RunId { get; set; } = "";
    public FollowUpDraftStatus Status { get; set; } = FollowUpDraftStatus.Draft;
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Rationale { get; set; }
    public string? Repository { get; set; }
    public string? Assignee { get; set; }
    public string? Branch { get; set; }
    public string? LabelsJson { get; set; }
    public string? CreatedItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioRun? Run { get; set; }
    public OratorioItem? CreatedItem { get; set; }
}

public sealed class OratorioDecision
{
    public string DecisionId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string RoundId { get; set; } = "";
    public DecisionType Decision { get; set; }
    public string AuthorName { get; set; } = "operator";
    public string? CommentId { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioComment? Comment { get; set; }
}

public sealed class OratorioTimelineEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public string? RoundId { get; set; }
    public string? RunId { get; set; }
    public TimelineEventKind Kind { get; set; }
    public ActorKind ActorKind { get; set; }
    public string ActorName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public OratorioItem? Item { get; set; }
    public OratorioRound? Round { get; set; }
    public OratorioRun? Run { get; set; }
}

public sealed class OratorioConfigurationChange
{
    public string ChangeId { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset CreatedAt { get; set; }
    public string Actor { get; set; } = "local-admin";
    public string? RemoteAddress { get; set; }
    public string BaseRevision { get; set; } = "";
    public string NewRevision { get; set; } = "";
    public string ChangedFieldsJson { get; set; } = "[]";
    public string ImpactWarningsJson { get; set; } = "[]";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}

public sealed class OratorioGitHubSyncJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("n");
    public GitHubSyncTrigger Trigger { get; set; } = GitHubSyncTrigger.Manual;
    public GitHubSyncMode Mode { get; set; } = GitHubSyncMode.Incremental;
    public GitHubSyncStatus Status { get; set; } = GitHubSyncStatus.Queued;
    public int RepositoriesTotal { get; set; }
    public int RepositoriesCompleted { get; set; }
    public int RepositoriesFailed { get; set; }
    public int IssuesImported { get; set; }
    public int PullRequestsImported { get; set; }
    public int CommentsImported { get; set; }
    public int Skipped { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<OratorioGitHubSyncRepositoryRun> RepositoryRuns { get; set; } = [];
}

public sealed class OratorioGitHubSyncRepositoryRun
{
    public string RepositoryRunId { get; set; } = Guid.NewGuid().ToString("n");
    public string JobId { get; set; } = "";
    public string Repository { get; set; } = "";
    public GitHubSyncRepositoryStatus Status { get; set; } = GitHubSyncRepositoryStatus.Queued;
    public GitHubSyncRepositoryPhase Phase { get; set; } = GitHubSyncRepositoryPhase.Queued;
    public int IssuesDiscovered { get; set; }
    public int PullRequestsDiscovered { get; set; }
    public int IssuesImported { get; set; }
    public int PullRequestsImported { get; set; }
    public int CommentsImported { get; set; }
    public int Skipped { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioGitHubSyncJob? Job { get; set; }
}

public sealed class OratorioGitLabSyncJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("n");
    public SourceSyncTrigger Trigger { get; set; } = SourceSyncTrigger.Manual;
    public SourceSyncMode Mode { get; set; } = SourceSyncMode.Incremental;
    public SourceSyncStatus Status { get; set; } = SourceSyncStatus.Queued;
    public int ProjectsTotal { get; set; }
    public int ProjectsCompleted { get; set; }
    public int ProjectsFailed { get; set; }
    public int IssuesImported { get; set; }
    public int MergeRequestsImported { get; set; }
    public int CommentsImported { get; set; }
    public int Skipped { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<OratorioGitLabSyncProjectRun> ProjectRuns { get; set; } = [];
}

public sealed class OratorioGitLabSyncProjectRun
{
    public string ProjectRunId { get; set; } = Guid.NewGuid().ToString("n");
    public string JobId { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public SourceSyncProjectStatus Status { get; set; } = SourceSyncProjectStatus.Queued;
    public SourceSyncProjectPhase Phase { get; set; } = SourceSyncProjectPhase.Queued;
    public int IssuesDiscovered { get; set; }
    public int MergeRequestsDiscovered { get; set; }
    public int IssuesImported { get; set; }
    public int MergeRequestsImported { get; set; }
    public int CommentsImported { get; set; }
    public int Skipped { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public OratorioGitLabSyncJob? Job { get; set; }
}

public sealed class OratorioSourceSyncSchedule
{
    public string Provider { get; set; } = "";
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 300;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastScheduledAt { get; set; }
    public string? LastJobId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
