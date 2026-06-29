import { create } from 'zustand'

export interface PerforceChangelistEntry {
  id: string
  isDefault: boolean
  description: string
  user: string
  client: string
  status: string
}

export interface PerforceThreadTarget {
  provider: 'perforce'
  changelist: string
}

export interface PerforceChangelistSnapshot {
  changelists: PerforceChangelistEntry[]
  target: PerforceThreadTarget
}

export type PerforceChangelistStatus = 'idle' | 'loading' | 'available' | 'error'

export interface PerforceChangelistThreadState {
  threadId: string
  status: PerforceChangelistStatus
  snapshot: PerforceChangelistSnapshot | null
  errorMessage: string | null
  requestId: number
}

interface PerforceChangelistStore {
  byThreadId: Record<string, PerforceChangelistThreadState>
  ensure(threadId: string, options?: { force?: boolean }): Promise<void>
  setTarget(threadId: string, changelist: string): Promise<void>
  createChangelist(threadId: string, description: string): Promise<PerforceChangelistEntry>
  reset(): void
}

let nextRequestId = 0
const inFlight = new Map<string, Promise<void>>()

export function changelistLabel(changelist: string | null | undefined): string {
  const value = (changelist ?? '').trim()
  return value && value !== 'default' ? value : 'default'
}

export const usePerforceChangelistStore = create<PerforceChangelistStore>((set, get) => ({
  byThreadId: {},

  async ensure(threadId, options = {}) {
    const id = threadId.trim()
    if (!id) return
    const current = get().byThreadId[id]
    if (!options.force && current?.status === 'available') return
    const existing = inFlight.get(id)
    if (existing && !options.force) {
      await existing
      return
    }

    const requestId = ++nextRequestId
    set((state) => ({
      byThreadId: {
        ...state.byThreadId,
        [id]: {
          threadId: id,
          status: 'loading',
          snapshot: state.byThreadId[id]?.snapshot ?? null,
          errorMessage: null,
          requestId
        }
      }
    }))

    const request = (async () => {
      try {
        const snapshot = await window.api.appServer.sendRequest(
          'sourceControl/changelist/list',
          { threadId: id },
          30_000
        ) as PerforceChangelistSnapshot
        const latest = get().byThreadId[id]
        if (latest?.requestId !== requestId) return
        set((state) => ({
          byThreadId: {
            ...state.byThreadId,
            [id]: {
              threadId: id,
              status: 'available',
              snapshot,
              errorMessage: null,
              requestId
            }
          }
        }))
      } catch (err) {
        const latest = get().byThreadId[id]
        if (latest?.requestId !== requestId) return
        set((state) => ({
          byThreadId: {
            ...state.byThreadId,
            [id]: {
              threadId: id,
              status: 'error',
              snapshot: state.byThreadId[id]?.snapshot ?? null,
              errorMessage: err instanceof Error ? err.message : String(err),
              requestId
            }
          }
        }))
      } finally {
        inFlight.delete(id)
      }
    })()

    inFlight.set(id, request)
    await request
  },

  async setTarget(threadId, changelist) {
    const id = threadId.trim()
    if (!id) return
    const target = await window.api.appServer.sendRequest(
      'sourceControl/threadTarget/update',
      { threadId: id, target: { provider: 'perforce', changelist } },
      20_000
    ) as { target: PerforceThreadTarget }

    set((state) => {
      const current = state.byThreadId[id]
      if (!current) return state
      return {
        byThreadId: {
          ...state.byThreadId,
          [id]: {
            ...current,
            snapshot: current.snapshot
              ? { ...current.snapshot, target: target.target }
              : { changelists: [], target: target.target }
          }
        }
      }
    })
  },

  async createChangelist(threadId, description) {
    const id = threadId.trim()
    const result = await window.api.appServer.sendRequest(
      'sourceControl/changelist/create',
      { threadId: id, description, setAsTarget: true },
      30_000
    ) as { changelist: PerforceChangelistEntry, target: PerforceThreadTarget }

    set((state) => {
      const current = state.byThreadId[id]
      const snapshot = current?.snapshot ?? { changelists: [], target: result.target }
      const changelists = [
        ...snapshot.changelists.filter((entry) => entry.id !== result.changelist.id),
        result.changelist
      ]
      return {
        byThreadId: {
          ...state.byThreadId,
          [id]: {
            threadId: id,
            status: 'available',
            snapshot: { changelists, target: result.target },
            errorMessage: null,
            requestId: current?.requestId ?? ++nextRequestId
          }
        }
      }
    })
    return result.changelist
  },

  reset() {
    inFlight.clear()
    set({ byThreadId: {} })
  }
}))
