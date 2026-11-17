import { useEffect } from 'react'
import { create } from 'zustand'
import type { ThreadTasksSnapshot } from '../../shared/threadTasks'

interface ThreadTasksState {
  byThread: Record<string, ThreadTasksSnapshot>
  apply: (snapshot: ThreadTasksSnapshot) => void
  load: (threadId: string) => Promise<void>
}

export const useThreadTasksStore = create<ThreadTasksState>((set, get) => ({
  byThread: {},
  apply: (snapshot) => set((state) => ({ byThread: { ...state.byThread, [snapshot.threadId]: snapshot } })),
  load: async (threadId) => {
    get().apply(await window.api.threadTasks.list(threadId))
  }
}))

let unsubscribe: (() => void) | null = null

function subscribe(): void {
  if (unsubscribe) return
  unsubscribe = window.api.threadTasks.onChanged((snapshot) => useThreadTasksStore.getState().apply(snapshot))
}

export function useThreadAwaitsAnswer(threadId: string): boolean {
  const snapshot = useThreadTasks(threadId)
  return snapshot?.question != null || snapshot?.tasks.some((task) => task.kind === 'offer' && task.state === 'pending') === true
}

export function useThreadTasks(threadId: string): ThreadTasksSnapshot | undefined {
  const snapshot = useThreadTasksStore((state) => state.byThread[threadId])
  useEffect(() => {
    if (!threadId) return
    subscribe()
    void useThreadTasksStore.getState().load(threadId).catch(() => undefined)
  }, [threadId])
  return snapshot
}
