using System.Text.Json.Serialization;

namespace DotCraft.Oratorio.Domain;

public enum ItemKind
{
    Issue,
    PullRequest,
    LocalTask
}

public enum ItemState
{
    Discovered,
    Dispatching,
    Running,
    AwaitingReview,
    Approved,
    Rejected,
    Failed,
    Archived
}

public enum TaskStatus
{
    [JsonStringEnumMemberName("todo")]
    Todo,
    [JsonStringEnumMemberName("in_progress")]
    InProgress,
    [JsonStringEnumMemberName("in_review")]
    InReview,
    [JsonStringEnumMemberName("done")]
    Done,
    [JsonStringEnumMemberName("cancelled")]
    Cancelled
}

public enum RoundStatus
{
    Open,
    Running,
    AwaitingReview,
    Approved,
    ChangesRequested,
    Superseded,
    Rejected,
    Cancelled,
    Failed
}

public enum RunStatus
{
    Queued,
    Dispatching,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}

public enum WorktreeStatus
{
    NotRequired,
    Preparing,
    Ready,
    CleanupPending,
    Cleaned,
    Failed
}

public enum MockOutcome
{
    Success,
    Fail,
    Timeout
}

public enum CheckState
{
    NotConfigured,
    Pending,
    Attention,
    Passing,
    Failing
}

public enum SourceState
{
    Open,
    Closed,
    Merged,
    Unknown
}

public enum SourceDetailsStatus
{
    NotRequired,
    Stale,
    Current,
    Failed
}

public enum ArchiveReason
{
    Manual,
    SourceClosed,
    SourceMerged
}

public enum AuthorKind
{
    Operator,
    Source,
    Agent,
    System
}

public enum CommentVisibility
{
    Internal,
    Source
}

public enum CommentPurpose
{
    Feedback,
    DiscussionQuestion,
    DiscussionReply,
    SourceContext,
    SystemNote
}

public enum DiscussionTurnStatus
{
    Pending,
    Running,
    Succeeded,
    Failed
}

public enum DecisionType
{
    Approve,
    RequestChanges,
    Reject,
    Reopen,
    ReReview
}

public enum TimelineEventKind
{
    SourceSynced,
    RoundCreated,
    RunQueued,
    RunStarted,
    RunCompleted,
    RunCancelled,
    RunFailed,
    CommentAdded,
    DecisionRecorded,
    CheckUpdated,
    ItemReopened,
    ItemUpdated,
    ItemArchived,
    SourceWriteQueued,
    SourceWriteSucceeded,
    SourceWriteFailed,
    ReviewFindingResolved,
    ReviewFindingReopened
}

public enum ActorKind
{
    Operator,
    System,
    MockRunner,
    Source,
    Agent
}

public enum SourceWriteKind
{
    IssueComment,
    PullRequestReview,
    CheckRun,
    LocalCommit,
    BranchPush,
    PullRequestCreation,
    MergeRequestNote,
    MergeRequestDiscussion,
    CommitStatus,
    MergeRequestCreation,
    PullRequestUpdate,
    ResolveReviewThread
}

public enum SourceWriteStatus
{
    Pending,
    Succeeded,
    Failed
}

public enum SourceSyncTrigger
{
    Manual,
    Webhook,
    Scheduled
}

public enum SourceSyncMode
{
    Incremental,
    Full
}

public enum SourceSyncStatus
{
    Queued,
    Running,
    Succeeded,
    PartialFailed,
    Failed
}

public enum SourceSyncProjectStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public enum SourceSyncProjectPhase
{
    Queued,
    Fetching,
    Importing,
    Done,
    Failed
}

public enum ReviewDraftStatus
{
    Draft,
    Published,
    Discarded,
    PublishFailed
}

public enum ReviewDraftCommentStatus
{
    Accepted,
    Skipped
}

public enum ReviewFindingResolutionState
{
    Open,
    Resolved
}

public enum ReviewFindingResolutionKind
{
    Fixed,
    Dismissed
}

public enum RunPurpose
{
    ReviewAnalysis,
    Implementation
}

public enum RunDispatchTrigger
{
    Manual,
    AppBinding,
    AutoImplementation,
    AutoReview,
    AutoFollowUp,
    GitHubMentionReview
}

public enum DeliveryPolicy
{
    ManualDelivery,
    AutoPr
}

public enum ImplementationDraftStatus
{
    Draft,
    Delivered,
    DeliveryFailed
}

public enum FollowUpDraftStatus
{
    Draft,
    Created,
    Discarded
}

public enum GitHubSyncTrigger
{
    Manual,
    Webhook,
    Scheduled
}

public enum GitHubCommentCommandKind
{
    Review
}

public enum GitHubCommentCommandStatus
{
    Queued,
    Dispatched,
    Rejected,
    Failed
}

public enum GitHubSyncMode
{
    Incremental,
    Full
}

public enum GitHubSyncStatus
{
    Queued,
    Running,
    Succeeded,
    PartialFailed,
    Failed
}

public enum GitHubSyncRepositoryStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public enum GitHubSyncRepositoryPhase
{
    Queued,
    Fetching,
    Importing,
    Done,
    Failed
}
