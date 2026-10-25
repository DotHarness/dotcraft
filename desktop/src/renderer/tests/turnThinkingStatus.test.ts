import { describe, expect, it } from 'vitest'
import { turnThinkingLabel } from '../components/conversation/turnThinkingStatus'
import type { ConversationItem } from '../types/conversation'

function item(overrides: Partial<ConversationItem> & Pick<ConversationItem, 'id' | 'type' | 'status' | 'createdAt'>): ConversationItem {
  return overrides
}

describe('turnThinkingLabel', () => {
  it('keeps the Thinking label while visible work is on screen', () => {
    const label = turnThinkingLabel({
      items: [
        item({ id: 'tool', type: 'toolCall', status: 'started', createdAt: '2026-01-01T00:00:00Z' }),
        item({ id: 'message', type: 'agentMessage', status: 'completed', text: 'Reading the config', createdAt: '2026-01-01T00:00:01Z' }),
      ],
      working: true,
      activeItemId: null,
      streamingReasoning: '',
      waitingForAnswer: false,
    })
    expect(label).toEqual({ kind: 'thinking', title: undefined })
  })

  it('reuses the latest completed reasoning title after visible work starts', () => {
    const items = [
      item({ id: 'reasoning', type: 'reasoningContent', status: 'completed', reasoning: '**Checking the config**\n\nMore detail', createdAt: '2026-01-01T00:00:00Z' }),
      item({ id: 'tool', type: 'toolCall', status: 'started', createdAt: '2026-01-01T00:00:01Z' }),
    ]
    expect(turnThinkingLabel({ items, working: true, activeItemId: null, streamingReasoning: '', waitingForAnswer: false }))
      .toEqual({ kind: 'thinking', title: 'Checking the config' })
  })

  it('keeps the previous title while a new reasoning block is still empty', () => {
    const items = [
      item({ id: 'older', type: 'reasoningContent', status: 'completed', reasoning: '**Checking the config**', createdAt: '2026-01-01T00:00:00Z' }),
      item({ id: 'live', type: 'reasoningContent', status: 'streaming', reasoning: '', createdAt: '2026-01-01T00:00:01Z' }),
    ]
    expect(turnThinkingLabel({ items, working: true, activeItemId: 'live', streamingReasoning: '', waitingForAnswer: false }))
      .toEqual({ kind: 'thinking', title: 'Checking the config' })
  })

  it('prioritizes waiting for the user over the thinking title', () => {
    const items = [
      item({ id: 'reasoning', type: 'reasoningContent', status: 'completed', reasoning: '**Checking the config**', createdAt: '2026-01-01T00:00:00Z' }),
    ]
    expect(turnThinkingLabel({ items, working: true, activeItemId: null, streamingReasoning: '', waitingForAnswer: true }))
      .toEqual({ kind: 'waitingForAnswer' })
  })
})
