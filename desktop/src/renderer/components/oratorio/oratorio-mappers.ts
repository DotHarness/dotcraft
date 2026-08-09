import type { ItemDetailResponse, ItemState, ItemSummaryDto, RunDto } from './oratorio-contracts'
import type { DiscussionComment, OratorioTask, TaskCapabilities, TaskColumn, TaskState } from './oratorio-model'

export function mapItemSummary(item: ItemSummaryDto): OratorioTask {
  const provider = item.source === 'github' || item.source === 'gitlab' ? item.source : 'local'
  const column = mapColumn(item.taskStatus, item.state)
  const kind = item.kind === 'pullRequest' ? 'Pull request' : item.kind === 'issue' ? 'Issue' : 'Task'
  return {
    id: item.itemId || `${item.source}:${item.externalId}`,
    shortId: item.shortId || (provider === 'local' ? item.externalId : `#${item.externalId}`),
    sourceLabel: sourceItemLabel(provider, item.externalId, kind),
    provider,
    repository: item.repository || (provider === 'local' ? 'Current workspace' : 'Unknown repository'),
    kind,
    title: item.title,
    description: item.latestSummary || '',
    assignee: item.assignee,
    labels: item.labels ?? [],
    column,
    state: mapState(item.state),
    check: item.checkState === 'passing' || item.checkState === 'failing' || item.checkState === 'pending' ? item.checkState : undefined,
    lifecycle: item.sourceState === 'merged' ? 'merged' : item.sourceState === 'closed' ? 'closed' : 'open',
    synced: item.lastSourceSyncAt ? relativeTime(item.lastSourceSyncAt) : undefined,
    updated: relativeTime(item.sourceUpdatedAt || item.updatedAt),
    headSha: item.headSha?.slice(0, 7) || undefined,
    archived: item.state === 'archived',
    cancelled: item.taskStatus === 'cancelled',
    branch: item.branch || undefined,
    round: item.currentRound,
    artifacts: { reviewDrafts: 0, implementationDrafts: 0, followUpDrafts: 0, comments: 0, writes: 0 },
    capabilities: capabilitiesFor(item.state)
  }
}

function sourceItemLabel(provider: OratorioTask['provider'], externalId: string, kind: OratorioTask['kind']): string {
  if (provider === 'local') return 'Task'
  const match = externalId.trim().match(/(?:^|[#!])(\d+)$/)
  return match ? `#${match[1]}` : kind
}

export function mapItemDetail(detail: ItemDetailResponse): OratorioTask {
  const task = mapItemSummary(detail.item)
  const latestRun = detail.runs.at(-1)
  return {
    ...task,
    description: detail.item.description || detail.item.latestSummary || '',
    artifacts: {
      reviewDrafts: detail.reviewDrafts?.length ?? 0,
      actionableReviewDrafts: detail.reviewDrafts?.filter((draft) => draft.status === 'draft' || draft.status === 'publishFailed').length ?? 0,
      implementationDrafts: detail.implementationDrafts?.length ?? 0,
      actionableImplementationDrafts: detail.implementationDrafts?.filter((draft) => draft.status === 'draft' || draft.status === 'deliveryFailed').length ?? 0,
      followUpDrafts: detail.followUpDrafts?.length ?? 0,
      actionableFollowUpDrafts: detail.followUpDrafts?.filter((draft) => draft.status === 'draft').length ?? 0,
      comments: detail.comments?.length ?? 0,
      writes: detail.sourceWrites?.length ?? 0
    },
    run: latestRun ? mapRun(latestRun) : undefined,
    detail
  }
}

export function mapComment(comment: NonNullable<ItemDetailResponse['comments']>[number]): DiscussionComment {
  return {
    id: comment.commentId,
    author: comment.authorName || comment.authorKind,
    role: comment.authorKind.toLowerCase() === 'agent' ? 'agent' : 'operator',
    body: comment.body,
    time: new Date(comment.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    purpose: comment.purpose === 'discussionQuestion' ? 'question' : comment.purpose === 'discussionReply' ? 'reply' : 'note'
  }
}

function mapRun(run: RunDto): NonNullable<OratorioTask['run']> {
  return {
    runId: run.runId,
    status: run.status === 'timedOut' ? 'timed-out' : run.status,
    attempt: run.attempt,
    threadAvailable: Boolean(run.threadId),
    threadId: run.threadId || undefined,
    workspacePath: run.baseWorkspacePath || undefined,
    activity: run.statusMessage || run.summary || run.status
  }
}

function mapState(state: ItemState): TaskState {
  if (state === 'awaitingReview') return 'awaiting-review'
  return ['discovered', 'dispatching', 'running', 'approved', 'rejected', 'failed', 'archived'].includes(state) ? state as TaskState : 'discovered'
}

function mapColumn(status: ItemSummaryDto['taskStatus'], state: ItemState): TaskColumn {
  if (status === 'in_progress') return 'in-progress'
  if (status === 'in_review') return 'in-review'
  if (status === 'done' || status === 'cancelled') return 'done'
  if (!status && (state === 'dispatching' || state === 'running' || state === 'failed')) return 'in-progress'
  if (!status && state === 'awaitingReview') return 'in-review'
  if (!status && (state === 'approved' || state === 'rejected' || state === 'archived')) return 'done'
  return 'todo'
}

function capabilitiesFor(state: ItemState): TaskCapabilities {
  if (state === 'discovered') return { dispatch: true, implement: true, autoTarget: true, reviewOnly: true }
  if (state === 'dispatching' || state === 'running') return { cancelRun: true }
  if (state === 'failed') return { retry: true }
  if (state === 'awaitingReview') return { decide: true, reReview: true }
  if (state === 'archived') return { reopen: true }
  return { archive: true, reReview: state === 'approved' }
}

function relativeTime(value: string): string {
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp)) return value
  const minutes = Math.max(0, Math.round((Date.now() - timestamp) / 60_000))
  if (minutes < 1) return 'now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  return hours < 24 ? `${hours} hr ago` : `${Math.round(hours / 24)} days ago`
}
