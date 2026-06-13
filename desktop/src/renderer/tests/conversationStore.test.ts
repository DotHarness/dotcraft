import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { useConversationStore } from '../stores/conversationStore'
import { getStreamingToolDisplay } from '../utils/toolCallDisplay'

// Helper to get latest state without subscribing
const s = () => useConversationStore.getState()

/** Minimal raw turn fixture (wire format) */
function makeTurn(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: 'turn-1',
    threadId: 'thread-1',
    status: 'running',
    items: [],
    startedAt: new Date().toISOString(),
    ...overrides
  }
}

beforeEach(() => {
  s().reset()
  useConversationStore.setState({ remoteWorkspaceActive: false })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('conversationStore — initial state', () => {
  it('starts with empty turns and idle status', () => {
    const state = s()
    expect(state.turns).toHaveLength(0)
    expect(state.turnStatus).toBe('idle')
    expect(state.streamingMessage).toBe('')
    expect(state.streamingMessageLastDeltaAt).toBeNull()
    expect(state.pendingMessage).toBeNull()
    expect(state.maintenanceKind).toBeNull()
  })
})

describe('maintenance state', () => {
  it('tracks consolidation maintenance from system events', () => {
    s().onSystemEvent('consolidating')

    expect(s().maintenanceKind).toBe('consolidating')
    expect(s().systemLabel).toBe('systemStatus.consolidating')

    s().onSystemEvent('consolidationCancelled')

    expect(s().maintenanceKind).toBeNull()
    expect(s().systemLabel).toBeNull()
  })

  it('tracks only thread-level compaction as maintenance', () => {
    s().onSystemEvent('compacting', { turnId: 'turn-1' })
    expect(s().maintenanceKind).toBeNull()

    s().onSystemEvent('compacting', { turnId: null })
    expect(s().maintenanceKind).toBe('compacting')

    s().onSystemEvent('compactCancelled')
    expect(s().maintenanceKind).toBeNull()
  })

  it('hydrates consolidation maintenance label from runtime snapshots', () => {
    s().setMaintenanceKind('consolidating')

    expect(s().maintenanceKind).toBe('consolidating')
    expect(s().systemLabel).toBe('systemStatus.consolidating')
  })

  it('hydrates manual compaction label from runtime snapshots', () => {
    s().setMaintenanceKind('compacting')

    expect(s().maintenanceKind).toBe('compacting')
    expect(s().systemLabel).toBe('systemStatus.compacting.manual')
  })

  it('clears only maintenance-derived labels when runtime maintenance ends', () => {
    s().setMaintenanceKind('consolidating')
    s().setMaintenanceKind(null)

    expect(s().maintenanceKind).toBeNull()
    expect(s().systemLabel).toBeNull()

    s().onSystemEvent('compacting', { turnId: 'turn-1' })
    s().setMaintenanceKind(null)

    expect(s().maintenanceKind).toBeNull()
    expect(s().systemLabel).toBe('systemStatus.compacting')
  })
})

describe('turn lifecycle', () => {
  it('onTurnStarted adds a turn and sets running state', () => {
    s().onTurnStarted(makeTurn())

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-1')
    expect(state.turnStatus).toBe('running')
    expect(state.activeTurnId).toBe('turn-1')
    expect(state.turnStartedAt).not.toBeNull()
  })

  it('onAgentMessageDelta accumulates into streamingMessage', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-05-22T10:00:00.000Z'))
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onAgentMessageDelta('Hello')
    expect(s().streamingMessageLastDeltaAt).toBe(Date.now())
    vi.setSystemTime(new Date('2026-05-22T10:00:01.000Z'))
    s().onAgentMessageDelta(', world')

    expect(s().streamingMessage).toBe('Hello, world')
    expect(s().streamingMessageLastDeltaAt).toBe(Date.now())
  })

  it('onItemStarted (agentMessage) adds a streaming placeholder to turn.items', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    const items = s().turns[0].items
    expect(items).toHaveLength(1)
    expect(items[0].type).toBe('agentMessage')
    expect(items[0].id).toBe('item-1')
    expect(items[0].status).toBe('streaming')
  })

  it('onItemStarted is idempotent for replayed agentMessage items', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })

    expect(s().turns[0].items.filter((i) => i.id === 'item-1')).toHaveLength(1)
  })

  it('onItemStarted/onItemCompleted appends guidance userMessage without duplicating it', () => {
    s().onTurnStarted(makeTurn({
      items: [
        {
          id: 'user-initial',
          type: 'userMessage',
          status: 'completed',
          payload: { text: 'initial request' },
          createdAt: '2026-04-25T10:00:00.000Z',
          completedAt: '2026-04-25T10:00:00.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          payload: { toolName: 'ReadFile', callId: 'call-1', arguments: { path: 'a.txt' } },
          createdAt: '2026-04-25T10:00:01.000Z',
          completedAt: '2026-04-25T10:00:02.000Z'
        }
      ]
    }))

    const guidanceItem = {
      id: 'user-guidance',
      type: 'userMessage',
      status: 'completed',
      payload: { text: 'guidance request', deliveryMode: 'guidance' },
      createdAt: '2026-04-25T10:00:03.000Z',
      completedAt: '2026-04-25T10:00:03.000Z'
    }
    s().onItemStarted({ turnId: 'turn-1', item: guidanceItem })
    s().onItemCompleted({ turnId: 'turn-1', item: guidanceItem })

    const items = s().turns[0].items
    expect(items.map((i) => i.id)).toEqual(['user-initial', 'tool-1', 'user-guidance'])
    const guidance = items.find((i) => i.id === 'user-guidance')
    expect(guidance?.type).toBe('userMessage')
    expect(guidance?.text).toBe('guidance request')
    expect(guidance?.deliveryMode).toBe('guidance')
    expect(items.filter((i) => i.id === 'user-guidance')).toHaveLength(1)
  })

  it('ignores non-guidance userMessage item events because turn/started owns the initial prompt', () => {
    s().onTurnStarted(makeTurn({
      items: [
        {
          id: 'local-user',
          type: 'userMessage',
          status: 'completed',
          payload: { text: 'initial request' },
          createdAt: '2026-04-25T10:00:00.000Z',
          completedAt: '2026-04-25T10:00:00.000Z'
        }
      ]
    }))

    const serverInitialUser = {
      id: 'server-user',
      type: 'userMessage',
      status: 'completed',
      payload: { text: 'initial request' },
      createdAt: '2026-04-25T10:00:00.000Z',
      completedAt: '2026-04-25T10:00:00.000Z'
    }
    s().onItemStarted({ turnId: 'turn-1', item: serverInitialUser })
    s().onItemCompleted({ turnId: 'turn-1', item: serverInitialUser })

    expect(s().turns[0].items.map((i) => i.id)).toEqual(['local-user'])
  })

  it('onItemStarted/onCommandExecutionDelta/onItemCompleted track command execution output', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-1',
        type: 'commandExecution',
        payload: {
          callId: 'exec-1',
          command: 'npm test',
          workingDirectory: 'C:/repo',
          source: 'host',
          status: 'inProgress',
          aggregatedOutput: ''
        }
      }
    })

    s().onCommandExecutionDelta({
      turnId: 'turn-1',
      itemId: 'cmd-1',
      delta: 'line 1\n'
    })
    s().onCommandExecutionDelta({
      turnId: 'turn-1',
      itemId: 'cmd-1',
      delta: 'line 2\n'
    })

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-1',
        type: 'commandExecution',
        completedAt: new Date().toISOString(),
        payload: {
          callId: 'exec-1',
          command: 'npm test',
          workingDirectory: 'C:/repo',
          source: 'host',
          status: 'completed',
          aggregatedOutput: 'line 1\nline 2\n',
          exitCode: 0,
          durationMs: 1500
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'cmd-1')
    expect(item?.type).toBe('commandExecution')
    expect(item?.aggregatedOutput).toBe('line 1\nline 2\n')
    expect(item?.status).toBe('completed')
    expect(item?.executionStatus).toBe('completed')
    expect(item?.exitCode).toBe(0)
    expect(item?.duration).toBe(1500)
  })

  it('maps commandExecution executionStatus from payload.status, not wire item lifecycle status', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-wire',
        type: 'commandExecution',
        status: 'started',
        payload: {
          callId: 'exec-wire',
          command: 'ping',
          workingDirectory: 'C:/repo',
          source: 'host',
          status: 'inProgress',
          aggregatedOutput: ''
        }
      }
    })

    const cmd = s().turns[0].items.find((i) => i.id === 'cmd-wire')
    expect(cmd?.type).toBe('commandExecution')
    expect(cmd?.executionStatus).toBe('inProgress')
  })

  it('mirrors command execution output onto the matching Exec toolCall item', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-1',
        type: 'toolCall',
        payload: {
          callId: 'exec-2',
          toolName: 'Exec',
          arguments: { command: 'npm test' }
        }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-2',
        type: 'commandExecution',
        payload: {
          callId: 'exec-2',
          command: 'npm test',
          status: 'inProgress',
          aggregatedOutput: ''
        }
      }
    })
    s().onCommandExecutionDelta({ turnId: 'turn-1', itemId: 'cmd-2', delta: 'chunk\n' })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-1')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('chunk\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })

  it('applies terminal output that arrives before the matching Exec toolCall item', () => {
    s().onTurnStarted(makeTurn())
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-early',
        command: 'ping -n 4 10.8.8.8',
        workingDirectory: 'C:/repo',
        source: 'host',
        status: 'running',
        output: 'Pinging 10.8.8.8\n'
      },
      delta: 'Pinging 10.8.8.8\n'
    })

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-early',
        type: 'toolCall',
        payload: {
          callId: 'exec-terminal-early',
          toolName: 'Exec',
          arguments: { command: 'ping -n 4 10.8.8.8' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-early')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('Pinging 10.8.8.8\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.command).toBe('ping -n 4 10.8.8.8')
  })

  it('keeps completed terminal snapshot fields when the Exec toolCall completes later', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-complete',
        type: 'toolCall',
        createdAt: '2026-06-01T10:00:00.000Z',
        payload: {
          callId: 'exec-terminal-complete',
          toolName: 'Exec',
          arguments: { command: 'ping -n 4 10.8.8.8' }
        }
      }
    })

    s().onTerminalEvent({
      event: 'terminal/completed',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-complete',
        command: 'ping -n 4 10.8.8.8',
        status: 'failed',
        output: 'Request timed out.\nExit code: 1',
        exitCode: 1,
        wallTimeMs: 21152
      }
    })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-complete',
        type: 'toolCall',
        completedAt: '2026-06-01T10:00:21.152Z',
        payload: {
          callId: 'exec-terminal-complete',
          toolName: 'Exec',
          arguments: { command: 'ping -n 4 10.8.8.8' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-complete')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.aggregatedOutput).toBe('Request timed out.\nExit code: 1')
    expect(toolItem?.executionStatus).toBe('failed')
    expect(toolItem?.exitCode).toBe(1)
    expect(toolItem?.duration).toBe(21152)
  })

  it('onItemCompleted carries the interactive UI descriptor (toolUi) and result _meta for a dynamicToolCall', () => {
    const ui = { resourceUri: 'ui://dotcraft-sample/card', visibility: ['model', 'app'], prefersBorder: true }
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'dyn-1',
        type: 'dynamicToolCall',
        createdAt: '2026-06-01T10:00:00.000Z',
        payload: { callId: 'c1', toolName: 'ShowCard', namespace: 'sample', arguments: { note: 'hello' }, ui }
      }
    })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'dyn-1',
        type: 'dynamicToolCall',
        completedAt: '2026-06-01T10:00:01.000Z',
        payload: {
          callId: 'c1',
          toolName: 'ShowCard',
          namespace: 'sample',
          arguments: { note: 'hello' },
          structuredResult: { title: 'Sample Card', value: 'hello' },
          _meta: { accent: 'sample' },
          ui
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'dyn-1')
    expect(item?.status).toBe('completed')
    expect(item?.toolUi?.resourceUri).toBe('ui://dotcraft-sample/card')
    expect(item?.toolUi?.prefersBorder).toBe(true)
    expect(item?.meta).toEqual({ accent: 'sample' })
    expect(item?.structuredResult).toEqual({ title: 'Sample Card', value: 'hello' })
  })

  it('uses terminal snapshots as authoritative output instead of duplicating deltas', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-snapshot',
        type: 'toolCall',
        payload: {
          callId: 'exec-terminal-snapshot',
          toolName: 'Exec',
          arguments: { command: 'npm test' }
        }
      }
    })

    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-snapshot',
        status: 'running',
        output: 'line 1\n'
      },
      delta: 'line 1\n'
    })
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-snapshot',
        status: 'running',
        output: 'line 1\nline 2\n'
      },
      delta: 'line 2\n'
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-snapshot')
    expect(toolItem?.aggregatedOutput).toBe('line 1\nline 2\n')
  })

  it('ignores runInBackground terminal events for inline Exec tool output', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-background',
        type: 'toolCall',
        payload: {
          callId: 'exec-terminal-background',
          toolName: 'Exec',
          arguments: { command: 'sleep 60' }
        }
      }
    })

    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-background',
        status: 'running',
        backgroundReason: 'runInBackground',
        output: 'still running\n'
      },
      delta: 'still running\n'
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-background')
    expect(toolItem?.aggregatedOutput).toBeUndefined()
  })

  it('applies pending terminal output when setTurns later loads the matching Exec toolCall', () => {
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: {
        threadId: 'thread-1',
        turnId: 'turn-1',
        callId: 'exec-terminal-setturns',
        status: 'running',
        output: 'booting\n'
      },
      delta: 'booting\n'
    })

    s().setTurns([
      makeTurn({
        items: [
          {
            id: 'tool-terminal-setturns',
            type: 'toolCall',
            status: 'started',
            toolCallId: 'exec-terminal-setturns',
            toolName: 'Exec',
            arguments: { command: 'npm test' },
            createdAt: '2026-06-01T10:00:00.000Z'
          }
        ]
      })
    ])

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-setturns')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('booting\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })

  it('uses toolExecution completion to settle the matching toolCall without storing the enhancement item', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-wait',
        type: 'toolCall',
        payload: {
          callId: 'wait-1',
          toolName: 'WaitAgent',
          arguments: { childThreadId: 'thread_child' }
        }
      }
    })

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'execution-wait',
        type: 'toolExecution',
        completedAt: '2026-04-25T10:00:02.000Z',
        payload: {
          callId: 'wait-1',
          toolName: 'WaitAgent',
          status: 'completed',
          success: true,
          durationMs: 1200,
          resultPreview: 'agent done'
        }
      }
    })

    const items = s().turns[0].items
    expect(items.some((i) => i.type === 'toolExecution')).toBe(false)
    const toolItem = items.find((i) => i.id === 'tool-wait')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.success).toBe(true)
    expect(toolItem?.duration).toBe(1200)
    expect(toolItem?.resultPreview).toBe('agent done')
    expect(toolItem?.result).toBe('agent done')
  })

  it('stores early toolExecution completion and settles the matching toolCall when it starts', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'execution-read',
        type: 'toolExecution',
        completedAt: '2026-04-25T10:00:02.000Z',
        payload: {
          callId: 'read-early-execution',
          toolName: 'ReadFile',
          status: 'completed',
          success: true,
          durationMs: 900,
          resultPreview: 'read done'
        }
      }
    })

    expect(s().turns[0].items).toHaveLength(0)
    expect(s().pendingToolCompletionsByCallKey.size).toBe(1)

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-read',
        type: 'toolCall',
        payload: {
          callId: 'read-early-execution',
          toolName: 'ReadFile',
          arguments: { path: 'docs/readme.md' }
        }
      }
    })

    const items = s().turns[0].items
    expect(items.some((i) => i.type === 'toolExecution')).toBe(false)
    expect(s().pendingToolCompletionsByCallKey.size).toBe(0)
    const toolItem = items.find((i) => i.id === 'tool-read')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.success).toBe(true)
    expect(toolItem?.duration).toBe(900)
    expect(toolItem?.resultPreview).toBe('read done')
    expect(toolItem?.result).toBe('read done')
  })

  it('stores early toolResult completion and settles the matching toolCall when it starts', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'result-read',
        type: 'toolResult',
        completedAt: '2026-04-25T10:00:03.000Z',
        payload: {
          callId: 'read-early-result',
          result: 'file contents',
          success: true
        }
      }
    })

    expect(s().turns[0].items).toHaveLength(0)
    expect(s().pendingToolCompletionsByCallKey.size).toBe(1)

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-read-result',
        type: 'toolCall',
        createdAt: '2026-04-25T10:00:01.000Z',
        payload: {
          callId: 'read-early-result',
          toolName: 'ReadFile',
          arguments: { path: 'docs/notes.md' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-read-result')
    expect(s().pendingToolCompletionsByCallKey.size).toBe(0)
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.result).toBe('file contents')
    expect(toolItem?.success).toBe(true)
    expect(toolItem?.duration).toBe(2000)
    expect(toolItem?.completedAt).toBe('2026-04-25T10:00:03.000Z')
  })

  it('preserves realtime completed toolCall state over a stale thread/read hydrate', () => {
    s().onTurnStarted(makeTurn({ id: 'turn-preserve' }))
    s().onItemStarted({
      turnId: 'turn-preserve',
      item: {
        id: 'tool-read',
        type: 'toolCall',
        createdAt: '2026-04-25T10:00:01.000Z',
        payload: {
          callId: 'call-read',
          toolName: 'ReadFile',
          arguments: { path: 'docs/readme.md' }
        }
      }
    })
    s().onItemCompleted({
      turnId: 'turn-preserve',
      item: {
        id: 'result-read',
        type: 'toolResult',
        completedAt: '2026-04-25T10:00:03.000Z',
        payload: {
          callId: 'call-read',
          result: 'file contents',
          success: true
        }
      }
    })

    s().setTurns([
      makeTurn({
        id: 'turn-preserve',
        status: 'running',
        startedAt: '2026-04-25T10:00:00.000Z',
        items: [
          {
            id: 'tool-read',
            type: 'toolCall',
            status: 'completed',
            createdAt: '2026-04-25T10:00:01.000Z',
            payload: {
              callId: 'call-read',
              toolName: 'ReadFile',
              arguments: { path: 'docs/readme.md' }
            }
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-read')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.result).toBe('file contents')
    expect(toolItem?.success).toBe(true)
    expect(toolItem?.completedAt).toBe('2026-04-25T10:00:03.000Z')
  })

  it('preserves settled parallel explore tools and resolved approvals over stale hydrate', () => {
    s().onTurnStarted(makeTurn({ id: 'turn-parallel' }))
    const calls = [
      ['tool-profile', 'call-profile', 'ReadFile', 'docs/profile.md', 'req-profile'],
      ['tool-notes', 'call-notes', 'ReadFile', 'docs/notes.md', 'req-notes'],
      ['tool-assets', 'call-assets', 'FindFiles', 'docs/assets', 'req-assets']
    ] as const

    for (const [itemId, callId, toolName, path, requestId] of calls) {
      s().onItemStarted({
        turnId: 'turn-parallel',
        item: {
          id: itemId,
          type: 'toolCall',
          createdAt: `2026-04-25T10:00:0${calls.findIndex((entry) => entry[0] === itemId) + 1}.000Z`,
          payload: {
            callId,
            toolName,
            arguments: { path }
          }
        }
      })
      s().onApprovalRequest(`bridge-${requestId}`, {
        threadId: 'thread-1',
        turnId: 'turn-parallel',
        requestId,
        approvalType: 'file',
        operation: 'read',
        target: path,
        reason: `Read ${path}`
      })
      s().onApprovalResolved({
        threadId: 'thread-1',
        turnId: 'turn-parallel',
        requestId,
        decision: 'accept'
      })
      s().onItemCompleted({
        turnId: 'turn-parallel',
        item: {
          id: `result-${callId}`,
          type: 'toolResult',
          completedAt: `2026-04-25T10:00:1${calls.findIndex((entry) => entry[0] === itemId) + 1}.000Z`,
          payload: {
            callId,
            result: `${toolName} done`,
            success: true
          }
        }
      })
    }

    s().setTurns([
      makeTurn({
        id: 'turn-parallel',
        status: 'running',
        startedAt: '2026-04-25T10:00:00.000Z',
        items: calls.map(([itemId, callId, toolName, path], index) => ({
          id: itemId,
          type: 'toolCall',
          status: 'completed',
          createdAt: `2026-04-25T10:00:0${index + 1}.000Z`,
          payload: {
            callId,
            toolName,
            arguments: { path }
          }
        }))
      })
    ], { preserveExistingRealtime: true })

    const items = s().turns[0].items
    const settledTools = items.filter((item) => item.type === 'toolCall')
    expect(settledTools).toHaveLength(3)
    expect(settledTools.every((item) => item.success === true && item.result != null)).toBe(true)
    const approvalCards = items.filter((item) => item.type === 'approvalCard')
    expect(approvalCards).toHaveLength(3)
    expect(approvalCards.every((item) => item.approvalState === 'accepted')).toBe(true)
  })

  it('does not preserve realtime state across a different thread hydrate', () => {
    s().onTurnStarted(makeTurn({ id: 'turn-a', threadId: 'thread-a' }))
    s().onItemStarted({
      turnId: 'turn-a',
      item: {
        id: 'tool-read',
        type: 'toolCall',
        createdAt: '2026-04-25T10:00:01.000Z',
        payload: {
          callId: 'call-read',
          toolName: 'ReadFile',
          arguments: { path: 'docs/a.md' }
        }
      }
    })
    s().onItemCompleted({
      turnId: 'turn-a',
      item: {
        id: 'result-read',
        type: 'toolResult',
        completedAt: '2026-04-25T10:00:02.000Z',
        payload: {
          callId: 'call-read',
          result: 'thread a contents',
          success: true
        }
      }
    })

    s().setTurns([
      makeTurn({
        id: 'turn-b',
        threadId: 'thread-b',
        status: 'running',
        startedAt: '2026-04-25T10:01:00.000Z',
        items: [
          {
            id: 'tool-read',
            type: 'toolCall',
            status: 'completed',
            createdAt: '2026-04-25T10:01:01.000Z',
            payload: {
              callId: 'call-read',
              toolName: 'ReadFile',
              arguments: { path: 'docs/b.md' }
            }
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn-b')
    const toolItem = s().turns[0].items.find((item) => item.id === 'tool-read')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.result).toBeUndefined()
    expect(toolItem?.success).toBeUndefined()
  })

  it('keeps terminal toolExecution status when historical commandExecution is still inProgress', () => {
    const denial = 'MODE_POLICY_DENIED\nTool: Exec\nCurrentMode: Plan'

    s().setTurns([
      makeTurn({
        status: 'completed',
        items: [
          {
            id: 'tool-denied',
            type: 'toolCall',
            status: 'completed',
            toolName: 'Exec',
            toolCallId: 'exec-denied',
            arguments: { command: 'Get-Content README.md | Select-String DotCraft' },
            createdAt: '2026-06-11T07:08:02.030Z',
            completedAt: '2026-06-11T07:08:02.035Z'
          },
          {
            id: 'cmd-denied',
            type: 'commandExecution',
            status: 'started',
            toolCallId: 'exec-denied',
            command: 'Get-Content README.md | Select-String DotCraft',
            executionStatus: 'inProgress',
            aggregatedOutput: '',
            createdAt: '2026-06-11T07:08:02.030Z'
          },
          {
            id: 'tool-execution-denied',
            type: 'toolExecution',
            status: 'completed',
            toolCallId: 'exec-denied',
            toolName: 'Exec',
            executionStatus: 'failed',
            success: false,
            resultPreview: denial,
            duration: 5,
            createdAt: '2026-06-11T07:08:02.030Z',
            completedAt: '2026-06-11T07:08:02.035Z'
          }
        ]
      })
    ])

    const items = s().turns[0].items
    expect(items.some((i) => i.type === 'toolExecution')).toBe(false)
    const toolItem = items.find((i) => i.id === 'tool-denied')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.executionStatus).toBe('failed')
    expect(toolItem?.success).toBe(false)
    expect(toolItem?.aggregatedOutput).toBe(denial)
    expect(toolItem?.resultPreview).toBe(denial)
  })

  it('keeps SubAgent streaming argument previews bounded for large prompts', () => {
    const largePrompt = 'x'.repeat(20000)
    s().onTurnStarted(makeTurn())

    s().onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'spawn-stream',
      toolName: 'SpawnAgent',
      callId: 'call-spawn',
      delta: `{"agentPrompt":"${largePrompt}`
    })

    const item = s().turns[0].items.find((i) => i.id === 'spawn-stream')
    expect(item?.type).toBe('toolCall')
    expect(item?.toolName).toBe('SpawnAgent')
    expect(item?.status).toBe('streaming')
    expect(item?.argumentsPreview?.length).toBeLessThan(1200)

    const display = getStreamingToolDisplay('SpawnAgent', item?.argumentsPreview, 'en')
    expect(display.label).toMatch(/^Spawning agent for: x+/)
    expect(display.label.length).toBeLessThan(90)
    expect(item?.argumentsPreview).not.toContain(largePrompt)
  })

  it('merges an existing command execution into Exec when toolCall starts later', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-pre',
        type: 'commandExecution',
        payload: {
          callId: 'exec-pre',
          command: 'npm test',
          source: 'host',
          status: 'inProgress',
          aggregatedOutput: 'booting\n'
        }
      }
    })

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-pre',
        type: 'toolCall',
        payload: {
          callId: 'exec-pre',
          toolName: 'Exec',
          arguments: { command: 'npm test' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-pre')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.aggregatedOutput).toBe('booting\n')
    expect(toolItem?.commandSource).toBe('host')
  })

  it('keeps Exec live when toolCall completes after command execution already started', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-live',
        type: 'commandExecution',
        payload: {
          callId: 'exec-live',
          command: 'npm test',
          source: 'host',
          status: 'inProgress',
          aggregatedOutput: 'booting\n'
        }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-live',
        type: 'toolCall',
        payload: {
          callId: 'exec-live',
          toolName: 'Exec'
        }
      }
    })

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'tool-live',
        type: 'toolCall',
        completedAt: new Date().toISOString(),
        payload: {
          callId: 'exec-live',
          toolName: 'Exec',
          arguments: { command: 'npm test' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-live')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.arguments?.command).toBe('npm test')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.aggregatedOutput).toBe('booting\n')
  })

  it('mirrors command execution onto matching RunCommand toolCall (not only Exec)', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-rc',
        type: 'toolCall',
        payload: {
          callId: 'run-1',
          toolName: 'RunCommand',
          arguments: { command: 'echo hi' }
        }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-rc',
        type: 'commandExecution',
        payload: {
          callId: 'run-1',
          command: 'echo hi',
          status: 'inProgress',
          aggregatedOutput: ''
        }
      }
    })
    s().onCommandExecutionDelta({ turnId: 'turn-1', itemId: 'cmd-rc', delta: 'out\n' })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-rc')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.toolName).toBe('RunCommand')
    expect(toolItem?.aggregatedOutput).toBe('out\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })

  it('onItemCompleted (agentMessage) updates placeholder in place and clears buffer', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onAgentMessageDelta('Final text')
    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'item-1', type: 'agentMessage', createdAt: new Date().toISOString() }
    })

    const state = s()
    expect(state.streamingMessage).toBe('')
    expect(state.streamingMessageLastDeltaAt).toBeNull()
    const items = state.turns[0].items
    expect(items).toHaveLength(1)
    expect(items[0].text).toBe('Final text')
    expect(items[0].type).toBe('agentMessage')
    expect(items[0].status).toBe('completed')
  })

  it('onTurnCompleted marks turn as completed and clears running state', () => {
    s().onTurnStarted(makeTurn())
    s().onTurnCompleted(makeTurn({ status: 'completed', completedAt: new Date().toISOString() }))

    const state = s()
    expect(state.turnStatus).toBe('idle')
    expect(state.activeTurnId).toBeNull()
    expect(state.turns[0].status).toBe('completed')
  })

  it('onTurnFailed marks turn as failed with error message', () => {
    s().onTurnStarted(makeTurn())
    s().onTurnFailed(makeTurn(), 'API error')

    const state = s()
    expect(state.turnStatus).toBe('idle')
    expect(state.turns[0].status).toBe('failed')
    expect(state.turns[0].error).toBe('API error')
  })

  it('onTurnCancelled marks turn as cancelled with reason', () => {
    s().onTurnStarted(makeTurn())
    s().onTurnCancelled(makeTurn(), 'user requested')

    const state = s()
    expect(state.turnStatus).toBe('idle')
    expect(state.turns[0].status).toBe('cancelled')
    expect(state.turns[0].cancelReason).toBe('user requested')
  })
})

