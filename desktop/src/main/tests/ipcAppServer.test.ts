import { EventEmitter } from 'node:events'
import { PassThrough } from 'node:stream'
import { createInterface } from 'node:readline'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'

vi.mock('electron', () => ({ dialog: {} }))
vi.mock('../AppServerManager', () => ({ resolveBinaryLocation: vi.fn() }))
import { DesktopAppServerClient } from '../DesktopAppServerClient'
import { registerAppServerHandlers } from '../ipcAppServer'
import type { IpcHandlerCallbacks, WindowIpcContext } from '../ipcBridgeContracts'
import { AppServerDeliveryConsumer } from '../../preload/appServerDelivery'
import type { AppServerDelivery, AppServerIpcRequest } from '../../shared/appServerBoundary'
import { applyWorkspaceThreadListRequestResult, beginWorkspaceThreadListRequest } from '../workspaceThreadCache'

const clients: DesktopAppServerClient[] = []
afterEach(() => { for (const client of clients.splice(0)) client.dispose() })

function wire() {
  const input = new PassThrough()
  const output = new PassThrough()
  const client = new DesktopAppServerClient(input, output)
  clients.push(client)
  const lines = createInterface({ input: output, crlfDelay: Infinity })[Symbol.asyncIterator]()
  return {
    client,
    read: async () => JSON.parse((await lines.next()).value!) as { id: string; method: string; params: Record<string, unknown> },
    push: (...messages: object[]) => input.write(messages.map(message => JSON.stringify({ jsonrpc: '2.0', ...message })).join('\n') + '\n')
  }
}

function bridge(getClient: () => DesktopAppServerClient | null, overrides: Partial<IpcHandlerCallbacks> = {}) {
  const listeners = new Map<string, (event: unknown, request: AppServerIpcRequest) => void>()
  const handlers = new Map<string, (...args: unknown[]) => unknown>()
  const applied: string[] = []
  const deliveries: AppServerDelivery[] = []
  const consumer = new AppServerDeliveryConsumer(request => listeners.get('appserver:request')!({}, request), payload => applied.push(String(payload.params)))
  const owner = Object.assign(new EventEmitter(), {
    isDestroyed: () => false,
    webContents: { id: 1, isDestroyed: () => false, send: (_channel: string, delivery: AppServerDelivery) => {
      deliveries.push(delivery)
      consumer.accept(delivery)
    } }
  }) as unknown as BrowserWindow
  const ipc = { on: (channel: string, listener: never) => { listeners.set(channel, listener) } }
  registerAppServerHandlers({ owner, ipc, handleSafe: (channel, listener) => handlers.set(channel, listener), getWireClient: getClient,
    callbacks: {
      getSettings: () => ({ locale: 'en' }), getWorkspaceStatus: () => ({ workspacePath: '/workspace' }),
      getConnectionStatus: () => ({ capabilities: { dynamicToolRebind: true } }),
      getConnectionContext: () => ({ workspacePath: '/workspace', capabilities: { dynamicToolRebind: true } }), ...overrides
    }
  } as unknown as WindowIpcContext)
  return { consumer, applied, deliveries, handlers, notify: (method: string, params: unknown) => {
    owner.webContents.send('appserver:delivery', { kind: 'notification', payload: { method, params } })
  } }
}

