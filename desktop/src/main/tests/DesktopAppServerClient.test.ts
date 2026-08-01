import { PassThrough } from 'stream'
import { createInterface } from 'readline'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
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
        capabilities: { threadManagement: true, desktopExtension: { enabled: true } },
        dashboardUrl: 'http://127.0.0.1:1234/'
      }
    })
    expect((await readRequest()).method).toBe('initialized')
    const result = await pending
    expect(result.capabilities.desktopExtension).toEqual({ enabled: true })
    expect(result.dashboardUrl).toBe('http://127.0.0.1:1234/')
  })

  it('forwards raw notifications and server requests without blocking JSON-RPC', async () => {
    const notifications: Array<[string, unknown]> = []
    client.onNotification((method, params) => notifications.push([method, params]))
    client.onServerRequest(async (method, params) => ({ method, params }))

    push({ jsonrpc: '2.0', method: 'extension/unknown', params: { value: 1 } })
    push({ jsonrpc: '2.0', id: 'request-1', method: 'extension/request', params: { value: 2 } })

    await expect.poll(() => notifications).toEqual([['extension/unknown', { value: 1 }]])
    expect(await readRequest()).toEqual({
      jsonrpc: '2.0',
      id: 'request-1',
      result: { method: 'extension/request', params: { value: 2 } }
    })
  })

  it('keeps explicit request timeouts and response correlation in SDK Wire', async () => {
    const first = client.sendRequest<{ value: number }>('fixture/one', {})
    const request = await readRequest()
    push({ jsonrpc: '2.0', id: request.id, result: { value: 7 } })
    await expect(first).resolves.toEqual({ value: 7 })
    await expect(client.sendRequest('fixture/timeout', {}, 5)).rejects.toThrow(/timed out/i)
  })
})
