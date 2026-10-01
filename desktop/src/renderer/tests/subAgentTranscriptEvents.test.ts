import { describe, expect, it } from 'vitest'
import {
  appendLiveText,
  applyTranscriptEvent,
  mergeHistoryTurns,
  type TranscriptTimeline
} from '../stores/subAgentTranscriptEvents'
import type { ConversationTurn } from '../types/conversation'

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

  it('lets history replace streamed items while keeping the message still streaming', () => {
    let timeline = apply(empty, 'turn/started', { turn: { id: 'turn-1', threadId: THREAD, status: 'running' } })
    timeline = startedMessage(timeline)
    timeline = appendLiveText(timeline, 'msg-1', 'Halfway')
    const history: ConversationTurn[] = [{
      id: 'turn-1',
      threadId: THREAD,
      status: 'running',
      startedAt: '2026-10-01T00:00:00.000Z',
      items: [{ id: 'read-1', type: 'toolCall', status: 'completed', createdAt: '2026-10-01T00:00:00.500Z' }]
    }]

    const merged = mergeHistoryTurns(timeline, history)

    expect(merged.turns[0].items.map((item) => [item.id, item.status])).toEqual([
      ['read-1', 'completed'],
      ['msg-1', 'streaming']
    ])
    expect(merged.live?.text).toBe('Halfway')
  })

  it('drops the live text once history holds the completed message', () => {
    const timeline = appendLiveText(startedMessage(empty), 'msg-1', 'Done')
    const history: ConversationTurn[] = [{
      id: 'turn-1',
      threadId: THREAD,
      status: 'completed',
      startedAt: '2026-10-01T00:00:00.000Z',
      items: [{ id: 'msg-1', type: 'agentMessage', status: 'completed', text: 'Done', createdAt: '2026-10-01T00:00:01.000Z' }]
    }]

    const merged = mergeHistoryTurns(timeline, history)

    expect(merged.live).toBeNull()
    expect(merged.turns[0].items).toHaveLength(1)
  })
})
