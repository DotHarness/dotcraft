import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useReviewPanelStore } from '../stores/reviewPanelStore'

const s = () => useReviewPanelStore.getState()

function makeTurn(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: 'turn-review-1',
    threadId: 'thread-review-1',
    status: 'running',
    items: [],
    startedAt: new Date().toISOString(),
    ...overrides
  }
}

beforeEach(() => {
  useReviewPanelStore.setState({
    openedTaskId: null,
    taskDetail: null,
    reviewThreadId: null,
    subscriptionActive: false,
    turns: [],
    turnStatus: 'idle',
    activeTurnId: null,
    streamingMessage: '',
    streamingMessageLastDeltaAt: null,
    streamingReasoning: '',
    streamingReasoningStartedAt: null,
    activeItemId: null,
    streamingActive: false,
    pendingTerminalByCallId: new Map(),
    loading: false,
    loadError: null,
    subAgentEntries: [],
    _seq: 0
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('reviewPanelStore streaming message timing', () => {
  it('records the latest assistant text delta time and clears it when the item completes', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-05-22T10:00:00.000Z'))

    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: { id: 'message-review-1', type: 'agentMessage' }
    })

    expect(s().streamingMessageLastDeltaAt).toBeNull()

    s().onAgentMessageDelta('Hello')

    expect(s().streamingMessage).toBe('Hello')
    expect(s().streamingMessageLastDeltaAt).toBe(Date.now())

    s().onItemCompleted({
      turnId: 'turn-review-1',
      item: { id: 'message-review-1', type: 'agentMessage' }
    })

    expect(s().streamingMessage).toBe('')
    expect(s().streamingMessageLastDeltaAt).toBeNull()
  })

  it('applies terminal output that arrives before the matching review Exec toolCall item', () => {
    s().onTurnStarted(makeTurn())
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-review-1',
        turnId: 'turn-review-1',
        callId: 'exec-review-terminal',
        command: 'ping -n 4 10.8.8.8',
        workingDirectory: 'C:/repo',
        source: 'host',
        status: 'running',
        output: 'Pinging 10.8.8.8\n'
      },
      delta: 'Pinging 10.8.8.8\n'
    })

    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'tool-review-terminal',
        type: 'toolCall',
        payload: {
          callId: 'exec-review-terminal',
          toolName: 'Exec',
          arguments: { command: 'ping -n 4 10.8.8.8' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-review-terminal')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('Pinging 10.8.8.8\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })
})
