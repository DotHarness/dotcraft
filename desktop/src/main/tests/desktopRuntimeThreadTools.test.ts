import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  DESKTOP_THREAD_TOOL_NAMESPACE,
  DESKTOP_THREAD_COORDINATION_CONTEXT_KEY,
  buildDesktopThreadAdditionalContext,
  buildDesktopThreadDynamicTools,
  handleDesktopRuntimeThreadToolCall,
  resetDesktopThreadToolBindings,
  sendDesktopAppServerRequest,
  type AppServerRequestClient
} from '../desktopRuntimeThreadTools'

type RequestHandler = (method: string, params?: unknown, timeoutMs?: number | null) => Promise<unknown>

function createClient(handler: RequestHandler): AppServerRequestClient {
  const sendRequest = vi.fn(handler) as unknown as AppServerRequestClient['sendRequest']
  return {
    sendRequest
  }
}

describe('desktop runtime thread tools', () => {
  beforeEach(() => {
    resetDesktopThreadToolBindings()
    vi.clearAllMocks()
  })

  it('declares Desktop thread tools on thread/start and remembers the created binding', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-1' } }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'thread/start', {
      identity: { channelName: 'dotcraft-desktop' },
      historyMode: 'server'
    })
    await sendDesktopAppServerRequest(client, 'turn/start', {
      threadId: 'thread-1',
      input: [{ type: 'text', text: 'hello' }]
    }, undefined, {
      supportsDynamicToolRebind: true
    })

    const startCall = vi.mocked(client.sendRequest).mock.calls[0]
    expect(startCall[0]).toBe('thread/start')
    expect(startCall[1]).toEqual(expect.objectContaining({
      additionalContext: expect.objectContaining({
        [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: expect.objectContaining({
          kind: 'application',
          value: expect.stringContaining('CreateThread')
        })
      }),
      dynamicTools: expect.arrayContaining([
        expect.objectContaining({
          type: 'namespace',
          name: DESKTOP_THREAD_TOOL_NAMESPACE,
          tools: expect.arrayContaining([
            expect.objectContaining({ type: 'function', name: 'CreateThread' }),
            expect.objectContaining({ type: 'function', name: 'ListThreads' })
          ])
        })
      ])
    }))
    expect(vi.mocked(client.sendRequest).mock.calls.map((call) => call[0])).toEqual([
      'thread/start',
      'turn/start'
    ])
  })

  it('declares all Desktop thread tools as deferred', () => {
    const tools = buildDesktopThreadDynamicTools()

    expect(tools).toHaveLength(1)
    const namespace = tools[0]
    expect(namespace.type).toBe('namespace')
    if (namespace.type !== 'namespace') throw new Error('Expected a namespace declaration')
    expect(namespace.name).toBe(DESKTOP_THREAD_TOOL_NAMESPACE)
    expect(namespace.tools).toHaveLength(7)
    expect(namespace.tools.every((tool) => tool.deferLoading === true)).toBe(true)
    expect(namespace.tools.map((tool) => tool.name)).toContain('SetThreadPinned')
  })

  it('declares Desktop thread coordination as runtime additional context', () => {
    const additionalContext = buildDesktopThreadAdditionalContext()

    expect(additionalContext).toEqual({
      [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: {
        kind: 'application',
        value: expect.stringContaining('search for the relevant thread tool first')
      }
    })
  })

  it('rebinds Desktop thread tools before sending a turn on an older thread', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/resume') return { thread: { id: 'thread-old' } }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'turn/start', {
      threadId: 'thread-old',
      input: [{ type: 'text', text: 'continue' }]
    }, undefined, {
      supportsDynamicToolRebind: true
    })

    const calls = vi.mocked(client.sendRequest).mock.calls
    expect(calls[0][0]).toBe('thread/resume')
    expect(calls[0][1]).toEqual(expect.objectContaining({
      threadId: 'thread-old',
      additionalContext: expect.objectContaining({
        [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: expect.objectContaining({
          kind: 'application',
          value: expect.stringContaining('SendMessageToThread')
        })
      }),
      dynamicTools: expect.arrayContaining([
        expect.objectContaining({
          type: 'namespace',
          name: DESKTOP_THREAD_TOOL_NAMESPACE,
          tools: expect.arrayContaining([
            expect.objectContaining({ name: 'SendMessageToThread' })
          ])
        })
      ])
    }))
    expect(calls[1][0]).toBe('turn/start')
  })

  it('rebinds the current Desktop connection before opening a historical visualization', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/resume') return { thread: { id: 'thread-old' } }
      if (method === 'visualization/view/open') return { viewHandle: 'view-1', fragment: '<div />' }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'visualization/view/open', {
      threadId: 'thread-old', turnId: 'turn-1', itemId: 'item-1', file: 'chart.html'
    }, undefined, { supportsDynamicToolRebind: true })

    const calls = vi.mocked(client.sendRequest).mock.calls
    expect(calls.map(call => call[0])).toEqual(['thread/resume', 'visualization/view/open'])
    expect(calls[0][1]).toEqual(expect.objectContaining({
      threadId: 'thread-old',
      dynamicTools: expect.arrayContaining([expect.objectContaining({ name: DESKTOP_THREAD_TOOL_NAMESPACE })]),
      additionalContext: expect.objectContaining({ [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: expect.anything() })
    }))
  })

  it('uses a plain resume for visualization binding when dynamic rebind is unavailable', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/resume') return { thread: { id: 'thread-old' } }
      if (method === 'visualization/view/open') return { viewHandle: 'view-1' }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'visualization/view/open', {
      threadId: 'thread-old', turnId: 'turn-1', itemId: 'item-1', file: 'chart.html'
    })

    expect(vi.mocked(client.sendRequest).mock.calls[0]).toEqual(['thread/resume', { threadId: 'thread-old' }])
  })

  it('shares one visualization resume across concurrent opens in the same thread', async () => {
    let finishResume!: () => void
    const resumePending = new Promise<void>(resolve => { finishResume = resolve })
    const client = createClient(async (method) => {
      if (method === 'thread/resume') {
        await resumePending
        return { thread: { id: 'thread-old' } }
      }
      if (method === 'visualization/view/open') return { viewHandle: 'view-1' }
      throw new Error(`unexpected ${method}`)
    })

    const first = sendDesktopAppServerRequest(client, 'visualization/view/open', {
      threadId: 'thread-old', turnId: 'turn-1', itemId: 'item-1', file: 'one.html'
    })
    const second = sendDesktopAppServerRequest(client, 'visualization/view/open', {
      threadId: 'thread-old', turnId: 'turn-1', itemId: 'item-2', file: 'two.html'
    })
    await Promise.resolve()
    expect(vi.mocked(client.sendRequest).mock.calls.filter(call => call[0] === 'thread/resume')).toHaveLength(1)

    finishResume()
    await Promise.all([first, second])
    expect(vi.mocked(client.sendRequest).mock.calls.map(call => call[0])).toEqual([
      'thread/resume', 'visualization/view/open', 'visualization/view/open'
    ])
  })

  it('does not eagerly bind visualizations during thread/read and retries a failed on-demand resume', async () => {
    let resumeAttempts = 0
    const client = createClient(async (method) => {
      if (method === 'thread/read') return { thread: { id: 'thread-old' } }
      if (method === 'thread/resume') {
        resumeAttempts++
        if (resumeAttempts === 1) throw new Error('connection changed')
        return { thread: { id: 'thread-old' } }
      }
      if (method === 'visualization/view/open') return { viewHandle: 'view-1' }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'thread/read', { threadId: 'thread-old', includeTurns: true })
    expect(vi.mocked(client.sendRequest).mock.calls.map(call => call[0])).toEqual(['thread/read'])

    const openParams = { threadId: 'thread-old', turnId: 'turn-1', itemId: 'item-1', file: 'chart.html' }
    await expect(sendDesktopAppServerRequest(client, 'visualization/view/open', openParams)).rejects.toThrow('connection changed')
    await expect(sendDesktopAppServerRequest(client, 'visualization/view/open', openParams)).resolves.toEqual({ viewHandle: 'view-1' })
    expect(resumeAttempts).toBe(2)
  })

  it('reuses start binding on one client and rebinds after the client changes', async () => {
    const firstClient = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-1' } }
      if (method === 'visualization/view/open') return { viewHandle: 'view-1' }
      throw new Error(`unexpected ${method}`)
    })
    const secondClient = createClient(async (method) => {
      if (method === 'thread/resume') return { thread: { id: 'thread-1' } }
      if (method === 'visualization/view/open') return { viewHandle: 'view-2' }
      throw new Error(`unexpected ${method}`)
    })
    const openParams = { threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1', file: 'chart.html' }

    await sendDesktopAppServerRequest(firstClient, 'thread/start', { identity: { channelName: 'dotcraft-desktop' } })
    await sendDesktopAppServerRequest(firstClient, 'visualization/view/open', openParams)
    expect(vi.mocked(firstClient.sendRequest).mock.calls.map(call => call[0])).toEqual([
      'thread/start', 'visualization/view/open'
    ])

    await sendDesktopAppServerRequest(secondClient, 'visualization/view/open', openParams)
    expect(vi.mocked(secondClient.sendRequest).mock.calls.map(call => call[0])).toEqual([
      'thread/resume', 'visualization/view/open'
    ])
  })

  it('preserves non-Desktop runtime additional context when declaring thread tools', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-1' } }
      throw new Error(`unexpected ${method}`)
    })

    await sendDesktopAppServerRequest(client, 'thread/start', {
      identity: { channelName: 'dotcraft-desktop' },
      additionalContext: {
        'sample.runtime': { kind: 'application', value: 'keep me' }
      }
    })

    const startCall = vi.mocked(client.sendRequest).mock.calls[0]
    expect(startCall[1]).toEqual(expect.objectContaining({
      additionalContext: {
        'sample.runtime': { kind: 'application', value: 'keep me' },
        [DESKTOP_THREAD_COORDINATION_CONTEXT_KEY]: expect.objectContaining({
          kind: 'application'
        })
      }
    }))
  })

  it('handles ListThreads through thread/list with Desktop identity', async () => {
    const client = createClient(async (method, params) => {
      expect(method).toBe('thread/list')
      expect(params).toEqual(expect.objectContaining({
        identity: expect.objectContaining({
          channelName: 'dotcraft-desktop',
          workspacePath: 'F:\\examples\\workspace'
        }),
        scope: 'workspace',
        includeSubAgents: false,
        includeArchived: false,
        limit: 5,
        query: 'login'
      }))
      return {
        data: [
          {
            id: 'thread-1',
            displayName: 'Fix login',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-06-01T00:00:00Z',
            lastActiveAt: '2026-06-01T00:01:00Z'
          }
        ],
        nextCursor: 'cursor-2',
        totalMatched: 2
      }
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'ListThreads',
      arguments: { query: 'login', limit: 5 }
    }, 'F:\\examples\\workspace')

    expect(result?.success).toBe(true)
    expect(result?.structuredContent).toEqual(expect.objectContaining({
      count: 1,
      threads: [expect.objectContaining({ id: 'thread-1', displayName: 'Fix login' })],
      nextCursor: 'cursor-2',
      totalMatched: 2
    }))
  })

  it('summarizes ReadThread payload content without raw outputs', async () => {
    const client = createClient(async (method, params) => {
      const response = {
        turnPage: {
          order: 'oldestFirst',
          limit: 1,
          totalTurns: 2,
          startOrdinal: 2,
          endOrdinal: 2,
          nextCursor: 'older-cursor',
          hasMore: true
        },
        thread: {
          id: 'thread-1',
          displayName: 'Investigate renderer',
          status: 'active',
          originChannel: 'dotcraft-desktop',
          createdAt: '2026-06-01T00:00:00Z',
          lastActiveAt: '2026-06-01T00:02:00Z',
          runtime: { running: false, busy: false },
          queuedInputs: [{ id: 'queued-1' }],
          turns: [
            {
              id: 'turn-2',
              status: 'completed',
              startedAt: '2026-06-01T00:01:00Z',
              completedAt: '2026-06-01T00:02:00Z',
              items: [
                {
                  id: 'item-user',
                  type: 'userMessage',
                  status: 'completed',
                  payload: {
                    text: 'Please inspect shell output',
                    nativeInputParts: [
                      { type: 'text', text: 'Please inspect shell output' },
                      { type: 'localImage', fileName: 'screen.png', path: 'C:\\tmp\\screen.png', mimeType: 'image/png' },
                      { type: 'fileRef', displayPath: 'src/main.ts', path: 'E:\\examples\\workspace\\src\\main.ts' }
                    ]
                  }
                },
                {
                  id: 'item-command',
                  type: 'commandExecution',
                  status: 'completed',
                  payload: {
                    command: 'dotnet test',
                    workingDirectory: 'E:\\examples\\workspace',
                    status: 'completed',
                    exitCode: 0,
                    durationMs: 123,
                    aggregatedOutput: 'SECRET_OUTPUT_SHOULD_NOT_APPEAR'
                  }
                },
                {
                  id: 'item-call',
                  type: 'toolCall',
                  status: 'completed',
                  payload: {
                    toolName: 'ReadFile',
                    callId: 'call-1',
                    arguments: { path: 'src/main.ts' }
                  }
                },
                {
                  id: 'item-result',
                  type: 'toolResult',
                  status: 'completed',
                  payload: {
                    callId: 'call-1',
                    success: true,
                    result: 'SECRET_RESULT_SHOULD_NOT_APPEAR'
                  }
                },
                {
                  id: 'item-agent',
                  type: 'agentMessage',
                  status: 'completed',
                  payload: { text: 'The shell output is now terminal-based.' }
                }
              ]
            }
          ]
        }
      }
      if (method === 'thread/turns/list') {
        expect(params).toEqual(expect.objectContaining({ threadId: 'thread-1', limit: 1 }))
        return { data: response.thread.turns.map(({ items: _items, ...turn }) => turn), nextCursor: 'older-turns' }
      }
      if (method === 'thread/items/list') {
        return {
          data: [...response.thread.turns[0].items].reverse().map((item) => ({ turnId: 'turn-2', item })),
          nextCursor: 'older-items'
        }
      }
      expect(method).toBe('thread/read')
      return { thread: { ...response.thread, turns: undefined } }
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'ReadThread',
      arguments: { threadId: 'thread-1', turnLimit: 1 }
    }, 'F:\\examples\\workspace')

    expect(result?.success).toBe(true)
    const text = result?.contentItems?.[0]?.text ?? ''
    expect(text).toContain('Thread thread-1: Investigate renderer')
    expect(text).toContain('Turns: 1 total; showing 1-1 (more older turns available)')
    expect(text).toContain('User: Please inspect shell output')
    expect(text).toContain('Command: dotnet test [completed] exit=0')
    expect(text).toContain('Tool call: ReadFile callId=call-1')
    expect(text).toContain('Agent: The shell output is now terminal-based.')
    expect(text).not.toContain('SECRET_OUTPUT_SHOULD_NOT_APPEAR')
    expect(text).not.toContain('SECRET_RESULT_SHOULD_NOT_APPEAR')

    const structured = result?.structuredContent as {
      thread: {
        turnCount: number
        queuedInputCount: number
        page: { hasMore: boolean }
        turns: Array<{ id: string; items: Array<Record<string, unknown>> }>
      }
    }
    expect(structured.thread.turnCount).toBe(1)
    expect(structured.thread.queuedInputCount).toBe(1)
    expect(structured.thread.queuedInputs).toHaveLength(1)
    expect(structured.thread.page.hasMore).toBe(true)
    expect(structured.thread.page.turnCursor).toBe('older-turns')
    expect(structured.thread.page.itemCursor).toBe('older-items')
    expect(structured.thread.turns).toHaveLength(1)
    expect(structured.thread.turns[0].id).toBe('turn-2')
    expect(structured.thread.turns[0].items[0]).toEqual(expect.objectContaining({
      text: 'Please inspect shell output',
      content: expect.arrayContaining([
        expect.objectContaining({ type: 'localImage', fileName: 'screen.png', path: 'C:\\tmp\\screen.png' }),
        expect.objectContaining({ type: 'fileRef', displayPath: 'src/main.ts' })
      ])
    }))
    expect(structured.thread.turns[0].items[1]).toEqual(expect.objectContaining({
      command: 'dotnet test',
      workingDirectory: 'E:\\examples\\workspace',
      outputChars: 'SECRET_OUTPUT_SHOULD_NOT_APPEAR'.length
    }))
    expect(JSON.stringify(structured)).not.toContain('SECRET_RESULT_SHOULD_NOT_APPEAR')
  })

  it('includes truncated ReadThread outputs when requested', async () => {
    const client = createClient(async (method) => {
      const turn = {
        id: 'turn-1',
        status: 'completed',
        items: [
          {
            id: 'item-command',
            type: 'commandExecution',
            status: 'completed',
            payload: {
              command: 'npm test',
              status: 'completed',
              aggregatedOutput: 'abcdefghijklmnopqrstuvwxyz'
            }
          },
          {
            id: 'item-result',
            type: 'toolResult',
            status: 'completed',
            payload: { callId: 'call-1', success: true, result: '0123456789abcdef' }
          },
          {
            id: 'item-dynamic',
            type: 'dynamicToolCall',
            status: 'completed',
            payload: {
              namespace: 'desktop', toolName: 'ReadThread', callId: 'call-2', success: true,
              contentItems: [{ type: 'text', text: 'dynamic tool returned a long preview' }],
              structuredContent: { value: 'structured result preview' }
            }
          }
        ]
      }
      if (method === 'thread/turns/list') return { data: [{ id: turn.id, status: turn.status }], nextCursor: null }
      if (method === 'thread/items/list') return { data: [...turn.items].reverse().map((item) => ({ turnId: turn.id, item })), nextCursor: null }
      if (method !== 'thread/read') throw new Error(`unexpected ${method}`)
      return {
        thread: {
          id: 'thread-1',
          displayName: 'Tool output',
          status: 'active',
          turns: []
        }
      }
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'ReadThread',
      arguments: {
        threadId: 'thread-1',
        includeOutputs: true,
        maxOutputCharsPerItem: 12
      }
    }, 'F:\\examples\\workspace')

    const structured = result?.structuredContent as {
      thread: {
        turns: Array<{ items: Array<Record<string, unknown>> }>
      }
    }
    expect(structured.thread.turns[0].items[0]).toEqual(expect.objectContaining({
      output: 'abcdefghi...'
    }))
    expect(structured.thread.turns[0].items[1]).toEqual(expect.objectContaining({
      result: '012345678...'
    }))
    expect(structured.thread.turns[0].items[2]).toEqual(expect.objectContaining({
      contentPreview: 'dynamic t...',
      structuredContentPreview: '{"value":...'
    }))
  })

  it('passes CreateThread model through thread configuration', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-1', displayName: 'Research' } }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'CreateThread',
      arguments: { prompt: 'start', displayName: 'Research', model: 'gpt-5', reasoningEffort: 'high' }
    }, 'F:\\examples\\workspace')

    expect(result?.success).toBe(true)
    expect(vi.mocked(client.sendRequest).mock.calls[0][1]).toEqual(expect.objectContaining({
      displayName: 'Research',
      config: {
        model: 'gpt-5',
        reasoning: {
          enabled: true,
          effort: 'high',
          output: 'full'
        }
      }
    }))
  })

  it('records the calling thread as spawnedFromThreadId on CreateThread', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-child', displayName: 'Research' } }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'CreateThread',
      threadId: 'thread-parent',
      arguments: { prompt: 'start' }
    }, 'F:\\examples\\workspace')

    expect(result?.success).toBe(true)
    const startCall = vi.mocked(client.sendRequest).mock.calls.find((call) => call[0] === 'thread/start')
    expect(startCall?.[1]).toEqual(expect.objectContaining({ spawnedFromThreadId: 'thread-parent' }))
  })

  it('omits spawnedFromThreadId when CreateThread has no calling thread', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/start') return { thread: { id: 'thread-child' } }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'CreateThread',
      arguments: { prompt: 'start' }
    }, 'F:\\examples\\workspace')

    const startCall = vi.mocked(client.sendRequest).mock.calls.find((call) => call[0] === 'thread/start')
    expect(startCall?.[1]).not.toHaveProperty('spawnedFromThreadId')
  })

  it('queues SendMessageToThread when the target thread is busy', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/read') return { thread: { id: 'thread-1', status: 'active' } }
      if (method === 'thread/resume') return { thread: { id: 'thread-1' } }
      if (method === 'turn/start') throw new Error('Invalid request: TurnInProgress')
      if (method === 'turn/enqueue') return { queuedInput: { id: 'queued-1' } }
      throw new Error(`unexpected ${method}`)
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SendMessageToThread',
      arguments: { threadId: 'thread-1', prompt: 'follow up' }
    }, 'F:\\examples\\workspace', {
      supportsDynamicToolRebind: true
    })

    expect(result?.success).toBe(true)
    expect(result?.structuredContent).toEqual(expect.objectContaining({
      threadId: 'thread-1',
      queued: true,
      queuedInput: { id: 'queued-1' }
    }))
    expect(vi.mocked(client.sendRequest).mock.calls.map((call) => call[0])).toEqual([
      'thread/read',
      'thread/resume',
      'turn/start',
      'turn/enqueue'
    ])
  })

  it('updates persistent reasoning effort before sending a message', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/read') {
        return {
          thread: {
            id: 'thread-1',
            status: 'active',
            configuration: {
              mode: 'agent',
              model: 'gpt-5',
              reasoning: { enabled: true, effort: 'medium', output: 'summary' }
            }
          }
        }
      }
      if (method === 'thread/config/update') return {}
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      throw new Error(`unexpected ${method}`)
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SendMessageToThread',
      arguments: { threadId: 'thread-1', prompt: 'follow up', reasoningEffort: 'extraHigh' }
    }, 'F:\\examples\\workspace')

    expect(result?.success).toBe(true)
    expect(vi.mocked(client.sendRequest).mock.calls.map((call) => call[0])).toEqual([
      'thread/read',
      'thread/config/update',
      'turn/start'
    ])
    expect(vi.mocked(client.sendRequest).mock.calls[1][1]).toEqual({
      threadId: 'thread-1',
      config: {
        mode: 'agent',
        model: 'gpt-5',
        reasoning: {
          enabled: true,
          effort: 'extraHigh',
          output: 'summary'
        }
      }
    })
  })

  it('returns UnsupportedOption for SendMessageToThread model overrides', async () => {
    const client = createClient(async () => {
      throw new Error('should not be called')
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SendMessageToThread',
      arguments: { threadId: 'thread-1', prompt: 'follow up', model: 'gpt-5' }
    }, 'F:\\examples\\workspace')

    expect(result).toEqual(expect.objectContaining({
      success: false,
      errorCode: 'UnsupportedOption'
    }))
    expect(client.sendRequest).not.toHaveBeenCalled()
  })

  it('pins a non-archived top-level thread through Desktop settings', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/read') {
        return {
          thread: {
            id: 'thread-1',
            status: 'active',
            source: { kind: 'user' },
            originChannel: 'dotcraft-desktop'
          }
        }
      }
      throw new Error(`unexpected ${method}`)
    })
    const settingsHost = {
      getSettings: vi.fn(() => ({
        pinnedThreadIdsByWorkspace: {
          'F:\\examples\\workspace': ['thread-old']
        }
      })),
      updateSettings: vi.fn(async () => {}),
      onPinnedThreadIdsChanged: vi.fn()
    }

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SetThreadPinned',
      arguments: { threadId: 'thread-1', pinned: true }
    }, 'F:\\examples\\workspace', { settingsHost })

    expect(result?.success).toBe(true)
    expect(settingsHost.updateSettings).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        'f:/examples/workspace': ['thread-1', 'thread-old']
      }
    })
    expect(settingsHost.onPinnedThreadIdsChanged).toHaveBeenCalledWith('f:/examples/workspace', ['thread-1', 'thread-old'])
  })

  it('unpins without reading the target thread', async () => {
    const client = createClient(async () => {
      throw new Error('should not be called')
    })
    const settingsHost = {
      getSettings: vi.fn(() => ({
        pinnedThreadIdsByWorkspace: {
          'F:\\examples\\workspace': ['thread-1', 'thread-old']
        }
      })),
      updateSettings: vi.fn(async () => {}),
      onPinnedThreadIdsChanged: vi.fn()
    }

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SetThreadPinned',
      arguments: { threadId: 'thread-1', pinned: false }
    }, 'F:\\examples\\workspace', { settingsHost })

    expect(result?.success).toBe(true)
    expect(client.sendRequest).not.toHaveBeenCalled()
    expect(settingsHost.updateSettings).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        'f:/examples/workspace': ['thread-old']
      }
    })
  })

  it('rejects subagent child threads when pinning', async () => {
    const client = createClient(async (method) => {
      if (method === 'thread/read') {
        return {
          thread: {
            id: 'thread-child',
            status: 'active',
            source: { kind: 'subagent' },
            originChannel: 'subagent'
          }
        }
      }
      throw new Error(`unexpected ${method}`)
    })
    const settingsHost = {
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(async () => {}),
      onPinnedThreadIdsChanged: vi.fn()
    }

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SetThreadPinned',
      arguments: { threadId: 'thread-child', pinned: true }
    }, 'F:\\examples\\workspace', { settingsHost })

    expect(result).toEqual(expect.objectContaining({
      success: false,
      errorCode: 'TargetUnsupported'
    }))
    expect(settingsHost.updateSettings).not.toHaveBeenCalled()
  })

  it('ignores dynamic tool calls outside the Desktop thread-tool namespace', async () => {
    const client = createClient(async () => {
      throw new Error('should not be called')
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: 'other',
      tool: 'ListThreads',
      arguments: {}
    }, 'F:\\examples\\workspace')

    expect(result).toBeUndefined()
    expect(client.sendRequest).not.toHaveBeenCalled()
  })
})
