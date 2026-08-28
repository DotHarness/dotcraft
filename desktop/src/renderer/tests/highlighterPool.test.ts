import { describe, expect, it, vi } from 'vitest'
import { HighlighterPool } from '../highlight/pool/highlighterPool'
import type { BackendState, HighlightBackend } from '../highlight/pool/backend'
import type { HighlightRequestMessage, HighlightResultMessage } from '../highlight/pool/protocol'
import type { FileHighlightResult } from '../highlight/types'

const RESULT: FileHighlightResult = { lines: [[{ text: 'x' }]], highlighted: true }

/** A backend that holds every request open until the test releases it. */
function controllableBackend(slots = 1): HighlightBackend & {
  requests: HighlightRequestMessage[]
  settle: () => Promise<void>
} {
  const pending: (() => void)[] = []
  const requests: HighlightRequestMessage[] = []
  let busy = 0

  return {
    state: 'initialized' as BackendState,
    totalSlots: slots,
    get busySlots() { return busy },
    get freeSlots() { return slots - busy },
    workersFailed: false,
    requests,
    ready: () => Promise.resolve(),
    run: (request) => {
      requests.push(request)
      busy++
      return new Promise<HighlightResultMessage>((resolve) => {
        pending.push(() => {
          busy--
          resolve({ type: 'result', id: request.id, requestType: 'file', result: RESULT })
        })
      })
    },
    settle: async () => {
      while (pending.length > 0) pending.shift()?.()
      await Promise.resolve()
      await Promise.resolve()
    },
    terminate: () => {}
  }
}

function fileRequest(cacheKey: string): { cacheKey: string; name: string; contents: string } {
  return { cacheKey, name: 'a.ts', contents: 'const x = 1' }
}

