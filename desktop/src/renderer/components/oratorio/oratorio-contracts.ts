export type ItemState = 'discovered' | 'dispatching' | 'running' | 'awaitingReview' | 'approved' | 'rejected' | 'failed' | 'archived'
export type TaskStatus = 'todo' | 'in_progress' | 'in_review' | 'done' | 'cancelled'
export type ItemKind = 'issue' | 'pullRequest' | 'localTask'
export type RunStatus = 'queued' | 'dispatching' | 'running' | 'succeeded' | 'failed' | 'cancelled' | 'timedOut'
export type SourceWriteStatus = 'pending' | 'succeeded' | 'failed'
export type ReviewDraftStatus = 'draft' | 'published' | 'discarded' | 'publishFailed'
export type ImplementationDraftStatus = 'draft' | 'delivered' | 'deliveryFailed'
export type FollowUpDraftStatus = 'draft' | 'created' | 'discarded'
export type ReviewFindingResolutionKind = 'fixed' | 'dismissed'

export interface ItemSummaryDto {
  itemId?: string | null; source: string; externalId: string; kind: ItemKind; title: string
  repository: string | null; assignee: string | null; branch: string | null; externalUrl?: string | null
  labels?: string[] | null; sourceUpdatedAt?: string | null; lastSourceSyncAt?: string | null
  isDraft?: boolean | null; headSha?: string | null; sourceState?: 'open' | 'closed' | 'merged' | 'unknown' | null
  sourceDetailsStatus?: 'notRequired' | 'stale' | 'current' | 'failed' | null
  sourceDetailsHydratedAt?: string | null; sourceDetailsErrorCode?: string | null; sourceDetailsErrorMessage?: string | null
  state: ItemState; currentRound: number; checkState: 'notConfigured' | 'pending' | 'attention' | 'passing' | 'failing'
  latestSummary: string | null; createdAt: string; updatedAt: string; parentItemId?: string | null
  generatedFromDraftId?: string | null; shortId?: string | null; taskStatus?: TaskStatus | null; boardSortOrder?: number | null
}

export interface ItemDto extends ItemSummaryDto {
  itemId: string; workspaceId: string; description: string | null; currentRunId: string | null; lastSourceSyncAt: string | null
}

export interface CommentDto {
  commentId: string; roundId: string | null; authorKind: string; authorName: string; body: string
  visibility: string; purpose?: 'feedback' | 'discussionQuestion' | 'discussionReply' | 'sourceContext' | 'systemNote'
  createdAt: string; source?: string | null; sourceCommentId?: string | null; externalUrl?: string | null
}

export interface RoundDto { roundId: string; roundNumber: number; status: string; summary: string | null; createdAt: string; completedAt: string | null }
export interface DecisionDto { decisionId: string; roundId: string; decision: 'approve' | 'requestChanges' | 'reject' | 'reopen' | 'reReview'; authorName: string; commentId: string | null; body: string | null; createdAt: string }
export interface TimelineEventDto { eventId: string; roundId: string | null; runId: string | null; kind: string; actorKind: string; actorName: string; title: string; body: string | null; metadataJson: string | null; createdAt: string }
export interface SourceSnapshotDto { snapshotId?: string | null; source?: string | null; externalId?: string | null; repository?: string | null; headSha?: string | null; sourceUpdatedAt?: string | null; syncedAt?: string | null; payloadJson?: string | null }

export interface SourceWriteDto {
  writeId: string; itemId: string; roundId: string | null; decisionId: string | null; source: string; kind: string
  canonicalKind?: string | null; intent: string; status: SourceWriteStatus; repository: string | null; number: number | null
  headSha: string | null; externalId: string | null; externalUrl: string | null; attemptCount: number
  errorCode: string | null; errorMessage: string | null; createdAt: string; updatedAt: string; completedAt: string | null
}

