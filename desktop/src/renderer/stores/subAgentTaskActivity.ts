import { toSummaryItem } from '../components/conversation/summary/sources'
import { useThreadStore } from './threadStore'
import type { WorkflowRunClient } from './workflowRunCache'
import { retainThreadTaskFeed, resetThreadTaskFeeds, type ThreadTaskFeed } from './threadTaskFeed'
import { useSubAgentTaskActivity } from './threadTaskState'
import { getThreadConnection, isThreadObservationCurrent } from './threadConnectionStore'
import { useConnectionStore } from './connectionStore'
import { createScopedWorkflowClient } from './scopedWorkflowClient'
import type { SubAgentChild } from './subAgentChildren'
import { useSubAgentStore } from './subAgentStore'

export { useSubAgentTaskActivity, type SubAgentTasks } from './threadTaskState'

interface TaskObservation {
  connectionKey: string
  connectionId: string
  children: Map<string, ThreadTaskFeed>
  client: WorkflowRunClient
  dispose(): void
}

const observations = new Map<string, TaskObservation>()

function sourceFor(parentThreadId: string) {
  return getThreadConnection(parentThreadId) ?? useConnectionStore.getState()
}

function createObservation(connectionKey: string, connectionId: string): TaskObservation {
  const client = createScopedWorkflowClient(connectionKey, connectionId)
  const observation: TaskObservation = {
    connectionKey,
    connectionId,
    children: new Map(),
    client,
    dispose() {
      for (const feed of this.children.values()) feed.release()
      client.dispose()
    }
  }
  const running = new Map<string, boolean>()
  observation.client.onNotification('thread/status/changed', params => {
    if (!params.threadId) return
    const feed = observation.children.get(params.threadId)
    if (!feed) return
    const wasRunning = running.get(params.threadId) ?? useThreadStore.getState().runtimeSnapshots.get(params.threadId)?.running
    running.set(params.threadId, params.runtime?.running === true)
    if (params.runtime?.running === false) {
      if (wasRunning) feed.beginIdle()
      else void feed.refresh()
      void useSubAgentStore.getState().fetchChildren(params.threadId).catch(() => {})
    }
  })
  observation.client.onNotification('item/completed', params => {
    const feed = observation.children.get(params.threadId)
    if (!feed) return
    feed.observeCommandExecution(params.item, params.turnId)
    const item = toSummaryItem(params.item, params.turnId)
    if (item) feed.addItems([item])
  })
  return observation
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
  if (!source.connectionKey || !source.connectionId || !isThreadObservationCurrent({
    connectionKey: source.connectionKey, connectionId: source.connectionId
  })) return
  if (!observation) {
    observation = createObservation(source.connectionKey, source.connectionId)
    observations.set(parentThreadId, observation)
  }
  const ids = new Set([parentThreadId, ...children
    .filter(child => !child.isPlaceholder && child.status !== 'closed').map(child => child.childThreadId)])
  for (const [id, feed] of observation.children) {
    if (ids.has(id)) continue
    feed.release()
    observation.children.delete(id)
  }
  const discoveries: Promise<boolean>[] = []
  for (const id of ids) {
    let feed = observation.children.get(id)
    if (!feed) {
      feed = retainThreadTaskFeed(observation.client, id)
      observation.children.set(id, feed)
    }
    discoveries.push(feed.refresh())
  }
  await Promise.all(discoveries)
}

export function beginSubAgentIdleReconciliation(threadId: string): void {
  if (useThreadStore.getState().runtimeSnapshots.get(threadId)?.running !== true) return
  for (const observation of observations.values()) {
    const feed = observation.children.get(threadId)
    if (feed) {
      feed.beginIdle()
      return
    }
  }
}

function taskControls(threadId: string): ThreadTaskFeed {
  for (const observation of observations.values()) {
    const feed = observation.children.get(threadId)
    if (feed) return feed
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
  resetThreadTaskFeeds()
  useSubAgentTaskActivity.setState({ tasksByThread: new Map(), reconcilingThreads: new Set() })
}