describe('AppServer ordered IPC requests', () => {
  it('applies same-batch resume authority before notifications without waiting for invoke or wrapper promises', async () => {
    const server = wire()
    const ipc = bridge(() => server.client)
    server.client.onNotification(ipc.notify)
    const pending = ipc.consumer.request('thread/resume', { threadId: 'thread' }, undefined, {
      onResponse: result => ipc.applied.push(String((result as { label: string }).label))
    })
    const request = await server.read()
    expect(request.params).toEqual({ threadId: 'thread' })
    server.push({ id: request.id, result: { thread: { id: 'thread' }, label: 'snapshot', currentState: {} } },
      { method: 'fixture/delta', params: 'suffix' })
    await pending
    await vi.waitFor(() => expect(ipc.applied).toEqual(['snapshot', 'suffix']))
  })

  it('keeps keyed requests and their completion bookkeeping on the originating client', async () => {
    const a = wire(), b = wire()
    let foreground = a.client
    const entryA = { workspacePath: '/a', threads: [], errorMessage: 'timeout' }
    const entryB = { workspacePath: '/b', threads: [] }
    const entries = new Map([[a.client, entryA], [b.client, entryB]])
    const ipc = bridge(() => foreground, {
      getConnectionClient: key => key === 'a' ? a.client : b.client,
      onAppServerRequestStarted: (client, method, params) => beginWorkspaceThreadListRequest(entries.get(client)!, method, params),
      onAppServerRequestCompleted: (client, method, params, result, generation) => {
        applyWorkspaceThreadListRequestResult(entries.get(client)!, method, params, result, generation)
      }
    })
    const pendingA = ipc.consumer.request('thread/list', { identity: { workspacePath: '/a' }, scope: 'workspace', includeSubAgents: true }, undefined, undefined, 'a')
    const requestA = await a.read()
    foreground = b.client
    const pendingB = ipc.consumer.request('thread/list', { identity: { workspacePath: '/b' }, scope: 'workspace', includeSubAgents: true })
    const requestB = await b.read()
    b.push({ id: requestB.id, result: { data: [{ id: 'b' }] } })
    await pendingB
    a.push({ id: requestA.id, result: { data: [{ id: 'a' }] } })
    await pendingA
    expect(entryA.threads).toEqual([{ id: 'a' }])
    expect(entryB.threads).toEqual([{ id: 'b' }])
    expect(entryA.errorMessage).toBeUndefined()
  })

  it('keeps paged foreground lists on their originating cache across workspace switches', async () => {
    const a = wire(), b = wire()
    let foreground = a.client
    const entryA = { workspacePath: '/a', threads: [] as unknown[], errorMessage: 'timeout' }
    const entryB = { workspacePath: '/b', threads: [] as unknown[] }
    const entries = new Map([[a.client, entryA], [b.client, entryB]])
    const ipc = bridge(() => foreground, {
      getConnectionContext: client => ({
        workspacePath: entries.get(client)!.workspacePath, capabilities: { dynamicToolRebind: true }
      }),
      onAppServerRequestStarted: (client, method, params) =>
        beginWorkspaceThreadListRequest(entries.get(client)!, method, params),
      onAppServerRequestCompleted: (client, method, params, result, generation) => {
        applyWorkspaceThreadListRequestResult(entries.get(client)!, method, params, result, generation)
      }
    })
    const paramsA = { identity: { workspacePath: '/a' }, scope: 'workspace', includeSubAgents: true, limit: 50 }
    const firstPage = Array.from({ length: 50 }, (_, index) => ({ id: `a-${index}` }))
    const pendingFirst = ipc.consumer.request('thread/list', paramsA)
    const firstRequest = await a.read()
    foreground = b.client
    const pendingOther = ipc.consumer.request('thread/list', { ...paramsA, identity: { workspacePath: '/b' } })
    const otherRequest = await b.read()
    b.push({ id: otherRequest.id, result: { data: [{ id: 'b' }], nextCursor: null } })
    await pendingOther
    a.push({ id: firstRequest.id, result: { data: firstPage, nextCursor: 'a-next' } })
    await pendingFirst
    expect(entryA.threads).toEqual(firstPage)
    expect(entryB.threads).toEqual([{ id: 'b' }])

    foreground = a.client
    const pendingLast = ipc.consumer.request('thread/list', { ...paramsA, cursor: 'a-next' })
    const lastRequest = await a.read()
    foreground = b.client
    a.push({ id: lastRequest.id, result: { data: [{ id: 'a-50' }], nextCursor: null } })
    await pendingLast
    expect(entryA.threads).toEqual([...firstPage, { id: 'a-50' }])
    expect(entryB.threads).toEqual([{ id: 'b' }])
    expect(entryA.errorMessage).toBeUndefined()
  })

  it('rejects a keyed request from an expired connection lifetime before sending it', async () => {
    const server = wire()
    const send = vi.spyOn(server.client, 'sendRequest')
    const ipc = bridge(() => server.client, { getConnectionClient: () => server.client })
    await expect(ipc.consumer.request('thread/unsubscribe', { threadId: 'thread' }, undefined,
      { connectionId: 'expired', attachmentOwner: 'warm' }, 'a')).rejects.toThrow('no longer active')
    expect(send).not.toHaveBeenCalled()
  })

  it('rejects foreground requests without a connection and preserves wire error delivery order and fields', async () => {
    const unavailable = bridge(() => null)
    await expect(unavailable.consumer.request('command/list', {})).rejects.toMatchObject({ message: 'AppServer is not connected (command/list)' })
    const server = wire()
    const ipc = bridge(() => server.client)
    server.client.onNotification(ipc.notify)
    const pending = ipc.consumer.request('thread/resume', { threadId: 'thread' })
    const rejection = expect(pending).rejects.toMatchObject({ rpcCode: -32101, data: { code: 'busy' } })
    const request = await server.read()
    server.push({ id: request.id, error: { code: -32101, message: 'Busy', data: { code: 'busy' } } },
      { method: 'fixture/delta', params: 'suffix' })
    await rejection
    await vi.waitFor(() => expect(ipc.deliveries).toHaveLength(2))
    expect(ipc.deliveries.map(delivery => delivery.kind)).toEqual(['response', 'notification'])
  })

  it('enriches creation identity and tools, and strips authentication token requests on the one RPC path', async () => {
    const server = wire()
    const ipc = bridge(() => server.client)
    const start = ipc.consumer.request('thread/start', { displayName: 'New chat' })
    const request = await server.read()
    expect(request.params).toMatchObject({ identity: { workspacePath: '/workspace' }, dynamicTools: expect.any(Array) })
    server.push({ id: request.id, result: { thread: { id: 'created' } } })
    await start
    const auth = ipc.consumer.request('provider/auth/status', { providerId: 'provider', includeToken: true, refreshToken: true })
    const authRequest = await server.read()
    expect(authRequest.params).toEqual({ providerId: 'provider' })
    server.push({ id: authRequest.id, result: { isAuthenticated: true } })
    await auth
  })
})
