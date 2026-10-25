import { randomBytes } from 'node:crypto'
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import type { ThreadTask, ThreadTaskDefaults, ThreadTaskKind, ThreadTasksSnapshot } from '../../shared/threadTasks'

export const MAX_THREAD_TASKS = 20
export const PROPOSAL_EXPIRY_MS = 7 * 24 * 60 * 60 * 1000
const TASK_ID_PATTERN = /^task_[0-9a-f]{8}$/

export function isThreadTaskId(value: string): boolean {
  return TASK_ID_PATTERN.test(value)
}

export interface NewThreadTask {
  threadId: string
  workspacePath: string
  kind: ThreadTaskKind
  title: string
  prompt?: string
  background?: string
  defaults: ThreadTaskDefaults
  toolCallId?: string
  turnId?: string
}

export interface AddedThreadTask {
  task: ThreadTask
  evicted: ThreadTask[]
  others: ThreadTask[]
}

interface ThreadEntry {
  tasks: ThreadTask[]
}

type ChangeListener = (snapshot: ThreadTasksSnapshot) => void

export class ThreadTaskStore {
  private threads = new Map<string, ThreadEntry>()
  private readonly listeners = new Set<ChangeListener>()

  constructor(
    private readonly filePath: string | null,
    private readonly now: () => number = Date.now
  ) {
    this.load()
  }

  onChanged(listener: ChangeListener): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  snapshot(threadId: string): ThreadTasksSnapshot {
    const entry = this.entry(threadId)
    return { threadId, tasks: entry ? entry.tasks.map((task) => ({ ...task })) : [] }
  }

  get(threadId: string, taskId: string): ThreadTask | undefined {
    const task = this.entry(threadId)?.tasks.find((candidate) => candidate.id === taskId)
    return task ? { ...task } : undefined
  }

  add(input: NewThreadTask): AddedThreadTask {
    const entry = this.entry(input.threadId) ?? { tasks: [] }
    const task: ThreadTask = {
      id: `task_${randomBytes(4).toString('hex')}`,
      threadId: input.threadId,
      workspacePath: input.workspacePath,
      kind: input.kind,
      state: 'pending',
      title: input.title,
      ...(input.prompt ? { prompt: input.prompt } : {}),
      ...(input.background ? { background: input.background } : {}),
      defaults: { ...input.defaults },
      ...(input.toolCallId ? { toolCallId: input.toolCallId } : {}),
      ...(input.turnId ? { turnId: input.turnId } : {}),
      createdAt: new Date(this.now()).toISOString()
    }
    const tasks = [...entry.tasks, task]
    const evicted: ThreadTask[] = []
    for (let index = 0; tasks.length > MAX_THREAD_TASKS && index < tasks.length - 1;) {
      if (isLive(tasks[index])) {
        index++
        continue
      }
      evicted.push(...tasks.splice(index, 1))
    }
    this.threads.set(input.threadId, { ...entry, tasks })
    this.changed(input.threadId)
    return {
      task: { ...task },
      evicted: evicted.filter((candidate) => candidate.state === 'pending'),
      others: tasks.filter((candidate) => candidate.id !== task.id && candidate.kind === 'proposal' && candidate.state === 'pending')
    }
  }

  update(threadId: string, taskId: string, patch: Partial<Omit<ThreadTask, 'id' | 'threadId' | 'createdAt'>>): ThreadTask | undefined {
    const entry = this.entry(threadId)
    const index = entry?.tasks.findIndex((task) => task.id === taskId) ?? -1
    if (!entry || index < 0) return undefined
    const next: ThreadTask = { ...entry.tasks[index], ...patch }
    for (const key of Object.keys(next) as Array<keyof ThreadTask>) {
      if (next[key] === undefined) delete next[key]
    }
    entry.tasks[index] = next
    this.changed(threadId)
    return { ...next }
  }

  private entry(threadId: string): ThreadEntry | undefined {
    const entry = this.threads.get(threadId)
    if (!entry) return undefined
    if (this.refresh(entry)) this.changed(threadId)
    return entry
  }

  private refresh(entry: ThreadEntry): boolean {
    const now = this.now()
    let changed = false
    for (const [index, task] of entry.tasks.entries()) {
      if (task.kind === 'proposal' && task.state === 'pending' && now - Date.parse(task.createdAt) > PROPOSAL_EXPIRY_MS) {
        entry.tasks[index] = { ...task, state: 'expired' }
        changed = true
      }
    }
    return changed
  }

  private changed(threadId: string): void {
    this.save()
    const snapshot = this.snapshot(threadId)
    for (const listener of this.listeners) listener(snapshot)
  }

  private load(): void {
    if (!this.filePath || !existsSync(this.filePath)) return
    try {
      const stored = JSON.parse(readFileSync(this.filePath, 'utf8')) as { threads: Record<string, ThreadEntry> }
      for (const [threadId, entry] of Object.entries(stored.threads)) {
        this.threads.set(threadId, { tasks: entry.tasks.map(recoverAfterRestart) })
      }
    } catch {
      this.threads = new Map()
    }
  }

  private save(): void {
    if (!this.filePath) return
    const threads: Record<string, ThreadEntry> = {}
    for (const [threadId, entry] of this.threads) {
      if (entry.tasks.length > 0) threads[threadId] = entry
    }
    try {
      mkdirSync(dirname(this.filePath), { recursive: true })
      const temporary = `${this.filePath}.tmp`
      writeFileSync(temporary, JSON.stringify({ threads }), 'utf8')
      renameSync(temporary, this.filePath)
    } catch (error) {
      console.warn('[threadTasks] failed to save proposals', error)
    }
  }
}

function isLive(task: ThreadTask): boolean {
  return task.kind === 'offer' && (task.state === 'pending' || task.state === 'accepted' || task.state === 'starting')
}

function recoverAfterRestart(task: ThreadTask): ThreadTask {
  if (task.kind === 'offer' && task.state === 'pending') return { ...task, state: 'notAnswered' }
  if (task.state === 'starting') return { ...task, state: task.kind === 'offer' ? 'accepted' : 'pending' }
  return task
}
