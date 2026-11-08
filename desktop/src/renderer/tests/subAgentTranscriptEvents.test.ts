import { describe, expect, it } from 'vitest'
import {
  appendLiveText,
  applyTranscriptEvent,
  restoreTranscript,
  type TranscriptTimeline
} from '../stores/subAgentTranscriptEvents'
import type { ConversationTurn } from '../types/conversation'
import type { Thread } from '../types/thread'
import { makeRestoreResult } from './threadRestorationFixtures'

const THREAD = 'child-1'
const empty: TranscriptTimeline = { turns: [], live: null }

function apply(timeline: TranscriptTimeline, method: string, params: Record<string, unknown>): TranscriptTimeline {
  return applyTranscriptEvent(timeline, method, { threadId: THREAD, ...params }) ?? timeline
}

function startedMessage(timeline: TranscriptTimeline): TranscriptTimeline {
  return apply(timeline, 'item/started', {
    turnId: 'turn-1',
    item: { id: 'msg-1', type: 'agentMessage', status: 'started', createdAt: '2026-10-01T00:00:01.000Z' }
  })
}

describe('subagent transcript events', () => {
  it('restores the snapshot streaming base rather than cached text or omitted live Items', () => {
    const cached: ConversationTurn[] = [{
      id: 'turn', threadId: THREAD, status: 'running', startedAt: '2026-01-01T00:00:00Z',
      items: [
        { id: 'tool', type: 'toolCall', status: 'completed', createdAt: '2026-01-01T00:01:00Z' },
        { id: 'message', type: 'agentMessage', status: 'streaming', text: 'obsolete', createdAt: '2026-01-01T00:02:00Z' },
        { id: 'reasoning', type: 'reasoningContent', status: 'streaming', reasoning: 'cached reasoning', createdAt: '2026-01-01T00:03:00Z' }
      ]
    }]
    const result = makeRestoreResult({ id: THREAD, turns: [{
      id: 'turn', threadId: THREAD, status: 'running', startedAt: '2026-01-01T00:00:00Z',
      itemsView: 'summary', itemCount: 4,
      items: [{ id: 'message', type: 'agentMessage', status: 'started', createdAt: '2026-01-01T00:02:00Z', payload: { text: 'snapshot base' } }]
    }] } as unknown as Thread)
    const restored = restoreTranscript(result, cached)
    expect(restored.turns[0].items.map(item => item.id)).toEqual(['tool', 'message', 'reasoning'])
    expect(restored.live).toEqual({ itemId: 'message', kind: 'agentMessage', text: 'snapshot base' })
    expect(appendLiveText(restored, 'message', ' suffix').live?.text).toBe('snapshot base suffix')
  })

  it('streams an agent message from its start through deltas to completion', () => {
    let timeline = apply(empty, 'turn/started', { turn: { id: 'turn-1', threadId: THREAD, status: 'running' } })
    timeline = startedMessage(timeline)
    timeline = appendLiveText(timeline, 'msg-1', 'Checking ')
    timeline = appendLiveText(timeline, 'msg-1', 'the tests')

    expect(timeline.live).toEqual({ itemId: 'msg-1', kind: 'agentMessage', text: 'Checking the tests' })
    expect(timeline.turns[0].items[0].status).toBe('streaming')

    timeline = apply(timeline, 'item/completed', {
      turnId: 'turn-1',
      item: { id: 'msg-1', type: 'agentMessage', status: 'completed', createdAt: '2026-10-01T00:00:01.000Z' }
    })
    timeline = apply(timeline, 'turn/completed', { turn: { id: 'turn-1', threadId: THREAD, status: 'completed' } })

    expect(timeline.live).toBeNull()
    expect(timeline.turns[0].status).toBe('completed')
    expect(timeline.turns[0].items[0]).toMatchObject({ status: 'completed', text: 'Checking the tests' })
  })

  it('ignores deltas for a message whose start it did not see', () => {
    const timeline = appendLiveText(empty, 'msg-unknown', 'partial')
    expect(timeline).toBe(empty)
  })

  it('keeps a completed item when a replayed start arrives after it', () => {
    let timeline = apply(empty, 'item/completed', {
      turnId: 'turn-1',
      item: { id: 'tool-1', type: 'toolCall', status: 'completed', toolName: 'ReadFile', createdAt: '2026-10-01T00:00:01.000Z' }
    })
    timeline = apply(timeline, 'item/started', {
      turnId: 'turn-1',
      item: { id: 'tool-1', type: 'toolCall', status: 'started', toolName: 'ReadFile', createdAt: '2026-10-01T00:00:01.000Z' }
    })

    expect(timeline.turns[0].items[0].status).toBe('completed')
  })

})
