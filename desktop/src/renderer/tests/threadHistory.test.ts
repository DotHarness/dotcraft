import { describe, expect, it, vi } from 'vitest'
import { hydrateTurns, listThreadTurns, readThreadHistoryHead } from '../utils/threadHistory'

interface ItemsListParams {
  turnId?: string
  cursor?: string | null
}

/** Serves Turn pages newest-first and Item pages scoped to one Turn, oldest-first. */
function makeRequest(
  turnPages: Record<string, { data: Array<{ id: string }>; nextCursor: string | null; backwardsCursor?: string | null }>,
  itemsByTurn: Record<string, Array<{ id: string }>>,
  itemPageSize = 500
) {
  return vi.fn(async (method: string, params: Record<string, unknown>) => {
    if (method === 'thread/read') {
      return { thread: { id: 'thread-1', displayName: null, turns: [] } }
    }
    if (method === 'thread/turns/list') {
      const cursor = (params.cursor as string | null) ?? 'head'
      return turnPages[cursor]
    }
    if (method === 'thread/items/list') {
      const { turnId, cursor } = params as ItemsListParams
      const all = itemsByTurn[turnId ?? ''] ?? []
      const offset = cursor == null ? 0 : Number(cursor)
      const data = all.slice(offset, offset + itemPageSize).map((item) => ({ turnId, item }))
      const nextOffset = offset + itemPageSize
      return { data, nextCursor: nextOffset < all.length ? String(nextOffset) : null }
    }
    throw new Error(`unexpected method ${method}`)
  })
}

describe('thread history paging', () => {
  it('keeps completed summary turns and reads every other turn in full', async () => {
    const request = makeRequest(
      {
        head: {
          data: [
            { id: 'turn-4', status: 'running', itemsView: 'summary', itemCount: 3, items: [{ id: 'user-2' }] },
            { id: 'turn-3', status: 'cancelled', itemsView: 'summary', itemCount: 2, items: [{ id: 'user-3' }] },
            { id: 'turn-2', status: 'failed', itemsView: 'summary', itemCount: 2, items: [{ id: 'user-4' }] },
            { id: 'turn-1', status: 'completed', itemsView: 'summary', itemCount: 4, items: [{ id: 'user-1' }, { id: 'final-1' }] }
          ] as Array<{ id: string }>,
          nextCursor: null
        }
      },
      {
        'turn-4': [{ id: 'user-2' }, { id: 'tool-2' }, { id: 'reply-2' }],
        'turn-3': [{ id: 'user-3' }, { id: 'tool-3' }],
        'turn-2': [{ id: 'user-4' }, { id: 'tool-4' }]
      }
    )

    const result = await readThreadHistoryHead(request, 'thread-1')

    expect(request.mock.calls.find(([method]) => method === 'thread/turns/list')?.[1]).toMatchObject({ itemsView: 'summary' })
    expect(result.thread.turns[0]).toMatchObject({ id: 'turn-1', itemsView: 'summary', items: [{ id: 'user-1' }, { id: 'final-1' }] })
    expect(result.thread.turns[1]).toMatchObject({ id: 'turn-2', items: [{ id: 'user-4' }, { id: 'tool-4' }] })
    expect(result.thread.turns[2]).toMatchObject({ id: 'turn-3', items: [{ id: 'user-3' }, { id: 'tool-3' }] })
    expect(result.thread.turns[3]).toMatchObject({ id: 'turn-4', items: [{ id: 'user-2' }, { id: 'tool-2' }, { id: 'reply-2' }] })
    expect(result.thread.turns.slice(1).map((turn) => turn.itemsView)).toEqual([undefined, undefined, undefined])
    expect(request.mock.calls.filter(([method]) => method === 'thread/items/list').map(([, params]) => params.turnId).sort()).toEqual(['turn-2', 'turn-3', 'turn-4'])
  })

  it('returns the newest turns oldest-first with every item hydrated', async () => {
    const request = makeRequest(
      { head: { data: [{ id: 'turn-3' }, { id: 'turn-2' }, { id: 'turn-1' }], nextCursor: 'older' } },
      {
        'turn-1': [{ id: 'item-1a' }, { id: 'item-1b' }],
        'turn-2': [{ id: 'item-2a' }],
        'turn-3': [{ id: 'item-3a' }]
      }
    )

    const result = await readThreadHistoryHead(request, 'thread-1')

    expect(result.thread.turns.map((turn) => turn.id)).toEqual(['turn-1', 'turn-2', 'turn-3'])
    expect(result.thread.turns[0].items).toEqual([{ id: 'item-1a' }, { id: 'item-1b' }])
    expect(result.turnCursor).toBe('older')
  })

  it('pages a single turn until its item cursor is exhausted', async () => {
    const items = Array.from({ length: 7 }, (_unused, index) => ({ id: `item-${index}` }))
    const request = makeRequest({}, { 'turn-1': items }, 3)

    const [turn] = await hydrateTurns(request, 'thread-1', [{ id: 'turn-1', status: 'completed', createdAt: '' }])

    expect(turn.items).toEqual(items)
    const itemCalls = request.mock.calls.filter(([method]) => method === 'thread/items/list')
    expect(itemCalls).toHaveLength(3)
    expect(itemCalls[0][1]).toMatchObject({ turnId: 'turn-1', cursor: null, sortDirection: 'ascending' })
    expect(itemCalls[1][1]).toMatchObject({ turnId: 'turn-1', cursor: '3' })
  })

  it('lists turn metadata in the requested direction without reading items', async () => {
    const request = makeRequest(
      { older: { data: [{ id: 'turn-2' }, { id: 'turn-3' }], nextCursor: 'newer', backwardsCursor: 'at-turn-2' } },
      {}
    )

    const page = await listThreadTurns(request, 'thread-1', 'older', 'ascending')

    expect(page).toMatchObject({ nextCursor: 'newer', backwardsCursor: 'at-turn-2' })
    expect(page.turns.map((turn) => turn.id)).toEqual(['turn-2', 'turn-3'])
    expect(request).toHaveBeenCalledExactlyOnceWith('thread/turns/list', {
      threadId: 'thread-1',
      cursor: 'older',
      limit: 5,
      sortDirection: 'ascending'
    })
  })

  it('rejects an item cursor that fails to advance', async () => {
    const request = vi.fn(async (method: string) => {
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }], nextCursor: null }
      return { data: [{ turnId: 'turn-1', item: { id: 'item-1' } }], nextCursor: 'stuck' }
    })

    await expect(hydrateTurns(request, 'thread-1', [{ id: 'turn-1', status: 'completed', createdAt: '' }]))
      .rejects.toThrow(/unchanged cursor/)
  })
})
