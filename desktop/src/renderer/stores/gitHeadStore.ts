import { create } from 'zustand'
import type { GitHeadInspection } from '../../shared/gitHead'
import { normalizeGitPathKey } from './gitStore'

const GIT_HEAD_CACHE_MS = 5_000

export interface GitHeadEntry {
  status: 'checking' | 'resolved'
  inspection: GitHeadInspection | null
  updatedAt: number | null
}

interface GitHeadStore {
  byPath: Record<string, GitHeadEntry>
  ensure(path: string): Promise<void>
  reset(): void
}

const inFlight = new Map<string, Promise<void>>()
let generation = 0

export const useGitHeadStore = create<GitHeadStore>((set, get) => ({
  byPath: {},

  async ensure(path) {
    const trimmed = path.trim()
    const key = normalizeGitPathKey(trimmed)
    if (!key) return

    const cached = get().byPath[key]
    if (cached?.status === 'resolved' && cached.updatedAt != null
      && Date.now() - cached.updatedAt < GIT_HEAD_CACHE_MS) {
      return
    }
    const pending = inFlight.get(key)
    if (pending) return pending

    set((state) => ({
      byPath: {
        ...state.byPath,
        [key]: {
          status: 'checking',
          inspection: state.byPath[key]?.inspection ?? null,
          updatedAt: state.byPath[key]?.updatedAt ?? null
        }
      }
    }))

    const inspectHead = window.api?.git?.inspectHead
    if (typeof inspectHead !== 'function') {
      set((state) => ({
        byPath: {
          ...state.byPath,
          [key]: { status: 'resolved', inspection: { kind: 'none' }, updatedAt: Date.now() }
        }
      }))
      return
    }

    const requestGeneration = generation
    const request = inspectHead(trimmed)
      .then((inspection) => {
        if (requestGeneration !== generation) return
        set((state) => ({
          byPath: {
            ...state.byPath,
            [key]: { status: 'resolved', inspection, updatedAt: Date.now() }
          }
        }))
      })
      .catch(() => {
        if (requestGeneration !== generation) return
        set((state) => ({
          byPath: {
            ...state.byPath,
            [key]: { status: 'resolved', inspection: { kind: 'none' }, updatedAt: Date.now() }
          }
        }))
      })
      .finally(() => {
        if (inFlight.get(key) === request) inFlight.delete(key)
      })

    inFlight.set(key, request)
    await request
  },

  reset() {
    generation += 1
    inFlight.clear()
    set({ byPath: {} })
  }
}))
