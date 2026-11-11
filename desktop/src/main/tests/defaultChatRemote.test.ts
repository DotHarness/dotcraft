import { EventEmitter } from 'node:events'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { HubEvent } from '../desktopHub'

const host = vi.hoisted(() => ({ createClient: vi.fn(), chatPath: '/fixture/chats' }))
vi.mock('electron', () => ({ app: { getPath: () => '', getLocale: () => 'en-US' } }))
vi.mock('../defaultChatWorkspace', () => ({
  resolveDefaultChatWorkspacePath: () => host.chatPath,
  isDefaultChatWorkspace: (path: string) => path === host.chatPath,
  ensureDefaultChatWorkspace: vi.fn()
}))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: { fromWebSocket: host.createClient } }))
vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: () => ({}) }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ getWorkspaceStatus: () => ({ status: 'ready' }) }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: {} }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))

import { connectViaWebSocket } from '../windows/windowConnections'
import { buildDefaultChatSummary, disposeWorkspaceConnection, getWorkspaceProjectsPayload, refreshConnectionThreadList } from '../windows/windowProjects'
import { createSecondaryWorkspaceConnection, ensureDefaultChatConnection, isHubAppServerLifecycleEvent, isSecondaryProjectEvent, refreshSecondaryWorkspaceConnections, startHubEventSubscription } from '../windows/windowSecondaryConnections'

class Client extends EventEmitter {
  hasRetainedThreadAttachments = false
  onNotification(): void {}
  onServerRequest(): void {}
  sendRequest = vi.fn(async () => ({ data: [{ id: 'fixture-local-chat' }] }))
  dispose = vi.fn(() => { this.emit('disposed') })
}

const initialized = { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} }
const localEndpoint = 'ws://127.0.0.1:1234/ws'
const remoteKey = 'remote:device:fixture:project'

function fixture() {
  let created!: (client: Client) => void
  const localCreated = new Promise<Client>(resolve => { created = resolve })
  host.createClient.mockImplementation((url: string) => {
    const client = new Client()
    if (url === localEndpoint) created(client)
    return client
  })
  let publish!: (event: HubEvent) => void
  const hub = {
    ensureAppServer: vi.fn(async () => ({ endpoints: { appServerWebSocket: localEndpoint } })),
    listAppServers: vi.fn(async () => []),
    subscribeEvents: vi.fn(async (listener: (event: HubEvent) => void) => { publish = listener })
  }
  const ctx = {
    argv: [], settings: { connectionMode: 'remote', locale: 'en' }, currentWorkspacePath: host.chatPath,
    activeRemoteProject: {
      projectId: remoteKey, source: 'device', displayPath: '/fixture/remote', identityWorkspacePath: '/fixture/remote',
      name: 'Fixture remote', remote: { source: 'device' }
    },
    activeRemoteWorkspace: { source: 'device' }, workspaceConnections: new Map(), startupProjectStates: new Map(),
    wireClient: new Client(), mainWindow: { isDestroyed: () => false },
    app: { createHubClient: () => hub, localProjects: { list: () => [], has: () => false },
      pairedComputers: { savedProjects: () => [] } },
    runtimeIsStopping: () => false, resolveConnectionMode: () => 'remote',
    normalizeWorkspaceConnectionKey: (path: string) => path, localProjectId: (path: string) => path,
    getPinnedThreadIdsForWorkspace: () => [], getPinnedThreadIdsForProject: () => [], isProjectPinned: () => false,
    emitWorkspaceProjects: vi.fn(), emitConnectionStatus: vi.fn(), reregisterIpcForWorkspace: vi.fn(),
    clearPendingDeviceOpenRetry: vi.fn(), resetThreadToolState: vi.fn(), flushPendingWorkspaceOpenThread: vi.fn(),
    scheduleSecondaryWorkspaceRefresh: vi.fn(), disposeDeviceProjectConnection: vi.fn(),
    makeThreadListIdentity: (path: string) => ({ workspacePath: path })
  } as unknown as DesktopWindowRuntime
  ctx.getWorkspaceConnection = path => ctx.workspaceConnections.get(path)
  ctx.isWorkspaceForeground = path => !ctx.activeRemoteProject && ctx.currentWorkspacePath === path
  ctx.buildDefaultChatSummary = () => buildDefaultChatSummary(ctx)
  ctx.getWorkspaceProjectsPayload = () => getWorkspaceProjectsPayload(ctx)
  ctx.ensureDefaultChatConnection = () => ensureDefaultChatConnection(ctx)
  ctx.refreshSecondaryWorkspaceConnections = () => refreshSecondaryWorkspaceConnections(ctx)
  ctx.createSecondaryWorkspaceConnection = (...args) => createSecondaryWorkspaceConnection(ctx, ...args)
  ctx.disposeWorkspaceConnection = entry => disposeWorkspaceConnection(ctx, entry)
  ctx.refreshConnectionThreadList = vi.fn(entry => refreshConnectionThreadList(ctx, entry))
  ctx.isHubAppServerLifecycleEvent = event => isHubAppServerLifecycleEvent(ctx, event)
  ctx.isSecondaryProjectEvent = event => isSecondaryProjectEvent(ctx, event)
  ctx.isCurrentWorkspaceEvent = (event, path) => event.workspacePath === path
  ctx.connectViaWebSocket = vi.fn((...args) => connectViaWebSocket(ctx, ...args))
  return { ctx, hub, localCreated, publish: (event: HubEvent) => publish(event) }
}

