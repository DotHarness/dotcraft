import type { ItemDetailResponse, ItemState, ItemSummaryDto, RunDto } from './oratorio-contracts'
import type { OratorioTask, TaskCapabilities, TaskColumn, TaskState } from './oratorio-model'

export function mapItemSummary(item: ItemSummaryDto): OratorioTask {
  const provider = item.source === 'github' || item.source === 'gitlab' ? item.source : 'local'
  const column = mapColumn(item.taskStatus, item.state)
  const kind = item.kind === 'pullRequest' ? 'Pull request' : item.kind === 'issue' ? 'Issue' : 'Task'
  return {
    id: item.itemId || `${item.source}:${item.externalId}`,
    shortId: item.shortId || (provider === 'local' ? item.externalId : `#${item.externalId}`),
    sourceLabel: sourceItemLabel(provider, item.externalId, kind),
    provider,
    repository: item.repository ?? '',
    kind,
    title: item.title,
    description: item.latestSummary || '',
    assignee: item.assignee,
    labels: item.labels ?? [],
    column,
    state: mapState(item.state),
    latestResult: item.latestResult ?? 'none',
    lifecycle: item.sourceState === 'merged' ? 'merged' : item.sourceState === 'closed' ? 'closed' : 'open',
    syncedAt: item.lastSourceSyncAt || undefined,
    updatedAt: item.sourceUpdatedAt || item.updatedAt,
    headSha: item.headSha?.slice(0, 7) || undefined,
    archived: item.state === 'archived',
    cancelled: item.taskStatus === 'cancelled',
    branch: item.branch || undefined,
    round: item.currentRound,
    capabilities: capabilitiesFor(item.state)
  }
}
function sourceItemLabel(provider: OratorioTask['provider'], externalId: string, kind: OratorioTask['kind']): string {
  if (provider === 'local') return 'Task'
  const match = externalId.trim().match(/(?:^|([#!]))(\d+)$/)
  return match ? `${match[1] === '!' ? '!' : '#'}${match[2]}` : kind
}
export function mapItemDetail(detail: ItemDetailResponse): OratorioTask {
  const task = mapItemSummary(detail.item)
  const latestRun = detail.runs.at(-1)
  const body = detail.item.description || ''
  return {
    ...task,
    description: task.description || sourceSummary(body) || '',
    body,
    run: latestRun ? mapRun(latestRun) : undefined,
    detail
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

function sourceSummary(description: string | null | undefined): string | null {
  const source = description?.trim()
  if (!source) return null

  const lines = source.replace(/\r\n/g, '\n').split('\n')
  const summary: string[] = []
  let readingSummary = false

  for (const line of lines) {
    const heading = line.match(/^#{2,4}\s+(.+?)\s*$/)
    if (heading) {
      const name = heading[1].trim().toLowerCase()
      if (name === 'summary') {
        readingSummary = true
        continue
      }
      if (readingSummary) break
    }
    if (readingSummary) summary.push(line)
  }

  return summary.join('\n').trim() || source
}
