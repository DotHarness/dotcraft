import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { connectWorkspaceForLoadedWindow } from '../windows/windowStatus'

vi.mock('electron', () => ({}))
vi.mock('../ipcBridge', () => ({}))
vi.mock('../voice/voiceIpc', () => ({}))

function fixture(connected = false) {
  const entry = { kind: 'local', role: 'foreground', connected, client: new EventEmitter() }
  const restore = vi.fn(async () => {})
  const ctx = {
    app: { restoreRecentProjects: restore },
    connectToAppServer: vi.fn(async () => true),
    getWorkspaceConnection: vi.fn(() => entry),
    runtimeIsStopping: () => false
  } as unknown as DesktopWindowRuntime
  const win = { isDestroyed: () => false } as Electron.BrowserWindow
  return { ctx, win, entry, restore }
}

describe('initial window project restore readiness', () => {
  it('waits for initialize readiness rather than merely starting the foreground transport', async () => {
    const { ctx, win, entry, restore } = fixture()
    await connectWorkspaceForLoadedWindow(ctx, win, '/project')
    expect(restore).not.toHaveBeenCalled()
    entry.connected = true
    entry.client.emit('ready')
    expect(restore).toHaveBeenCalledExactlyOnceWith(ctx)
    entry.client.emit('ready')
    expect(restore).toHaveBeenCalledTimes(1)
  })

  it('restores immediately when startup reused an initialized foreground connection', async () => {
    const { ctx, win, restore } = fixture(true)
    await connectWorkspaceForLoadedWindow(ctx, win, '/project')
    expect(restore).toHaveBeenCalledExactlyOnceWith(ctx)
  })

  it('does not restore projects when the startup connection was replaced before readiness', async () => {
    const { ctx, win, entry, restore } = fixture()
    await connectWorkspaceForLoadedWindow(ctx, win, '/project')
    ctx.getWorkspaceConnection = () => undefined
    entry.client.emit('ready')
    expect(restore).not.toHaveBeenCalled()
  })
})
