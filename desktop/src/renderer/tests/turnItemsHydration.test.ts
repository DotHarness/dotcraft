import { describe, expect, it } from 'vitest'
import { mergeRestoredTurns } from '../stores/turnItemsHydration'
import type { ConversationItem, ConversationTurn } from '../types/conversation'

const item = (id: string, minute: number, extra: Partial<ConversationItem> = {}): ConversationItem => ({
  id, type: 'agentMessage', status: 'completed', text: id,
  createdAt: new Date(Date.UTC(2026, 0, 1, 0, minute)).toISOString(), ...extra
})
const turn = (items: ConversationItem[], extra: Partial<ConversationTurn> = {}): ConversationTurn => ({
  id: 'turn', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T00:00:00Z', items, ...extra
})

describe('restored Turn hydration', () => {
  it('keeps hydrated payloads omitted by a summary while applying incoming content', () => {
    const cached = turn([item('tool', 1, { type: 'toolCall', result: 'loaded output' }), item('reply', 2)])
    const incoming = turn([item('reply', 2, { text: 'authoritative reply' })], { summary: { itemCount: 2 } })
    const [restored] = mergeRestoredTurns([cached], [incoming])
    expect(restored.items).toEqual([cached.items[0], incoming.items[0]])
    expect(restored.summary).toBeUndefined()
  })

  it('retains partial payloads and terminal evidence but starts from the new snapshot cursor', () => {
    const cached = turn([item('tool', 1, {
      type: 'toolCall', executionStatus: 'completed', terminalRunning: false, aggregatedOutput: 'finished'
    })], { summary: { itemCount: 5, nextCursor: 'old-connection-cursor' } })
    const incoming = turn([item('tool', 1, {
      type: 'toolCall', status: 'started', executionStatus: 'inProgress', terminalRunning: true
    }), item('reply', 3)], { status: 'running', summary: { itemCount: 5 } })
    const [restored] = mergeRestoredTurns([cached], [incoming])
    expect(restored.status).toBe('completed')
    expect(restored.items[0]).toMatchObject({ status: 'completed', aggregatedOutput: 'finished', terminalRunning: false })
    expect(restored.summary).toEqual({ itemCount: 5 })
  })

  it('does not restore removed Turns or retain Items omitted by a full authoritative snapshot', () => {
    const cached = turn([item('removed-item', 1), item('reply', 2)])
    const removed = turn([item('old', 0)], { id: 'removed-turn' })
    const incoming = turn([item('reply', 2, { text: 'current' })])
    expect(mergeRestoredTurns([removed, cached], [incoming])).toEqual([incoming])
  })
})
