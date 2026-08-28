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
    shellRuntimeByCallId: new Map(),
    loading: false,
    loadError: null,
    _seq: 0
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('reviewPanelStore streaming message timing', () => {
  it('maps and upserts repeated error notifications', () => {
    s().onTurnStarted(makeTurn())
    const errorItem = {
      id: 'review-error-1',
      type: 'error',
      payload: {
        message: 'Namespace resolution failed.',
        code: 'agent_error',
        fatal: true
      },
      createdAt: '2026-07-15T00:00:00.000Z',
      completedAt: '2026-07-15T00:00:01.000Z'
    }

    s().onItemCompleted({ turnId: 'turn-review-1', item: errorItem })
    s().onItemCompleted({ turnId: 'turn-review-1', item: errorItem })

    expect(s().turns[0].items).toHaveLength(1)
    expect(s().turns[0].items[0].text).toBe('Namespace resolution failed.')
  })

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

  it('keeps review imageGeneration item through live completion', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'review-image-1',
        type: 'imageGeneration',
        payload: {
          callId: 'ig_review',
          status: 'inProgress',
          mediaType: 'image/png'
        },
        createdAt: '2026-07-08T04:31:00.000Z'
      }
    })
    s().onItemCompleted({
      turnId: 'turn-review-1',
      item: {
        id: 'review-image-1',
        type: 'imageGeneration',
        payload: {
          callId: 'ig_review',
          status: 'completed',
          result: 'AQID',
          mediaType: 'image/png',
          savedPath: '<workspace>/.craft/generated_images/thread/ig_review.png'
        },
        createdAt: '2026-07-08T04:31:00.000Z',
        completedAt: '2026-07-08T04:31:02.000Z'
      }
    })

    const imageItem = s().turns[0].items.find((item) => item.id === 'review-image-1')
    expect(imageItem?.type).toBe('imageGeneration')
    expect(imageItem?.status).toBe('completed')
    expect(imageItem?.imageGenerationStatus).toBe('completed')
    expect(imageItem?.result).toBe('AQID')
    expect(imageItem?.savedPath).toBe('<workspace>/.craft/generated_images/thread/ig_review.png')
  })

  it('applies terminal output that arrives before the matching review Exec toolCall item', () => {
    vi.useFakeTimers()
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
    expect(toolItem?.aggregatedOutput).toBeUndefined()
    expect(toolItem?.executionStatus).toBe('inProgress')
    vi.advanceTimersByTime(50)
    expect(s().shellRuntimeByCallId.get('exec-review-terminal')).toEqual({
      source: 'terminal',
      output: 'Pinging 10.8.8.8\n'
    })
  })

  it('batches review output, prefers terminal snapshots, and commits once on completion', () => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'tool-review-burst',
        type: 'toolCall',
        payload: { callId: 'exec-review-burst', toolName: 'Exec', arguments: { command: 'many-lines' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'command-review-burst',
        type: 'commandExecution',
        payload: { callId: 'exec-review-burst', command: 'many-lines', status: 'inProgress', aggregatedOutput: '' }
      }
    })
    const durableTurn = s().turns[0]

    for (let index = 0; index < 100; index++) {
      s().onCommandExecutionDelta({
        turnId: 'turn-review-1',
        itemId: 'command-review-burst',
        delta: `${index}\n`
      })
    }
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        turnId: 'turn-review-1',
        callId: 'exec-review-burst',
        status: 'running',
        output: 'terminal-authoritative\n'
      },
      delta: 'terminal-authoritative\n'
    })

    expect(s().turns[0]).toBe(durableTurn)
    expect(s().shellRuntimeByCallId.size).toBe(0)
    vi.advanceTimersByTime(50)
    expect(s().turns[0]).toBe(durableTurn)
    expect(s().shellRuntimeByCallId.get('exec-review-burst')).toEqual({
      source: 'terminal',
      output: 'terminal-authoritative\n'
    })

    s().onTerminalEvent({
      event: 'terminal/completed',
      terminal: {
        turnId: 'turn-review-1',
        callId: 'exec-review-burst',
        status: 'completed',
        output: 'terminal-authoritative\nfinal\n',
        exitCode: 0,
        wallTimeMs: 321
      }
    })

    const completedTool = s().turns[0].items.find((item) => item.id === 'tool-review-burst')
    expect(completedTool?.aggregatedOutput).toBe('terminal-authoritative\nfinal\n')
    expect(completedTool?.executionStatus).toBe('completed')
    expect(completedTool?.exitCode).toBe(0)
    expect(completedTool?.duration).toBe(321)
    expect(s().shellRuntimeByCallId.size).toBe(0)

    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: { turnId: 'turn-review-1', callId: 'exec-review-burst', status: 'running' },
      delta: 'late\n'
    })
    vi.advanceTimersByTime(100)
    expect(s().shellRuntimeByCallId.size).toBe(0)
  })

  it('uses commandExecution as a review fallback and clears pending batches when closed', () => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'tool-review-fallback',
        type: 'toolCall',
        payload: { callId: 'exec-review-fallback', toolName: 'Exec', arguments: { command: 'fallback' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-review-1',
      item: {
        id: 'command-review-fallback',
        type: 'commandExecution',
        payload: { callId: 'exec-review-fallback', command: 'fallback', status: 'inProgress', aggregatedOutput: '' }
      }
    })

    s().onCommandExecutionDelta({
      turnId: 'turn-review-1',
      itemId: 'command-review-fallback',
      delta: 'fallback output\n'
    })
    vi.advanceTimersByTime(50)
    expect(s().shellRuntimeByCallId.get('exec-review-fallback')).toEqual({
      source: 'commandExecution',
      output: 'fallback output\n'
    })

    s().onCommandExecutionDelta({
      turnId: 'turn-review-1',
      itemId: 'command-review-fallback',
      delta: 'pending\n'
    })
    s().destroyReviewPanel()
    vi.advanceTimersByTime(100)
    expect(s().shellRuntimeByCallId.size).toBe(0)
  })
})
