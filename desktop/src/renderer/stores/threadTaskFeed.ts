import { createTerminalFeed } from '../components/conversation/summary/terminalFeed'
import type { SummaryItem } from '../components/conversation/summary/sources'
import type { SessionItem } from '@dotcraft/sdk/contracts'
import { workflowRunsFor, type WorkflowRunClient } from './workflowRunCache'
import { publishThreadTasks, reconcileThreadActivity, type SubAgentTasks } from './threadTaskState'

export interface ThreadTaskFeed {
  subscribe(listener: (tasks: SubAgentTasks) => void): () => void
  refresh(): Promise<boolean>
  ensure(): Promise<boolean>
  beginIdle(): void
  addItems(items: readonly SummaryItem[]): void
  observeCommandExecution(item: Pick<SessionItem, 'type' | 'payload' | 'turnId'>, turnId?: string | null): void
  stopTerminal(sessionId: string): Promise<void>
  stopWorkflow(runId: string): Promise<void>
  release(): void
}

interface FeedEntry {
  feed: ThreadTaskFeed
  users: number
  dispose(): void
}

const feeds = new Map<string, FeedEntry>()
const owners = new Map<string, symbol>()

export function retainThreadTaskFeed(provider: WorkflowRunClient, threadId: string): ThreadTaskFeed {
  const ownedClient = provider.forThread?.(threadId)
  const client = ownedClient ?? provider
  const scope = client.connectionKey()
  const key = scope + '\0' + threadId
  const existing = feeds.get(key)
  if (existing) {
    ownedClient?.dispose()
    existing.users += 1
    return existing.feed
  }
  const token = Symbol()
  owners.set(threadId, token)
  let disposed = false
  let generation = 0
  let tasks: SubAgentTasks = { terminals: [], workflows: [] }
  let loaded = false
  let workflowsLoaded = false
  let inFlight: Promise<boolean> | null = null
  let queued = false
  const listeners = new Set<(tasks: SubAgentTasks) => void>()
  const current = (): boolean => !disposed && scope !== '' && client.connectionKey() === scope && owners.get(threadId) === token
  const publish = (): void => {
    if (!current()) return
    publishThreadTasks(threadId, tasks)
    for (const listener of listeners) listener(tasks)
  }
  const terminals = createTerminalFeed({ appServer: client }, threadId, values => {
    tasks = { ...tasks, terminals: values }
    publish()
  })
  const workflows = workflowRunsFor(client)
  const stopWorkflows = workflows.getState().observe(threadId, values => {
    tasks = { ...tasks, workflows: values.map(run => ({
      runId: run.runId, name: run.name, paused: run.status === 'paused',
      startedAt: run.startedAt ?? run.createdAt ?? null
    })) }
    publish()
  })
  const refresh = (): Promise<boolean> => {
    if (!current()) return Promise.resolve(false)
    if (inFlight) {
      queued = true
      return inFlight
    }
    const started = generation
    const work = async (): Promise<boolean> => {
      let success = false
      do {
        queued = false
        const terminalRead = terminals.refresh()
        const workflowRead = workflowsLoaded ? workflows.getState().refresh(threadId) : workflows.getState().list(threadId)
        const values = await Promise.all([terminalRead, workflowRead])
        workflowsLoaded ||= values[1]
        if (!current() || started !== generation) return false
        success = values.every(Boolean)
        loaded ||= success
      } while (queued)
      if (success) reconcileThreadActivity(threadId, false)
      return success
    }
    const pending = work().finally(() => { if (inFlight === pending) inFlight = null })
    inFlight = pending
    return pending
  }
  const stopConnection = client.onConnectionChange(connected => {
    generation += 1
    inFlight = null
    queued = false
    if (connected) {
      void refresh()
    } else if (owners.get(threadId) === token) {
      reconcileThreadActivity(threadId, false)
      publishThreadTasks(threadId, null)
    }
  })
  const entry: FeedEntry = {
    users: 1,
    feed: {
      subscribe(listener) {
        listeners.add(listener)
        listener(tasks)
        return () => { listeners.delete(listener) }
      },
      refresh,
      ensure: () => inFlight ?? (loaded ? Promise.resolve(true) : refresh()),
      beginIdle() {
        if (!current()) return
        reconcileThreadActivity(threadId, true)
        void refresh()
      },
      addItems: terminals.addItems,
      observeCommandExecution: terminals.observeCommandExecution,
      stopTerminal: terminals.stop,
      stopWorkflow: runId => workflows.getState().stop(threadId, runId),
      release() {
        entry.users -= 1
        if (entry.users > 0) return
        entry.dispose()
        if (feeds.get(key) === entry) feeds.delete(key)
      }
    },
    dispose() {
      disposed = true
      terminals.dispose()
      stopWorkflows()
      stopConnection()
      ownedClient?.dispose()
      listeners.clear()
      if (owners.get(threadId) === token) {
        owners.delete(threadId)
        reconcileThreadActivity(threadId, false)
        publishThreadTasks(threadId, null)
      }
    }
  }
  feeds.set(key, entry)
  return entry.feed
}

export function resetThreadTaskFeeds(): void {
  for (const entry of feeds.values()) entry.dispose()
  feeds.clear()
  owners.clear()
}
