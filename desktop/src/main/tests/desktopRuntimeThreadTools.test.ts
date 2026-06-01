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
