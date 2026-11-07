import { describe, expect, it, vi } from 'vitest'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import { readTurnItems, readTurnPage } from './projectHistory'

describe('bounded history', () => {
  it('preserves the requested summary view for active and terminal turns', async () => {
    const turn = (id: string, status: string) => ({ id, status, itemsView: 'summary', itemCount: 200, items: [{ id: id + '-user', turnId: id }] })
    const request = vi.fn(async () => ({
      data: [turn('active', 'running'), turn('failed', 'failed'), turn('done', 'completed')], nextCursor: 'older',
    }))
    const page = await readTurnPage({ request } as unknown as DotCraftWireClient, 'thread', null)
    expect(page.turns.map((entry) => [entry.turn.id, entry.turn.itemsView, entry.items.length])).toEqual([
      ['done', 'summary', 1], ['failed', 'summary', 1], ['active', 'summary', 1],
    ])
    expect(page.nextCursor).toBe('older')
    expect(request).toHaveBeenCalledExactlyOnceWith('thread/turns/list', expect.objectContaining({ limit: 5, itemsView: 'summary' }))
  })

  it('returns one Item page with its cursor and only continues on an explicit read', async () => {
    const request = vi.fn(async (_method: string, params: { cursor?: string }) => ({
      data: [{ item: { id: params.cursor ? 'second' : 'first' } }], nextCursor: params.cursor ? null : 'next',
    }))
    const client = { request } as unknown as DotCraftWireClient
    const first = await readTurnItems(client, 'thread', 'turn')
    expect(first.items.map((item) => item.id)).toEqual(['first'])
    expect(first.nextCursor).toBe('next')
    expect(request).toHaveBeenCalledTimes(1)
    const second = await readTurnItems(client, 'thread', 'turn', first.nextCursor)
    expect(second.items.map((item) => item.id)).toEqual(['second'])
    expect(second.nextCursor).toBeNull()
    expect(request.mock.calls[1][1]).toMatchObject({ limit: 100, cursor: 'next' })
  })
})
