import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useGitHeadStore } from '../stores/gitHeadStore'
import { normalizeGitPathKey } from '../stores/gitStore'
import type { GitHeadInspection } from '../../shared/gitHead'

const inspectHead = vi.fn()

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

describe('gitHeadStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.restoreAllMocks()
    useGitHeadStore.getState().reset()
    inspectHead.mockResolvedValue({ kind: 'branch', label: 'main' })
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: { api: { git: { inspectHead } } }
    })
  })

  it('deduplicates normalized paths and reuses a result for five seconds', async () => {
    const now = vi.spyOn(Date, 'now').mockReturnValue(1_000)
    const pending = deferred<GitHeadInspection>()
    inspectHead.mockReturnValue(pending.promise)

    const first = useGitHeadStore.getState().ensure('C:\\repo')
    const second = useGitHeadStore.getState().ensure('C:/repo/')
    expect(inspectHead).toHaveBeenCalledTimes(1)

    pending.resolve({ kind: 'branch', label: 'main' })
    await Promise.all([first, second])
    await useGitHeadStore.getState().ensure('C:\\repo')
    expect(inspectHead).toHaveBeenCalledTimes(1)

    now.mockReturnValue(6_001)
    inspectHead.mockResolvedValue({ kind: 'branch', label: 'feature/details' })
    await useGitHeadStore.getState().ensure('C:\\repo')
    expect(inspectHead).toHaveBeenCalledTimes(2)
    expect(useGitHeadStore.getState().byPath[normalizeGitPathKey('C:\\repo')].inspection)
      .toEqual({ kind: 'branch', label: 'feature/details' })
  })

  it('ignores a stale result after reset', async () => {
    const pending = deferred<GitHeadInspection>()
    inspectHead.mockReturnValue(pending.promise)

    const request = useGitHeadStore.getState().ensure('C:\\repo')
    useGitHeadStore.getState().reset()
    pending.resolve({ kind: 'branch', label: 'stale' })
    await request

    expect(useGitHeadStore.getState().byPath).toEqual({})
  })
})
