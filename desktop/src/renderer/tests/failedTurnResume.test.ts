import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { canResumeLastTurn, findSupersededRetryTurnIds } from '../utils/failedTurnResume'

function item(id: string, type: ConversationItem['type']): ConversationItem {
  return { id, type, status: 'completed' }
}

function turn(id: string, overrides: Partial<ConversationTurn> = {}): ConversationTurn {
  return {
    id,
    threadId: 'thread-1',
    status: 'completed',
    items: [],
    startedAt: '2026-10-14T00:00:00.000Z',
    ...overrides
  }
}

describe('canResumeLastTurn', () => {
  it('allows resuming a latest turn that was interrupted or failed on a transient provider error', () => {
    expect(canResumeLastTurn([turn('t1', { status: 'failed', providerError: 'serverOverloaded' })])).toBe(true)
    expect(canResumeLastTurn([turn('t1', { status: 'failed', providerError: 'responseTooManyFailedAttempts' })])).toBe(true)
    expect(canResumeLastTurn([turn('t1', { status: 'cancelled' })])).toBe(true)
  })

  it('rejects failures that a retry cannot fix and turns that did not fail', () => {
    expect(canResumeLastTurn([turn('t1', { status: 'failed', providerError: 'usageLimitExceeded' })])).toBe(false)
    expect(canResumeLastTurn([turn('t1', { status: 'failed', providerError: 'rateLimitExceeded' })])).toBe(false)
    expect(canResumeLastTurn([turn('t1', { status: 'failed', error: 'tool crashed' })])).toBe(false)
    expect(canResumeLastTurn([
      turn('t1', { status: 'failed', providerError: 'serverOverloaded' }),
      turn('t2')
    ])).toBe(false)
    expect(canResumeLastTurn([])).toBe(false)
  })
})

describe('findSupersededRetryTurnIds', () => {
  it('keeps the original failure and only the latest of identical retry failures', () => {
    const turns = [
      turn('original', { status: 'failed', providerError: 'serverOverloaded', items: [item('u', 'userMessage'), item('e1', 'error')] }),
      turn('retry-1', { status: 'failed', providerError: 'serverOverloaded', items: [item('e2', 'error')] }),
      turn('retry-2', { status: 'failed', providerError: 'serverOverloaded', items: [item('e3', 'error')] }),
      turn('retry-3', { status: 'failed', providerError: 'serverOverloaded', items: [item('e4', 'error')] })
    ]

    expect([...findSupersededRetryTurnIds(turns)]).toEqual(['retry-1', 'retry-2'])
  })

  it('keeps retries that failed differently or produced output', () => {
    const turns = [
      turn('retry-1', { status: 'failed', providerError: 'serverOverloaded', items: [item('e1', 'error')] }),
      turn('retry-2', { status: 'failed', providerError: 'internalServerError', items: [item('e2', 'error')] }),
      turn('retry-3', { status: 'failed', providerError: 'internalServerError', items: [item('a', 'agentMessage'), item('e3', 'error')] })
    ]

    expect(findSupersededRetryTurnIds(turns).size).toBe(0)
  })

  it('keeps a summarized retry whose omitted items may hold output', () => {
    const turns = [
      turn('retry-1', { status: 'failed', providerError: 'serverOverloaded', items: [item('e1', 'error')], summary: { itemCount: 3 } }),
      turn('retry-2', { status: 'failed', providerError: 'serverOverloaded', items: [item('e2', 'error')] })
    ]

    expect(findSupersededRetryTurnIds(turns).size).toBe(0)
  })
})
