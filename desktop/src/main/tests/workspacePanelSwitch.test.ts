import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

const calls = vi.hoisted(() => ({ destroy: vi.fn(), invalidate: vi.fn(), revoke: vi.fn() }))
vi.mock('../viewerBrowser', () => ({ viewerBrowserManager: { destroyAllTabs: calls.destroy } }))
vi.mock('../windowFileAccess', () => ({ invalidateForegroundFileAccess: calls.invalidate, revokeAllWindowFileAccess: calls.revoke }))
vi.mock('../defaultChatWorkspace', () => ({
  isDefaultChatWorkspace: (path: string) => path === '/chats', ensureDefaultChatWorkspace: vi.fn()
}))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: vi.fn() }))
vi.mock('../settings', () => ({ forgetLocalProject: vi.fn(), saveLocalProjectDetails: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ listSetupModels: vi.fn(), loginSetupProvider: vi.fn(), runWorkspaceSetup: vi.fn() }))

import { applyWorkspaceThreadNotification } from '../windows/windowProjects'
import { recordForegroundWorkspace } from '../windows/windowSecondaryConnections'
import { buildCallbacks, clearWorkspaceSelection, switchForegroundWorkspace } from '../windows/windowWorkspaceActions'

function fixture() {
  return {
    mainWindow: { webContents: { id: 101 }, isDestroyed: () => false, setTitle: vi.fn() },
    currentWorkspacePath: '/a', settings: { locale: 'en' },
    app: { persistWindowSettings: vi.fn() },
    recordForegroundWorkspace: vi.fn(), emitWorkspaceProjects: vi.fn(),
    connectToAppServer: vi.fn(async () => true), workspaceTitleName: () => 'B',
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

  it('rejects an existing-only switch if the refreshed project list no longer includes the destination', async () => {
    const ctx = fixture()
    const paths = new Set(['/removed'])
    Object.assign(ctx.app, {
      localProjects: {
        refresh: vi.fn(async () => {
          paths.delete('/removed')
          return true
        }),
        has: (path: string) => paths.has(path)
      }
    })

    await expect(switchForegroundWorkspace(ctx, '/removed', undefined, { existingProjectOnly: true })).rejects.toThrow()
    expect(calls.invalidate).not.toHaveBeenCalled()
    expect(ctx.recordForegroundWorkspace).not.toHaveBeenCalled()
    expect(ctx.connectToAppServer).not.toHaveBeenCalled()
  })

  it('discards a history switch still refreshing when a newer workspace switch starts', async () => {
    const ctx = fixture()
    const refresh = Promise.withResolvers<boolean>()
    Object.assign(ctx.app, { localProjects: { refresh: () => refresh.promise, has: () => true } })

    const history = switchForegroundWorkspace(ctx, '/history', undefined, { existingProjectOnly: true })
    await switchForegroundWorkspace(ctx, '/newer')
    refresh.resolve(true)

    expect(await history).toBe(false)
    expect(ctx.recordForegroundWorkspace).toHaveBeenCalledExactlyOnceWith('/newer', true)
    expect(ctx.connectToAppServer).toHaveBeenCalledExactlyOnceWith('/newer')
  })

  it('does not finish a history switch superseded while its AppServer is connecting', async () => {
    const ctx = fixture()
    const started = Promise.withResolvers<void>()
    const connection = Promise.withResolvers<boolean>()
    Object.assign(ctx.app, { localProjects: { refresh: async () => true, has: () => true } })
    ctx.connectToAppServer = vi.fn(async (path: string) => {
      if (path !== '/history') return true
      started.resolve()
      return connection.promise
    })

    const history = switchForegroundWorkspace(ctx, '/history', undefined, { existingProjectOnly: true })
    await started.promise
    await switchForegroundWorkspace(ctx, '/newer')
    connection.resolve(false)

    expect(await history).toBe(false)
    expect(ctx.mainWindow!.setTitle).toHaveBeenCalledTimes(1)
  })

  it.each(['/registered', '/chats'])('switches to %s without registering it in existing-only mode', async path => {
    const ctx = fixture()
    const open = vi.fn()
    Object.assign(ctx.app, { localProjects: {
      refresh: vi.fn(async () => true), has: (path: string) => path === '/registered', open
    } })
    ctx.recordForegroundWorkspace = (workspacePath, addProject) => recordForegroundWorkspace(ctx, workspacePath, addProject)

    await expect(switchForegroundWorkspace(ctx, path, undefined, { existingProjectOnly: true })).resolves.toBeUndefined()

    expect(ctx.connectToAppServer).toHaveBeenCalledExactlyOnceWith(path)
    expect(open).not.toHaveBeenCalled()
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

  it('removes the archived thread from the owning connection without a server notification', async () => {
    const ctx = fixture()
    const sendRequest = vi.fn().mockResolvedValue({})
    const key = 'remote:device:computer:project'
    const thread = { id: 'thread', runtime: { activeTurnId: 'remote-turn' } }
    const entry = { key, connected: true, threads: [thread], client: { sendRequest } }
    const other = { key: '/same/path', connected: true, threads: [thread] }
    ctx.workspaceConnections = new Map([[key, entry as never], [other.key, other as never]])
    ctx.getWorkspaceConnection = vi.fn()
    ctx.refreshConnectionThreadList = vi.fn(async () => {})
    ctx.applyWorkspaceThreadNotification = (target, method, params) =>
      applyWorkspaceThreadNotification(ctx, target, method, params)

    await buildCallbacks(ctx).archiveThreadInWorkspace?.('/same/path', 'thread', key)

    expect(sendRequest).toHaveBeenCalledExactlyOnceWith('thread/archive', { threadId: 'thread' })
    expect(entry.threads).toEqual([])
    expect(other.threads).toEqual([thread])
    expect(ctx.getWorkspaceConnection).not.toHaveBeenCalled()
    expect(ctx.refreshConnectionThreadList).not.toHaveBeenCalled()
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
