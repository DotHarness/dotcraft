import type { BackgroundTerminalSnapshot, SubAgentChild, WorkflowRunSummary } from '@dotcraft/sdk/contracts'

export type TaskSource = 'shell' | 'agent' | 'workflow'

export type TaskStatus = 'running' | 'completed' | 'failed' | 'stopped'

interface TaskBase {
  id: string
  title: string
  status: TaskStatus
  startedAt: string | null
  endedAt: string | null
  stop: string | null
}

export type BackgroundTask =
  | (TaskBase & { kind: 'shell'; output: string | null })
  | (TaskBase & { kind: 'agent'; role: string | null })
  | (TaskBase & { kind: 'workflow'; agents: number })

export type TaskSources = Partial<Record<TaskSource, BackgroundTask[]>>

const OUTPUT_LINES = 12

const TERMINAL_STATUS: Record<string, TaskStatus> = {
  running: 'running',
  completed: 'completed',
  killed: 'stopped',
}

const WORKFLOW_STATUS: Record<string, TaskStatus> = {
  running: 'running',
  paused: 'running',
  succeeded: 'completed',
  stopped: 'stopped',
}

function tail(output: string | undefined): string | null {
  const lines = (output ?? '').trimEnd().split(/\r?\n/)
  const text = lines.slice(-OUTPUT_LINES).join('\n')
  return text.trim() ? text : null
}

export function terminalTask(terminal: BackgroundTerminalSnapshot): BackgroundTask | null {
  if (terminal.backgroundReason !== 'runInBackground' || !terminal.sessionId) return null
  const status = TERMINAL_STATUS[terminal.status ?? ''] ?? 'failed'
  return {
    kind: 'shell',
    id: terminal.sessionId,
    title: (terminal.command ?? '').split(/\r?\n/, 1)[0],
    status,
    startedAt: terminal.startedAt ?? null,
    endedAt: terminal.completedAt ?? null,
    stop: status === 'running' ? terminal.sessionId : null,
    output: tail(terminal.output),
  }
}

export function agentTask({ edge, thread }: SubAgentChild): BackgroundTask | null {
  const id = edge?.childThreadId
  if (!id) return null
  const running = thread?.runtime?.running === true
  return {
    kind: 'agent',
    id,
    title: edge.agentNickname || thread?.displayName || edge.taskName || id,
    status: running ? 'running' : edge.status === 'closed' ? 'stopped' : 'completed',
    startedAt: edge.createdAt ?? thread?.createdAt ?? null,
    endedAt: running ? null : (edge.updatedAt ?? thread?.lastActiveAt ?? null),
    stop: running && edge.supportsClose && edge.agentPath ? edge.agentPath : null,
    role: [edge.agentRole, edge.profileName].filter(Boolean).join(' · ') || null,
  }
}

export function workflowTask(run: WorkflowRunSummary): BackgroundTask {
  const status = WORKFLOW_STATUS[run.status] ?? 'failed'
  return {
    kind: 'workflow',
    id: run.runId,
    title: run.description || run.name,
    status,
    startedAt: run.startedAt ?? run.createdAt,
    endedAt: run.completedAt ?? null,
    stop: run.controls.canStop ? run.runId : null,
    agents: run.totals.agentCount,
  }
}

export function upsertTask(tasks: BackgroundTask[] | undefined, task: BackgroundTask): BackgroundTask[] {
  const list = tasks ?? []
  return list.some((entry) => entry.id === task.id) ? list.map((entry) => (entry.id === task.id ? task : entry)) : [task, ...list]
}

export function mergeAgents(previous: BackgroundTask[] | undefined, listed: BackgroundTask[]): BackgroundTask[] {
  const ids = new Set(listed.map((task) => task.id))
  const gone = (previous ?? [])
    .filter((task) => !ids.has(task.id))
    .map((task): BackgroundTask => (task.status === 'running' ? { ...task, status: 'stopped', stop: null } : task))
  return [...listed, ...gone]
}

function time(value: string | null): number {
  return Date.parse(value ?? '') || 0
}

export function chatTasks(sources: TaskSources | undefined): { running: BackgroundTask[]; completed: BackgroundTask[] } {
  const all = [...(sources?.shell ?? []), ...(sources?.agent ?? []), ...(sources?.workflow ?? [])]
  return {
    running: all.filter((task) => task.status === 'running').sort((left, right) => time(right.startedAt) - time(left.startedAt)),
    completed: all
      .filter((task) => task.status !== 'running')
      .sort((left, right) => time(right.endedAt ?? right.startedAt) - time(left.endedAt ?? left.startedAt)),
  }
}

export function runningTaskCount(sources: TaskSources | undefined): number {
  return Object.values(sources ?? {})
    .flat()
    .filter((task) => task.status === 'running').length
}
