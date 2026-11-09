import { create } from 'zustand'
import type { ServerNotificationMethods } from '@dotcraft/sdk/contracts'
import { createTerminalFeed } from '../components/conversation/summary/terminalFeed'
import { workflowRunsFor, type WorkflowRunClient } from './workflowRunCache'
import { getThreadConnection, isThreadObservationCurrent } from './threadConnectionStore'
import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import type { SubAgentChild } from './subAgentChildren'

export const useSubAgentTaskActivity = create<{
  activeThreadIds: ReadonlySet<string>
  pendingThreadIds: ReadonlySet<string>
}>(() => ({ activeThreadIds: new Set(), pendingThreadIds: new Set() }))

interface TaskObservation {
  connectionKey: string
  connectionId: string
  children: Map<string, { refresh(): Promise<void>; dispose(): void }>
  client: WorkflowRunClient
  dispose(): void
}

const observations = new Map<string, TaskObservation>()

function sourceFor(parentThreadId: string) {
  return getThreadConnection(parentThreadId) ?? useConnectionStore.getState()
}

function setActive(threadId: string, active: boolean): void {
  useSubAgentTaskActivity.setState(state => {
    if (state.activeThreadIds.has(threadId) === active) return state
    const activeThreadIds = new Set(state.activeThreadIds)
    if (active) activeThreadIds.add(threadId)
    else activeThreadIds.delete(threadId)
    return { activeThreadIds }
  })
}

function setPending(threadId: string, pending: boolean): void {
  useSubAgentTaskActivity.setState(state => {
    if (state.pendingThreadIds.has(threadId) === pending) return state
    const pendingThreadIds = new Set(state.pendingThreadIds)
    if (pending) pendingThreadIds.add(threadId)
    else pendingThreadIds.delete(threadId)
    return { pendingThreadIds }
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

export async function syncSubAgentTaskActivity(parentThreadId: string, children: readonly SubAgentChild[]): Promise<void> {
  const source = sourceFor(parentThreadId)
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
    observation = createObservation(parentThreadId, source.connectionKey, source.connectionId)
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
      if (useSubAgentTaskActivity.getState().pendingThreadIds.has(id)) discoveries.push(existing.refresh())
      continue
    }
    let disposed = false
    let terminalActive = false
    let workflowActive = false
    setPending(id, true)
    const publish = (): void => { if (!disposed) setActive(id, terminalActive || workflowActive) }
    const terminals = createTerminalFeed({ appServer: observation.client }, id, values => {
      terminalActive = values.length > 0
      publish()
    })
    const workflows = workflowRunsFor(observation.client)
    const stopWorkflows = workflows.getState().observe(id, values => {
      workflowActive = values.length > 0
      publish()
    })
    const client = observation.client
    const refresh = async (): Promise<void> => {
      const results = await Promise.all([terminals.refresh(), workflows.getState().list(id)])
      if (!disposed && client.connectionKey() && results.every(Boolean)) setPending(id, false)
    }
    const stopConnection = client.onConnectionChange(connected => {
      if (connected) {
        void refresh()
        return
      }
      terminalActive = false
      workflowActive = false
      setPending(id, true)
      publish()
    })
    observation.children.set(id, {
      refresh,
      dispose() {
        disposed = true
        terminals.dispose()
        stopWorkflows()
        stopConnection()
        setActive(id, false)
        setPending(id, false)
      }
    })
    discoveries.push(refresh())
  }
  await Promise.all(discoveries)
}

export function clearSubAgentTaskActivity(parentThreadId: string): void {
  observations.get(parentThreadId)?.dispose()
  observations.delete(parentThreadId)
}

export function resetSubAgentTaskActivity(): void {
  for (const observation of observations.values()) observation.dispose()
  observations.clear()
  useSubAgentTaskActivity.setState({ activeThreadIds: new Set(), pendingThreadIds: new Set() })
}
