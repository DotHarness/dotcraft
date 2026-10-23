import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import {
  selectCapacityRetryDelaySeconds,
  selectLatestCreatePlanTurnId,
  useConversationStore
} from '../stores/conversationStore'
import { getStreamingToolDisplay } from '../utils/streamingToolDisplay'
import { isToolItemLive } from '../utils/toolCallAggregation'
import { coreToolFields, streamingToolCallStarted } from './testToolPresentation'

const s = () => useConversationStore.getState()

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

describe('selectLatestCreatePlanTurnId', () => {
  it('keeps the latest completed plan pending when cleanup tools follow CreatePlan', () => {
    s().setTurns([{
      id: 'turn-plan',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-07-31T13:55:00.000Z',
      completedAt: '2026-07-31T13:56:00.000Z',
      items: [
        {
          id: 'item-plan',
          type: 'toolCall',
          status: 'completed',
          toolName: 'CreatePlan', ...coreToolFields('CreatePlan'),
          toolCallId: 'call-plan',
          success: true
        },
        {
          id: 'item-close',
          type: 'toolCall',
          status: 'completed',
          toolName: 'ListAgents', ...coreToolFields('ListAgents'),
          toolCallId: 'call-close',
          success: true
        },
        {
          id: 'item-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'Plan ready.'
        }
      ]
    }])

    expect(selectLatestCreatePlanTurnId(s())).toBe('turn-plan')
  })

  it('does not revive a plan from an older turn after a later turn completes', () => {
    s().setTurns([
      {
        id: 'turn-plan',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-07-31T13:55:00.000Z',
        items: [{
          id: 'item-plan',
          type: 'toolCall',
          status: 'completed',
          toolName: 'CreatePlan', ...coreToolFields('CreatePlan'),
          toolCallId: 'call-plan',
          success: true
        }]
      },
      {
        id: 'turn-follow-up',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-07-31T13:57:00.000Z',
        items: [{
          id: 'item-follow-up',
          type: 'agentMessage',
          status: 'completed',
          text: 'Updated without creating another plan.'
        }]
      }
    ])

    expect(selectLatestCreatePlanTurnId(s())).toBeNull()
  })

  it('ignores failed CreatePlan calls in the latest turn', () => {
    s().setTurns([{
      id: 'turn-failed-plan',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-07-31T13:55:00.000Z',
      items: [{
        id: 'item-plan',
        type: 'toolCall',
        status: 'completed',
        toolName: 'CreatePlan', ...coreToolFields('CreatePlan'),
        toolCallId: 'call-plan',
        success: false
      }]
    }])

    expect(selectLatestCreatePlanTurnId(s())).toBeNull()
  })
})

