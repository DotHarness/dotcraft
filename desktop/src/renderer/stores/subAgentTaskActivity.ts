import { create } from 'zustand'
import type { ServerNotificationMethods } from '@dotcraft/sdk/contracts'
import { createTerminalFeed, type SummaryTerminal } from '../components/conversation/summary/terminalFeed'
import type { SummaryWorkflow } from '../components/conversation/summary/feed'
import { toSummaryItem } from '../components/conversation/summary/sources'
import { useThreadStore } from './threadStore'
import { workflowRunsFor, type WorkflowRunClient } from './workflowRunCache'
import { getThreadConnection, isThreadObservationCurrent } from './threadConnectionStore'
import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import type { SubAgentChild } from './subAgentChildren'

export interface SubAgentTasks {
  terminals: readonly SummaryTerminal[]
  workflows: readonly SummaryWorkflow[]
}

export const useSubAgentTaskActivity = create<{
  tasksByThread: ReadonlyMap<string, SubAgentTasks>
}>(() => ({ tasksByThread: new Map() }))

interface TaskObservation {
  connectionKey: string
  connectionId: string
  children: Map<string, {
    refresh(): Promise<void>
    stopTerminal(sessionId: string): Promise<void>
    stopWorkflow(runId: string): Promise<void>
    dispose(): void
  }>
  client: WorkflowRunClient
  dispose(): void
}

const observations = new Map<string, TaskObservation>()

function sourceFor(parentThreadId: string) {
  return getThreadConnection(parentThreadId) ?? useConnectionStore.getState()
}

function setTasks(threadId: string, tasks: SubAgentTasks | null): void {
  useSubAgentTaskActivity.setState(state => {
    const active = tasks && (tasks.terminals.length > 0 || tasks.workflows.length > 0)
    if (!active && !state.tasksByThread.has(threadId)) return state
    const tasksByThread = new Map(state.tasksByThread)
    if (active) tasksByThread.set(threadId, tasks)
    else tasksByThread.delete(threadId)
    return { tasksByThread }
  })
}

function createObservation(parentThreadId: string, connectionKey: string, connectionId: string): TaskObservation {
  let disposed = false
  const cleanups = new Set<() => void>()
  const own = (cleanup: () => void): (() => void) => {
    cleanups.add(cleanup)
    return () => { cleanups.delete(cleanup); cleanup() }
  }
  const scope = (): string => {
    const current = sourceFor(parentThreadId)
    return !disposed && isThreadObservationCurrent({ connectionKey, connectionId })
      && current.connectionKey === connectionKey && current.connectionId === connectionId
      ? `${connectionKey}\u0000${connectionId}` : ''
  }
  return {
    connectionKey,
    connectionId,
    children: new Map(),
    client: {
      connectionKey: scope,
      request(method, params) {
        return window.api.appServer.sendConnectionRequest(connectionKey, method, params, undefined, { connectionId })
      },
      onNotification(method, listener) {
        return own(window.api.appServer.onNotificationRaw(payload => {
          if (scope() && payload.connectionKey === connectionKey && payload.connectionId === connectionId && payload.method === method) {
            listener(payload.params as ServerNotificationMethods[typeof method]['params'])
          }
        }))
      },
      onConnectionChange(listener) {
        let previous = scope()
        const changed = (): void => {
          const next = scope()
          if (next === previous) return
          previous = next
          listener(next !== '')
        }
        const stopConnection = useConnectionStore.subscribe(changed)
        const stopProjects = useWorkspaceProjectsStore.subscribe(changed)
        return own(() => { stopConnection(); stopProjects() })
      }
    },
    dispose() {
      disposed = true
      for (const child of this.children.values()) child.dispose()
      for (const cleanup of cleanups) cleanup()
      cleanups.clear()
    }
  }
}

