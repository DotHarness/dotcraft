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
          namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
          name: 'CreateThread'
        }),
        expect.objectContaining({
          namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
          name: 'ListThreads'
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

    expect(tools).toHaveLength(6)
    expect(tools.every((tool) => tool.deferLoading === true)).toBe(true)
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
        expect.objectContaining({ name: 'SendMessageToThread' })
      ])
    }))
    expect(calls[1][0]).toBe('turn/start')
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
          workspacePath: 'F:\\dotcraft'
        }),
        includeSubAgents: false
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
        ]
      }
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'ListThreads',
      arguments: { query: 'login', limit: 5 }
    }, 'F:\\dotcraft')

    expect(result?.success).toBe(true)
    expect(result?.structuredResult).toEqual(expect.objectContaining({
      count: 1,
      threads: [expect.objectContaining({ id: 'thread-1', displayName: 'Fix login' })]
    }))
  })

  it('summarizes ReadThread payload content without raw outputs', async () => {
    const client = createClient(async (method, params) => {
      expect(method).toBe('thread/read')
      expect(params).toEqual({
        threadId: 'thread-1',
        includeTurns: true
      })
      return {
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
              id: 'turn-1',
              status: 'completed',
              startedAt: '2026-06-01T00:00:00Z',
              completedAt: '2026-06-01T00:00:01Z',
              items: [
                {
                  id: 'item-old',
                  type: 'userMessage',
                  status: 'completed',
                  payload: { text: 'Older prompt' }
                }
              ]
            },
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
                      { type: 'fileRef', displayPath: 'src/main.ts', path: 'E:\\Git\\dotcraft\\src\\main.ts' }
                    ]
                  }
                },
                {
                  id: 'item-command',
                  type: 'commandExecution',
                  status: 'completed',
                  payload: {
                    command: 'dotnet test',
                    workingDirectory: 'E:\\Git\\dotcraft',
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
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'ReadThread',
      arguments: { threadId: 'thread-1', turnLimit: 1 }
    }, 'F:\\dotcraft')

    expect(result?.success).toBe(true)
    const text = result?.contentItems?.[0]?.text ?? ''
    expect(text).toContain('Thread thread-1: Investigate renderer')
    expect(text).toContain('Turns: 2 total; showing 2-2 (more older turns available)')
    expect(text).toContain('User: Please inspect shell output')
    expect(text).toContain('Command: dotnet test [completed] exit=0')
    expect(text).toContain('Tool call: ReadFile callId=call-1')
    expect(text).toContain('Agent: The shell output is now terminal-based.')
    expect(text).not.toContain('SECRET_OUTPUT_SHOULD_NOT_APPEAR')
    expect(text).not.toContain('SECRET_RESULT_SHOULD_NOT_APPEAR')

    const structured = result?.structuredResult as {
      thread: {
        turnCount: number
        queuedInputCount: number
        page: { hasMore: boolean }
        turns: Array<{ id: string; items: Array<Record<string, unknown>> }>
      }
    }
    expect(structured.thread.turnCount).toBe(2)
    expect(structured.thread.queuedInputCount).toBe(1)
    expect(structured.thread.page.hasMore).toBe(true)
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
      workingDirectory: 'E:\\Git\\dotcraft',
      outputChars: 'SECRET_OUTPUT_SHOULD_NOT_APPEAR'.length
    }))
    expect(JSON.stringify(structured)).not.toContain('SECRET_RESULT_SHOULD_NOT_APPEAR')
  })

  it('includes truncated ReadThread outputs when requested', async () => {
    const client = createClient(async (method) => {
      if (method !== 'thread/read') throw new Error(`unexpected ${method}`)
      return {
        thread: {
          id: 'thread-1',
          displayName: 'Tool output',
          status: 'active',
          turns: [
            {
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
                  payload: {
                    callId: 'call-1',
                    success: true,
                    result: '0123456789abcdef'
                  }
                },
                {
                  id: 'item-dynamic',
                  type: 'dynamicToolCall',
                  status: 'completed',
                  payload: {
                    namespace: 'desktop',
                    toolName: 'ReadThread',
                    callId: 'call-2',
                    success: true,
                    contentItems: [
                      { type: 'text', text: 'dynamic tool returned a long preview' }
                    ],
                    structuredResult: { value: 'structured result preview' }
                  }
                }
              ]
            }
          ]
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
    }, 'F:\\dotcraft')

    const structured = result?.structuredResult as {
      thread: {
        turns: Array<{ items: Array<Record<string, unknown>> }>
      }
    }
    expect(structured.thread.turns[0].items[0]).toEqual(expect.objectContaining({
      output: 'abcdefghijkl...'
    }))
    expect(structured.thread.turns[0].items[1]).toEqual(expect.objectContaining({
      result: '0123456789ab...'
    }))
    expect(structured.thread.turns[0].items[2]).toEqual(expect.objectContaining({
      contentPreview: 'dynamic tool...',
      structuredResultPreview: '{"value":"st...'
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
      arguments: { prompt: 'start', displayName: 'Research', model: 'gpt-5' }
    }, 'F:\\dotcraft')

    expect(result?.success).toBe(true)
    expect(vi.mocked(client.sendRequest).mock.calls[0][1]).toEqual(expect.objectContaining({
      displayName: 'Research',
      config: { model: 'gpt-5' }
    }))
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
    }, 'F:\\dotcraft', {
      supportsDynamicToolRebind: true
    })

    expect(result?.success).toBe(true)
    expect(result?.structuredResult).toEqual(expect.objectContaining({
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

  it('returns UnsupportedOption for SendMessageToThread model overrides', async () => {
    const client = createClient(async () => {
      throw new Error('should not be called')
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: DESKTOP_THREAD_TOOL_NAMESPACE,
      tool: 'SendMessageToThread',
      arguments: { threadId: 'thread-1', prompt: 'follow up', model: 'gpt-5' }
    }, 'F:\\dotcraft')

    expect(result).toEqual(expect.objectContaining({
      success: false,
      errorCode: 'UnsupportedOption'
    }))
    expect(client.sendRequest).not.toHaveBeenCalled()
  })

  it('ignores dynamic tool calls outside the Desktop thread-tool namespace', async () => {
    const client = createClient(async () => {
      throw new Error('should not be called')
    })

    const result = await handleDesktopRuntimeThreadToolCall(client, {
      namespace: 'other',
      tool: 'ListThreads',
      arguments: {}
    }, 'F:\\dotcraft')

    expect(result).toBeUndefined()
    expect(client.sendRequest).not.toHaveBeenCalled()
  })
})
