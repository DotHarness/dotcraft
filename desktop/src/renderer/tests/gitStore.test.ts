import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  normalizeGitPathKey,
  useGitStore,
  type GitBranchListSnapshot
} from '../stores/gitStore'

const gitListBranches = vi.fn()

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function snapshot(current: string): GitBranchListSnapshot {
  return {
    current,
    detachedHead: null,
    branches: [
      { name: 'main', current: current === 'main' },
      { name: 'feature', current: current === 'feature' }
    ]
  }
}

describe('gitStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useGitStore.getState().reset()
    gitListBranches.mockResolvedValue(snapshot('main'))
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        api: {
          git: {
            listBranches: gitListBranches
          }
        }
      }
    })
  })

  it('deduplicates concurrent branch requests for the same normalized path', async () => {
    const pending = createDeferred<GitBranchListSnapshot>()
    gitListBranches.mockReturnValue(pending.promise)

    const first = useGitStore.getState().ensureBranches('C:\\repo')
    const second = useGitStore.getState().ensureBranches('C:/repo/')

    expect(gitListBranches).toHaveBeenCalledTimes(1)
    pending.resolve(snapshot('main'))
    await Promise.all([first, second])

    const state = useGitStore.getState().branchesByPath[normalizeGitPathKey('C:/repo')]
    expect(state.status).toBe('available')
    expect(state.snapshot?.current).toBe('main')
  })

  it('keeps the previous snapshot while a forced refresh is pending', async () => {
    await useGitStore.getState().ensureBranches('C:\\repo')
    expect(useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')].snapshot?.current)
      .toBe('main')

    const pending = createDeferred<GitBranchListSnapshot>()
    gitListBranches.mockReturnValue(pending.promise)
    const refresh = useGitStore.getState().ensureBranches('C:\\repo', { force: true })

    const refreshing = useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')]
    expect(refreshing.status).toBe('available')
    expect(refreshing.refreshing).toBe(true)
    expect(refreshing.snapshot?.current).toBe('main')

    pending.resolve(snapshot('feature'))
    await refresh

    const refreshed = useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')]
    expect(refreshed.refreshing).toBe(false)
    expect(refreshed.snapshot?.current).toBe('feature')
  })

  it('ignores stale branch results after reset', async () => {
    const pending = createDeferred<GitBranchListSnapshot>()
    gitListBranches.mockReturnValue(pending.promise)

    const request = useGitStore.getState().ensureBranches('C:\\repo')
    useGitStore.getState().reset()
    pending.resolve(snapshot('main'))
    await request

    expect(useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')]).toBeUndefined()
  })

  it('marks a path unavailable when branch probing fails', async () => {
    gitListBranches.mockRejectedValue(new Error('not a git repository'))

    await useGitStore.getState().ensureBranches('C:\\repo')

    const state = useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')]
    expect(state.status).toBe('unavailable')
    expect(state.snapshot).toBeNull()
    expect(state.errorMessage).toBe('not a git repository')
  })

  it('marks remote workspaces unavailable without calling git', async () => {
    await useGitStore.getState().ensureBranches('C:\\repo', { remote: true })

    expect(gitListBranches).not.toHaveBeenCalled()
    expect(useGitStore.getState().branchesByPath[normalizeGitPathKey('C:\\repo')].status)
      .toBe('unavailable')
  })
})
