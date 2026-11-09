import { PassThrough } from 'stream'
import { once } from 'events'
import { createInterface } from 'readline'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DesktopAppServerClient } from '../DesktopAppServerClient'

describe('DesktopAppServerClient', () => {
  let fromServer: PassThrough
  let toServer: PassThrough
  let client: DesktopAppServerClient
  let outbound: AsyncIterator<string>

  beforeEach(() => {
    fromServer = new PassThrough()
    toServer = new PassThrough()
    outbound = createInterface({ input: toServer, crlfDelay: Infinity })[Symbol.asyncIterator]()
    client = new DesktopAppServerClient(fromServer, toServer)
  })

  afterEach(() => client.dispose())

  async function readRequest(): Promise<Record<string, unknown>> {
    const line = await outbound.next()
    expect(line.done).toBe(false)
    return JSON.parse(line.value!) as Record<string, unknown>
  }

  function push(message: Record<string, unknown>): void {
    fromServer.write(`${JSON.stringify(message)}\n`)
  }

  it('uses the Desktop initialization profile and sends initialized afterward', async () => {
    expect(client.connectionId).toBeNull()
    const pending = client.initialize('1.2.3')
    const request = await readRequest()
    expect(request.method).toBe('initialize')
    expect(request.params).toMatchObject({
      clientInfo: { name: 'dotcraft-desktop', title: 'DotCraft', version: '1.2.3' },
      capabilities: {
        approvalSupport: true,
        requestUserInputSupport: true,
        backgroundTerminals: true,
        nodeRepl: { backend: 'desktop-node' },
        browserUse: { backend: 'desktop-iab', supportsCancel: true }
      }
    })
    push({
      jsonrpc: '2.0',
      id: request.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '1', protocolVersion: '1' },
        capabilities: { threadManagement: true, pluginManagement: true },
        dashboardUrl: 'http://127.0.0.1:1234/'
      }
    })
    expect((await readRequest()).method).toBe('initialized')
    const result = await pending
    expect(result.capabilities.pluginManagement).toBe(true)
    expect(result.dashboardUrl).toBe('http://127.0.0.1:1234/')
    const connectionId = client.connectionId
    expect(connectionId).toEqual(expect.any(String))
    await client.releaseViewAttachments()
    expect(client.connectionId).toBe(connectionId)
    client.dispose()
    expect(client.connectionId).toBeNull()
  })

  it('invalidates the source identity on disconnect and assigns a new lifetime after reconnect', async () => {
    vi.useFakeTimers()
    let reader: { resolve(message: Record<string, unknown>): void; reject(error: Error): void } | null = null
    const incoming: Record<string, unknown>[] = []
    const enqueue = (message: Record<string, unknown>): void => {
      const pending = reader
      reader = null
      if (pending) pending.resolve(message)
      else incoming.push(message)
    }
    const fail = (error: Error): void => {
      const pending = reader
      reader = null
      pending?.reject(error)
    }
    const remote = new DesktopAppServerClient({
      readMessage: () => incoming.length ? Promise.resolve(incoming.shift()!)
        : new Promise<Record<string, unknown>>((resolve, reject) => { reader = { resolve, reject } }),
      writeMessage: async message => {
        if (message.method === 'initialize') enqueue({ jsonrpc: '2.0', id: message.id,
          result: { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} } })
      },
      connect: async () => {},
      close: async () => { fail(new Error('closed')) }
    }, undefined, { autoReconnect: true })
    try {
      await remote.initialize()
      const first = remote.connectionId
      const closed = once(remote, 'close')
      const reconnected = once(remote, 'reconnected')
      fail(new Error('connection lost'))
      await closed
      expect(remote.connectionId).toBeNull()
      await vi.runOnlyPendingTimersAsync()
      await reconnected
      expect(remote.connectionId).toEqual(expect.any(String))
      expect(remote.connectionId).not.toBe(first)
    } finally {
      remote.dispose()
      vi.useRealTimers()
    }
  })

  it('forwards raw notifications and server requests without blocking JSON-RPC', async () => {
    const notifications: Array<[string, unknown]> = []
    client.onNotification((method, params) => notifications.push([method, params]))
    client.onServerRequest(async (method, params) => ({ method, params }))

    push({ jsonrpc: '2.0', method: 'extension/unknown', params: { value: 1 } })
    push({ jsonrpc: '2.0', id: 'request-1', method: 'extension/request', params: { value: 2 } })

    const response = await readRequest()
    expect(notifications).toEqual([['extension/unknown', { value: 1 }]])
    expect(response).toEqual({
      jsonrpc: '2.0',
      id: 'request-1',
      result: { method: 'extension/request', params: { value: 2 } }
    })
  })

  it('holds the wire subscription through UI unsubscribe and releases it at browser turn end', async () => {
    const retain = client.retainBrowserTurn('thread', 'turn')
    expect(client.hasRetainedThreadAttachments).toBe(true)
    const subscribe = await readRequest()
    expect(subscribe).toMatchObject({ method: 'thread/resume', params: { threadId: 'thread' } })
    const detach = client.sendRequest('thread/unsubscribe', { threadId: 'thread' })
    push({ jsonrpc: '2.0', id: subscribe.id, result: { currentState: { turnId: 'turn', turnStatus: 'running' } } })
    await expect(retain).resolves.toBe(true)
    await expect(detach).resolves.toEqual({})
    const release = client.releaseBrowserTurn('thread', 'turn')
    const unsubscribe = await readRequest()
    expect(unsubscribe).toMatchObject({ method: 'thread/unsubscribe', params: { threadId: 'thread' } })
    push({ jsonrpc: '2.0', id: unsubscribe.id, result: {} })
    await release
    expect(client.hasRetainedThreadAttachments).toBe(false)
    await client.releaseBrowserTurn('thread', 'turn')
    const next = client.sendRequest('fixture/next', {})
    const request = await readRequest()
    expect(request.method).toBe('fixture/next')
    push({ jsonrpc: '2.0', id: request.id, result: {} })
    await next
  })
})
