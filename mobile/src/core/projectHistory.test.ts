import { describe, expect, it, vi } from 'vitest'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import { readTurnPage } from './projectHistory'

describe('turn pages', () => {
  it('keeps completed summary turns and reads every other turn in full', async () => {
    const turn = (id: string, status: string) => ({ id, status, itemsView: 'summary', itemCount: 2, items: [{ id: `${id}-user`, turnId: id }] })
    const request = vi.fn(async (method: string, params: { turnId?: string }) =>
      method === 'thread/turns/list'
        ? { data: [turn('cancelled', 'cancelled'), turn('failed', 'failed'), turn('done', 'completed')], nextCursor: null }
        : { data: [{ turnId: params.turnId, item: { id: `${params.turnId}-user` } }, { turnId: params.turnId, item: { id: `${params.turnId}-tool` } }], nextCursor: null },
    )

    const page = await readTurnPage({ request } as unknown as DotCraftWireClient, 'thread-1', null)

    expect(page.turns.map((entry) => [entry.turn.id, entry.turn.itemsView ?? null, entry.items.length])).toEqual([
      ['done', 'summary', 1],
      ['failed', null, 2],
      ['cancelled', null, 2],
    ])
  })
})
