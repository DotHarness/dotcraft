import { EventEmitter } from 'node:events'
import { describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { emitConnectionStatus } from '../windows/windowStatus'
import { buildCallbacks } from '../windows/windowWorkspaceActions'

vi.mock('electron', () => ({}))
vi.mock('../ipcBridge', () => ({
  broadcastConnectionStatus: vi.fn(), sanitizeHttpOrHttpsUrl: (value: unknown) => value
}))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../viewerBrowser', () => ({ viewerBrowserManager: {} }))
vi.mock('../windowFileAccess', () => ({}))
vi.mock('../defaultChatWorkspace', () => ({}))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../ripgrepRuntime', () => ({}))
vi.mock('../settings', () => ({}))
vi.mock('../workspaceSetup', () => ({ runWorkspaceSetup: vi.fn(async () => {}) }))

function fixture() {
  const restore = vi.fn(async () => { ctx.startupProjectRestore = undefined })
  const ctx = {
    app: { restoreRecentProjects: restore, refreshAppMenu: vi.fn() },
    mainWindow: { webContents: { id: 101 } },
    currentWorkspacePath: '/project', settings: {},
    startupProjectRestore: 'pending',
    emitCurrentWorkspaceStatus: vi.fn()
  } as unknown as DesktopWindowRuntime
  return { ctx, restore }
}

describe('initial foreground readiness', () => {
  it('restores recent projects after setup completes and the foreground connection becomes ready', async () => {
    const { ctx, restore } = fixture()
    const client = new EventEmitter()
    ctx.connectToAppServer = vi.fn(async () => {
      emitConnectionStatus(ctx, ctx.mainWindow!, { status: 'connecting' })
      client.once('ready', () => emitConnectionStatus(ctx, ctx.mainWindow!, { status: 'connected' }))
      return true
    })

    await buildCallbacks(ctx).onRunWorkspaceSetup({ providerMode: 'skip', model: '', preference: {} })
    expect(restore).not.toHaveBeenCalled()
    client.emit('ready')
    expect(restore).toHaveBeenCalledExactlyOnceWith(ctx)
    expect(ctx.currentWorkspacePath).toBe('/project')
    emitConnectionStatus(ctx, ctx.mainWindow!, { status: 'connected' })
    expect(restore).toHaveBeenCalledTimes(1)
  })

  it('does not turn later foreground readiness into startup restore when launch was ineligible', () => {
    const { ctx, restore } = fixture()
    ctx.startupProjectRestore = undefined
    emitConnectionStatus(ctx, ctx.mainWindow!, { status: 'connected' })
    expect(restore).not.toHaveBeenCalled()
  })
})
