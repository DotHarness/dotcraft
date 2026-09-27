import { create } from 'zustand'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

interface TurnBookmarkStore {
  loaded: boolean
  byThread: Record<string, string[]>
  /** Applies the persisted setting once; after that this store is the only writer. */
  hydrate(byThread: Record<string, string[]> | undefined): void
  toggle(key: string, entryId: string): void
  forgetThread(threadId: string): void
}

export function turnBookmarkKey(workspacePath: string, threadId: string): string | null {
  const workspaceKey = normalizeWorkspaceProjectKey(workspacePath)
  return workspaceKey && threadId ? `${workspaceKey}::${threadId}` : null
}

function persist(changes: Record<string, string[]>): void {
  void window.api.settings
    .set({ turnBookmarksByThread: changes })
    .catch((err: unknown) => console.error('settings:set turnBookmarksByThread failed:', err))
}

export const useTurnBookmarkStore = create<TurnBookmarkStore>((set, get) => ({
  loaded: false,
  byThread: {},

  hydrate(byThread) {
    if (!get().loaded) set({ loaded: true, byThread: byThread ?? {} })
  },

  toggle(key, entryId) {
    const current = get().byThread[key] ?? []
    const ids = current.includes(entryId) ? current.filter((id) => id !== entryId) : [...current, entryId]
    const byThread = { ...get().byThread, [key]: ids }
    if (ids.length === 0) delete byThread[key]
    set({ byThread })
    persist({ [key]: ids })
  },

  forgetThread(threadId) {
    const keys = Object.keys(get().byThread).filter((key) => key.endsWith(`::${threadId}`))
    if (keys.length === 0) return
    const byThread = { ...get().byThread }
    for (const key of keys) delete byThread[key]
    set({ byThread })
    persist(Object.fromEntries(keys.map((key) => [key, []])))
  }
}))
