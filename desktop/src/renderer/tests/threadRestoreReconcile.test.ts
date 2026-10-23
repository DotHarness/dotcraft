import { describe, expect, it } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import type { ThreadRuntimeSnapshot } from '../types/thread'
import { conversationNeedsFullSnapshotReconcile } from '../utils/threadRestoreReconcile'

const idleRuntime: ThreadRuntimeSnapshot = {
  running: false,
  busy: false,
  waitingOnApproval: false,
  waitingOnInput: false,
  waitingOnPlanConfirmation: false,
  maintenanceKind: null
}

const runningRuntime: ThreadRuntimeSnapshot = {
  ...idleRuntime,
  running: true
}

function conversation(overrides: Partial<Parameters<typeof conversationNeedsFullSnapshotReconcile>[0]['conversation']> = {}) {
  return {
    turnStatus: 'idle' as const,
    activeTurnId: null,
    turns: [] as ConversationTurn[],
    pendingApproval: null,
    pendingApprovals: [],
    pendingUserInput: null,
    streamingMessage: '',
    streamingReasoning: '',
    activeItemId: null,
    maintenanceKind: null,
    ...overrides
  }
}

function runningReadToolTurn(): ConversationTurn {
  return {
    id: 'turn-1',
    threadId: 'thread-1',
    status: 'running',
    startedAt: '2026-04-25T10:00:00.000Z',
    items: [
      {
        id: 'tool-read',
        type: 'toolCall',
        status: 'completed',
        toolName: 'ReadFile',
        toolCallId: 'call-read',
        arguments: { path: 'docs/readme.md' },
        createdAt: '2026-04-25T10:00:01.000Z'
      }
    ]
  }
}

function settledReadToolTurn(): ConversationTurn {
  return {
    ...runningReadToolTurn(),
    status: 'completed',
    completedAt: '2026-04-25T10:00:02.000Z',
    items: [
      {
        id: 'tool-read',
        type: 'toolCall',
        status: 'completed',
        toolName: 'ReadFile',
        toolCallId: 'call-read',
        arguments: { path: 'docs/readme.md' },
        result: 'contents',
        success: true,
        createdAt: '2026-04-25T10:00:01.000Z',
        completedAt: '2026-04-25T10:00:02.000Z'
      }
    ]
  }
}

describe('conversationNeedsFullSnapshotReconcile', () => {
  const running = { turnStatus: 'running' as const, activeTurnId: 'turn-1', turns: [runningReadToolTurn()] }

  it('requests a full snapshot when the idle server disagrees with lingering local state', () => {
    const lingering: Array<Parameters<typeof conversation>[0]> = [
      running,
      { turns: [runningReadToolTurn()] },
      {
        turnStatus: 'waitingInput' as const,
        pendingUserInput: { bridgeId: 'bridge-input', requestId: 'input-1', threadId: 'thread-1', turnId: 'turn-1', isBlocking: true, questions: [] }
      }
    ]
    for (const local of lingering) {
      expect(conversationNeedsFullSnapshotReconcile({ runtime: idleRuntime, conversation: conversation(local) })).toBe(true)
    }
  })

  it('does not request a full snapshot while the server is active or both sides are settled', () => {
    expect(conversationNeedsFullSnapshotReconcile({ runtime: runningRuntime, conversation: conversation(running) })).toBe(false)
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: { ...idleRuntime, waitingOnPlanConfirmation: true },
      conversation: conversation(running)
    })).toBe(false)
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: idleRuntime,
      conversation: conversation({ turns: [settledReadToolTurn()] })
    })).toBe(false)
  })
})
