import { EventEmitter } from 'node:events'
import { tmpdir } from 'node:os'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { BrowserWindow, IpcMainInvokeEvent } from 'electron'
import type { DesktopAppServerClient } from '../DesktopAppServerClient'
import type { IpcHandlerCallbacks } from '../ipcBridgeContracts'

const mocks = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => unknown>(),
  prepare: vi.fn(),
  registerRoute: vi.fn(),
  removeRoute: vi.fn()
}))
vi.mock('electron', () => ({
  app: { getPath: () => tmpdir() },
  ipcMain: {
    handle: (channel: string, handler: (...args: unknown[]) => unknown) => mocks.handlers.set(channel, handler),
    removeHandler: (channel: string) => mocks.handlers.delete(channel)
  }
}))
vi.mock('../pluginFileProtocol', () => ({
  registerDesktopPluginModuleRoute: mocks.registerRoute,
  removeDesktopPluginModuleRoute: mocks.removeRoute
}))
vi.mock('../desktopPluginArtifactCache', async () => {
  const actual = await vi.importActual<typeof import('../desktopPluginArtifactCache')>('../desktopPluginArtifactCache')
  return {
    ...actual,
    DesktopPluginArtifactCache: class {
      prepare = mocks.prepare
      prune = vi.fn().mockResolvedValue(undefined)
    }
  }
})
import { desktopPluginSourceKey } from '../desktopPluginArtifactCache'
import { registerDesktopPluginModuleIpc, unregisterDesktopPluginModuleIpc } from '../desktopPluginModuleIpc'
import { createWindowIpcMain, type WindowIpcMain } from '../windowIpc'

const scopes: { owner: BrowserWindow; ipc: WindowIpcMain }[] = []
afterEach(() => {
  for (const { owner, ipc } of scopes.splice(0)) {
    unregisterDesktopPluginModuleIpc(false, owner.webContents.id)
    ipc.dispose()
  }
  vi.clearAllMocks()
})
function registration(id: number, workspace: string, client: DesktopAppServerClient) {
  const owner = Object.assign(new EventEmitter(), {
    isDestroyed: () => false,
    webContents: { id, isDestroyed: () => false, send: vi.fn() }
  }) as unknown as BrowserWindow
  const ipc = createWindowIpcMain(owner)
  scopes.push({ owner, ipc })
  const callbacks = {
    getSettings: () => ({
      connectionMode: 'remote',
      remote: { url: `ws://host-${id}:9100` },
      remoteDesktopPluginGrants: [desktopPluginSourceKey(`ws://host-${id}:9100/`, workspace)]
    }),
    getWorkspaceStatus: () => ({ workspacePath: workspace }),
    getConnectionStatus: () => ({ capabilities: { desktopPluginArtifacts: true } })
  } as unknown as IpcHandlerCallbacks
  registerDesktopPluginModuleIpc(() => client, callbacks, workspace, ipc, id)
  return { owner, ipc, callbacks }
}
function invoke(owner: BrowserWindow, channel: string, ...args: unknown[]): unknown {
  return mocks.handlers.get(channel)!({ sender: owner.webContents } as IpcMainInvokeEvent, ...args)
}
function client(workspacePath: string) {
  return { sendRequest: vi.fn().mockResolvedValue({ workspacePath, plugins: [] }) } as unknown as DesktopAppServerClient
}

describe('Desktop extension IPC window ownership', () => {
  it('keeps context and client bindings isolated through another window registering, switching and closing', async () => {
    const clientA = client('/remote-a'),
      clientB = client('/remote-b')
    const a = registration(1, '/remote-a', clientA)
    const b = registration(2, '/remote-b', clientB)
    await expect(invoke(a.owner, 'desktop-plugin:context')).resolves.toMatchObject({ workspacePath: '/remote-a' })
    await expect(invoke(b.owner, 'desktop-plugin:context')).resolves.toMatchObject({ workspacePath: '/remote-b' })
    unregisterDesktopPluginModuleIpc(true, a.owner.webContents.id)
    expect(() => invoke(a.owner, 'desktop-plugin:context')).toThrow('active Desktop window')
    await expect(invoke(b.owner, 'desktop-plugin:context')).resolves.toMatchObject({ workspacePath: '/remote-b' })
    const replacement = client('/new-a')
    registerDesktopPluginModuleIpc(() => replacement, a.callbacks, '/new-a', a.ipc, a.owner.webContents.id)
    await expect(invoke(a.owner, 'desktop-plugin:context')).resolves.toMatchObject({ workspacePath: '/new-a' })
    unregisterDesktopPluginModuleIpc(false, a.owner.webContents.id)
    expect(() => invoke(a.owner, 'desktop-plugin:context')).toThrow('active Desktop window')
    await expect(invoke(b.owner, 'desktop-plugin:context')).resolves.toMatchObject({ workspacePath: '/remote-b' })
    expect(clientA.sendRequest).toHaveBeenCalledOnce()
    expect(replacement.sendRequest).toHaveBeenCalledOnce()
    expect(clientB.sendRequest).toHaveBeenCalledTimes(3)
  })

  it('rejects a pending artifact registration after its window closes without publishing a route', async () => {
    const remote = client('/remote-a')
    vi.mocked(remote.sendRequest).mockResolvedValue({
      workspacePath: '/remote-a',
      plugins: [{ id: 'fixture.desktop', installed: true, enabled: true, desktop: { revision: 'revision-a' } }]
    } as never)
    const a = registration(11, '/remote-a', remote)
    const context = (await invoke(a.owner, 'desktop-plugin:context')) as { sourceKey: string }
    let release!: (root: string) => void
    let started!: () => void
    const preparationStarted = new Promise<void>((resolve) => {
      started = resolve
    })
    mocks.prepare.mockImplementationOnce(() => {
      started()
      return new Promise<string>((resolve) => {
        release = resolve
      })
    })
    const request = invoke(a.owner, 'desktop-plugin:register-module', {
      pluginId: 'fixture.desktop',
      revision: 'revision-a',
      rootPath: '/not-local',
      sourceKey: context.sourceKey
    })
    await preparationStarted
    unregisterDesktopPluginModuleIpc(false, a.owner.webContents.id)
    release('/downloaded/artifact')
    await expect(request).rejects.toThrow('source changed')
    expect(mocks.registerRoute).not.toHaveBeenCalled()
  })
})
