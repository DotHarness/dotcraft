using DotCraft.Oratorio.Domain;
using System.Text.Json;
using BoardTaskStatus = DotCraft.Oratorio.Domain.TaskStatus;

namespace DotCraft.Oratorio.Api;

public sealed record ItemListResponse(IReadOnlyList<ItemSummaryDto> Items, string? NextCursor);

public sealed record TaskListResponse(IReadOnlyList<ItemSummaryDto> Tasks, string? NextCursor);

public sealed record DotCraftAppBindingHandoffRequest(string Url);

public sealed record DotCraftAppBindingStatusResponse(
    string AppId,
    bool Available,
    bool Configured,
    bool Connected,
    string State,
    string WorkspacePath,
    string Endpoint,
    string EndpointSource,
    string? AccountLabel,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? ExpiresAt,
    string? Diagnostic,
    string Message);

public sealed record ItemDetailResponse(
    ItemDto Item,
    IReadOnlyList<RoundDto> Rounds,
    IReadOnlyList<RunDto> Runs,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<DecisionDto> Decisions,
    IReadOnlyList<TimelineEventDto> Timeline,
    IReadOnlyList<SourceWriteDto> SourceWrites,
    IReadOnlyList<ReviewDraftDto> ReviewDrafts,
    IReadOnlyList<ImplementationDraftDto> ImplementationDrafts,
    IReadOnlyList<FollowUpDraftDto> FollowUpDrafts,
    IReadOnlyList<DiscussionTurnDto> DiscussionTurns,
    SourceSnapshotDto? SourceSnapshot);

public sealed record ItemSummaryDto(
    string ItemId,
    string Source,
    string ExternalId,
    ItemKind Kind,
    string Title,
    string? Repository,
    string? Assignee,
    string? Branch,
    string? ExternalUrl,
    IReadOnlyList<string> Labels,
    DateTimeOffset? SourceUpdatedAt,
    DateTimeOffset? LastSourceSyncAt,
    bool IsDraft,
    string? HeadSha,
    SourceState SourceState,
    SourceDetailsStatus SourceDetailsStatus,
    DateTimeOffset? SourceDetailsHydratedAt,
    string? SourceDetailsErrorCode,
    string? SourceDetailsErrorMessage,
    DateTimeOffset? SourceClosedAt,
    DateTimeOffset? SourceMergedAt,
    ArchiveReason? ArchiveReason,
    ItemState State,
    int CurrentRound,
    CheckState CheckState,
    string? LatestSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ParentItemId,
    string? GeneratedFromDraftId,
    string? ShortId,
    BoardTaskStatus TaskStatus,
    long BoardSortOrder);

public sealed record ItemDto(
    string ItemId,
    string WorkspaceId,
    string Source,
    string ExternalId,
    ItemKind Kind,
    string Title,
    string? Description,
    string? Repository,
    string? Assignee,
    string? Branch,
    string? ExternalUrl,
    IReadOnlyList<string> Labels,
    DateTimeOffset? SourceUpdatedAt,
    bool IsDraft,
    string? HeadSha,
    SourceState SourceState,
    SourceDetailsStatus SourceDetailsStatus,
    DateTimeOffset? SourceDetailsHydratedAt,
    string? SourceDetailsErrorCode,
    string? SourceDetailsErrorMessage,
    DateTimeOffset? SourceClosedAt,
    DateTimeOffset? SourceMergedAt,
    ArchiveReason? ArchiveReason,
    ItemState State,
    int CurrentRound,
    string? CurrentRunId,
    string? LatestSummary,
    CheckState CheckState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSourceSyncAt,
    string? ParentItemId,
    string? GeneratedFromDraftId,
    string? ShortId,
    BoardTaskStatus TaskStatus,
    long BoardSortOrder);