export interface ReviewDraftCommentDto {
  draftCommentId: string; severity: string; title: string; body: string; path: string; line: number; side: string
  startLine: number | null; startSide: string | null; suggestionOriginal: string | null; suggestionReplacement: string | null
  commentOnlyReason: string | null; status: 'accepted' | 'skipped'; warning: string | null
  resolutionState: 'open' | 'resolved'; resolutionKind: ReviewFindingResolutionKind | null
  resolvedByKind: string | null; resolutionNote: string | null; resolvedAt: string | null
}

export interface ReviewDraftDto {
  draftId: string; itemId: string; roundId: string; runId: string; status: ReviewDraftStatus; summaryBody: string
  majorCount: number; minorCount: number; suggestionCount: number; warnings: string[]; acceptedCount: number
  warningCount: number; resolvedCount: number; createdAt: string; updatedAt: string; publishedAt: string | null
  sourceWriteId: string | null; comments: ReviewDraftCommentDto[]
}

export interface ImplementationDraftDto {
  draftId: string; itemId: string; roundId: string; runId: string; status: ImplementationDraftStatus
  deliveryPolicy: 'manualDelivery' | 'autoPr'; summary: string; tests: string[]; risks: string[]; changedFiles: string[]
  proposedCommitMessage: string; proposedPrTitle: string; proposedPrBody: string; branchName: string | null
  commitSha: string | null; pullRequestItemId: string | null; pullRequestUrl: string | null; sourceWriteId: string | null
  errorCode: string | null; errorMessage: string | null; createdAt: string; updatedAt: string; deliveredAt: string | null
}

export interface FollowUpDraftDto {
  draftId: string; itemId: string; roundId: string; runId: string; status: FollowUpDraftStatus; title: string; body: string
  rationale: string | null; repository: string | null; assignee: string | null; branch: string | null; labels: string[]
  createdItemId: string | null; createdAt: string; updatedAt: string; resolvedAt: string | null
}

export interface RunDto {
  runId: string; roundId: string; attempt: number; status: RunStatus; runnerKind: string; threadId: string | null
  turnId: string | null; startedAt: string | null; completedAt: string | null; summary: string | null
  errorCode: string | null; errorMessage: string | null; progressPercent: number; statusMessage: string | null
  lastHeartbeatAt: string | null; baseWorkspacePath: string | null; worktreePath: string | null; worktreeBranch: string | null
  baseRef: string | null; baseSha: string | null; worktreeStatus: string; worktreeErrorCode: string | null
  worktreeErrorMessage: string | null; retryCount: number; nextRetryAt: string | null; targetHeadSha: string | null
  purpose: 'reviewAnalysis' | 'implementation'; dispatchTrigger: string; deliveryPolicy: 'manualDelivery' | 'autoPr'; implementationTurnCount: number
}

export interface DiscussionTurnDto {
  discussionTurnId: string; itemId: string; roundId: string | null; questionCommentId: string; replyCommentId: string | null
  baseRunId: string; threadId: string; turnId: string | null; status: 'pending' | 'running' | 'succeeded' | 'failed'
  errorCode: string | null; errorMessage: string | null; createdAt: string; updatedAt: string; startedAt: string | null; completedAt: string | null
}

export interface TaskListResponse { tasks: ItemSummaryDto[]; nextCursor: string | null }
export interface ItemDetailResponse {
  item: ItemDto; rounds?: RoundDto[]; runs: RunDto[]; comments?: CommentDto[]; timeline: TimelineEventDto[]
  decisions?: DecisionDto[]; sourceWrites?: SourceWriteDto[]; reviewDrafts?: ReviewDraftDto[]
  implementationDrafts?: ImplementationDraftDto[]; followUpDrafts?: FollowUpDraftDto[]
  discussionTurns?: DiscussionTurnDto[]; sourceSnapshot?: SourceSnapshotDto | null
}

export interface SourceSyncJobDto { jobId: string; provider: string; status: string; mode: string; createdAt: string; updatedAt: string; projects?: unknown[] }
