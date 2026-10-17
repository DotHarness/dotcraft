import type { LatestResult } from './oratorio-contracts'
export type BoardMode = 'active' | 'all' | 'cancelled' | 'archived'
export type TaskColumn = 'todo' | 'in-progress' | 'in-review' | 'done'
export type TaskState = 'discovered' | 'dispatching' | 'running' | 'awaiting-review' | 'approved' | 'rejected' | 'failed' | 'archived'
export type RunStatus = 'queued' | 'dispatching' | 'running' | 'failed' | 'cancelled' | 'timed-out' | 'succeeded'
export type Provider = 'github' | 'gitlab' | 'local'

export interface TaskCapabilities {
  dispatch?: boolean
  implement?: boolean
  autoTarget?: boolean
  reviewOnly?: boolean
  cancelRun?: boolean
  retry?: boolean
  decide?: boolean
  reReview?: boolean
  archive?: boolean
  reopen?: boolean
}
export interface TaskRunSummary {
  runId?: string
  status: RunStatus
  attempt: number
  threadAvailable: boolean
  threadId?: string
  workspacePath?: string
  activity: string
}

export interface OratorioTask {
  id: string
  shortId: string
  sourceLabel: string
  provider: Provider
  repository: string
  kind: 'Issue' | 'Pull request' | 'Task'
  title: string
  description: string
  body?: string
  assignee: string | null
  labels: string[]
  column: TaskColumn
  state: TaskState
  latestResult: LatestResult
  lifecycle?: 'open' | 'closed' | 'merged'
  syncedAt?: string
  updatedAt: string
  headSha?: string
  archived?: boolean
  cancelled?: boolean
  branch?: string
  round?: number
  capabilities: TaskCapabilities
  run?: TaskRunSummary
  actionError?: string
  detail?: import('./oratorio-contracts').ItemDetailResponse
}

export const ORATORIO_COLUMNS: TaskColumn[] = ['todo', 'in-progress', 'in-review', 'done']

const PUBLIC_INSTANCES = new Set(['github.com', 'gitlab.com'])

export function repositoryPath(repository: string): string {
  const match = repository.match(/^[a-z]+:([^/]+)\/(.+)$/)
  if (!match) return repository
  const [, instance, path] = match
  return PUBLIC_INSTANCES.has(instance.toLowerCase()) ? path : `${instance}/${path}`
}

export function repositoryName(repository: string): string {
  const match = repository.match(/^[a-z]+:[^/]+\/(.+)$/)
  const segments = (match ? match[1] : repository).split('/').filter(Boolean)
  return segments.slice(-2).join('/')
}

export function taskMatches(task: OratorioTask, query: string, repository: string, assignee: string): boolean {
  if (repository !== 'all' && task.repository !== repository) return false
  if (assignee !== 'all' && (task.assignee ?? 'unassigned') !== assignee) return false
  const parts = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
  return parts.every((part) => {
    if (part.startsWith('source:')) return task.provider === part.slice(7)
    if (part.startsWith('label:')) return task.labels.some((label) => label.toLowerCase() === part.slice(6))
    const haystack = [task.shortId, task.title, task.repository, task.assignee ?? '', ...task.labels].join(' ').toLowerCase()
    return haystack.includes(part)
  })
}