public sealed record RoundDto(
    string RoundId,
    int RoundNumber,
    RoundStatus Status,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record RunDto(
    string RunId,
    string RoundId,
    int Attempt,
    RunStatus Status,
    string RunnerKind,
    string? ThreadId,
    string? TurnId,
    string? AppServerEndpoint,
    string? AppServerIdentity,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Summary,
    string? ErrorCode,
    string? ErrorMessage,
    int ProgressPercent,
    string? StatusMessage,
    DateTimeOffset? LastHeartbeatAt,
    string? BaseWorkspacePath,
    string? WorktreePath,
    string? WorktreeBranch,
    string? BaseRef,
    string? BaseSha,
    WorktreeStatus WorktreeStatus,
    string? WorktreeErrorCode,
    string? WorktreeErrorMessage,
    int RetryCount,
    DateTimeOffset? NextRetryAt,
    string? LeaseOwner,
    DateTimeOffset? LeaseAcquiredAt,
    DateTimeOffset? WorktreeCleanupAfterAt,
    DateTimeOffset? WorktreeCleanedAt,
    RunPurpose Purpose,
    RunDispatchTrigger DispatchTrigger,
    string? TargetHeadSha,
    DeliveryPolicy DeliveryPolicy,
    int ImplementationTurnCount);

public sealed record CommentDto(
    string CommentId,
    string? RoundId,
    AuthorKind AuthorKind,
    string AuthorName,
    string Body,
    CommentVisibility Visibility,
    CommentPurpose Purpose,
    DateTimeOffset CreatedAt,
    string? Source,
    string? SourceCommentId,
    string? ExternalUrl,
    DateTimeOffset? SourceUpdatedAt);

public sealed record DiscussionTurnDto(
    string DiscussionTurnId,
    string ItemId,
    string? RoundId,
    string QuestionCommentId,
    string? ReplyCommentId,
    string BaseRunId,
    string ThreadId,
    string? TurnId,
    DiscussionTurnStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record DecisionDto(
    string DecisionId,
    string RoundId,
    DecisionType Decision,
    string AuthorName,
    string? CommentId,
    string? Body,
    DateTimeOffset CreatedAt);

public sealed record TimelineEventDto(
    string EventId,
    string? RoundId,
    string? RunId,
    TimelineEventKind Kind,
    ActorKind ActorKind,
    string ActorName,
    string Title,
    string? Body,
    string? MetadataJson,
    DateTimeOffset CreatedAt);

public sealed record SourceWriteDto(
    string WriteId,
    string ItemId,
    string? RoundId,
    string? DecisionId,
    string Source,
    SourceWriteKind Kind,
    string CanonicalKind,
    string Intent,
    SourceWriteStatus Status,
    string? Repository,
    int? Number,
    string? HeadSha,
    string RequestJson,
    string? ResponseJson,
    string? ExternalId,
    string? ExternalUrl,
    int AttemptCount,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record SourcesResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SourceProviderStatusDto> Providers);

public sealed record SourceProviderStatusDto(
    string Provider,
    string DisplayName,
    string Endpoint,
    bool Configured,
    string AuthenticationState,
    SourceProviderCapabilityDto ReadCapability,
    SourceProviderCapabilityDto WriteCapability,
    SourceProviderCapabilityDto WebhookCapability,
    int ConfiguredProjectCount,
    DateTimeOffset? LastSyncAt,
    string? Diagnostic,
    IReadOnlyList<SourceProjectDto> Projects);

public sealed record SourceProviderCapabilityDto(
    bool Available,
    string State,
    string? Reason);

public sealed record SourceProjectDto(
    string Provider,
    string Instance,
    string ProjectPath,
    string Key,
    string DisplayName,
    SourceProviderCapabilityDto? ReadCapability = null,
    SourceProviderCapabilityDto? WriteCapability = null,
    SourceProviderCapabilityDto? WebhookCapability = null);

public sealed record SourceSyncJobRequest(
    string Provider,
    SourceSyncMode? Mode = null,
    IReadOnlyList<string>? Projects = null);

public sealed record SourceSyncSchedulesResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SourceSyncScheduleDto> Schedules);

public sealed record SourceSyncScheduleUpdateRequest(
    bool Enabled,
    int? IntervalSeconds = null);

public sealed record SourceSyncScheduleDto(
    string Provider,
    bool Enabled,
    int IntervalSeconds,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastScheduledAt,
    string? LastJobId,
    SourceSyncStatus? LastJobStatus,
    DateTimeOffset? LastJobCompletedAt,
    string? LastErrorCode,
    string? LastErrorMessage,
    bool ReadAvailable,
    string? DisabledReason,
    DateTimeOffset UpdatedAt);

public sealed record SourceSyncJobDto(
    string JobId,
    string Provider,
    SourceSyncTrigger Trigger,
    SourceSyncMode Mode,
    SourceSyncStatus Status,
    int ProjectsTotal,
    int ProjectsCompleted,
    int ProjectsFailed,
    int IssuesImported,
    int ReviewTargetsImported,
    int CommentsImported,
    int Skipped,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<SourceSyncProjectRunDto> Projects);

public sealed record SourceSyncProjectRunDto(
    string ProjectRunId,
    string JobId,
    string Provider,
    string SourceProjectKey,
    string ProjectPath,
    string DisplayName,
    SourceSyncProjectStatus Status,
    SourceSyncProjectPhase Phase,
    int IssuesDiscovered,
    int ReviewTargetsDiscovered,
    int IssuesImported,
    int ReviewTargetsImported,
    int CommentsImported,
    int Skipped,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ReviewDraftDto(
    string DraftId,
    string ItemId,
    string RoundId,
    string RunId,
    ReviewDraftStatus Status,
    string SummaryBody,
    int MajorCount,
    int MinorCount,
    int SuggestionCount,
    IReadOnlyList<string> Warnings,
    int AcceptedCount,
    int WarningCount,
    int ResolvedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    string? SourceWriteId,
    IReadOnlyList<ReviewDraftCommentDto> Comments);

public sealed record ReviewDraftCommentDto(
    string DraftCommentId,
    string Severity,
    string Title,
    string Body,
    string Path,
    int Line,
    string Side,
    int? StartLine,
    string? StartSide,
    string? SuggestionOriginal,
    string? SuggestionReplacement,
    string? CommentOnlyReason,
    ReviewDraftCommentStatus Status,
    string? Warning,
    ReviewFindingResolutionState ResolutionState,
    ReviewFindingResolutionKind? ResolutionKind,
    AuthorKind? ResolvedByKind,
    string? ResolutionNote,
    DateTimeOffset? ResolvedAt);

public sealed record SubmitReviewDraftRequest(ReviewDraftSummaryRequest Summary, IReadOnlyList<ReviewDraftCommentRequest>? Comments);

public sealed record ReviewDraftSummaryRequest(int MajorCount, int MinorCount, int SuggestionCount, string? Body);

public sealed record ReviewDraftCommentRequest(
    string? Severity,
    string? Title,
    string? Body,
    string? Path,
    ReviewDraftSuggestionRequest? Suggestion,
    ReviewDraftCommentOnlyRequest? CommentOnly);

public sealed record ReviewDraftSuggestionRequest(string? OldText, string? NewText);

public sealed record ReviewDraftCommentOnlyRequest(
    int Line,
    string? Side,
    int? StartLine,
    string? StartSide,
    string? Reason);

public sealed record SubmitReviewDraftResponse(string DraftId, int AcceptedCount, int WarningCount, IReadOnlyList<string> Warnings);

public sealed record ReviewDraftUpdateRequest(string? SummaryBody, IReadOnlyList<ReviewDraftCommentUpdateRequest>? Comments);

public sealed record ReviewDraftCommentUpdateRequest(string DraftCommentId, string? Body, string? SuggestionReplacement, string? CommentOnlyReason = null);

public sealed record ResolveReviewFindingRequest(string FindingId, string ResolutionKind, string? Note);

public sealed record ResolveReviewFindingResponse(string FindingId, string ResolutionState, string? ResolutionKind);

public sealed record ResolveReviewFindingOperatorRequest(string ResolutionKind, string? Note);

public sealed record ImplementationDraftDto(
    string DraftId,
    string ItemId,
    string RoundId,
    string RunId,
    ImplementationDraftStatus Status,
    DeliveryPolicy DeliveryPolicy,
    string Summary,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> ChangedFiles,
    string ProposedCommitMessage,
    string ProposedPrTitle,
    string ProposedPrBody,
    string? BranchName,
    string? CommitSha,
    string? PullRequestItemId,
    string? PullRequestUrl,
    string? SourceWriteId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeliveredAt);

public sealed record SubmitImplementationDraftRequest(
    string? Summary,
    IReadOnlyList<string>? Tests,
    IReadOnlyList<string>? Risks,
    IReadOnlyList<string>? ChangedFiles,
    string? ProposedCommitMessage,
    string? ProposedPrTitle,
    string? ProposedPrBody);

public sealed record SubmitImplementationDraftResponse(string DraftId, DeliveryPolicy DeliveryPolicy);

public sealed record FollowUpDraftDto(
    string DraftId,
    string ItemId,
    string RoundId,
    string RunId,
    FollowUpDraftStatus Status,
    string Title,
    string Body,
    string? Rationale,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string> Labels,
    string? CreatedItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record SubmitFollowUpDraftRequest(IReadOnlyList<FollowUpProposalRequest>? Proposals);

public sealed record FollowUpProposalRequest(
    string? Title,
    string? Body,
    string? Rationale,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string>? Labels);

public sealed record SubmitFollowUpDraftResponse(IReadOnlyList<string> DraftIds, int AcceptedCount);

public sealed record FollowUpDraftUpdateRequest(
    string? Title,
    string? Body,
    string? Rationale,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string>? Labels);

public sealed record CreateItemRequest(
    string Source,
    string ExternalId,
    ItemKind Kind,
    string Title,
    string? Description,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string>? Labels = null);

public sealed record CreateLocalTaskRequest(
    string Title,
    string? Description,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string>? Labels);

public sealed record UpdateItemRequest(
    string Title,
    string? Description,
    string? Repository,
    string? Assignee,
    string? Branch,
    IReadOnlyList<string>? Labels);

public sealed record CommentRequest(string Body, int? RoundNumber);

public sealed record DiscussionTurnRequest(string Body, int? RoundNumber, string? ModelId);

public sealed record SubmitDiscussionReplyRequest(string DiscussionTurnId, string Body);

public sealed record SubmitDiscussionReplyResponse(string DiscussionTurnId, string ReplyCommentId);

public sealed record DispatchRequest(
    string? Mode,
    string? Note,
    MockOutcome? MockOutcome,
    int? MockDurationSeconds,
    string? WorkMode = null,
    DeliveryPolicy? DeliveryPolicy = null);

public sealed record DecisionRequest(string? Body);

public sealed record CancelRunRequest(string? Body);

public sealed record TaskReorderRequest(IReadOnlyList<TaskReorderEntry> Updates);

public sealed record TaskReorderEntry(string TaskId, long SortOrder);

public sealed record TaskReorderResponse(IReadOnlyList<ItemSummaryDto> Tasks);

public sealed record RunResponse(RunDto Run);

public sealed record SourceSnapshotDto(
    string SnapshotId,
    string Source,
    string ExternalId,
    string? Repository,
    string? HeadSha,
    DateTimeOffset? SourceUpdatedAt,
    string PayloadJson,
    DateTimeOffset SyncedAt);

public sealed record GitHubSourceStatusResponse(
    bool Enabled,
    bool AppAuthenticationConfigured,
    bool WritesEnabled,
    bool WriteConfigured,
    bool WebhookSecretConfigured,
    string Endpoint,
    IReadOnlyList<string> Repositories,
    DateTimeOffset? LastSyncAt);

public sealed record GitHubSyncResponse(
    IReadOnlyList<string> RepositoriesScanned,
    int IssuesImported,
    int PullRequestsImported,
    int CommentsImported,
    int Skipped,
    IReadOnlyList<GitHubSyncErrorDto> Errors,
    DateTimeOffset SyncedAt);

public sealed record GitHubSyncErrorDto(string? Repository, string Code, string Message);

public sealed record GitHubSyncJobRequest(
    GitHubSyncMode? Mode = null,
    IReadOnlyList<string>? Repositories = null);

public sealed record GitHubSyncJobDto(
    string JobId,
    GitHubSyncTrigger Trigger,
    GitHubSyncMode Mode,
    GitHubSyncStatus Status,
    int RepositoriesTotal,
    int RepositoriesCompleted,
    int RepositoriesFailed,
    int IssuesImported,
    int PullRequestsImported,
    int CommentsImported,
    int Skipped,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<GitHubSyncRepositoryRunDto> Repositories);

public sealed record GitHubSyncRepositoryRunDto(
    string RepositoryRunId,
    string JobId,
    string Repository,
    GitHubSyncRepositoryStatus Status,
    GitHubSyncRepositoryPhase Phase,
    int IssuesDiscovered,
    int PullRequestsDiscovered,
    int IssuesImported,
    int PullRequestsImported,
    int CommentsImported,
    int Skipped,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record DotCraftStatusResponse(
    bool Configured,
    bool Connected,
    string Health,
    bool AutoStart,
    string WorkspacePath,
    string Endpoint,
    string EndpointSource,
    string ApprovalPolicy,
    int RunTimeoutSeconds,
    bool ManagedWorktreesEnabled,
    string WorktreeRootPolicy,
    int GlobalMaxActiveRuns,
    int MaxActiveRunsPerRepository,
    int MaxActiveRunsPerSource,
    string Reason,
    string? Message);

public sealed record DotCraftWorkspacesResponse(
    DateTimeOffset GeneratedAt,
    DotCraftWorkspacesSummary Summary,
    IReadOnlyList<DotCraftWorkspaceDto> Workspaces);

public sealed record DotCraftWorkspacesSummary(
    int Total,
    int Connected,
    int HubManaged,
    string DefaultPath);

public sealed record DotCraftWorkspaceDto(
    string Path,
    string Label,
    bool IsDefault,
    IReadOnlyList<string> Repositories,
    bool Configured,
    bool Connected,
    string Health,
    string Endpoint,
    string EndpointSource,
    bool HubManaged,
    string Reason,
    string Message);

public sealed record SettingsDiagnosticsResponse(
    DateTimeOffset GeneratedAt,
    SettingsServiceDiagnostics Service,
    IReadOnlyDictionary<string, bool> Capabilities,
    SettingsGitHubDiagnostics GitHub,
    SettingsGitLabDiagnostics GitLab,
    SettingsDotCraftDiagnostics DotCraft,
    SettingsRuntimeDiagnostics Runtime,
    SettingsRedactionDiagnostics Redaction);

public sealed record SettingsServiceDiagnostics(
    string Name,
    string Mode,
    string WorkspaceMode);

public sealed record SettingsGitHubDiagnostics(
    bool Available,
    bool Enabled,
    string Authentication,
    bool WritesEnabled,
    bool WriteConfigured,
    bool WebhookSecretConfigured,
    string Endpoint,
    IReadOnlyList<string> Repositories,
    DateTimeOffset? LastSyncAt);

public sealed record SettingsGitLabDiagnostics(
    bool Available,
    bool Enabled,
    string Authentication,
    bool WritesEnabled,
    bool WriteConfigured,
    bool WebhookSecretConfigured,
    bool WebhookSigningTokenConfigured,
    string WebhookVerificationMode,
    string Endpoint,
    string ApiBaseUrl,
    IReadOnlyList<string> Projects,
    DateTimeOffset? LastSyncAt,
    IReadOnlyList<string> RecentSyncFailures,
    IReadOnlyList<string> RecentSourceWriteFailures);

public sealed record SettingsDotCraftDiagnostics(
    bool Available,
    bool Configured,
    bool Connected,
    string Health,
    string Endpoint,
    string EndpointSource,
    string WorkspacePath,
    string ApprovalPolicy,
    int RunTimeoutSeconds,
    bool HubDiscoveryEnabled,
    string? Message);

public sealed record SettingsRuntimeDiagnostics(
    bool ManagedWorktreesEnabled,
    string WorktreeRootPolicy,
    string WorktreeBranchPrefix,
    int GlobalMaxActiveRuns,
    int MaxActiveRunsPerRepository,
    int MaxActiveRunsPerSource,
    int MaxRunAttempts,
    int RetryBackoffSeconds,
    int MaxRetryBackoffSeconds,
    int StallTimeoutSeconds,
    int SucceededWorktreeRetentionHours,
    int FailedWorktreeRetentionHours,
    bool WorktreeCleanupEnabled,
    int WorktreeCleanupIntervalSeconds);

public sealed record SettingsRedactionDiagnostics(
    bool SecretsRedacted,
    IReadOnlyList<string> RedactedFields,
    IReadOnlyList<string> UrlPartsRemoved);

public sealed record ServerConfigurationResponse(
    DateTimeOffset GeneratedAt,
    bool Writable,
    string? DisabledReason,
    string Revision,
    string OverlayPath,
    ServerConfigurationDto Configuration,
    IReadOnlyList<string> ImpactWarnings,
    IReadOnlyList<ConfigurationChangeDto> RecentChanges,
    bool RestartRequired = false,
    string? RestartSignature = null);

public sealed record ServerConfigurationUpdateRequest(
    string? BaseRevision,
    bool ConfirmImpact,
    ServerConfigurationDto? Configuration,
    bool DetectGitHubInstallations = false);

public sealed record ServerConfigurationUpdateResponse(
    ServerConfigurationResponse Configuration,
    string ChangeId,
    IReadOnlyList<string> AppliedFields,
    IReadOnlyList<GitHubInstallationProfileDetectionWarningDto> GitHubInstallationWarnings,
    bool RestartRequired = false,
    string? RestartSignature = null);

public sealed record ServerConfigurationDto(
    GitHubServerConfigurationDto GitHub,
    GitLabServerConfigurationDto GitLab,
    DotCraftServerConfigurationDto DotCraft,
    RuntimeServerConfigurationDto Runtime,
    AutomationServerConfigurationDto Automation);

public sealed record GitHubServerConfigurationDto(
    string Endpoint,
    string? AppId,
    IReadOnlyList<GitHubInstallationProfileDto> InstallationProfiles,
    IReadOnlyList<string> Repositories,
    bool WritesEnabled,
    GitHubSecretConfigurationDto? Secrets = null);

public sealed record GitHubInstallationProfileDto(
    string Instance,
    string Owner,
    string InstallationId,
    string Source);

public sealed record GitHubInstallationProfileDetectionWarningDto(
    string Instance,
    string Owner,
    string Repository,
    string Code,
    string Message);

public sealed record GitHubSecretConfigurationDto(
    SecretConfigurationFieldDto PrivateKey,
    SecretConfigurationFieldDto PrivateKeyPath,
    SecretConfigurationFieldDto WebhookSecret);

public sealed record GitLabServerConfigurationDto(
    bool Enabled,
    bool WritesEnabled,
    string Endpoint,
    string ApiBaseUrl,
    IReadOnlyList<string> Projects,
    IReadOnlyList<GitLabProjectProfileDto> ProjectProfiles,
    bool AllowLocalDevelopmentUnsafeWebhooks);

public sealed record GitLabProjectProfileDto(
    string Instance,
    string ProjectPath,
    string TokenKind,
    GitLabSecretConfigurationDto? Secrets = null);

public sealed record GitLabSecretConfigurationDto(
    SecretConfigurationFieldDto Token,
    SecretConfigurationFieldDto WebhookSecret,
    SecretConfigurationFieldDto WebhookSigningToken);

public sealed record SecretConfigurationFieldDto(
    bool Configured,
    string Mode = "unchanged",
    string? Value = null);

public sealed record DotCraftServerConfigurationDto(
    IReadOnlyList<DotCraftRepositoryWorkspaceRouteDto> RepositoryWorkspaceRoutes,
    string AppServerUrl,
    bool HubDiscoveryEnabled,
    string HubLockPath,
    string ApprovalPolicy,
    int RunTimeoutSeconds);

public sealed record DotCraftRepositoryWorkspaceRouteDto(
    string Project,
    string WorkspacePath);

public sealed record RuntimeServerConfigurationDto(
    bool ManagedWorktreesEnabled,
    string WorktreeRoot,
    string WorktreeBranchPrefix,
    int GlobalMaxActiveRuns,
    int MaxActiveRunsPerRepository,
    int MaxActiveRunsPerSource,
    int MaxRunAttempts,
    int RetryBackoffSeconds,
    int MaxRetryBackoffSeconds,
    int StallTimeoutSeconds,
    int SucceededWorktreeRetentionHours,
    int FailedWorktreeRetentionHours,
    bool WorktreeCleanupEnabled,
    int WorktreeCleanupIntervalSeconds);

public sealed record AutomationServerConfigurationDto(
    bool AutoDispatchEnabled,
    IReadOnlyList<string> AutoDispatchAllowLabels,
    IReadOnlyList<string> AutoDispatchBlockLabels,
    DeliveryPolicy DeliveryPolicy,
    int MaxImplementationTurns,
    IReadOnlyList<string> AutoReviewRepositories,
    bool AutoReviewPublishEnabled,
    IReadOnlyList<string> AutoReviewPublishRepositories,
    bool AutoFollowUpEnabled,
    IReadOnlyList<string> AutoFollowUpRepositories,
    int MaxFollowUpRounds);

public sealed record ConfigurationChangeDto(
    string ChangeId,
    DateTimeOffset CreatedAt,
    string Actor,
    string? RemoteAddress,
    string BaseRevision,
    string NewRevision,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<string> ImpactWarnings,
    string BeforeJson,
    string AfterJson);

public sealed record DrawerSnapshotResponse(
    string RunId,
    IReadOnlyList<ConversationItemDto> Items,
    PlanSnapshotDto? Plan,
    RunSummaryDto? Run,
    DateTimeOffset? Watermark,
    bool FromCache);

public sealed record ConversationItemDto(
    string Id,
    string? TurnId,
    string Type,
    string Status,
    JsonElement Payload,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CompletedAt,
    bool Streaming = false);

public sealed record PlanSnapshotDto(
    string? Title,
    string? Overview,
    string? Content,
    IReadOnlyList<PlanTodoDto> Todos);

public sealed record PlanTodoDto(
    string Id,
    string Content,
    string Priority,
    string Status);

public sealed record RunSummaryDto(
    string RunId,
    string Status,
    string? ThreadId,
    string? TurnId,
    string? TurnStatus,
    string? ErrorCode,
    string? ErrorMessage,
    int ProgressPercent,
    string? StatusMessage,
    DateTimeOffset? UpdatedAt);

public sealed record TurnInputPartDto(
    string Type,
    string? Text,
    string? Name,
    string? Path,
    string? DisplayPath,
    string? Url,
    string? MimeType,
    string? FileName,
    JsonElement? Extra = null);

public sealed record SubmitTurnRequest(IReadOnlyList<TurnInputPartDto> Input, string? ModelId);

public sealed record SubmitTurnResponse(string Mode, string? TurnId, string? QueuedInputId);

public sealed record InterruptResponse(bool Accepted);

public sealed record ModelInfoDto(string Id, string DisplayName, string? Provider = null);

public sealed record ErrorResponse(ErrorBody Error);

public sealed record ErrorBody(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);
