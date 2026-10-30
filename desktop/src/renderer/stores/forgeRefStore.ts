import { useEffect } from 'react'
import { create } from 'zustand'
import type {
  ForgeBranchPullResult,
  ForgePullSummary,
  ForgeRefResult,
  ForgeRefTarget,
  ForgeRepo
} from '../../shared/forgeReferences'
import { normalizeGitPathKey } from './gitStore'

const RESULT_TTL_MS = 60_000
const FAILURE_TTL_MS = 5_000
const REPO_TTL_MS = 5 * 60_000
const ORIGIN_PULL_REFRESH_MS = 30_000

interface ForgeRefEntry<T> {
  status: 'loading' | 'resolved'
  result: T | null
  updatedAt: number | null
}

type Slice = 'refs' | 'branchPulls' | 'originPulls' | 'repos'

interface ForgeRefStore {
  refs: Record<string, ForgeRefEntry<ForgeRefResult>>
  branchPulls: Record<string, ForgeRefEntry<ForgeBranchPullResult>>
  originPulls: Record<string, ForgeRefEntry<ForgeBranchPullResult>>
  repos: Record<string, ForgeRefEntry<ForgeRepo | null>>
  ensureRef(target: ForgeRefTarget): Promise<void>
  ensureBranchPull(path: string, branch: string, options?: { force?: boolean }): Promise<void>
  ensureOriginPull(originUrl: string, branch: string): Promise<void>
  ensureRepo(path: string): Promise<void>
}

export type ForgeRefLoad = { status: 'loading' } | ForgeRefResult

const UNAVAILABLE = { status: 'error', error: 'unavailable' } as const
const inFlight = new Map<string, Promise<void>>()

function forgeRefKey(target: ForgeRefTarget): string {
  return `${target.provider}|${target.host}/${target.path}${target.kind === 'pull' ? '!' : '#'}${target.number}`.toLowerCase()
}

function branchPullKey(path: string, branch: string): string {
  const pathKey = normalizeGitPathKey(path.trim())
  const branchName = branch.trim()
  return pathKey && branchName ? `${pathKey}\n${branchName}` : ''
}

function originPullKey(originUrl: string, branch: string): string {
  const origin = originUrl.trim()
  const branchName = branch.trim()
  return origin && branchName ? `${origin}
${branchName}` : ''
}

function ttlFor(result: unknown): number {
  return (result as { status?: string } | null)?.status === 'error' ? FAILURE_TTL_MS : RESULT_TTL_MS
}

export const useForgeRefStore = create<ForgeRefStore>((set, get) => {
  async function ensure<T>(
    slice: Slice,
    key: string,
    ttlMs: (result: T | null) => number,
    request: () => Promise<T>,
    fallback: T,
    force = false
  ): Promise<void> {
    if (!key) return
    const entries = get()[slice] as Record<string, ForgeRefEntry<T>>
    const cached = entries[key]
    if (!force && cached?.status === 'resolved' && cached.updatedAt != null && Date.now() - cached.updatedAt < ttlMs(cached.result)) {
      return
    }
    const inFlightKey = `${slice}:${key}`
    const pending = inFlight.get(inFlightKey)
    if (pending) return pending

    const write = (entry: ForgeRefEntry<T>): void => {
      set((state) => ({ [slice]: { ...state[slice], [key]: entry } }) as Partial<ForgeRefStore>)
    }
    write({ status: 'loading', result: cached?.result ?? null, updatedAt: cached?.updatedAt ?? null })

    const settle = (result: T): void => write({ status: 'resolved', result, updatedAt: Date.now() })
    const promise = Promise.resolve()
      .then(request)
      .then(settle, () => settle(fallback))
      .finally(() => {
        if (inFlight.get(inFlightKey) === promise) inFlight.delete(inFlightKey)
      })
    inFlight.set(inFlightKey, promise)
    await promise
  }

  return {
    refs: {},
    branchPulls: {},
    originPulls: {},
    repos: {},

    ensureRef(target) {
      return ensure<ForgeRefResult>('refs', forgeRefKey(target), ttlFor, () => window.api.forge.getReferenceSummary(target), UNAVAILABLE)
    },

    ensureBranchPull(path, branch, options) {
      return ensure<ForgeBranchPullResult>(
        'branchPulls',
        branchPullKey(path, branch),
        ttlFor,
        () => window.api.forge.getBranchPullRequest(path.trim(), branch.trim()),
        UNAVAILABLE,
        options?.force === true
      )
    },

    ensureOriginPull(originUrl, branch) {
      return ensure<ForgeBranchPullResult>(
        'originPulls',
        originPullKey(originUrl, branch),
        ttlFor,
        () => window.api.forge.getOriginBranchPullRequest(originUrl.trim(), branch.trim()),
        UNAVAILABLE
      )
    },

    ensureRepo(path) {
      const trimmed = path.trim()
      return ensure<ForgeRepo | null>('repos', normalizeGitPathKey(trimmed), () => REPO_TTL_MS, () => window.api.forge.getWorkspaceRepo(trimmed), null)
    }
  }
})

export function useForgeRefLoad(target: ForgeRefTarget): ForgeRefLoad {
  const key = forgeRefKey(target)
  const entry = useForgeRefStore((state) => state.refs[key])
  const { provider, host, path, kind, number } = target
  useEffect(() => {
    void useForgeRefStore.getState().ensureRef({ provider, host, path, kind, number })
  }, [host, kind, number, path, provider])
  return entry?.result ?? { status: 'loading' }
}

export function useWorkspaceForgeRepo(path: string | null | undefined): ForgeRepo | null {
  const trimmed = path?.trim() ?? ''
  const key = trimmed ? normalizeGitPathKey(trimmed) : ''
  const repo = useForgeRefStore((state) => (key ? state.repos[key]?.result ?? null : null))
  useEffect(() => {
    if (trimmed) void useForgeRefStore.getState().ensureRepo(trimmed)
  }, [trimmed])
  return repo
}

export function useBranchPullRequest(path: string | null | undefined, branch: string | null | undefined, enabled: boolean): ForgePullSummary | null {
  const key = enabled && path && branch ? branchPullKey(path, branch) : ''
  const result = useForgeRefStore((state) => (key ? state.branchPulls[key]?.result ?? null : null))
  useEffect(() => {
    if (key && path && branch) void useForgeRefStore.getState().ensureBranchPull(path, branch)
  }, [branch, key, path])
  return result?.status === 'ready' ? result.ref : null
}

export function useOriginBranchPullRequest(
  originUrl: string | null | undefined,
  branch: string | null | undefined,
  enabled: boolean
): ForgePullSummary | null {
  const key = enabled && originUrl && branch ? originPullKey(originUrl, branch) : ''
  const result = useForgeRefStore((state) => (key ? state.originPulls[key]?.result ?? null : null))
  const pull = result?.status === 'ready' ? result.ref : null
  const pullOpen = pull != null && pull.state !== 'merged' && pull.state !== 'closed'
  useEffect(() => {
    if (!key || !originUrl || !branch) return
    const refresh = (): void => {
      void useForgeRefStore.getState().ensureOriginPull(originUrl, branch)
    }
    refresh()
    window.addEventListener('focus', refresh)
    const timer = pullOpen
      ? window.setInterval(() => {
          if (document.hasFocus()) refresh()
        }, ORIGIN_PULL_REFRESH_MS)
      : undefined
    return () => {
      window.removeEventListener('focus', refresh)
      window.clearInterval(timer)
    }
  }, [branch, key, originUrl, pullOpen])
  return pull
}