describe('maintenance state', () => {
  it('tracks only thread-level compaction as maintenance and labels it by scope', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('compacting', { turnId: 'turn-1' })
    expect(s().maintenanceKind).toBeNull()
    expect(s().systemLabel).toBe('systemStatus.compacting')

    s().onSystemEvent('compacting', { turnId: null })
    expect(s().maintenanceKind).toBe('compacting')
    expect(s().systemLabel).toBe('systemStatus.compacting.manual')

    s().onSystemEvent('compactCancelled')
    expect(s().maintenanceKind).toBeNull()

    for (const terminal of ['compacted', 'compactFailed', 'unknown-event-xyz']) {
      s().onSystemEvent('compacting')
      s().onSystemEvent(terminal)
      expect(s().systemLabel, terminal).toBe(terminal === 'unknown-event-xyz' ? 'systemStatus.compacting.manual' : null)
      s().onSystemEvent('compacted')
    }
  })

  it('clears only maintenance-derived labels when runtime maintenance ends', () => {
    s().setMaintenanceKind('compacting')
    expect(s().systemLabel).toBe('systemStatus.compacting.manual')
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
  it('onAgentMessageDelta accumulates into streamingMessage', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-05-22T10:00:00.000Z'))
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    const firstDeltaAt = Date.now()
    s().onAgentMessageDelta('Hello')
    expect(s().streamingMessageLastDeltaAt).toBeNull()
    vi.advanceTimersByTime(16)
    expect(s().streamingMessageLastDeltaAt).toBe(firstDeltaAt)
    vi.setSystemTime(new Date('2026-05-22T10:00:01.000Z'))
    const secondDeltaAt = Date.now()
    s().onAgentMessageDelta(', world')
    vi.advanceTimersByTime(16)

    expect(s().streamingMessage).toBe('Hello, world')
    expect(s().streamingMessageLastDeltaAt).toBe(secondDeltaAt)
  })

  it('onItemCompleted upserts repeated error notifications by item id', () => {
    s().onTurnStarted(makeTurn())
    const errorItem = {
      id: 'error-1',
      type: 'error',
      payload: {
        message: 'Namespace resolution failed.',
        code: 'agent_error',
        fatal: true
      },
      createdAt: '2026-07-15T00:00:00.000Z',
      completedAt: '2026-07-15T00:00:01.000Z'
    }

    s().onItemCompleted({ turnId: 'turn-1', item: errorItem })
    s().onItemCompleted({ turnId: 'turn-1', item: errorItem })

    expect(s().turns[0].items.filter((item) => item.id === 'error-1')).toHaveLength(1)
    expect(s().turns[0].items[0].text).toBe('Namespace resolution failed.')
  })

  it('onItemStarted/onItemCompleted keeps live imageGeneration output with following assistant text', () => {
    s().onTurnStarted(makeTurn())

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'image-generation-1',
        type: 'imageGeneration',
        status: 'started',
        payload: {
          callId: 'ig_live',
          status: 'inProgress',
          mediaType: 'image/png'
        },
        createdAt: '2026-07-08T04:31:00.000Z'
      }
    })

    let imageItem = s().turns[0].items.find((item) => item.id === 'image-generation-1')
    expect(imageItem?.type).toBe('imageGeneration')
    expect(imageItem?.status).toBe('started')
    expect(imageItem?.imageGenerationStatus).toBe('inProgress')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'image-generation-1',
        type: 'imageGeneration',
        status: 'completed',
        payload: {
          callId: 'ig_live',
          status: 'completed',
          revisedPrompt: 'A bright plaza',
          result: 'AQID',
          mediaType: 'image/png',
          savedPath: '<workspace>/.craft/generated_images/thread/ig_live.png'
        },
        createdAt: '2026-07-08T04:31:00.000Z',
        completedAt: '2026-07-08T04:31:02.000Z'
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'agent-message-1',
        type: 'agentMessage',
        createdAt: '2026-07-08T04:31:03.000Z'
      }
    })
    s().onAgentMessageDelta('Generated image.')
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'agent-message-1',
        type: 'agentMessage',
        text: 'Generated image.',
        completedAt: '2026-07-08T04:31:04.000Z'
      }
    })

    const items = s().turns[0].items
    expect(items.map((item) => item.id)).toEqual(['image-generation-1', 'agent-message-1'])
    imageItem = items.find((item) => item.id === 'image-generation-1')
    expect(imageItem?.status).toBe('completed')
    expect(imageItem?.imageGenerationStatus).toBe('completed')
    expect(imageItem?.result).toBe('AQID')
    expect(imageItem?.mediaType).toBe('image/png')
    expect(imageItem?.savedPath).toBe('<workspace>/.craft/generated_images/thread/ig_live.png')
    expect(items.find((item) => item.id === 'agent-message-1')?.text).toBe('Generated image.')
  })

  it('onItemCompleted creates imageGeneration output before start and ignores stale start', () => {
    s().onTurnStarted(makeTurn())

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'image-generation-early',
        type: 'ImageGeneration',
        status: 'completed',
        payload: {
          callId: 'ig_early',
          status: 'completed',
          result: 'BAUG',
          mediaType: 'image/png'
        },
        createdAt: '2026-07-08T04:31:00.000Z',
        completedAt: '2026-07-08T04:31:02.000Z'
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'image-generation-early',
        type: 'ImageGeneration',
        status: 'started',
        payload: {
          callId: 'ig_early',
          status: 'inProgress',
          mediaType: 'image/png'
        },
        createdAt: '2026-07-08T04:31:00.000Z'
      }
    })

    const images = s().turns[0].items.filter((item) => item.id === 'image-generation-early')
    expect(images).toHaveLength(1)
    expect(images[0].type).toBe('imageGeneration')
    expect(images[0].status).toBe('completed')
    expect(images[0].imageGenerationStatus).toBe('completed')
    expect(images[0].result).toBe('BAUG')
  })

  it('onItemStarted/onItemCompleted appends guidance userMessage without duplicating it', () => {
    s().onTurnStarted(makeTurn({
      items: [
        {
          id: 'user-initial',
          type: 'userMessage',
          status: 'completed',
          payload: { text: 'initial request', clientUserMessageId: 'initial' },
          createdAt: '2026-04-25T10:00:00.000Z',
          completedAt: '2026-04-25T10:00:00.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          payload: { toolName: 'ReadFile', ...coreToolFields('ReadFile'), callId: 'call-1', arguments: { path: 'a.txt' } },
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

  it('reconciles repeated initial userMessage events by submission identity', () => {
    s().onTurnStarted(makeTurn({
      items: [
        {
          id: 'local-user',
          type: 'userMessage',
          status: 'completed',
          payload: { text: 'initial request', clientUserMessageId: 'initial' },
          createdAt: '2026-04-25T10:00:00.000Z',
          completedAt: '2026-04-25T10:00:00.000Z'
        }
      ]
    }))

    const serverInitialUser = {
      id: 'server-user',
      type: 'userMessage',
      status: 'completed',
      payload: { text: 'initial request', clientUserMessageId: 'initial' },
      createdAt: '2026-04-25T10:00:00.000Z',
      completedAt: '2026-04-25T10:00:00.000Z'
    }
    s().onItemStarted({ turnId: 'turn-1', item: serverInitialUser })
    s().onItemCompleted({ turnId: 'turn-1', item: serverInitialUser })

    expect(s().turns[0].items.map((i) => i.id)).toEqual(['server-user'])
  })

  it('onItemStarted/onCommandExecutionDelta/onItemCompleted track command execution output', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-1',
        type: 'commandExecution',
        status: 'started',
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
    expect(s().turns[0].items[0].executionStatus).toBe('inProgress')

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

  it('merges remote transfer progress into its tool call', () => {
    s().setTurns([{
      id: 'turn-transfer',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-09-11T00:00:00Z',
      items: [
        {
          id: 'tool-transfer',
          type: 'toolCall',
          status: 'running',
          toolName: 'RemoteToolHost.Transfer',
          toolCallId: 'call-transfer',
          createdAt: '2026-09-11T00:00:00Z'
        },
        {
          id: 'execution-transfer',
          type: 'toolExecution',
          status: 'running',
          toolName: 'RemoteToolHost.Transfer',
          toolCallId: 'call-transfer',
          createdAt: '2026-09-11T00:00:00Z'
        }
      ]
    }])
    const progress = {
      kind: 'remoteFileTransfer' as const,
      stage: 'transferring' as const,
      direction: 'upload' as const,
      localPath: 'C:/tools/checker',
      remotePath: 'D:/tools/checker',
      hostId: 'host-1',
      hostDisplayName: 'B-Laptop',
      transferredBytes: 512,
      completedFiles: 0,
      completedBytes: 0,
      totalBytes: 1024,
      totalFiles: 1,
      currentFile: 'checker.exe'
    }

    s().onToolExecutionProgress({
      turnId: 'turn-transfer',
      itemId: 'execution-transfer',
      callId: 'call-transfer',
      progress
    })

    expect(s().turns[0].items.find((entry) => entry.id === 'tool-transfer')?.transferProgress)
      .toEqual(progress)

    s().onItemCompleted({
      turnId: 'turn-transfer',
      item: {
        id: 'execution-transfer',
        type: 'toolExecution',
        completedAt: '2026-09-11T00:00:01Z',
        payload: {
          callId: 'call-transfer',
          toolName: 'RemoteToolHost.Transfer',
          status: 'completed',
          success: true,
          durationMs: 1000,
          resultPreview: '{"success":true,"completedFiles":1}'
        }
      }
    })

    expect(s().turns[0].items.find((entry) => entry.id === 'tool-transfer')?.transferProgress)
      .toBeUndefined()
  })

  it('applies terminal output that arrives before the matching Exec toolCall item', () => {
    vi.useFakeTimers()
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
          toolName: 'Exec', ...coreToolFields('Exec'),
          arguments: { command: 'ping -n 4 10.8.8.8' }
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-early')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBeUndefined()
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.command).toBe('ping -n 4 10.8.8.8')
    vi.advanceTimersByTime(50)
    expect(s().shellRuntimeByCallId.get('exec-terminal-early')?.output).toBe('Pinging 10.8.8.8\n')
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
          toolName: 'Exec', ...coreToolFields('Exec'),
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
          toolName: 'Exec', ...coreToolFields('Exec'),
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
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-terminal-snapshot',
        type: 'toolCall',
        payload: {
          callId: 'exec-terminal-snapshot',
          toolName: 'Exec', ...coreToolFields('Exec'),
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
    vi.advanceTimersByTime(50)

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-snapshot')
    expect(toolItem?.aggregatedOutput).toBeUndefined()
    expect(s().shellRuntimeByCallId.get('exec-terminal-snapshot')).toEqual({
      source: 'terminal',
      output: 'line 1\nline 2\n'
    })
  })

  it('batches high-frequency shell output without replacing the durable turn tree', () => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-batched',
        type: 'toolCall',
        payload: { callId: 'exec-batched', toolName: 'Exec', ...coreToolFields('Exec'), arguments: { command: 'many-lines' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-batched',
        type: 'commandExecution',
        payload: { callId: 'exec-batched', command: 'many-lines', status: 'inProgress', aggregatedOutput: '' }
      }
    })
    const durableTurn = s().turns[0]

    for (let index = 0; index < 100; index++) {
      s().onCommandExecutionDelta({
        turnId: 'turn-1',
        itemId: 'cmd-batched',
        delta: `${index}\n`
      })
    }

    expect(s().turns[0]).toBe(durableTurn)
    expect(s().shellRuntimeByCallId.has('exec-batched')).toBe(false)
    vi.advanceTimersByTime(49)
    expect(s().shellRuntimeByCallId.has('exec-batched')).toBe(false)
    vi.advanceTimersByTime(1)
    expect(s().turns[0]).toBe(durableTurn)
    expect(s().shellRuntimeByCallId.get('exec-batched')?.output).toContain('0\n1\n')
    expect(s().shellRuntimeByCallId.get('exec-batched')?.output).toContain('99\n')
  })

  it('prefers terminal output, isolates parallel calls, and cancels pending flushes on reset', () => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    for (const callId of ['exec-a', 'exec-b']) {
      s().onItemStarted({
        turnId: 'turn-1',
        item: {
          id: `tool-${callId}`,
          type: 'toolCall',
          payload: { callId, toolName: 'Exec', ...coreToolFields('Exec'), arguments: { command: callId } }
        }
      })
      s().onItemStarted({
        turnId: 'turn-1',
        item: {
          id: `cmd-${callId}`,
          type: 'commandExecution',
          payload: { callId, command: callId, status: 'inProgress', aggregatedOutput: '' }
        }
      })
    }
    s().onCommandExecutionDelta({ turnId: 'turn-1', itemId: 'cmd-exec-a', delta: 'compat-a\n' })
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: { turnId: 'turn-1', callId: 'exec-a', status: 'running', output: 'terminal-a\n' },
      delta: 'terminal-a\n'
    })
    s().onCommandExecutionDelta({ turnId: 'turn-1', itemId: 'cmd-exec-b', delta: 'compat-b\n' })
    vi.advanceTimersByTime(50)

    expect(s().shellRuntimeByCallId.get('exec-a')).toEqual({
      source: 'terminal',
      output: 'terminal-a\n'
    })
    expect(s().shellRuntimeByCallId.get('exec-b')).toEqual({
      source: 'commandExecution',
      output: 'compat-b\n'
    })

    s().onCommandExecutionDelta({ turnId: 'turn-1', itemId: 'cmd-exec-b', delta: 'late\n' })
    s().reset()
    vi.advanceTimersByTime(100)
    expect(s().shellRuntimeByCallId.size).toBe(0)
  })

  it.each([undefined, 'runInBackground'])('keeps a shell card live after its tool returns while the command still runs (%s)', (backgroundReason) => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-handed-back',
        type: 'toolCall',
        payload: { callId: 'exec-handed-back', toolName: 'Exec', ...coreToolFields('Exec'), arguments: { command: 'npm test' } }
      }
    })
    const terminal = { threadId: 'thread-1', turnId: 'turn-1', callId: 'exec-handed-back', backgroundReason }
    s().onTerminalEvent({ event: 'terminal/started', terminal: { ...terminal, status: 'running' } })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'execution-handed-back',
        type: 'toolExecution',
        payload: {
          callId: 'exec-handed-back',
          toolName: 'Exec', ...coreToolFields('Exec'),
          status: 'completed',
          success: true,
          resultPreview: 'Status: Running'
        }
      }
    })

    const handedBack = () => s().turns[0].items.find((i) => i.id === 'tool-handed-back')!
    expect(isToolItemLive(handedBack())).toBe(true)
    s().onTerminalEvent({
      event: 'terminal/outputDelta',
      terminal: { ...terminal, status: 'running' },
      delta: 'after hand-back\n'
    })
    vi.advanceTimersByTime(50)
    expect(s().shellRuntimeByCallId.get('exec-handed-back')?.output).toBe('after hand-back\n')

    s().onTerminalEvent({
      event: 'terminal/completed',
      terminal: { ...terminal, status: 'completed', exitCode: 0, output: 'after hand-back\ndone\n' }
    })
    expect(isToolItemLive(handedBack())).toBe(false)
    expect(handedBack().aggregatedOutput).toBe('after hand-back\ndone\n')
  })

  it('attaches terminal interaction input to the command card it was written to', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-interactive',
        type: 'toolCall',
        payload: { callId: 'exec-interactive', toolName: 'Exec', ...coreToolFields('Exec'), arguments: { command: 'npm init' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-interactive',
        type: 'commandExecution',
        payload: { callId: 'exec-interactive', command: 'npm init', status: 'inProgress', aggregatedOutput: '' }
      }
    })

    s().onTerminalInteraction({ turnId: 'turn-1', itemId: 'cmd-interactive', stdin: '' })
    s().onTerminalInteraction({ turnId: 'turn-1', itemId: 'cmd-interactive', stdin: 'my-app' })
    s().onTerminalInteraction({ turnId: 'turn-1', itemId: 'cmd-interactive', stdin: '\n' })

    const card = s().turns[0].items.find((i) => i.id === 'tool-interactive')
    expect(card?.terminalInputs).toEqual(['my-app'])
  })

  it('does not copy pending running terminal output into a restored durable turn', () => {
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
            toolName: 'Exec', ...coreToolFields('Exec'),
            arguments: { command: 'npm test' },
            createdAt: '2026-06-01T10:00:00.000Z'
          }
        ]
      })
    ])

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-terminal-setturns')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBeUndefined()
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
          toolName: 'SendMessage', ...coreToolFields('SendMessage'),
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
          toolName: 'SendMessage', ...coreToolFields('SendMessage'),
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

  it('uses failed toolExecution errorMessage as the matching toolCall preview', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'tool-strict-header',
        type: 'toolCall',
        payload: {
          callId: 'strict-header-1',
          toolName: 'local_strict_header_probe',
          arguments: {}
        }
      }
    })

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'execution-strict-header',
        type: 'toolExecution',
        completedAt: '2026-06-16T10:00:02.000Z',
        payload: {
          callId: 'strict-header-1',
          toolName: 'local_strict_header_probe',
          status: 'failed',
          success: false,
          durationMs: 80,
          resultPreview: 'Error: Function failed.',
          errorMessage: "mcp-method: expected 'tools/call', got None"
        }
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool-strict-header')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.executionStatus).toBe('failed')
    expect(toolItem?.success).toBe(false)
    expect(toolItem?.errorMessage).toBe("mcp-method: expected 'tools/call', got None")
    expect(toolItem?.resultPreview).toBe("mcp-method: expected 'tools/call', got None")
    expect(toolItem?.result).toBe("mcp-method: expected 'tools/call', got None")
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
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
          contentItems: [
            { type: 'text', text: 'Image: notes.png (3 bytes, image/png)' },
            { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
          ],
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
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
    expect(toolItem?.contentItems?.[1]).toEqual({
      type: 'image',
      mediaType: 'image/png',
      dataBase64: 'AQID'
    })
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
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
              toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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

  it('preserves a same-thread running turn when a stale empty thread/read snapshot arrives', () => {
    s().onTurnStarted(makeTurn({
      id: 'turn-builder-live',
      threadId: 'builder-thread',
      status: 'running'
    }))
    s().onItemStarted({
      turnId: 'turn-builder-live',
      item: {
        id: 'agent-streaming',
        type: 'agentMessage',
        status: 'streaming',
        createdAt: '2026-04-25T10:00:01.000Z'
      }
    })
    s().onAgentMessageDelta('Designing')

    s().setTurns([], {
      preserveExistingRealtime: true,
      realtimeScopeThreadId: 'builder-thread'
    })

    const state = s()
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn-builder-live')
    expect(state.turnStatus).toBe('running')
    expect(state.activeTurnId).toBe('turn-builder-live')
    expect(state.activeItemId).toBe('agent-streaming')
    expect(state.streamingMessage).toBe('Designing')
  })

  it('does not preserve a running turn for a stale empty snapshot from a different thread', () => {
    s().onTurnStarted(makeTurn({
      id: 'turn-other-live',
      threadId: 'other-thread',
      status: 'running'
    }))

    s().setTurns([], {
      preserveExistingRealtime: true,
      realtimeScopeThreadId: 'builder-thread'
    })

    const state = s()
    expect(state.turns).toHaveLength(0)
    expect(state.turnStatus).toBe('idle')
    expect(state.activeTurnId).toBeNull()
  })

  it('does not resurrect waitingApproval from a stale snapshot after its request was resolved', () => {
    s().onTurnStarted(makeTurn())
    s().onApprovalRequest('bridge-stale-approval', {
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'request-stale-approval',
      itemId: 'approval-stale-approval',
      approvalType: 'shell',
      operation: 'npm test'
    })
    s().onApprovalDecision('accept')

    s().setTurns([makeTurn({ status: 'waitingApproval' })], {
      preserveExistingRealtime: true,
      realtimeScopeThreadId: 'thread-1'
    })

    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
  })

  it('restores waitingApproval when the actionable request is still present', () => {
    s().onTurnStarted(makeTurn())
    s().onApprovalRequest('bridge-live-approval', {
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'request-live-approval',
      itemId: 'approval-live-approval',
      approvalType: 'shell',
      operation: 'npm test'
    })

    s().setTurns([makeTurn({ status: 'waitingApproval' })], {
      preserveExistingRealtime: true,
      realtimeScopeThreadId: 'thread-1'
    })

    expect(s().pendingApproval?.requestId).toBe('request-live-approval')
    expect(s().turnStatus).toBe('waitingApproval')
  })

  it('does not preserve terminal history over a same-thread empty snapshot', () => {
    s().setTurns([
      makeTurn({
        id: 'turn-builder-done',
        threadId: 'builder-thread',
        status: 'completed',
        completedAt: '2026-04-25T10:00:02.000Z'
      })
    ])

    s().setTurns([], {
      preserveExistingRealtime: true,
      realtimeScopeThreadId: 'builder-thread'
    })

    const state = s()
    expect(state.turns).toHaveLength(0)
    expect(state.turnStatus).toBe('idle')
    expect(state.activeTurnId).toBeNull()
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
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
              toolName: 'ReadFile', ...coreToolFields('ReadFile'),
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
            toolName: 'Exec', ...coreToolFields('Exec'),
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
            toolName: 'Exec', ...coreToolFields('Exec'),
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

    s().onItemStarted(streamingToolCallStarted('turn-1', 'spawn-stream', 'SpawnAgent', 'call-spawn'))
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

    const display = getStreamingToolDisplay(item!, item?.argumentsPreview, 'en')
    expect(display.label).toMatch(/^Spawning agent for: x+/)
    expect(item?.argumentsPreview).not.toContain(largePrompt)
  })

  it('enriches a delta-first tool placeholder without discarding its streaming preview', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted(streamingToolCallStarted('turn-1', 'exec-stream', 'Exec', 'call-exec'))
    s().onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'exec-stream',
      toolName: 'Exec',
      callId: 'call-exec',
      delta: '{"command":"dotnet test'
    })

    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'exec-stream',
        type: 'toolCall',
        createdAt: '2026-07-15T00:00:00Z',
        payload: {
          toolName: 'Exec', ...coreToolFields('Exec'),
          callId: 'call-exec',
          arguments: {},
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
          presentation: { presentationId: 'core.shell', options: {} }
        }
      }
    })

    const item = s().turns[0].items.find((candidate) => candidate.id === 'exec-stream')
    expect(item).toMatchObject({
      status: 'streaming',
      toolName: 'Exec', ...coreToolFields('Exec'),
      toolCallId: 'call-exec',
      arguments: {},
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell', options: {} }
    })
    expect(item?.argumentsPreview).toBe('{"command":"dotnet test')
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
          toolName: 'Exec', ...coreToolFields('Exec')
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
          toolName: 'Exec', ...coreToolFields('Exec'),
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
    expect(toolItem?.commandSource).toBe('host')
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

  it('onItemCompleted keeps streamed agent text and repairs an empty completed item', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onAgentMessageDelta('Streamed final text')
    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'item-1', type: 'agentMessage', payload: { text: 'Payload text' } }
    })
    expect(s().turns[0].items[0].text).toBe('Streamed final text')

    useConversationStore.setState((state) => ({
      turns: state.turns.map((turn) => ({
        ...turn,
        items: turn.items.map((item) => item.id === 'item-1' ? { ...item, text: '' } : item)
      }))
    }))
    const completion = {
      turnId: 'turn-1',
      item: { id: 'item-1', type: 'agentMessage', payload: { text: 'Recovered text' } }
    }
    s().onItemCompleted(completion)
    s().onItemCompleted(completion)

    expect(s().turns[0].items).toHaveLength(1)
    expect(s().turns[0].items[0].text).toBe('Recovered text')
  })

  it('onItemCompleted does not consume another agent item streaming buffer', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'item-1', type: 'agentMessage', payload: { text: 'First response' } }
    })
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-2', type: 'agentMessage' } })
    s().onAgentMessageDelta('Second response in progress')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'item-1', type: 'agentMessage', payload: { text: 'First response' } }
    })

    expect(s().streamingMessage).toBe('Second response in progress')
    expect(s().activeItemId).toBe('item-2')
    expect(s().turns[0].items.find((item) => item.id === 'item-1')?.text).toBe('First response')
  })

  it('onTurnCompleted reconciles authoritative items without dropping local items', () => {
    const startedAt = new Date().toISOString()
    s().onTurnStarted(makeTurn({ startedAt }))
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onItemStarted({
      turnId: 'turn-1',
      item: { id: 'local-tool', type: 'toolCall', toolName: 'ReadFile', ...coreToolFields('ReadFile'), createdAt: startedAt }
    })

    s().onTurnCompleted(makeTurn({
      status: 'completed',
      startedAt,
      items: [
        { id: 'item-1', type: 'agentMessage', payload: { text: 'Authoritative final text' } }
      ]
    }))

    const items = s().turns[0].items
    expect(items.find((item) => item.id === 'item-1')?.text).toBe('Authoritative final text')
    expect(items.some((item) => item.id === 'local-tool')).toBe(true)
  })

  it.each([
    ['completed', () => s().onTurnCompleted(makeTurn({ status: 'completed', completedAt: new Date().toISOString() })), {}],
    ['failed', () => s().onTurnFailed(makeTurn(), 'API error'), { error: 'API error' }],
    ['cancelled', () => s().onTurnCancelled(makeTurn(), 'user requested'), { cancelReason: 'user requested' }]
  ] as const)('settles a started turn as %s and clears running state', (status, settle, fields) => {
    s().onTurnStarted(makeTurn())
    expect(s()).toMatchObject({ turnStatus: 'running', activeTurnId: 'turn-1' })
    expect(s().turnStartedAt).not.toBeNull()

    settle()

    expect(s().turnStatus).toBe('idle')
    expect(s().activeTurnId).toBeNull()
    expect(s().turns[0]).toMatchObject({ status, ...fields })
  })
})

