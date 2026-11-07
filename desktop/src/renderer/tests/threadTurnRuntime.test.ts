import { beforeEach, describe, expect, it } from 'vitest'
import { createThreadConversation } from '../stores/conversationStore'
import { applyThreadTurnRuntime } from '../stores/threadTurnRuntime'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

beforeEach(() => {
  useThreadStore.getState().reset()
  useThreadStore.setState({
    activeThreadId: 'thread',
    threadList: [{
      id: 'thread', status: 'active', originChannel: 'dotcraft-desktop', displayName: 'Thread',
      createdAt: '2026-11-07T00:00:00Z', lastActiveAt: '2026-11-07T00:00:00Z'
    }]
  })
  useUIStore.setState({ planApprovalDismissed: {} })
})

describe('live turn runtime', () => {
  it('settles runtime from the actual terminal feed and derives the unconsumed current plan choice', () => {
    const conversation = createThreadConversation('thread')
    conversation.setState({
      threadMode: 'plan', restoredPlanTurnId: 'turn',
      plan: { title: 'Plan', overview: '', content: 'Do it', todos: [] }
    })
    applyThreadTurnRuntime('thread', { id: 'turn' }, true)
    expect(useThreadStore.getState().runtimeSnapshots.get('thread')?.running).toBe(true)
    applyThreadTurnRuntime('thread', { id: 'turn' }, false)
    expect(useThreadStore.getState().runtimeSnapshots.get('thread')).toMatchObject({
      running: false, waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: true
    })
    useUIStore.setState({ planApprovalDismissed: { turn: true } })
    applyThreadTurnRuntime('thread', { id: 'turn' }, false)
    expect(useThreadStore.getState().runtimeSnapshots.get('thread')?.waitingOnPlanConfirmation).toBe(false)
  })

  it('does not clear a newer running turn with a previous turn terminal update', () => {
    createThreadConversation('thread')
    applyThreadTurnRuntime('thread', { id: 'new' }, true)
    applyThreadTurnRuntime('thread', { id: 'old' }, false)
    expect(useThreadStore.getState().runtimeSnapshots.get('thread')).toMatchObject({ running: true, activeTurnId: 'new' })
  })
})
