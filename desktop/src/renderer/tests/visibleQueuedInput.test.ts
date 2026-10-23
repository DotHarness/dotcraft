import { describe, expect, it } from 'vitest'
import type { ConversationItem, QueuedTurnInput } from '../types/conversation'
import { isVisibleQueuedInput, isVisibleUserMessage, mergeVisibleQueueOrder } from '../utils/visibleUserMessage'

function queued(id: string, extra: Partial<QueuedTurnInput> = {}): QueuedTurnInput {
  return { id, threadId: 'thread', displayText: id, status: 'queued', createdAt: '2026-01-01T00:00:00Z', ...extra }
}

describe('queued input visibility', () => {
  const queue = [
    queued('a'),
    queued('notice', { triggerKind: 'terminal', triggerLabel: 'npm test' }),
    queued('b'),
    queued('subagent', { triggerKind: 'subagent', triggerLabel: 'reviewer', triggerRefId: 'agent-1' }),
    queued('c')
  ]

  it('leaves background notices out of the dock and keeps their positions when visible ones are reordered', () => {
    expect(queue.filter(isVisibleQueuedInput).map((item) => item.id)).toEqual(['a', 'b', 'c'])
    expect(mergeVisibleQueueOrder(queue, ['c', 'a', 'b'])).toEqual(['c', 'notice', 'a', 'subagent', 'b'])
  })
})

describe('workflow input visibility', () => {
  const continuation: ConversationItem = { id: 'continue', type: 'userMessage', status: 'completed', text: 'Continue the review', triggerKind: 'workflow', triggerLabel: 'review-change', triggerRefId: 'run_1', createdAt: '2026-01-01T00:00:00Z' }
  const notification: ConversationItem = { ...continuation, id: 'ended', text: 'Workflow review-change finished', triggerStatus: 'succeeded' }

  it('hides a workflow notification while other workflow inputs stay visible', () => {
    expect(isVisibleUserMessage(continuation)).toBe(true)
    expect(isVisibleUserMessage(notification)).toBe(false)
    expect(isVisibleQueuedInput(queued('continue', { triggerKind: 'workflow', triggerRefId: 'run_1' }))).toBe(true)
    expect(isVisibleQueuedInput(queued('ended', { triggerKind: 'workflow', triggerRefId: 'run_1', triggerStatus: 'succeeded' }))).toBe(false)
  })
})
