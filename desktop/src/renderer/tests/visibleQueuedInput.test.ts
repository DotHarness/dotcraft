import { describe, expect, it } from 'vitest'
import type { QueuedTurnInput } from '../types/conversation'
import { isVisibleQueuedInput, mergeVisibleQueueOrder } from '../utils/visibleUserMessage'

function queued(id: string, extra: Partial<QueuedTurnInput> = {}): QueuedTurnInput {
  return { id, threadId: 'thread', displayText: id, status: 'queued', createdAt: '2026-01-01T00:00:00Z', ...extra }
}

describe('queued input visibility', () => {
  const queue = [
    queued('a'),
    queued('notice', { triggerKind: 'terminal', triggerLabel: 'npm test' }),
    queued('b'),
    queued('c')
  ]

  it('leaves background terminal notices out of the dock', () => {
    expect(queue.filter(isVisibleQueuedInput).map((item) => item.id)).toEqual(['a', 'b', 'c'])
  })

  it('keeps hidden inputs in their positions when the visible ones are reordered', () => {
    expect(mergeVisibleQueueOrder(queue, ['c', 'a', 'b'])).toEqual(['c', 'notice', 'a', 'b'])
  })
})
