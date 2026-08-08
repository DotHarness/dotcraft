export type BoardMode = 'active' | 'all' | 'cancelled' | 'archived'
export type TaskColumn = 'todo' | 'in-progress' | 'in-review' | 'done'
export type TaskStage = 'intake' | 'analysis' | 'review' | 'decision' | 'closed'
export type TaskState = 'discovered' | 'dispatching' | 'running' | 'awaiting-review' | 'approved' | 'rejected' | 'failed' | 'archived'
export type RunStatus = 'queued' | 'dispatching' | 'running' | 'failed' | 'cancelled' | 'timed-out' | 'succeeded'
export type Provider = 'github' | 'gitlab' | 'local'

export interface TaskArtifacts {
  reviewDrafts: number
  actionableReviewDrafts?: number
  implementationDrafts: number
  actionableImplementationDrafts?: number
  followUpDrafts: number
  actionableFollowUpDrafts?: number
  comments: number
  writes: number
}

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
  provider: Provider
  repository: string
  kind: 'Issue' | 'Pull request' | 'Task'
  title: string
  description: string
  assignee: string | null
  labels: string[]
  column: TaskColumn
  state: TaskState
  check?: 'passing' | 'failing' | 'pending'
  lifecycle?: 'open' | 'closed' | 'merged'
  updated: string
  headSha?: string
  archived?: boolean
  cancelled?: boolean
  branch?: string
  round?: number
  artifacts: TaskArtifacts
  capabilities: TaskCapabilities
  run?: TaskRunSummary
  actionError?: string
  detail?: import('./oratorio-contracts').ItemDetailResponse
}

export interface DiscussionComment {
  id: string
  author: string
  role: 'operator' | 'agent'
  body: string
  time: string
  purpose?: 'question' | 'reply' | 'note'
}

export const ORATORIO_COLUMNS: Array<{ id: TaskColumn; label: string }> = [
  { id: 'todo', label: 'To do' },
  { id: 'in-progress', label: 'In progress' },
  { id: 'in-review', label: 'In review' },
  { id: 'done', label: 'Done' },
]


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
