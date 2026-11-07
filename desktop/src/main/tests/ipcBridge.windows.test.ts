import { EventEmitter } from 'events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow, IpcMainInvokeEvent } from 'electron'

const runtime = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => unknown>(),
  listeners: new Map<string, (...args: unknown[]) => unknown>()
}))
vi.mock('electron', () => ({
  ipcMain: {
    handle: vi.fn((channel, handler) => runtime.handlers.set(channel, handler)),
    removeHandler: vi.fn((channel) => runtime.handlers.delete(channel)),
    on: vi.fn((channel, listener) => runtime.listeners.set(channel, listener)),
    removeListener: vi.fn((channel) => runtime.listeners.delete(channel))
  },
  dialog: {},
  BrowserWindow: {}
}))
vi.mock('../AppServerManager', () => ({ resolveBinaryLocation: vi.fn() }))
vi.mock('../desktopRuntimeThreadTools', () => ({
  hasDesktopThreadTools: () => false,
  sendDesktopAppServerRequest: async (client: { sendRequest: (...args: unknown[]) => Promise<unknown> }, method: string, params: unknown, timeout: unknown, options: { onResponse?: (result: unknown) => void }) => {
    const result = await client.sendRequest(method, params, timeout)
    options.onResponse?.(result)
    return result
  }
}))

import { ipcMain } from 'electron'
import { registerAppServerHandlers } from '../ipcAppServer'
import type { IpcHandlerCallbacks, WindowIpcContext } from '../ipcBridgeContracts'
import { broadcastServerRequest, cancelWindowServerRequests, createServerRequestBridge } from '../ipcServerRequests'
import { createWindowIpcMain, type WindowIpcMain } from '../windowIpc'

const scopes: WindowIpcMain[] = []
const owners: BrowserWindow[] = []
function window(id: number): BrowserWindow {
  const owner = Object.assign(new EventEmitter(), {
    isDestroyed: () => false,
    webContents: { id, isDestroyed: () => false, send: vi.fn() }
  }) as unknown as BrowserWindow
  owners.push(owner)
  return owner
}
function register(owner: BrowserWindow, root: string, client: unknown): WindowIpcMain {
  const ipc = createWindowIpcMain(owner)
  scopes.push(ipc)
  const callbacks = {
    getSettings: () => ({ locale: 'en' }),
    getWorkspaceStatus: () => ({ workspacePath: root }),
    getConnectionStatus: () => ({ capabilities: {} }),
    getConnectionKey: () => root,
    getConnectionContext: () => ({ workspacePath: root })
  } as unknown as IpcHandlerCallbacks
  const context: WindowIpcContext = {
    owner,
    ipc,
    handleSafe: ipc.handle.bind(ipc),
    workspacePath: root,
    getWireClient: () => client as ReturnType<WindowIpcContext['getWireClient']>,
    callbacks
  }
  registerAppServerHandlers(context)
  ipc.handle('window:get-workspace-path', () => root)
  return ipc
}
let nextRequest = 0
function request(owner: BrowserWindow, method: string, params: unknown): Promise<unknown> {
  const requestId = ++nextRequest
  return new Promise(resolve => {
    vi.mocked(owner.webContents.send).mockImplementation((_channel, delivery) => {
      if (delivery.kind === 'response' && delivery.requestId === requestId) resolve(delivery.result)
    })
    runtime.listeners.get('appserver:request')!({ sender: owner.webContents }, { requestId, method, params })
  })
}
function invoke(owner: BrowserWindow, channel: string, ...args: unknown[]): unknown {
  return runtime.handlers.get(channel)!({ sender: owner.webContents } as IpcMainInvokeEvent, ...args)
}
afterEach(() => {
  for (const scope of scopes.splice(0)) scope.dispose()
  for (const owner of owners.splice(0)) cancelWindowServerRequests(owner)
  vi.clearAllMocks()
})

describe('window-owned IPC', () => {
  it('routes concurrent requests and identity to the requesting window, not the last registration', async () => {
    const a = window(1),
      b = window(2)
    let finishA!: (value: unknown) => void
    const clientA = {
      sendRequest: vi.fn(
        () =>
          new Promise((resolve) => {
            finishA = resolve
          })
      )
    }
    const clientB = { sendRequest: vi.fn().mockResolvedValue({ window: 'b' }) }
    register(a, '/a', clientA)
    register(b, '/b', clientB)
    const pendingA = request(a, 'thread/start', {})
    await expect(request(b, 'thread/start', {})).resolves.toEqual({ window: 'b' })
    expect(clientA.sendRequest).toHaveBeenCalledWith(
      'thread/start',
      expect.objectContaining({
        identity: expect.objectContaining({ workspacePath: '/a' })
      }),
      undefined
    )
    expect(clientB.sendRequest).toHaveBeenCalledWith(
      'thread/start',
      expect.objectContaining({
        identity: expect.objectContaining({ workspacePath: '/b' })
      }),
      undefined
    )
    finishA({ window: 'a' })
    await expect(pendingA).resolves.toEqual({ window: 'a' })
    expect(
      vi.mocked(ipcMain.on).mock.calls.filter(([channel]) => channel === 'appserver:request')
    ).toHaveLength(1)
  })

  it('switches and closes one window without removing the other window handlers', () => {
    const a = window(11),
      b = window(12)
    const client = { sendRequest: vi.fn() }
    const scopeA = register(a, '/a', client)
    register(b, '/b', client)
    scopeA.dispose()
    register(a, '/new-a', client)
    expect(invoke(a, 'window:get-workspace-path')).toBe('/new-a')
    expect(invoke(b, 'window:get-workspace-path')).toBe('/b')
    a.emit('closed')
    expect(() => invoke(a, 'window:get-workspace-path')).toThrow('active Desktop window')
    expect(invoke(b, 'window:get-workspace-path')).toBe('/b')
    expect(() => invoke(window(999), 'window:get-workspace-path')).toThrow('active Desktop window')
  })

  it('rejects cross-window answers and cancellations without settling the rightful request', async () => {
    const a = window(21),
      b = window(22)
    register(a, '/a', null)
    register(b, '/b', null)
    const settled = vi.fn()
    const request = createServerRequestBridge(settled, a)
    broadcastServerRequest(a, { bridgeId: request.bridgeId, method: 'item/approval/request', params: {} })
    expect(() => invoke(b, 'appserver:server-response', request.bridgeId, { decision: 'accept' })).toThrow(
      'different Desktop window'
    )
    expect(() => invoke(b, 'appserver:cancel-server-request', request.bridgeId)).toThrow('different Desktop window')
    expect(settled).not.toHaveBeenCalled()
    invoke(a, 'appserver:server-response', request.bridgeId, { decision: 'decline' })
    await expect(request.promise).resolves.toEqual({ decision: 'decline' })
    expect(settled).toHaveBeenCalledOnce()
    expect(b.webContents.send).not.toHaveBeenCalled()
  })
})
