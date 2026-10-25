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

export function useThreadTasks(threadId: string): ThreadTasksSnapshot | undefined {
  const snapshot = useThreadTasksStore((state) => state.byThread[threadId])
  useEffect(() => {
    subscribe()
    void useThreadTasksStore.getState().load(threadId).catch(() => undefined)
  }, [threadId])
  return snapshot
}
