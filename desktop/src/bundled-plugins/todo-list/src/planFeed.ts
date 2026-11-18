import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useEffect, useSyncExternalStore } from 'react'
import { readTodos, type PlanSnapshot, type ThreadTodos } from './model'

interface ThreadEntry {
  todos: ThreadTodos | null
  readonly listeners: Set<() => void>
}

const threads = new Map<string, ThreadEntry>()

function entryOf(threadId: string): ThreadEntry {
  let entry = threads.get(threadId)
  if (!entry) {
    entry = { todos: null, listeners: new Set() }
    threads.set(threadId, entry)
  }
  return entry
}

function publish(threadId: string, plan: PlanSnapshot | null | undefined): void {
  const entry = entryOf(threadId)
  entry.todos = readTodos(plan)
  for (const listener of entry.listeners) listener()
}

export function startPlanFeed(host: DesktopPluginHost): () => void {
  const stop = host.appServer.onNotification('plan/updated', (params) => {
    if (params.threadId) publish(params.threadId, params)
  })
  return () => {
    stop()
    threads.clear()
  }
}

/** The thread's to-do list, restored by `thread/read` and then replaced by each `plan/updated`. */
export function useThreadTodos(host: DesktopPluginHost, threadId: string): ThreadTodos | null {
  useEffect(() => {
    if (entryOf(threadId).todos !== null) return
    host.appServer.request('thread/read', { threadId }).then(
      (result) => { if (entryOf(threadId).todos === null) publish(threadId, result.thread.plan) },
      () => undefined
    )
  }, [host, threadId])
  return useSyncExternalStore(
    (listener) => {
      const { listeners } = entryOf(threadId)
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => threads.get(threadId)?.todos ?? null
  )
}
