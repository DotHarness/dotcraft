import { create } from 'zustand'
import type { WorkflowRunView, WorkflowRunUpdatedNotification } from '@dotcraft/sdk/contracts'

export interface WorkflowRunEntry {
  run: WorkflowRunView | null
  loading: boolean
  error: string | null
}

interface WorkflowRunStore {
  entries: Map<string, WorkflowRunEntry>
  load(threadId: string, runId: string): Promise<void>
  stop(threadId: string, runId: string): Promise<void>
}

const keyOf = (threadId: string, runId: string): string => `${threadId}\u0000${runId}`
let subscriptionsReady = false
const requestGenerations = new Map<string, number>()

export const useWorkflowRunStore = create<WorkflowRunStore>((set, get) => ({
  entries: new Map(),

  async load(threadId, runId) {
    ensureSubscriptions()
    const key = keyOf(threadId, runId)
    const generation = (requestGenerations.get(key) ?? 0) + 1
    requestGenerations.set(key, generation)
    const previous = get().entries.get(key)
    set((state) => {
      const entries = new Map(state.entries)
      entries.set(key, { run: previous?.run ?? null, loading: true, error: null })
      return { entries }
    })
    try {
      const result = await window.api.appServer.sendRequest('workflow/run/read', { threadId, runId })
      if (requestGenerations.get(key) !== generation) return
      set((state) => {
        const entries = new Map(state.entries)
        entries.set(key, { run: result.run, loading: false, error: null })
        return { entries }
      })
    } catch (error: unknown) {
      if (requestGenerations.get(key) !== generation) return
      set((state) => {
        const entries = new Map(state.entries)
        entries.set(key, {
          run: state.entries.get(key)?.run ?? null,
          loading: false,
          error: error instanceof Error ? error.message : String(error)
        })
        return { entries }
      })
    }
  },

  async stop(threadId, runId) {
    const result = await window.api.appServer.sendRequest('workflow/run/stop', { threadId, runId })
    set((state) => {
      const entries = new Map(state.entries)
      entries.set(keyOf(threadId, runId), { run: result.run, loading: false, error: null })
      return { entries }
    })
  }
}))

export function selectWorkflowRunEntry(
  entries: Map<string, WorkflowRunEntry>,
  threadId: string,
  runId: string
): WorkflowRunEntry | undefined {
  return entries.get(keyOf(threadId, runId))
}

function ensureSubscriptions(): void {
  if (subscriptionsReady) return
  subscriptionsReady = true
  window.api.appServer.onNotification((payload) => {
    if (payload.method !== 'workflow/run/updated') return
    const update = payload.params as WorkflowRunUpdatedNotification
    if (!useWorkflowRunStore.getState().entries.has(keyOf(update.threadId, update.runId))) return
    void useWorkflowRunStore.getState().load(update.threadId, update.runId)
  })
  window.api.appServer.onConnectionStatus(() => {
    for (const key of useWorkflowRunStore.getState().entries.keys()) {
      const separator = key.indexOf('\u0000')
      void useWorkflowRunStore.getState().load(key.slice(0, separator), key.slice(separator + 1))
    }
  })
}