describe('reasoning flow', () => {
  it('onReasoningDelta accumulates into streamingReasoning', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'r-1', type: 'reasoningContent' } })
    s().onReasoningDelta('Step 1.')
    s().onReasoningDelta(' Step 2.')

    expect(s().streamingReasoning).toBe('Step 1. Step 2.')
  })

  it('onItemStarted (reasoningContent) adds a streaming placeholder to turn.items', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'r-1', type: 'reasoningContent' } })
    const items = s().turns[0].items.filter((i) => i.type === 'reasoningContent')
    expect(items).toHaveLength(1)
    expect(items[0].id).toBe('r-1')
    expect(items[0].status).toBe('streaming')
  })

  it('onItemCompleted (reasoningContent) updates placeholder in place and clears buffer', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'r-1', type: 'reasoningContent' } })
    s().onReasoningDelta('Thinking deeply...')
    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'r-1', type: 'reasoningContent', createdAt: new Date().toISOString() }
    })

    const state = s()
    expect(state.streamingReasoning).toBe('')
    const reasoningItems = state.turns[0].items.filter((i) => i.type === 'reasoningContent')
    expect(reasoningItems).toHaveLength(1)
    expect(reasoningItems[0].reasoning).toBe('Thinking deeply...')
    expect(reasoningItems[0].status).toBe('completed')
  })
})