describe('HighlighterPool', () => {
  it('caches a result and returns it synchronously to the next caller', async () => {
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend })
    const first = {}
    const onResult = vi.fn()

    expect(pool.requestFile(first, fileRequest('k'), onResult)).toBeUndefined()
    await backend.settle()
    expect(onResult).toHaveBeenCalledWith(RESULT)

    const second = {}
    expect(pool.requestFile(second, fileRequest('k'), vi.fn())).toBe(RESULT)
    expect(backend.requests).toHaveLength(1)
  })

  it('shares one task between subscribers asking for the same content', async () => {
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend })
    const a = vi.fn()
    const b = vi.fn()

    pool.requestFile({}, fileRequest('k'), a)
    pool.requestFile({}, fileRequest('k'), b)

    expect(backend.requests).toHaveLength(1)
    await backend.settle()
    expect(a).toHaveBeenCalledWith(RESULT)
    expect(b).toHaveBeenCalledWith(RESULT)
  })

  it('drops a queued task once its last subscriber goes away', async () => {
    // One slot, so the second request has to wait in the queue where it can
    // still be called off — the case a fast scroll produces constantly.
    const backend = controllableBackend(1)
    const pool = new HighlighterPool({ backend })
    const held = {}
    const leaving = {}

    pool.requestFile(held, fileRequest('first'), vi.fn())
    pool.requestFile(leaving, fileRequest('second'), vi.fn())
    expect(backend.requests).toHaveLength(1)

    pool.release(leaving)
    await backend.settle()

    expect(backend.requests.map((request) => request.request.cacheKey)).toEqual(['first'])
  })

  it('keeps a priming task alive even though nothing is subscribed to it', async () => {
    const backend = controllableBackend(1)
    const pool = new HighlighterPool({ backend })

    pool.requestFile({}, fileRequest('first'), vi.fn())
    pool.primeFile(fileRequest('warm'))
    await backend.settle()
    await backend.settle()

    expect(backend.requests.map((request) => request.request.cacheKey)).toEqual(['first', 'warm'])
    expect(pool.peekFile('warm')).toBe(RESULT)
  })

  it('skips priming content the cache already holds', async () => {
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend })

    pool.requestFile({}, fileRequest('k'), vi.fn())
    await backend.settle()
    pool.primeFile(fileRequest('k'))

    expect(backend.requests).toHaveLength(1)
  })

  it('evicts the oldest entry once the cache is full', async () => {
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend, cacheSize: 2 })

    for (const key of ['a', 'b', 'c']) {
      pool.requestFile({}, fileRequest(key), vi.fn())
      await backend.settle()
    }

    expect(pool.peekFile('a')).toBeUndefined()
    expect(pool.peekFile('b')).toBe(RESULT)
    expect(pool.peekFile('c')).toBe(RESULT)
  })

  it('dispatches work queued before the backend finished starting', async () => {
    // A backend reporting no free slots until it is ready tells the pool it has
    // nowhere to dispatch, so without a re-drain the first request never leaves the queue.
    let ready = false
    let resolveReady = (): void => {}
    const readyPromise = new Promise<void>((resolve) => {
      resolveReady = () => { ready = true; resolve() }
    })
    const requests: HighlightRequestMessage[] = []
    const backend: HighlightBackend = {
      state: 'initialized',
      totalSlots: 1,
      busySlots: 0,
      get freeSlots() { return ready ? 1 : 0 },
      workersFailed: false,
      ready: () => readyPromise,
      run: (request) => {
        requests.push(request)
        return Promise.resolve({ type: 'result', id: request.id, requestType: 'file', result: RESULT })
      },
      terminate: () => {}
    }
    const pool = new HighlighterPool({ backend })
    const onResult = vi.fn()

    pool.requestFile({}, fileRequest('k'), onResult)
    expect(requests).toHaveLength(0)

    resolveReady()
    await readyPromise
    await Promise.resolve()
    await Promise.resolve()

    expect(requests).toHaveLength(1)
    expect(onResult).toHaveBeenCalledWith(RESULT)
  })

  it('stays usable after being terminated and mounted again', async () => {
    // React runs an effect, its cleanup, and the effect again on every mount in
    // development, so the provider terminates the pool once before it is really in
    // use. A pool that can only be torn down once is then dead for the whole session.
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend })

    pool.terminate()

    const onResult = vi.fn()
    pool.requestFile({}, fileRequest('k'), onResult)
    await backend.settle()

    expect(onResult).toHaveBeenCalledWith(RESULT)
  })

  it('forgets cached results when terminated', async () => {
    const backend = controllableBackend()
    const pool = new HighlighterPool({ backend })
    pool.requestFile({}, fileRequest('k'), vi.fn())
    await backend.settle()
    expect(pool.peekFile('k')).toBe(RESULT)

    pool.terminate()

    expect(pool.peekFile('k')).toBeUndefined()
  })

  it('reports pool activity to stat subscribers', () => {
    const backend = controllableBackend(2)
    const pool = new HighlighterPool({ backend })
    const stats = vi.fn()

    pool.subscribeToStats(stats)
    expect(stats).toHaveBeenCalledWith(expect.objectContaining({
      managerState: 'initialized',
      totalWorkers: 2,
      workersFailed: false
    }))

    pool.requestFile({}, fileRequest('k'), vi.fn())
    expect(pool.getStats()).toMatchObject({ activeTasks: 1, busyWorkers: 1 })
  })

  it('leaves the caller on plain text when the backend fails', async () => {
    const failing: HighlightBackend = {
      state: 'initialized',
      totalSlots: 1,
      busySlots: 0,
      freeSlots: 1,
      workersFailed: true,
      ready: () => Promise.resolve(),
      run: () => Promise.reject(new Error('boom')),
      terminate: () => {}
    }
    const pool = new HighlighterPool({ backend: failing })
    const onResult = vi.fn()

    pool.requestFile({}, fileRequest('k'), onResult)
    await Promise.resolve()
    await Promise.resolve()

    expect(onResult).not.toHaveBeenCalled()
    expect(pool.peekFile('k')).toBeUndefined()
  })
})
