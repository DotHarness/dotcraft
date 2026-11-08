import { afterEach, describe, expect, it } from 'vitest'
import { applyThreadRestoration } from '../stores/applyThreadRestoration'
import { createThreadConversation } from '../stores/conversationStore'
import { releaseThreadState } from '../stores/threadState'
import { useThreadStore } from '../stores/threadStore'
import { makeRestoreResult } from './threadRestorationFixtures'
import type { Thread } from '../types/thread'

const THREAD = 'restored-hydration'

afterEach(() => {
  releaseThreadState(THREAD)
  useThreadStore.getState().reset()
})

describe('conversation restoration hydration', () => {
  it('keeps cached tool payloads while restoring only the authoritative streaming base', () => {
    const store = createThreadConversation(THREAD)
    store.getState().setTurns([{
      id: 'turn', threadId: THREAD, status: 'running', startedAt: '2026-01-01T00:00:00Z',
      items: [
        { id: 'tool', type: 'toolCall', status: 'completed', result: 'loaded output', createdAt: '2026-01-01T00:01:00Z' },
        { id: 'message', type: 'agentMessage', status: 'streaming', text: 'obsolete', createdAt: '2026-01-01T00:02:00Z' },
        { id: 'reasoning', type: 'reasoningContent', status: 'streaming', reasoning: 'old reasoning', createdAt: '2026-01-01T00:03:00Z' }
      ]
    }])
    store.setState({ activeItemId: 'reasoning', streamingMessage: 'obsolete', streamingReasoning: 'old reasoning' })
    const snapshot = makeRestoreResult({
      id: THREAD, originChannel: 'dotcraft-desktop', status: 'active',
      runtime: { running: true, waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: false },
      turns: [{
        id: 'turn', threadId: THREAD, status: 'running', startedAt: '2026-01-01T00:00:00Z',
        itemsView: 'summary', itemCount: 3,
        items: [{ id: 'message', type: 'agentMessage', status: 'started', createdAt: '2026-01-01T00:02:00Z', payload: { text: 'snapshot base' } }]
      }]
    } as unknown as Thread)
    applyThreadRestoration(snapshot)
    expect(store.getState()).toMatchObject({
      activeItemId: 'message', streamingMessage: 'snapshot base', streamingReasoning: '', turnStatus: 'running'
    })
    expect(store.getState().turns[0].items.find(item => item.id === 'tool')?.result).toBe('loaded output')
  })
})