describe('reasoning flow', () => {
  it('streams reasoning into a placeholder and settles it in place', () => {
    vi.useFakeTimers()
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'r-1', type: 'reasoningContent' } })
    expect(s().turns[0].items).toMatchObject([{ id: 'r-1', type: 'reasoningContent', status: 'streaming' }])
    s().onReasoningDelta('Step 1.')
    s().onReasoningDelta(' Step 2.')
    vi.advanceTimersByTime(16)
    expect(s().streamingReasoning).toBe('Step 1. Step 2.')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: { id: 'r-1', type: 'reasoningContent', createdAt: new Date().toISOString() }
    })

    expect(s().streamingReasoning).toBe('')
    expect(s().turns[0].items).toMatchObject([{ id: 'r-1', reasoning: 'Step 1. Step 2.', status: 'completed' }])
  })
})

describe('token usage accumulation', () => {
  it('accumulates tokens within a turn and resets them on the next turn', () => {
    s().onTurnStarted(makeTurn())
    s().onUsageDelta(100, 50)
    s().onUsageDelta(200, 100)
    expect(s().inputTokens).toBe(300)
    expect(s().outputTokens).toBe(150)

    s().onTurnCompleted(makeTurn({ status: 'completed' }))
    s().onTurnStarted(makeTurn({ id: 'turn-2' }))

    const state = s()
    expect(state.inputTokens).toBe(0)
    expect(state.outputTokens).toBe(0)
  })
})