describe('token usage accumulation', () => {
  it('accumulates tokens via onUsageDelta', () => {
    s().onTurnStarted(makeTurn())
    s().onUsageDelta(100, 50)
    s().onUsageDelta(200, 100)

    const state = s()
    expect(state.inputTokens).toBe(300)
    expect(state.outputTokens).toBe(150)
  })

  it('resets tokens on new turn', () => {
    s().onTurnStarted(makeTurn())
    s().onUsageDelta(500, 200)
    s().onTurnCompleted(makeTurn({ status: 'completed' }))
    s().onTurnStarted(makeTurn({ id: 'turn-2' }))

    const state = s()
    expect(state.inputTokens).toBe(0)
    expect(state.outputTokens).toBe(0)
  })
})

describe('system events', () => {
  it('sets auto-compacting label on turn-scoped "compacting" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('compacting', { turnId: 'turn-1' })
    expect(s().systemLabel).toBe('systemStatus.compacting')
  })

  it('sets manual compacting label on thread-scoped "compacting" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('compacting')
    expect(s().systemLabel).toBe('systemStatus.compacting.manual')
  })

  it('clears label on "compacted" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('compacting')
    s().onSystemEvent('compacted')
    expect(s().systemLabel).toBeNull()
  })

  it('clears label on "compactFailed" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('compacting')
    s().onSystemEvent('compactFailed')
    expect(s().systemLabel).toBeNull()
  })

  it('clears label on "consolidationFailed" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('consolidating')
    expect(s().systemLabel).toBe('systemStatus.consolidating')
    s().onSystemEvent('consolidationFailed')
    expect(s().systemLabel).toBeNull()
  })

  it('clears label on "consolidationSkipped" event', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('consolidating')
    expect(s().systemLabel).toBe('systemStatus.consolidating')
    s().onSystemEvent('consolidationSkipped')
    expect(s().systemLabel).toBeNull()
  })

  it('ignores unknown system event kinds', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('unknown-event-xyz')
    expect(s().systemLabel).toBeNull()
  })

  it('records stream retry signals from streamError without changing systemLabel', () => {
    s().onTurnStarted(makeTurn())

    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      message: 'Reconnecting... 1/1'
    })

    expect(s().systemLabel).toBeNull()
    expect(s().streamRetrySignals).toHaveLength(1)
    expect(s().streamRetrySignals[0]).toMatchObject({
      turnId: 'turn-1',
      rawMessage: 'Reconnecting... 1/1',
      attempt: 1,
      max: 1
    })
  })

  it('dedupes identical stream retry signals and clears them when the turn ends', () => {
    s().onTurnStarted(makeTurn())

    s().onSystemEvent('streamError', { turnId: 'turn-1', message: 'Reconnecting... 1/2' })
    s().onSystemEvent('streamError', { turnId: 'turn-1', message: 'Reconnecting... 1/2' })
    s().onSystemEvent('streamError', { turnId: 'turn-1', message: 'Reconnecting... 2/2' })

    expect(s().streamRetrySignals.map((signal) => signal.rawMessage)).toEqual([
      'Reconnecting... 1/2',
      'Reconnecting... 2/2'
    ])

    s().onTurnCompleted(makeTurn({ status: 'completed' }))

    expect(s().streamRetrySignals).toEqual([])
  })

  it('clears stream retry signals when loading persisted turns', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('streamError', { turnId: 'turn-1', message: 'Reconnecting... 1/1' })

    s().setTurns([makeTurn({ status: 'completed' })])

    expect(s().streamRetrySignals).toEqual([])
  })
})

