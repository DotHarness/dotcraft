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
  it('requests a full snapshot when server is idle but local state is still running', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: idleRuntime,
      conversation: conversation({
        turnStatus: 'running',
        activeTurnId: 'turn-1',
        turns: [runningReadToolTurn()]
      })
    })).toBe(true)
  })

  it('requests a full snapshot when server is idle but a completed toolCall is awaiting result locally', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: idleRuntime,
      conversation: conversation({
        turns: [runningReadToolTurn()]
      })
    })).toBe(true)
  })

  it('does not request a full snapshot while the server still reports active runtime', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: runningRuntime,
      conversation: conversation({
        turnStatus: 'running',
        activeTurnId: 'turn-1',
        turns: [runningReadToolTurn()]
      })
    })).toBe(false)
  })

  it('does not request a full snapshot while the server waits on plan confirmation', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: {
        ...idleRuntime,
        waitingOnPlanConfirmation: true
      },
      conversation: conversation({
        turnStatus: 'running',
        activeTurnId: 'turn-1',
        turns: [runningReadToolTurn()]
      })
    })).toBe(false)
  })

  it('does not request a full snapshot when both server and local conversation are settled', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: idleRuntime,
      conversation: conversation({
        turns: [settledReadToolTurn()]
      })
    })).toBe(false)
  })

  it('requests a full snapshot when local interactive state remains after server idle', () => {
    expect(conversationNeedsFullSnapshotReconcile({
      runtime: idleRuntime,
      conversation: conversation({
        turnStatus: 'waitingInput',
        pendingUserInput: {
          bridgeId: 'bridge-input',
          requestId: 'input-1',
          turnId: 'turn-1',
          questions: []
        }
      })
    })).toBe(true)
  })
})