describe('system events', () => {
  it('ignores the first reissue, then keeps one reissue status that later events replace', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      messageKey: 'system.streamError',
      params: { attempt: 1, max: 5 }
    })
    expect(s().systemLabel).toBeNull()
    expect(s().streamRetry).toBeNull()

    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      messageKey: 'system.streamError',
      params: { attempt: 2, max: 5 }
    })
    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      messageKey: 'system.streamError.serverBusy',
      params: { attempt: 3, max: 5 }
    })

    expect(s().streamRetry).toMatchObject({
      turnId: 'turn-1',
      attempt: 3,
      max: 5,
      serverBusy: true
    })

    s().onTurnCompleted(makeTurn({ status: 'completed' }))

    expect(s().streamRetry).toBeNull()
  })

  it('clears the reissue status once its turn streams again, but not for another turn', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      messageKey: 'system.streamError',
      params: { attempt: 2, max: 5 }
    })

    s().clearStreamRetry('turn-0')
    expect(s().streamRetry).not.toBeNull()

    s().clearStreamRetry('turn-1')
    expect(s().streamRetry).toBeNull()
    expect(s().turnStatus).toBe('running')
  })

  it('clears the reissue status when loading persisted turns', () => {
    s().onTurnStarted(makeTurn())
    s().onSystemEvent('streamError', {
      turnId: 'turn-1',
      messageKey: 'system.streamError',
      params: { attempt: 2, max: 5 }
    })

    s().setTurns([makeTurn({ status: 'completed' })])

    expect(s().streamRetry).toBeNull()
  })

  it('keeps the provider classification from a failed turn and from reloaded history', () => {
    s().onTurnStarted(makeTurn())
    s().onTurnFailed({ id: 'turn-1', threadId: 'thread-1', providerError: 'usageLimitExceeded' }, 'boom')
    expect(s().turns[0]).toMatchObject({ status: 'failed', providerError: 'usageLimitExceeded' })

    s().setTurns([makeTurn({ status: 'failed', error: 'boom', providerError: 'serverOverloaded' })])

    expect(s().turns[0]).toMatchObject({ status: 'failed', providerError: 'serverOverloaded' })
    expect(selectCapacityRetryDelaySeconds(s())).toBe(10)
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

  it('classifies severity as usage deltas carry new input totals', () => {
    s().setContextUsage(baseSnapshot)
    expect(s().contextUsage).toMatchObject({ tokens: 40_000, severity: 'normal' })

    s().onUsageDelta(1000, 200, 180_500, 3000)
    expect(s().contextUsage).toMatchObject({ tokens: 180_500, severity: 'warning' })
    expect(s().contextUsage!.percentLeft).toBeCloseTo(1 - 180_500 / 200_000, 3)

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

  it('does not apply projected compacting snapshots or token counts to contextUsage', () => {
    s().setContextUsage({ ...baseSnapshot, tokens: 150_000, percentLeft: 0.25 })

    s().onSystemEvent('compacting', { tokenCount: 260_000, percentLeft: 0 })
    s().onSystemEvent('compacting', {
      tokenCount: 260_000,
      percentLeft: 0,
      contextUsage: {
        tokens: 260_000,
        contextWindow: 200_000,
        autoCompactThreshold: 180_000,
        warningThreshold: 176_000,
        errorThreshold: 194_000,
        percentLeft: 0
      }
    })

    expect(s().contextUsage?.tokens).toBe(150_000)
    expect(s().contextUsage?.percentLeft).toBe(0.25)
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

  it('ignores bare totals until a usage delta carries a full snapshot', () => {
    s().onUsageDelta(0, 0, 5000)
    expect(s().contextUsage).toBeNull()

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

    s().setContextUsage(null)
    expect(s().contextUsage).toBeNull()
  })
})

describe('systemNotice items', () => {
  it('appends a compaction notice once per item id', () => {
    s().onTurnStarted(makeTurn())
    const completion = {
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
    }
    s().onItemCompleted(completion)
    s().onItemCompleted(completion)

    const notices = s().turns[0].items.filter((i) => i.type === 'systemNotice')
    expect(notices).toHaveLength(1)
    expect(notices[0].systemNotice).toMatchObject({ kind: 'compacted', trigger: 'auto', tokensBefore: 180_000 })
  })

  it('keeps every remoteRoute field so a live route change renders a divider', () => {
    s().onTurnStarted(makeTurn())
    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'notice-route',
        type: 'systemNotice',
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        payload: {
          kind: 'remoteRoute',
          reason: 'connected',
          initiator: 'agent',
          hostId: 'peer_001',
          hostName: 'Studio PC',
          workspaceId: 'workspace_001',
          workspaceName: 'game-client'
        }
      }
    })

    const notice = s().turns[0].items.find((i) => i.type === 'systemNotice')?.systemNotice
    expect(notice).toMatchObject({
      kind: 'remoteRoute',
      reason: 'connected',
      initiator: 'agent',
      hostId: 'peer_001',
      hostName: 'Studio PC',
      workspaceId: 'workspace_001',
      workspaceName: 'game-client'
    })
  })
})

describe('setTurns', () => {
  it('restores waitingInput as the active turn state', () => {
    s().setTurns([
      makeTurn({ id: 'turn-wait-input', status: 'waitingInput', items: [] })
    ])

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().activeTurnId).toBe('turn-wait-input')
    expect(s().turnStartedAt).not.toBeNull()
  })

  it('keeps a waitingApproval turn active but runnable until its request is replayed', () => {
    s().setTurns([
      makeTurn({ id: 'turn-wait-approval', status: 'waitingApproval', items: [] })
    ])

    expect(s().turnStatus).toBe('running')
    expect(s().activeTurnId).toBe('turn-wait-approval')
    expect(s().turnStartedAt).not.toBeNull()
  })
})