describe('context usage (token ring)', () => {
  const baseSnapshot = {
    tokens: 40_000,
    contextWindow: 200_000,
    autoCompactThreshold: 180_000,
    warningThreshold: 176_000,
    errorThreshold: 194_000,
    percentLeft: 0.8
  }

  it('seeds contextUsage from setContextUsage and classifies severity', () => {
    s().setContextUsage(baseSnapshot)
    const usage = s().contextUsage
    expect(usage).not.toBeNull()
    expect(usage!.tokens).toBe(40_000)
    expect(usage!.severity).toBe('normal')
  })

  it('overrides tokens when onUsageDelta carries totalInputTokens', () => {
    s().setContextUsage(baseSnapshot)
    s().onUsageDelta(1000, 200, 180_500, 3000)
    const usage = s().contextUsage
    expect(usage!.tokens).toBe(180_500)
    expect(usage!.severity).toBe('warning')
    expect(usage!.percentLeft).toBeCloseTo(1 - 180_500 / 200_000, 3)
  })

  it('promotes severity to error past the error threshold', () => {
    s().setContextUsage(baseSnapshot)
    s().onUsageDelta(0, 0, 195_000)
    expect(s().contextUsage!.severity).toBe('error')
  })

  it('applies compacted system event to reset tokens and severity', () => {
    s().setContextUsage({ ...baseSnapshot, tokens: 195_000, percentLeft: 0.02 })
    s().onSystemEvent('compacted', { tokenCount: 44_000, percentLeft: 0.78 })
    const usage = s().contextUsage
    expect(usage!.tokens).toBe(44_000)
    expect(usage!.percentLeft).toBeCloseTo(0.78, 3)
    expect(usage!.severity).toBe('normal')
  })

  it('seeds contextUsage from a compacted system event full snapshot', () => {
    expect(s().contextUsage).toBeNull()

    s().onSystemEvent('compacted', {
      tokenCount: 195_000,
      percentLeft: 0.02,
      contextUsage: {
        tokens: 44_000,
        contextWindow: 200_000,
        autoCompactThreshold: 180_000,
        warningThreshold: 176_000,
        errorThreshold: 194_000,
        percentLeft: 0.78
      }
    })

    expect(s().contextUsage?.tokens).toBe(44_000)
    expect(s().contextUsage?.percentLeft).toBe(0.78)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('applies compacted systemNotice tokens to an existing context ring', () => {
    s().setContextUsage({ ...baseSnapshot, tokens: 195_000, percentLeft: 0.025 })
    s().onTurnStarted(makeTurn())

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'notice-ring',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: {
          kind: 'compacted',
          trigger: 'manual',
          mode: 'partial',
          tokensBefore: 195_000,
          tokensAfter: 44_000,
          percentLeftAfter: 0.78
        }
      }
    })

    expect(s().contextUsage?.tokens).toBe(44_000)
    expect(s().contextUsage?.percentLeft).toBe(0.78)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('applies compacted notices from refreshed thread history to an existing context ring', () => {
    s().setContextUsage({ ...baseSnapshot, tokens: 195_000, percentLeft: 0.025 })

    s().setTurns([{
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      items: [{
        id: 'notice-history',
        type: 'systemNotice',
        status: 'completed',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        systemNotice: {
          kind: 'compacted',
          trigger: 'manual',
          mode: 'partial',
          tokensAfter: 44_000,
          percentLeftAfter: 0.78
        }
      }],
      startedAt: new Date().toISOString(),
      completedAt: new Date().toISOString()
    } as ConversationTurn])

    expect(s().contextUsage?.tokens).toBe(44_000)
    expect(s().contextUsage?.percentLeft).toBe(0.78)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('applies compact skipped and failed snapshots to contextUsage', () => {
    s().setContextUsage({ ...baseSnapshot, tokens: 195_000, percentLeft: 0.02 })
    s().onSystemEvent('compactSkipped', { tokenCount: 196_000, percentLeft: 0.01 })
    expect(s().contextUsage!.tokens).toBe(196_000)
    expect(s().contextUsage!.percentLeft).toBeCloseTo(0.01, 3)

    s().onSystemEvent('compactFailed', { tokenCount: 197_000, percentLeft: 0 })
    expect(s().contextUsage!.tokens).toBe(197_000)
    expect(s().contextUsage!.percentLeft).toBe(0)
  })

  it('ignores totals when no snapshot has been seeded yet', () => {
    s().onUsageDelta(100, 50, 5000)
    expect(s().contextUsage).toBeNull()
  })

  it('seeds contextUsage from onUsageDelta full snapshot', () => {
    s().onUsageDelta(100, 50, 5000, 50, {
      tokens: 5000,
      contextWindow: 200_000,
      autoCompactThreshold: 180_000,
      warningThreshold: 176_000,
      errorThreshold: 194_000,
      percentLeft: 0.975
    })

    expect(s().inputTokens).toBe(100)
    expect(s().outputTokens).toBe(50)
    expect(s().contextUsage?.tokens).toBe(5000)
    expect(s().contextUsage?.percentLeft).toBe(0.975)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('clears contextUsage when setContextUsage(null) is called', () => {
    s().setContextUsage(baseSnapshot)
    s().setContextUsage(null)
    expect(s().contextUsage).toBeNull()
  })

  it('resets contextUsage on store reset', () => {
    s().setContextUsage(baseSnapshot)
    s().reset()
    expect(s().contextUsage).toBeNull()
  })
})

describe('systemNotice items', () => {
  it('appends a compaction notice to turn.items on item/completed', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'notice-1',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: {
          kind: 'compacted',
          trigger: 'auto',
          mode: 'partial',
          tokensBefore: 180_000,
          tokensAfter: 44_000,
          percentLeftAfter: 0.78,
          clearedToolResults: 2
        }
      }
    })

    const items = s().turns[0].items
    const notice = items.find((i) => i.type === 'systemNotice')
    expect(notice).toBeTruthy()
    expect(notice!.systemNotice?.kind).toBe('compacted')
    expect(notice!.systemNotice?.trigger).toBe('auto')
    expect(notice!.systemNotice?.tokensBefore).toBe(180_000)
  })

  it('dedupes systemNotice items when emitted twice with the same id', () => {
    s().onTurnStarted(makeTurn())
    const payload = {
      turnId: 'turn-1',
      item: {
        id: 'notice-dup',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: { kind: 'compacted', trigger: 'reactive', mode: 'micro' }
      }
    }
    s().onItemCompleted(payload)
    s().onItemCompleted(payload)
    const count = s().turns[0].items.filter((i) => i.type === 'systemNotice').length
    expect(count).toBe(1)
  })

  it('appends a memory consolidation notice to turn.items on item/completed', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'notice-memory',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: {
          kind: 'memoryConsolidated'
        }
      }
    })

    const notice = s().turns[0].items.find((i) => i.type === 'systemNotice')
    expect(notice?.systemNotice?.kind).toBe('memoryConsolidated')
  })

  it('preserves fork source thread id on fork notices', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'notice-forked',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: {
          kind: 'forked',
          sourceThreadId: 'thread-source'
        }
      }
    })

    const notice = s().turns[0].items.find((i) => i.type === 'systemNotice')
    expect(notice?.systemNotice?.kind).toBe('forked')
    expect(notice?.systemNotice?.sourceThreadId).toBe('thread-source')
  })
})

