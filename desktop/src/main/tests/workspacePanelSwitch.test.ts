import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

const calls = vi.hoisted(() => ({ destroy: vi.fn(), invalidate: vi.fn(), revoke: vi.fn() }))
vi.mock('../viewerBrowser', () => ({ viewerBrowserManager: { destroyAllTabs: calls.destroy } }))
vi.mock('../windowFileAccess', () => ({ invalidateForegroundFileAccess: calls.invalidate, revokeAllWindowFileAccess: calls.revoke }))
vi.mock('../defaultChatWorkspace', () => ({ isDefaultChatWorkspace: () => false, ensureDefaultChatWorkspace: vi.fn() }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: vi.fn() }))
vi.mock('../settings', () => ({ forgetLocalProject: vi.fn(), saveLocalProjectDetails: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ listSetupModels: vi.fn(), loginSetupProvider: vi.fn(), runWorkspaceSetup: vi.fn() }))

import { buildCallbacks, clearWorkspaceSelection, switchForegroundWorkspace } from '../windows/windowWorkspaceActions'

function fixture() {
  return {
    mainWindow: { webContents: { id: 101 }, isDestroyed: () => false, setTitle: vi.fn() },
    currentWorkspacePath: '/a', settings: { locale: 'en' },
    app: { persistWindowSettings: vi.fn() },
    recordForegroundWorkspace: vi.fn(), emitWorkspaceProjects: vi.fn(),
    connectToAppServer: vi.fn(async () => {}), workspaceTitleName: () => 'B',
    teardownRuntime: vi.fn(async () => {}), deviceOpenGeneration: 0,
    clearPendingDeviceOpenRetry: vi.fn(), demoteDeviceProject: vi.fn(() => false),
    closeActiveRemoteForwards: vi.fn(), reregisterIpcForWorkspace: vi.fn(),
    emitWorkspaceStatus: vi.fn(), getWorkspaceStatusForRenderer: vi.fn(), emitConnectionStatus: vi.fn()
  } as unknown as DesktopWindowRuntime
}

beforeEach(() => vi.clearAllMocks())
describe('panel resources on foreground workspace changes', () => {
  it('only invalidates foreground file admission while preserving browser pages and preview grants', async () => {
    const ctx = fixture()
    await switchForegroundWorkspace(ctx, '/b')
    expect(calls.invalidate).toHaveBeenCalledExactlyOnceWith(101)
    expect(calls.revoke).not.toHaveBeenCalled()
    expect(calls.destroy).not.toHaveBeenCalled()
    expect(ctx.teardownRuntime).not.toHaveBeenCalled()
    expect(ctx.connectToAppServer).toHaveBeenCalledExactlyOnceWith('/b')
  })

  it('promotes a retained remote by connection key without initializing its path locally', async () => {
    const ctx = fixture()
    const entry = { initializeResult: {}, key: 'remote:manual:server' }
    ctx.workspaceConnections = new Map([[entry.key, entry as never]])
    ctx.promoteWorkspaceConnection = vi.fn(async () => true)
    await switchForegroundWorkspace(ctx, '/remote/path', entry.key)
    expect(ctx.promoteWorkspaceConnection).toHaveBeenCalledExactlyOnceWith(entry)
    expect(ctx.connectToAppServer).not.toHaveBeenCalled()
  })

  it('archives through the requested remote connection instead of a local workspace with the same path', async () => {
    const ctx = fixture()
    const sendRequest = vi.fn(async () => ({}))
    const key = 'remote:device:computer:project'
    ctx.workspaceConnections = new Map([[key, { key, connected: true, client: { sendRequest } } as never]])
    ctx.getWorkspaceConnection = vi.fn()
    ctx.refreshConnectionThreadList = vi.fn(async () => {})
    await buildCallbacks(ctx).archiveThreadInWorkspace?.('/same/path', 'thread', key)
    expect(ctx.getWorkspaceConnection).not.toHaveBeenCalled()
    expect(sendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread' })
  })

  it('keeps explicit workspace clearing as a real teardown with grant revocation', async () => {
    const ctx = fixture()
    await clearWorkspaceSelection(ctx)
    expect(calls.revoke).toHaveBeenCalledExactlyOnceWith(101)
    expect(calls.destroy).toHaveBeenCalledExactlyOnceWith(ctx.mainWindow)
    expect(ctx.teardownRuntime).toHaveBeenCalledWith('clear workspace selection', { clearWorkspace: true })
    expect(ctx.currentWorkspacePath).toBe('')
  })
})