describe('reset', () => {
  it('clears all state back to initial values', () => {
    s().onTurnStarted(makeTurn())
    s().onAgentMessageDelta('some text')
    s().setPendingMessage({ text: 'pending' })
    s().setContextUsage({
      tokens: 1,
      contextWindow: 10,
      autoCompactThreshold: 8,
      warningThreshold: 7,
      errorThreshold: 9,
      percentLeft: 0.9
    })
    s().onPlanUpdated({ title: 'Some Plan', overview: '', content: '', todos: [] })
    s().reset()

    const state = s()
    expect(state.turns).toHaveLength(0)
    expect(state.turnStatus).toBe('idle')
    expect(state.streamingMessage).toBe('')
    expect(state.streamingMessageLastDeltaAt).toBeNull()
    expect(state.pendingMessage).toBeNull()
    expect(state.contextUsage).toBeNull()
    expect(state.plan).toBeNull()
  })
})

describe('onPlanUpdated', () => {
  it('replaces plan state on each update and defaults missing content', () => {
    s().onPlanUpdated({ title: 'Old Plan', overview: '', content: 'Old content', todos: [] })
    s().onPlanUpdated({
      title: 'My Plan',
      overview: 'Build something cool',
      content: '# Full Plan\n\nBody text',
      todos: [
        { id: '1', content: 'Step 1', status: 'completed' },
        { id: '2', content: 'Step 2', status: 'in_progress' }
      ]
    })

    expect(s().plan).toMatchObject({
      title: 'My Plan',
      overview: 'Build something cool',
      content: '# Full Plan\n\nBody text',
      todos: [{ status: 'completed' }, { status: 'in_progress' }]
    })

    s().onPlanUpdated({ title: 'Legacy Plan', overview: 'Legacy payload', todos: [] })
    expect(s().plan?.content).toBe('')
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
        payload: { callId: 'c-late', toolName: 'ReadFile', ...coreToolFields('ReadFile'), arguments: { path: 'a.ts' } }
      }
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'early',
        type: 'toolCall',
        createdAt: '2025-01-01T00:00:00.000Z',
        payload: { callId: 'c-early', toolName: 'ReadFile', ...coreToolFields('ReadFile'), arguments: { path: 'b.ts' } }
      }
    })
    const tools = s().turns[0].items.filter((i) => i.type === 'toolCall')
    expect(tools.map((t) => t.id)).toEqual(['early', 'late'])
  })

  it('does not duplicate replayed agentMessage, toolCall or commandExecution item starts', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    s().onItemStarted({ turnId: 'turn-1', item: { id: 'item-1', type: 'agentMessage' } })
    const toolItem = {
      id: 'tool-1',
      type: 'toolCall',
      createdAt: '2025-01-01T00:00:00.000Z',
      payload: { callId: 'c-1', toolName: 'ReadFile', ...coreToolFields('ReadFile'), arguments: { path: 'a.ts' } }
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

    expect(s().turns[0].items.map((i) => i.id).sort()).toEqual(['cmd-1', 'item-1', 'tool-1'])
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
        payload: { callId: 'c-1', toolName: 'ReadFile', ...coreToolFields('ReadFile'), arguments: { path: 'a.ts' } }
      }
    })
    expect(s().turns[0].items.map((i) => i.id)).toEqual(['msg-1', 'tool-1'])
    expect(s().turns[0].items[0].type).toBe('agentMessage')
    expect(s().turns[0].items[0].status).toBe('streaming')
  })
})