describe('pending message', () => {
  it('stores pending message', () => {
    s().setPendingMessage({
      text: 'Follow-up question',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })
    expect(s().pendingMessage).toEqual({
      text: 'Follow-up question',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })
  })

  it('clears pending message', () => {
    s().setPendingMessage({ text: 'text' })
    s().setPendingMessage(null)
    expect(s().pendingMessage).toBeNull()
  })
})

describe('setTurns', () => {
  it('populates turns from raw wire format', () => {
    const rawTurns = [
      makeTurn({ status: 'completed', items: [] }),
      makeTurn({ id: 'turn-2', status: 'completed', items: [] })
    ]
    s().setTurns(rawTurns)

    expect(s().turns).toHaveLength(2)
    expect(s().turns[0].id).toBe('turn-1')
    expect(s().turns[1].id).toBe('turn-2')
  })

  it('restores waitingInput as the active turn state', () => {
    s().setTurns([
      makeTurn({ id: 'turn-wait-input', status: 'waitingInput', items: [] })
    ])

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().activeTurnId).toBe('turn-wait-input')
    expect(s().turnStartedAt).not.toBeNull()
  })

  it('restores waitingApproval as the active turn state', () => {
    s().setTurns([
      makeTurn({ id: 'turn-wait-approval', status: 'waitingApproval', items: [] })
    ])

    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().activeTurnId).toBe('turn-wait-approval')
    expect(s().turnStartedAt).not.toBeNull()
  })
})

describe('reset', () => {
  it('clears all state back to initial values', () => {
    s().onTurnStarted(makeTurn())
    s().onAgentMessageDelta('some text')
    s().setPendingMessage({ text: 'pending' })
    s().reset()

    const state = s()
    expect(state.turns).toHaveLength(0)
    expect(state.turnStatus).toBe('idle')
    expect(state.streamingMessage).toBe('')
    expect(state.streamingMessageLastDeltaAt).toBeNull()
    expect(state.pendingMessage).toBeNull()
  })
})