export async function syncSubAgentTaskActivity(
  parentThreadId: string, children: readonly SubAgentChild[], connectionThreadId = parentThreadId
): Promise<void> {
  const source = sourceFor(connectionThreadId)
  let observation = observations.get(parentThreadId)
  if (observation && (observation.connectionKey !== source.connectionKey || observation.connectionId !== source.connectionId)) {
    observation.dispose()
    observations.delete(parentThreadId)
    observation = undefined
  }
  const ids = new Set(children.filter(child => !child.isPlaceholder && child.status !== 'closed').map(child => child.childThreadId))
  if (ids.size === 0) {
    observation?.dispose()
    observations.delete(parentThreadId)
    return
  }
  if (!source.connectionKey || !source.connectionId || !isThreadObservationCurrent({
    connectionKey: source.connectionKey, connectionId: source.connectionId
  })) return
  if (!observation) {
    observation = createObservation(connectionThreadId, source.connectionKey, source.connectionId)
    observations.set(parentThreadId, observation)
  }
  for (const [id, child] of observation.children) {
    if (ids.has(id)) continue
    child.dispose()
    observation.children.delete(id)
  }
  const discoveries: Promise<void>[] = []
  for (const id of ids) {
    const existing = observation.children.get(id)
    if (existing) {
      discoveries.push(existing.refresh())
      continue
    }
    let disposed = false
    let taskTerminals: readonly SummaryTerminal[] = []
    let taskWorkflows: readonly SummaryWorkflow[] = []
    const publish = (): void => { if (!disposed) setTasks(id, { terminals: taskTerminals, workflows: taskWorkflows }) }
    const terminals = createTerminalFeed({ appServer: observation.client }, id, values => {
      taskTerminals = values
      publish()
    })
    const workflows = workflowRunsFor(observation.client)
    const stopWorkflows = workflows.getState().observe(id, values => {
      taskWorkflows = values.map(run => ({
        runId: run.runId, name: run.name, paused: run.status === 'paused',
        startedAt: run.startedAt ?? run.createdAt ?? null
      }))
      publish()
    })
    const client = observation.client
    let activeTurnId = useThreadStore.getState().runtimeSnapshots.get(id)?.activeTurnId ?? null
    terminals.setActiveTurn(activeTurnId)
    let restoredTurnId: string | null = null
    let restoring: Promise<void> | null = null
    const restoreReturnedCalls = (): Promise<void> => {
      if (!activeTurnId || activeTurnId === restoredTurnId) return Promise.resolve()
      if (restoring) return restoring
      const turnId = activeTurnId
      restoring = (async () => {
        try {
          let cursor: string | undefined
          do {
            const page = await client.request('thread/items/list', { threadId: id, turnId, limit: 100, ...(cursor ? { cursor } : {}) })
            if (disposed || !client.connectionKey() || turnId !== activeTurnId) return
            for (const entry of page.data ?? []) {
              terminals.observeCommandExecution(entry.item, entry.turnId)
              const item = toSummaryItem(entry.item, entry.turnId)
              if (item) terminals.addItems([item])
            }
            cursor = page.nextCursor ?? undefined
          } while (cursor)
          restoredTurnId = turnId
        } catch {
          return
        } finally {
          restoring = null
        }
      })()
      return restoring
    }
    const refresh = async (): Promise<void> => {
      if (disposed || !client.connectionKey()) return
      await Promise.all([terminals.refresh(), workflows.getState().refresh(id), restoreReturnedCalls()])
    }
    const stopRuntime = client.onNotification('thread/status/changed', params => {
      if (params.threadId !== id) return
      activeTurnId = params.runtime?.activeTurnId ?? null
      terminals.setActiveTurn(activeTurnId)
      if (params.runtime?.running === false) void refresh()
    })
    const stopItems = client.onNotification('item/completed', params => {
      if (params.threadId !== id) return
      terminals.observeCommandExecution(params.item, params.turnId)
      const item = toSummaryItem(params.item, params.turnId)
      if (item) terminals.addItems([item])
    })
    const stopConnection = client.onConnectionChange(connected => {
      if (connected) {
        void refresh()
        return
      }
      taskTerminals = []
      taskWorkflows = []
      publish()
    })
    observation.children.set(id, {
      refresh,
      stopTerminal: terminals.stop,
      stopWorkflow: runId => workflows.getState().stop(id, runId),
      dispose() {
        disposed = true
        terminals.dispose()
        stopWorkflows()
        stopConnection()
        stopRuntime()
        stopItems()
        setTasks(id, null)
      }
    })
    discoveries.push(refresh())
  }
  await Promise.all(discoveries)
}

function taskControls(threadId: string) {
  for (const observation of observations.values()) {
    const controls = observation.children.get(threadId)
    if (controls) return controls
  }
  throw new Error('Background task owner is no longer observed.')
}

export async function stopSubAgentTerminal(threadId: string, sessionId: string): Promise<void> {
  await taskControls(threadId).stopTerminal(sessionId)
}

export async function stopSubAgentWorkflow(threadId: string, runId: string): Promise<void> {
  await taskControls(threadId).stopWorkflow(runId)
}

export function clearSubAgentTaskActivity(parentThreadId: string): void {
  observations.get(parentThreadId)?.dispose()
  observations.delete(parentThreadId)
}

export function resetSubAgentTaskActivity(): void {
  for (const observation of observations.values()) observation.dispose()
  observations.clear()
  useSubAgentTaskActivity.setState({ tasksByThread: new Map() })
}