describe('dynamicToolCall items', () => {
  it('stores and completes dynamicToolCall items without a toolResult companion', () => {
    s().onTurnStarted(makeTurn())
    s().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'dynamic-tool-1',
        type: 'dynamicToolCall',
        createdAt: '2025-01-01T00:00:00.000Z',
        payload: {
          namespace: 'workflow',
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
    expect(item?.pluginNamespace).toBe('workflow')

    s().onItemCompleted({
      turnId: 'turn-1',
      item: {
        id: 'dynamic-tool-1',
        type: 'dynamicToolCall',
        completedAt: '2025-01-01T00:00:01.000Z',
        payload: {
          namespace: 'workflow',
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

describe('capacity retry schedule', () => {
  const userTurn = (id: string, status: string): ConversationTurn => ({
    id,
    threadId: 'thread-1',
    status,
    items: [{ id: `${id}-u`, type: 'userMessage', status: 'completed', createdAt: new Date().toISOString() }],
    startedAt: new Date().toISOString(),
    providerError: status === 'failed' ? 'serverOverloaded' : undefined
  } as unknown as ConversationTurn)

  const reissuedTurn = (id: string): ConversationTurn => ({
    id,
    threadId: 'thread-1',
    status: 'failed',
    items: [],
    startedAt: new Date().toISOString(),
    providerError: 'serverOverloaded'
  } as unknown as ConversationTurn)

  const delayFor = (turns: ConversationTurn[]): number | null => {
    useConversationStore.setState({ turns })
    return selectCapacityRetryDelaySeconds(useConversationStore.getState())
  }

  it('escalates while reissues keep failing and hands control back once the schedule is spent', () => {
    const failures = [userTurn('turn-1', 'failed')]
    const expected = [10, 30, 120]
    for (const delay of expected) {
      expect(delayFor(failures)).toBe(delay)
      failures.push(reissuedTurn(`turn-${failures.length + 1}`))
    }
    failures.push(reissuedTurn('turn-5'))
    expect(delayFor(failures)).toBeNull()
  })

  it('resets when the run is interrupted by a turn that succeeded', () => {
    expect(delayFor([
      userTurn('turn-1', 'failed'),
      reissuedTurn('turn-2'),
      userTurn('turn-3', 'completed'),
      userTurn('turn-4', 'failed')
    ])).toBe(10)
  })
})