describe('optimistic turns', () => {
  it('addOptimisticTurn immediately adds the turn and sets running state', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)

    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('local-turn-1')
    expect(s().turns[0].items[0].text).toBe('Hello')
    expect(s().turnStatus).toBe('running')
  })

  it('onTurnStarted replaces optimistic turn, preserving user message items', () => {
    // Add optimistic turn
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)

    // Server confirms with real turn id
    s().onTurnStarted(makeTurn({ id: 'real-turn-1', items: [] }))

    const state = s()
    // The optimistic turn should be replaced by real-turn-1
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('real-turn-1')
    // User message from optimistic turn preserved
    expect(state.turns[0].items[0].text).toBe('Hello')
    expect(state.turns[0].items[0].type).toBe('userMessage')
  })

  it('removeOptimisticTurn removes the turn and resets running state', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-fail',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    expect(s().turnStatus).toBe('running')

    s().removeOptimisticTurn('local-turn-fail')
    expect(s().turns).toHaveLength(0)
    expect(s().turnStatus).toBe('idle')
    expect(s().activeTurnId).toBeNull()
  })

  it('promoteOptimisticTurn replaces local ID with server ID in turns and activeTurnId', () => {
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-123',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    expect(s().activeTurnId).toBe('local-turn-123')

    s().promoteOptimisticTurn('local-turn-123', 'turn_server_abc')

    const state = s()
    expect(state.activeTurnId).toBe('turn_server_abc')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn_server_abc')
    // Items should be preserved
    expect(state.turns[0].items[0].text).toBe('Hello')
  })

  it('preserveExistingRealtime prefers the server userMessage over the promoted optimistic preview', () => {
    const optimisticTurn: ConversationTurn = {
      id: 'local-turn-preview',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-preview',
          type: 'userMessage',
          status: 'completed',
          text: 'cancel this turn',
          nativeInputParts: [{ type: 'text', text: 'cancel this turn' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    }

    s().addOptimisticTurn(optimisticTurn)
    s().promoteOptimisticTurn('local-turn-preview', 'turn-server-preview')
    s().setTurns([
      makeTurn({
        id: 'turn-server-preview',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.050Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-preview',
            type: 'userMessage',
            status: 'completed',
            payload: {
              text: 'cancel this turn',
              nativeInputParts: [{ type: 'text', text: 'cancel this turn' }]
            },
            createdAt: '2026-06-13T10:00:00.050Z',
            completedAt: '2026-06-13T10:00:00.050Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    const state = s()
    const userMessages = state.turns.flatMap((turn) => turn.items.filter((item) => item.type === 'userMessage'))
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-preview')
    expect(state.turns[0].status).toBe('cancelled')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-preview')
  })

  it('onTurnStarted replaces a promoted optimistic preview with the canonical server userMessage', () => {
    s().addOptimisticTurn({
      id: 'local-turn-live',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-live',
          type: 'userMessage',
          status: 'completed',
          text: 'live canonical',
          nativeInputParts: [{ type: 'text', text: 'live canonical' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })
    s().promoteOptimisticTurn('local-turn-live', 'turn-server-live')

    s().onTurnStarted(makeTurn({
      id: 'turn-server-live',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-06-13T10:00:00.050Z',
      items: [
        {
          id: 'server-user-live',
          type: 'userMessage',
          status: 'completed',
          payload: {
            text: 'live canonical',
            nativeInputParts: [{ type: 'text', text: 'live canonical' }]
          },
          createdAt: '2026-06-13T10:00:00.050Z'
        }
      ]
    }))

    const state = s()
    const userMessages = state.turns[0].items.filter((item) => item.type === 'userMessage')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-live')
    expect(state.turnStatus).toBe('running')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-live')
  })

  it('does not keep a represented local optimistic turn when a snapshot arrives before promotion', () => {
    const optimisticTurn: ConversationTurn = {
      id: 'local-turn-cancel',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-cancel',
          type: 'userMessage',
          status: 'completed',
          text: 'stop me',
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    }

    s().addOptimisticTurn(optimisticTurn)
    s().setTurns([
      makeTurn({
        id: 'turn-server-cancel',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.100Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-cancel',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'stop me' },
            createdAt: '2026-06-13T10:00:00.100Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })
    s().promoteOptimisticTurn('local-turn-cancel', 'turn-server-cancel')

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-cancel')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
    expect(state.activeTurnId).toBeNull()
  })

  it('promoteOptimisticTurn coalesces when the server turn is already present', () => {
    s().setTurns([
      makeTurn({
        id: 'turn-server-existing',
        threadId: 'thread-1',
        status: 'running',
        startedAt: '2026-06-13T10:00:00.050Z',
        items: [
          {
            id: 'server-user-existing',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'already here' },
            createdAt: '2026-06-13T10:00:00.050Z'
          }
        ]
      })
    ])
    s().addOptimisticTurn({
      id: 'local-turn-existing',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-existing',
          type: 'userMessage',
          status: 'completed',
          text: 'already here',
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })

    s().promoteOptimisticTurn('local-turn-existing', 'turn-server-existing')

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-existing')
    expect(state.activeTurnId).toBe('turn-server-existing')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
  })

  it('keeps a newer same-text optimistic turn when the snapshot only has older history', () => {
    s().addOptimisticTurn({
      id: 'local-turn-repeat',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-repeat',
          type: 'userMessage',
          status: 'completed',
          text: 'repeat',
          createdAt: '2026-06-13T10:05:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:05:00.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-old-repeat',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-06-13T10:00:00.000Z',
        completedAt: '2026-06-13T10:00:01.000Z',
        items: [
          {
            id: 'server-user-old-repeat',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'repeat' },
            createdAt: '2026-06-13T10:00:00.000Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    expect(s().turns.map((turn) => turn.id)).toEqual(['turn-old-repeat', 'local-turn-repeat'])
  })

  it('keeps same-text terminal history inside clock skew when it completed before the optimistic turn', () => {
    s().addOptimisticTurn({
      id: 'local-turn-skew-repeat',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-skew-repeat',
          type: 'userMessage',
          status: 'completed',
          text: 'ok',
          createdAt: '2026-06-13T10:00:01.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:01.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-old-skew-repeat',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-06-13T10:00:00.500Z',
        completedAt: '2026-06-13T10:00:00.900Z',
        items: [
          {
            id: 'server-user-old-skew-repeat',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'ok' },
            createdAt: '2026-06-13T10:00:00.500Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    expect(s().turns.map((turn) => turn.id)).toEqual([
      'turn-old-skew-repeat',
      'local-turn-skew-repeat'
    ])
  })

  it('matches a terminal server turn inside backward clock skew when it completed after the optimistic turn began', () => {
    s().addOptimisticTurn({
      id: 'local-turn-skew-cancel',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-skew-cancel',
          type: 'userMessage',
          status: 'completed',
          text: 'cancel fast',
          createdAt: '2026-06-13T10:00:01.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:01.000Z'
    })

    s().setTurns([
      makeTurn({
        id: 'turn-server-skew-cancel',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: '2026-06-13T10:00:00.500Z',
        completedAt: '2026-06-13T10:00:01.200Z',
        items: [
          {
            id: 'server-user-skew-cancel',
            type: 'userMessage',
            status: 'completed',
            payload: { text: 'cancel fast' },
            createdAt: '2026-06-13T10:00:00.500Z'
          }
        ]
      })
    ], { preserveExistingRealtime: true })

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-server-skew-cancel')
    expect(state.turns[0].status).toBe('cancelled')
    expect(state.turns[0].items.filter((item) => item.type === 'userMessage')).toHaveLength(1)
  })

  it('promoteOptimisticTurn does not change activeTurnId if it was already replaced', () => {
    // Simulate race: turn/started arrived before turn/start response and already updated activeTurnId
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-999',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    }
    s().addOptimisticTurn(optimisticTurn)
    // Simulate turn/started replacing the turn already
    s().onTurnStarted(makeTurn({ id: 'turn_server_xyz' }))
    // activeTurnId is now 'turn_server_xyz' (not the local one)

    // promoteOptimisticTurn for the old local ID should be a no-op on activeTurnId
    s().promoteOptimisticTurn('local-turn-999', 'turn_server_from_response')
    // activeTurnId should still be 'turn_server_xyz' since it was already replaced
    expect(s().activeTurnId).toBe('turn_server_xyz')
  })

  it('Scenario B: onTurnStarted does not create duplicate when promoteOptimisticTurn ran first', () => {
    // Scenario B: RPC response arrives BEFORE the turn/started notification
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-456',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-1',
          type: 'userMessage',
          status: 'completed',
          text: 'Hello',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    // Step 1: optimistic turn added
    s().addOptimisticTurn(optimisticTurn)
    expect(s().turns).toHaveLength(1)
    expect(s().activeTurnId).toBe('local-turn-456')

    // Step 2: RPC response arrives first, promoting the turn
    s().promoteOptimisticTurn('local-turn-456', 'turn_001')
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn_001')
    expect(s().activeTurnId).toBe('turn_001')

    // Step 3: turn/started notification arrives — must NOT add a second turn
    s().onTurnStarted(makeTurn({ id: 'turn_001' }))

    const state = s()
    expect(state.turns).toHaveLength(1)               // no duplicate
    expect(state.turns[0].id).toBe('turn_001')
    expect(state.turns[0].items[0].text).toBe('Hello') // user message preserved
    expect(state.turnStatus).toBe('running')
    expect(state.activeTurnId).toBe('turn_001')
  })

  it('Scenario A: onTurnStarted notification arrives before RPC response (existing happy path)', () => {
    // Scenario A: notification arrives before RPC response — existing behaviour
    const optimisticTurn: import('../types/conversation').ConversationTurn = {
      id: 'local-turn-789',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-item-2',
          type: 'userMessage',
          status: 'completed',
          text: 'Hi there',
          createdAt: new Date().toISOString()
        }
      ],
      startedAt: new Date().toISOString()
    }
    // Step 1: optimistic turn added
    s().addOptimisticTurn(optimisticTurn)

    // Step 2: turn/started notification arrives first (local-turn-789 still exists)
    s().onTurnStarted(makeTurn({ id: 'turn_002' }))
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn_002')
    expect(s().turns[0].items[0].text).toBe('Hi there') // user message preserved

    // Step 3: RPC response arrives — promoteOptimisticTurn finds nothing to promote (no-op)
    s().promoteOptimisticTurn('local-turn-789', 'turn_002')
    expect(s().turns).toHaveLength(1)                  // still only one turn
    expect(s().activeTurnId).toBe('turn_002')
  })

  it('Scenario A: onTurnStarted replaces matching optimistic user message with canonical server item', () => {
    s().addOptimisticTurn({
      id: 'local-turn-canonical-first',
      threadId: 'thread-1',
      status: 'running',
      items: [
        {
          id: 'local-user-canonical-first',
          type: 'userMessage',
          status: 'completed',
          text: 'notification first',
          nativeInputParts: [{ type: 'text', text: 'notification first' }],
          createdAt: '2026-06-13T10:00:00.000Z'
        }
      ],
      startedAt: '2026-06-13T10:00:00.000Z'
    })

    s().onTurnStarted(makeTurn({
      id: 'turn-canonical-first',
      threadId: 'thread-1',
      startedAt: '2026-06-13T10:00:00.050Z',
      items: [
        {
          id: 'server-user-canonical-first',
          type: 'userMessage',
          status: 'completed',
          payload: {
            text: 'notification first',
            nativeInputParts: [{ type: 'text', text: 'notification first' }]
          },
          createdAt: '2026-06-13T10:00:00.050Z'
        }
      ]
    }))
    s().promoteOptimisticTurn('local-turn-canonical-first', 'turn-canonical-first')

    const userMessages = s().turns[0].items.filter((item) => item.type === 'userMessage')
    expect(s().turns).toHaveLength(1)
    expect(s().turns[0].id).toBe('turn-canonical-first')
    expect(userMessages).toHaveLength(1)
    expect(userMessages[0].id).toBe('server-user-canonical-first')
  })
})

describe('subAgent progress', () => {
  it('replaces subAgentEntries wholesale on each notification', () => {
    const first = [
      { label: 'agent-a', currentTool: 'ReadFile', currentToolDisplay: 'Reading file', isCompleted: false, inputTokens: 100, outputTokens: 50 },
      { label: 'agent-b', currentTool: 'WriteFile', currentToolDisplay: 'Writing file', isCompleted: false, inputTokens: 200, outputTokens: 80 }
    ]
    s().onSubagentProgress(first)
    expect(s().subAgentEntries).toHaveLength(2)
    expect(s().subAgentEntries[0].label).toBe('agent-a')

    // Second snapshot: agent-a completed, agent-c added
    const second = [
      { label: 'agent-a', currentTool: null, currentToolDisplay: null, isCompleted: true, inputTokens: 500, outputTokens: 200 },
      { label: 'agent-c', currentTool: 'Exec', currentToolDisplay: 'Running command', isCompleted: false, inputTokens: 50, outputTokens: 10 }
    ]
    s().onSubagentProgress(second)
    expect(s().subAgentEntries).toHaveLength(2)
    expect(s().subAgentEntries[0].label).toBe('agent-a')
    expect(s().subAgentEntries[0].isCompleted).toBe(true)
    expect(s().subAgentEntries[1].label).toBe('agent-c')
    // agent-b should be gone — replaced, not merged
    expect(s().subAgentEntries.find((e) => e.label === 'agent-b')).toBeUndefined()
  })

  it('resets to empty on onSubagentProgress with empty array', () => {
    s().onSubagentProgress([{ label: 'x', currentTool: null, currentToolDisplay: null, isCompleted: false, inputTokens: 0, outputTokens: 0 }])
    expect(s().subAgentEntries).toHaveLength(1)

    s().onSubagentProgress([])
    expect(s().subAgentEntries).toHaveLength(0)
  })
})

describe('revertFile / reapplyFile', () => {
  it('revertFile marks a single file as reverted', () => {
    s().upsertChangedFile({
      filePath: 'src/a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 5,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    s().upsertChangedFile({
      filePath: 'src/b.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 2,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })

    s().revertFile('src/a.ts')

    expect(s().changedFiles.get('src/a.ts')?.status).toBe('reverted')
    // b.ts untouched
    expect(s().changedFiles.get('src/b.ts')?.status).toBe('written')
  })

  it('reapplyFile sets a reverted file back to written', () => {
    s().upsertChangedFile({
      filePath: 'src/a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 5,
      deletions: 0,
      diffHunks: [],
      status: 'reverted',
      isNewFile: false
    })

    s().reapplyFile('src/a.ts')

    expect(s().changedFiles.get('src/a.ts')?.status).toBe('written')
  })

  it('revertFile does nothing for unknown file paths', () => {
    expect(() => s().revertFile('nonexistent.ts')).not.toThrow()
  })
})

describe('onPlanUpdated', () => {
  it('replaces plan state with the new plan', () => {
    expect(s().plan).toBeNull()

    s().onPlanUpdated({
      title: 'My Plan',
      overview: 'Build something cool',
      content: '# Full Plan\n\nBody text',
      todos: [
        { id: '1', content: 'Step 1', status: 'completed' },
        { id: '2', content: 'Step 2', status: 'in_progress' }
      ]
    })

    const plan = s().plan
    expect(plan).not.toBeNull()
    expect(plan?.title).toBe('My Plan')
    expect(plan?.overview).toBe('Build something cool')
    expect(plan?.content).toBe('# Full Plan\n\nBody text')
    expect(plan?.todos).toHaveLength(2)
    expect(plan?.todos[0].status).toBe('completed')
    expect(plan?.todos[1].status).toBe('in_progress')
  })

  it('replaces old plan on subsequent updates', () => {
    s().onPlanUpdated({ title: 'Old Plan', overview: '', content: 'Old content', todos: [] })
    s().onPlanUpdated({ title: 'New Plan', overview: 'Updated', content: 'New content', todos: [] })

    expect(s().plan?.title).toBe('New Plan')
    expect(s().plan?.content).toBe('New content')
  })

  it('falls back to empty content when plan/updated does not include content', () => {
    s().onPlanUpdated({ title: 'Legacy Plan', overview: 'Legacy payload', todos: [] })

    expect(s().plan?.content).toBe('')
  })

  it('reset() clears the plan', () => {
    s().onPlanUpdated({ title: 'Some Plan', overview: '', content: '', todos: [] })
    expect(s().plan).not.toBeNull()

    s().reset()
    expect(s().plan).toBeNull()
  })
})

describe('revertFilesForTurn', () => {
  it('marks all files in the given turn as reverted', () => {
    s().onTurnStarted(makeTurn())
    s().upsertChangedFile({
      filePath: 'src/a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 5,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    s().upsertChangedFile({
      filePath: 'src/b.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 3,
      deletions: 1,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    // Another turn's file should not be affected
    s().upsertChangedFile({
      filePath: 'src/c.ts',
      turnId: 'turn-2',
      turnIds: ['turn-2'],
      additions: 1,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: true
    })

    s().revertFilesForTurn('turn-1')

    expect(s().changedFiles.get('src/a.ts')?.status).toBe('reverted')
    expect(s().changedFiles.get('src/b.ts')?.status).toBe('reverted')
    // turn-2's file is unaffected
    expect(s().changedFiles.get('src/c.ts')?.status).toBe('written')
  })

  it('does nothing when no files match the given turnId', () => {
    s().upsertChangedFile({
      filePath: 'src/x.ts',
      turnId: 'turn-99',
      turnIds: ['turn-99'],
      additions: 1,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })

    s().revertFilesForTurn('turn-1') // different turn
    expect(s().changedFiles.get('src/x.ts')?.status).toBe('written')
  })

  it('matches revertFilesForTurn when turnIds includes an earlier turn', () => {
    s().upsertChangedFile({
      filePath: 'src/multi.ts',
      turnId: 'turn-2',
      turnIds: ['turn-1', 'turn-2'],
      additions: 1,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    s().revertFilesForTurn('turn-1')
    expect(s().changedFiles.get('src/multi.ts')?.status).toBe('reverted')
  })
})

describe('changedFiles persistence across turns', () => {
  it('does not clear changedFiles on onTurnStarted', () => {
    s().upsertChangedFile({
      filePath: 'keep.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 1,
      deletions: 0,
      diffHunks: [],
      status: 'written',
      isNewFile: true
    })
    s().onTurnStarted(makeTurn({ id: 'turn-2' }))
    expect(s().changedFiles.get('keep.ts')).toBeDefined()
  })
})

describe('tool item ordering by createdAt', () => {
  it('keeps toolCall items sorted after onItemStarted', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'late',
        type: 'toolCall',
        createdAt: '2025-01-02T00:00:00.000Z',
        payload: { callId: 'c-late', toolName: 'ReadFile', arguments: { path: 'a.ts' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'early',
        type: 'toolCall',
        createdAt: '2025-01-01T00:00:00.000Z',
        payload: { callId: 'c-early', toolName: 'ReadFile', arguments: { path: 'b.ts' } }
      }
    })
    const tools = s().turns[0].items.filter((i) => i.type === 'toolCall')
    expect(tools.map((t) => t.id)).toEqual(['early', 'late'])
  })

  it('does not duplicate replayed toolCall or commandExecution item starts', () => {
    s().onTurnStarted(makeTurn())
    const toolItem = {
      id: 'tool-1',
      type: 'toolCall',
      createdAt: '2025-01-01T00:00:00.000Z',
      payload: { callId: 'c-1', toolName: 'ReadFile', arguments: { path: 'a.ts' } }
    }
    const commandItem = {
      id: 'cmd-1',
      type: 'commandExecution',
      createdAt: '2025-01-01T00:00:01.000Z',
      payload: {
        callId: 'c-cmd',
        command: 'npm test',
        status: 'inProgress',
        aggregatedOutput: ''
      }
    }

    s().onItemStarted({ turnId: 'turn-1', item: toolItem })
    s().onItemStarted({ turnId: 'turn-1', item: toolItem })
    s().onItemStarted({ turnId: 'turn-1', item: commandItem })
    s().onItemStarted({ turnId: 'turn-1', item: commandItem })

    expect(s().turns[0].items.filter((i) => i.id === 'tool-1')).toHaveLength(1)
    expect(s().turns[0].items.filter((i) => i.id === 'cmd-1')).toHaveLength(1)
  })

  it('keeps agentMessage placeholder before toolCall when tool starts after message streaming', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'msg-1',
        type: 'agentMessage',
        createdAt: '2025-01-01T00:00:00.000Z'
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-1',
        type: 'toolCall',
        createdAt: '2025-01-02T00:00:00.000Z',
        payload: { callId: 'c-1', toolName: 'ReadFile', arguments: { path: 'a.ts' } }
      }
    })
    expect(s().turns[0].items.map((i) => i.id)).toEqual(['msg-1', 'tool-1'])
    expect(s().turns[0].items[0].type).toBe('agentMessage')
    expect(s().turns[0].items[0].status).toBe('streaming')
  })
})

describe('pluginFunctionCall items', () => {
  it('stores and completes pluginFunctionCall items without a toolResult companion', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'plugin-tool-1',
        type: 'pluginFunctionCall',
        createdAt: '2025-01-01T00:00:00.000Z',
        payload: {
          pluginId: 'browser',
          namespace: 'node_repl',
          callId: 'plugin-call-1',
          functionName: 'NodeReplJs',
          arguments: { code: 'console.log(1)' }
        }
      }
    })

    let item = s().turns[0].items.find((i) => i.id === 'plugin-tool-1')
    expect(item?.type).toBe('pluginFunctionCall')
    expect(item?.toolName).toBe('NodeReplJs')
    expect(item?.toolCallId).toBe('plugin-call-1')
    expect(item?.pluginId).toBe('browser')
    expect(item?.pluginNamespace).toBe('node_repl')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'plugin-tool-1',
        type: 'pluginFunctionCall',
        completedAt: '2025-01-01T00:00:01.000Z',
        payload: {
          pluginId: 'browser',
          namespace: 'node_repl',
          callId: 'plugin-call-1',
          functionName: 'NodeReplJs',
          arguments: { code: 'console.log(1)' },
          contentItems: [
            { type: 'text', text: '1' },
            { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
          ],
          success: true
        }
      }
    })

    item = s().turns[0].items.find((i) => i.id === 'plugin-tool-1')
    expect(item?.status).toBe('completed')
    expect(item?.result).toBe('1')
    expect(item?.success).toBe(true)
    expect(item?.contentItems?.[1]).toEqual({
      type: 'image',
      mediaType: 'image/png',
      dataBase64: 'abc123'
    })
    expect(s().turns[0].items.some((i) => i.type === 'toolResult')).toBe(false)
  })

  it('stores and completes dynamicToolCall items without a toolResult companion', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'dynamic-tool-1',
        type: 'dynamicToolCall',
        createdAt: '2025-01-01T00:00:00.000Z',
        payload: {
          namespace: 'oratorio',
          callId: 'dynamic-call-1',
          toolName: 'ListBoardItems',
          arguments: { status: 'todo' }
        }
      }
    })

    let item = s().turns[0].items.find((i) => i.id === 'dynamic-tool-1')
    expect(item?.type).toBe('dynamicToolCall')
    expect(item?.toolName).toBe('ListBoardItems')
    expect(item?.toolCallId).toBe('dynamic-call-1')
    expect(item?.pluginNamespace).toBe('oratorio')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'dynamic-tool-1',
        type: 'dynamicToolCall',
        completedAt: '2025-01-01T00:00:01.000Z',
        payload: {
          namespace: 'oratorio',
          callId: 'dynamic-call-1',
          toolName: 'ListBoardItems',
          arguments: { status: 'todo' },
          contentItems: [
            { type: 'text', text: '2 board items' },
            { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
          ],
          success: true
        }
      }
    })

    item = s().turns[0].items.find((i) => i.id === 'dynamic-tool-1')
    expect(item?.status).toBe('completed')
    expect(item?.result).toBe('2 board items')
    expect(item?.success).toBe(true)
    expect(item?.contentItems?.[1]).toEqual({
      type: 'image',
      mediaType: 'image/png',
      dataBase64: 'abc123'
    })
    expect(s().turns[0].items.some((i) => i.type === 'toolResult')).toBe(false)
  })
})

