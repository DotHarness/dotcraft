import { create } from 'zustand'

export interface GitBranchEntry {
  name: string
  current: boolean
}

export interface GitBranchListSnapshot {
  current: string | null
  detachedHead: string | null
  branches: GitBranchEntry[]
}

export type GitBranchPathStatus = 'checking' | 'available' | 'unavailable'

export interface GitBranchPathState {
  path: string
  status: GitBranchPathStatus
  snapshot: GitBranchListSnapshot | null
  refreshing: boolean
  errorMessage: string | null
  updatedAt: number | null
  requestId: number
}

interface EnsureBranchesOptions {
  force?: boolean
  remote?: boolean
}

interface GitStoreState {
  branchesByPath: Record<string, GitBranchPathState>
  generation: number
}

interface GitStoreActions {
  ensureBranches(path: string, options?: EnsureBranchesOptions): Promise<void>
  markUnavailable(path: string, errorMessage?: string | null): void
  reset(): void
}

type GitStore = GitStoreState & GitStoreActions

const inFlightByPath = new Map<string, Promise<void>>()
let nextRequestId = 0

const initialState: GitStoreState = {
  branchesByPath: {},
  generation: 0
}

export function normalizeGitPathKey(path: string | null | undefined): string {
  const normalized = (path ?? '').trim().replace(/\\/g, '/').replace(/\/+$/, '')
  if (!normalized) return ''
  return normalized.replace(/^([A-Za-z]):/, (match) => match.toLowerCase())
}

export function isGitBranchProbeSettled(status: GitBranchPathStatus | undefined | null): boolean {
  return status === 'available' || status === 'unavailable'
}

function unavailableState(path: string, previous: GitBranchPathState | undefined, errorMessage: string | null): GitBranchPathState {
  return {
    path,
    status: 'unavailable',
    snapshot: null,
    refreshing: false,
    errorMessage,
    updatedAt: Date.now(),
    requestId: previous?.requestId ?? 0
  }
}

export const useGitStore = create<GitStore>((set, get) => ({
  ...initialState,

  async ensureBranches(path, options = {}) {
    const trimmedPath = path.trim()
    const key = normalizeGitPathKey(trimmedPath)
    if (!key || options.remote === true) {
      get().markUnavailable(trimmedPath)
      return
    }

    const current = get().branchesByPath[key]
    if (!options.force && current?.status === 'available') {
      return
    }
    if (!options.force && current?.status === 'unavailable') {
      return
    }

    const existingRequest = inFlightByPath.get(key)
    if (existingRequest) {
      await existingRequest
      return
    }

    const requestId = ++nextRequestId
    const generation = get().generation
    set((state) => {
      const previous = state.branchesByPath[key]
      return {
        branchesByPath: {
          ...state.branchesByPath,
          [key]: {
            path: trimmedPath,
            status: previous?.snapshot ? 'available' : 'checking',
            snapshot: previous?.snapshot ?? null,
            refreshing: true,
            errorMessage: null,
            updatedAt: previous?.updatedAt ?? null,
            requestId
          }
        }
      }
    })

    const request = (async () => {
      try {
        const listBranches = window.api?.git?.listBranches
        if (typeof listBranches !== 'function') {
          throw new Error('Git branch API is unavailable.')
        }

        const snapshot = await listBranches(trimmedPath)
        const latest = get()
        if (latest.generation !== generation) return
        const currentEntry = latest.branchesByPath[key]
        if (currentEntry?.requestId !== requestId) return

        set((state) => ({
          branchesByPath: {
            ...state.branchesByPath,
            [key]: {
              path: trimmedPath,
              status: 'available',
              snapshot,
              refreshing: false,
              errorMessage: null,
              updatedAt: Date.now(),
              requestId
            }
          }
        }))
      } catch (err) {
        const latest = get()
        if (latest.generation !== generation) return
        const currentEntry = latest.branchesByPath[key]
        if (currentEntry?.requestId !== requestId) return

        set((state) => ({
          branchesByPath: {
            ...state.branchesByPath,
            [key]: unavailableState(
              trimmedPath,
              state.branchesByPath[key],
              err instanceof Error ? err.message : String(err)
            )
          }
        }))
      } finally {
        inFlightByPath.delete(key)
      }
    })()

    inFlightByPath.set(key, request)
    await request
  },

  markUnavailable(path, errorMessage = null) {
    const trimmedPath = path.trim()
    const key = normalizeGitPathKey(trimmedPath)
    if (!key) return
    set((state) => ({
      branchesByPath: {
        ...state.branchesByPath,
        [key]: unavailableState(trimmedPath, state.branchesByPath[key], errorMessage)
      }
    }))
  },

  reset() {
    inFlightByPath.clear()
    set((state) => ({
      branchesByPath: {},
      generation: state.generation + 1
    }))
  }
}))
