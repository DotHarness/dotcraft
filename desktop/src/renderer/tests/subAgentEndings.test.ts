import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { collectSubAgentEndings, hasEndingAfter } from '../utils/subAgentEndings'

function notice(path: string, createdAt: string): ConversationItem {
  return {
    id: `${path}-${createdAt}`, type: 'userMessage', status: 'completed', createdAt, deliveryMode: 'guidance',
    triggerKind: 'subagent', triggerLabel: path, triggerRefId: path, triggerStatus: 'completed'
  }
}

function turn(threadId: string, items: ConversationItem[]): ConversationTurn {
  return { id: `${threadId}-turn`, threadId, status: 'completed', items }
}

describe('subagent endings', () => {
  it('counts only this thread’s endings that came after the spawn', () => {
    const endings = collectSubAgentEndings([
      turn('parent', [notice('/root/review', '2026-09-02T00:05:00Z')]),
      turn('other', [notice('/root/lint', '2026-09-02T00:05:00Z')])
    ], 'parent')

    expect(hasEndingAfter(endings, '/root/review', '2026-09-02T00:00:00Z')).toBe(true)
    expect(hasEndingAfter(endings, '/root/review', '2026-09-02T00:06:00Z')).toBe(false)
    expect(hasEndingAfter(endings, '/root/lint', '2026-09-02T00:00:00Z')).toBe(false)
  })
})