describe('itemDiffs per tool call', () => {
  it('setTurns stores per-item incremental diffs and cumulative changedFiles', () => {
    const turn: ConversationTurn = {
      id: 'turn-h',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: new Date().toISOString(),
      items: [
        {
          id: 'tc-a',
          type: 'toolCall',
          status: 'completed',
          toolName: 'EditFile',
          toolCallId: 'call-a',
          arguments: { path: 'src/a.ts', oldText: 'alpha', newText: 'beta' },
          createdAt: '2025-01-01T00:00:01.000Z'
        },
        {
          id: 'tr-a',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'call-a',
          result: 'Successfully edited src/a.ts',
          success: true,
          createdAt: '2025-01-01T00:00:02.000Z',
          completedAt: '2025-01-01T00:00:02.000Z'
        },
        {
          id: 'tc-b',
          type: 'toolCall',
          status: 'completed',
          toolName: 'EditFile',
          toolCallId: 'call-b',
          arguments: { path: 'src/a.ts', oldText: 'beta', newText: 'gamma' },
          createdAt: '2025-01-01T00:00:03.000Z'
        },
        {
          id: 'tr-b',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'call-b',
          result: 'Successfully edited src/a.ts',
          success: true,
          createdAt: '2025-01-01T00:00:04.000Z',
          completedAt: '2025-01-01T00:00:04.000Z'
        }
      ]
    }
    s().setTurns([turn])
    const itemDiffs = s().itemDiffs
    expect(itemDiffs.size).toBe(2)
    expect(itemDiffs.get('tc-a')?.additions).toBe(1)
    expect(itemDiffs.get('tc-a')?.deletions).toBe(1)
    expect(itemDiffs.get('tc-b')?.additions).toBe(1)
    expect(itemDiffs.get('tc-b')?.deletions).toBe(1)
    expect(itemDiffs.get('tc-a')?.diffHunks).not.toEqual(itemDiffs.get('tc-b')?.diffHunks)

    const cum = s().changedFiles.get('src/a.ts')
    expect(cum).toBeDefined()
    expect(cum?.currentContent).toBe('gamma')
    expect(cum?.originalContent).toBe('alpha')
  })

  it('setTurns skips local file diffs for remote workspaces', () => {
    s().setRemoteWorkspaceActive(true)
    const turn: ConversationTurn = {
      id: 'turn-remote',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: new Date().toISOString(),
      items: [
        {
          id: 'tc-a',
          type: 'toolCall',
          status: 'completed',
          toolName: 'EditFile',
          toolCallId: 'call-a',
          arguments: { path: 'src/a.ts', oldText: 'alpha', newText: 'beta' },
          createdAt: '2025-01-01T00:00:01.000Z'
        },
        {
          id: 'tr-a',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'call-a',
          result: 'Successfully edited src/a.ts',
          success: true,
          createdAt: '2025-01-01T00:00:02.000Z',
          completedAt: '2025-01-01T00:00:02.000Z'
        }
      ]
    }

    s().setTurns([turn])

    expect(s().remoteWorkspaceActive).toBe(true)
    expect(s().changedFiles.size).toBe(0)
    expect(s().itemDiffs.size).toBe(0)
  })

  it('onItemCompleted toolResult stores distinct per-item diffs for two EditFile calls', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'edit-1',
        type: 'toolCall',
        createdAt: '2025-01-01T00:00:01.000Z',
        payload: {
          callId: 'c1',
          toolName: 'EditFile',
          arguments: { path: 'x.ts', oldText: 'A', newText: 'B' }
        }
      }
    })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        type: 'toolResult',
        callId: 'c1',
        result: 'Successfully edited x.ts',
        success: true
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'edit-2',
        type: 'toolCall',
        createdAt: '2025-01-01T00:00:02.000Z',
        payload: {
          callId: 'c2',
          toolName: 'EditFile',
          arguments: { path: 'x.ts', oldText: 'B', newText: 'C' }
        }
      }
    })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        type: 'toolResult',
        callId: 'c2',
        result: 'Successfully edited x.ts',
        success: true
      }
    })
    const ids = s().itemDiffs
    expect(ids.size).toBe(2)
    expect(ids.get('edit-1')?.diffHunks).not.toEqual(ids.get('edit-2')?.diffHunks)
  })

  it('reset clears itemDiffs', () => {
    s().setTurns([
      {
        id: 'turn-h',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        items: [
          {
            id: 'tc-a',
            type: 'toolCall',
            status: 'completed',
            toolName: 'EditFile',
            toolCallId: 'call-a',
            arguments: { path: 'src/a.ts', oldText: 'a', newText: 'b' },
            createdAt: '2025-01-01T00:00:01.000Z'
          },
          {
            id: 'tr-a',
            type: 'toolResult',
            status: 'completed',
            toolCallId: 'call-a',
            result: 'Successfully edited src/a.ts',
            success: true,
            createdAt: '2025-01-01T00:00:02.000Z',
            completedAt: '2025-01-01T00:00:02.000Z'
          }
        ]
      }
    ])
    expect(s().itemDiffs.size).toBeGreaterThan(0)
    s().reset()
    expect(s().itemDiffs.size).toBe(0)
  })
})
