import { describe, expect, it, vi } from 'vitest'
import { readThreadHistoryHead, readThreadTurnsPage } from '../utils/threadHistory'

interface ItemsListParams {
  turnId?: string
  cursor?: string | null
}

/** Serves Turn pages newest-first and Item pages scoped to one Turn, oldest-first. */
function makeRequest(
  turnPages: Record<string, { data: Array<{ id: string }>; nextCursor: string | null }>,
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
    const request = makeRequest(
      { head: { data: [{ id: 'turn-1' }], nextCursor: null } },
      { 'turn-1': items },
      3
    )

    const page = await readThreadTurnsPage(request, 'thread-1')

    expect(page.turns[0].items).toEqual(items)
    expect(page.nextCursor).toBeNull()
    const itemCalls = request.mock.calls.filter(([method]) => method === 'thread/items/list')
    expect(itemCalls).toHaveLength(3)
    expect(itemCalls[0][1]).toMatchObject({ turnId: 'turn-1', cursor: null, sortDirection: 'ascending' })
    expect(itemCalls[1][1]).toMatchObject({ turnId: 'turn-1', cursor: '3' })
  })

  it('follows the turn cursor for older pages', async () => {
    const request = makeRequest(
      {
        head: { data: [{ id: 'turn-2' }], nextCursor: 'older' },
        older: { data: [{ id: 'turn-1' }], nextCursor: null }
      },
      { 'turn-1': [{ id: 'item-1' }], 'turn-2': [{ id: 'item-2' }] }
    )

    const page = await readThreadTurnsPage(request, 'thread-1', 'older')

    expect(page.turns.map((turn) => turn.id)).toEqual(['turn-1'])
    expect(page.nextCursor).toBeNull()
  })

  it('rejects an item cursor that fails to advance', async () => {
    const request = vi.fn(async (method: string) => {
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }], nextCursor: null }
      return { data: [{ turnId: 'turn-1', item: { id: 'item-1' } }], nextCursor: 'stuck' }
    })

    await expect(readThreadTurnsPage(request, 'thread-1')).rejects.toThrow(/unchanged cursor/)
  })
})
