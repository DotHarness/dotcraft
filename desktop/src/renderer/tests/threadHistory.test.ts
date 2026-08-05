import { describe, expect, it, vi } from 'vitest'
import { readThreadHistoryHead } from '../utils/threadHistory'

describe('thread history page assembly', () => {
  it('merges descending pages chronologically and creates placeholders for item-only turns', async () => {
    const request = vi.fn(async (method: string) => {
      if (method === 'thread/read') {
        return { thread: { id: 'thread-1', displayName: null, turns: [] } }
      }
      if (method === 'thread/turns/list') {
        return {
          data: [{ id: 'turn-2', status: 'completed', startedAt: '2026-01-02T00:00:00Z' }],
          nextCursor: 'older-turns'
        }
      }
      return {
        data: [
          { turnId: 'turn-2', item: { id: 'item-2', createdAt: '2026-01-02T00:00:02Z' } },
          { turnId: 'turn-1', item: { id: 'item-1', createdAt: '2026-01-01T00:00:01Z' } }
        ],
        nextCursor: 'older-items'
      }
    })

    const result = await readThreadHistoryHead(request, 'thread-1')

    expect(result.thread.turns.map((turn) => turn.id)).toEqual(['turn-1', 'turn-2'])
    expect(result.thread.turns[0].items?.[0]).toMatchObject({ id: 'item-1' })
    expect(result.thread.turns[1].items?.[0]).toMatchObject({ id: 'item-2' })
    expect(result).toMatchObject({ turnCursor: 'older-turns', itemCursor: 'older-items' })
  })
})