beforeEach(() => vi.clearAllMocks())

describe('local Recents with a remote foreground', () => {
  it('ensures and loads local Chats on remote startup without taking over the foreground', async () => {
    const { ctx, hub, localCreated } = fixture()
    await ctx.connectViaWebSocket(host.chatPath, 'ws://127.0.0.1:2345/ws', {
      projectId: remoteKey, projectKind: 'remote', identityWorkspacePath: '/fixture/remote'
    })
    const remote = ctx.wireClient as unknown as Client
    remote.emit('ready', initialized)
    const local = await localCreated
    local.emit('ready', initialized)
    await vi.mocked(ctx.refreshConnectionThreadList).mock.results[0].value

    expect(hub.ensureAppServer).toHaveBeenCalledWith(host.chatPath, expect.anything())
    const payload = ctx.getWorkspaceProjectsPayload()
    expect(payload.foregroundProjectId).toBe(remoteKey)
    expect(payload.chat).toMatchObject({
      path: host.chatPath, kind: 'chat', state: 'secondary', loaded: true, threads: [{ id: 'fixture-local-chat' }]
    })
    expect(ctx.wireClient).toBe(remote)
    expect(ctx.reregisterIpcForWorkspace).toHaveBeenCalledTimes(1)
    expect(ctx.emitConnectionStatus).toHaveBeenLastCalledWith(ctx.mainWindow, expect.objectContaining({ status: 'connected' }))
  })

  it('replaces a stopped local Chat endpoint during remote-mode refresh without discovering other local projects', async () => {
    const { ctx, hub } = fixture()
    const remote = ctx.wireClient
    const old = ctx.createSecondaryWorkspaceConnection(host.chatPath, 'ws://127.0.0.1:1111/ws', { kind: 'chat' })
    old.connecting = false
    await refreshSecondaryWorkspaceConnections(ctx)
    const recovered = ctx.getWorkspaceConnection(host.chatPath)!
    expect(recovered.wsUrl).toBe(localEndpoint)
    expect(old.client.dispose).toHaveBeenCalledOnce()
    expect(hub.listAppServers).not.toHaveBeenCalled()
    expect(ctx.wireClient).toBe(remote)
  })

  it('routes local Chat Hub lifecycle events to background refresh, not the remote foreground', () => {
    const { ctx, hub, publish } = fixture()
    const remote = ctx.wireClient
    startHubEventSubscription(ctx, host.chatPath, hub as never)
    publish({ kind: 'appserver.exited', workspacePath: host.chatPath, at: '2026-11-08T00:00:00Z' })
    publish({ kind: 'appserver.running', workspacePath: host.chatPath, at: '2026-11-08T00:00:01Z',
      data: { endpoints: { appServerWebSocket: localEndpoint } } })
    expect(ctx.scheduleSecondaryWorkspaceRefresh).toHaveBeenCalledTimes(2)
    expect(remote!.dispose).not.toHaveBeenCalled()
    expect(ctx.connectViaWebSocket).not.toHaveBeenCalled()
    expect(ctx.emitConnectionStatus).not.toHaveBeenCalled()
    expect(ctx.wireClient).toBe(remote)
    ctx.hubEventAbortController?.abort()
  })

  it('keeps remote-only navigation scoped while allowing explicit local consumers', async () => {
    const { ctx, hub } = fixture()
    ctx.argv = ['--remote', 'ws://127.0.0.1:2345/ws']
    expect(ctx.buildDefaultChatSummary()).toBeUndefined()
    await ctx.refreshSecondaryWorkspaceConnections()
    expect(hub.ensureAppServer).not.toHaveBeenCalled()
    await ctx.ensureDefaultChatConnection()
    expect(hub.ensureAppServer).toHaveBeenCalledWith(host.chatPath, expect.anything())
  })
})
