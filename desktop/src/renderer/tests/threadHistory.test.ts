import { describe, expect, it, vi } from 'vitest'
import {
  createHistoryReadQueue, listThreadTurns, readAllTurnItems, readThreadHistoryHead,
  readTurnItemsPage, THREAD_OPENING_PAGE
} from '../utils/threadHistory'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(accept => { resolve = accept })
  return { promise, resolve }
}

describe('thread history paging', () => {
  it('opens five summary Turns without hydrating active or non-completed Items', async () => {
    const turns = ['running', 'cancelled', 'failed', 'completed'].map((status, index) => ({
      id: `turn-${index}`, status, itemsView: 'summary', itemCount: 1000, items: [{ id: `summary-${index}` }]
    }))
    const request = vi.fn(async (method: string) => method === 'thread/read'
      ? { thread: { id: 'thread-1', turns: [] } }
      : { data: turns, nextCursor: 'older' })

    const result = await readThreadHistoryHead(request, 'thread-1')

    expect(request).toHaveBeenCalledWith('thread/turns/list', {
      threadId: 'thread-1', cursor: null, sortDirection: 'descending', ...THREAD_OPENING_PAGE
    })
    expect(request.mock.calls.map(([method]) => method).sort()).toEqual(['thread/read', 'thread/turns/list'])
    expect(result.thread.turns).toEqual([...turns].reverse())
    expect(result.turnCursor).toBe('older')
    expect(THREAD_OPENING_PAGE.summaryTools).toContainEqual({ name: 'CreatePlan' })
  })

  it.each(['none', 'summary', 'full'] as const)('honors the explicit %s view without extra Item requests', async itemsView => {
    const request = vi.fn(async () => ({ data: [{ id: 'turn-1', itemsView }], nextCursor: null }))
    const result = await listThreadTurns(request, 'thread-1', 'older', 'ascending', 5, itemsView)

    expect(result.turns).toEqual([{ id: 'turn-1', itemsView }])
    expect(request).toHaveBeenCalledExactlyOnceWith('thread/turns/list', {
      threadId: 'thread-1', cursor: 'older', limit: 5, sortDirection: 'ascending', itemsView,
      ...(itemsView === 'summary' ? { summaryTools: THREAD_OPENING_PAGE.summaryTools } : {})
    })
  })

  it('returns one 100-Item page and its unread cursor', async () => {
    const items = Array.from({ length: 100 }, (_, index) => ({ id: `item-${index}` }))
    const request = vi.fn(async () => ({
      data: items.map(item => ({ turnId: 'turn-1', item })), nextCursor: 'next-100'
    }))

    await expect(readTurnItemsPage(request, 'thread-1', 'turn-1')).resolves.toEqual({ items, nextCursor: 'next-100' })
    expect(request).toHaveBeenCalledExactlyOnceWith('thread/items/list', {
      threadId: 'thread-1', turnId: 'turn-1', cursor: null, limit: 100, sortDirection: 'ascending'
    }, undefined)
  })

  it('applies an Item page inside the response boundary rather than the promise continuation', async () => {
    const pending = deferred<unknown>()
    const order: string[] = []
    const result = { data: [{ turnId: 'turn-1', item: { id: 'item-1' } }], nextCursor: 'more' }
    const request = vi.fn((_method, _params, options) => {
      options.onResponse(result)
      order.push('later delta')
      return pending.promise
    })
    const read = readTurnItemsPage(request, 'thread-1', 'turn-1', null, page => {
      expect(page.nextCursor).toBe('more')
      order.push('page')
    })
    expect(order).toEqual(['page', 'later delta'])
    pending.resolve(result)
    await read
    expect(order).toEqual(['page', 'later delta'])
  })

  it('exhausts Items only on an explicit complete read, deduplicating shared identities', async () => {
    const request = vi.fn(async (_method, params) => params.cursor == null
      ? { data: [{ item: { id: 'a', value: 'first' } }], nextCursor: 'next' }
      : { data: [{ item: { id: 'a', value: 'latest' } }, { item: { id: 'b' } }], nextCursor: null })

    await expect(readAllTurnItems(request, 'thread-1', 'turn-1')).resolves.toEqual([
      { id: 'a', value: 'latest' }, { id: 'b' }
    ])
    expect(request).toHaveBeenCalledTimes(2)
  })

  it('rejects a page whose cursor fails to advance', async () => {
    const request = vi.fn(async () => ({ data: [], nextCursor: 'stuck' }))
    await expect(readTurnItemsPage(request, 'thread-1', 'turn-1', 'stuck')).rejects.toThrow(/unchanged cursor/)
  })

  it('limits preview work to two jobs and skips queued obsolete targets', async () => {
    const read = createHistoryReadQueue(2)
    const first = deferred<void>()
    const second = deferred<void>()
    const staleRead = vi.fn(async () => undefined)
    const a = read(() => first.promise)
    const b = read(() => second.promise)
    let current = true
    const stale = read(staleRead, () => current).catch(error => error.message)
    current = false
    first.resolve()
    second.resolve()
    await Promise.all([a, b])
    expect(await stale).toMatch(/no longer needed/)
    expect(staleRead).not.toHaveBeenCalled()
  })
})
