import { EventEmitter } from 'node:events'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { HubEvent } from '../desktopHub'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'

const host = vi.hoisted(() => ({ createClient: vi.fn(), chatPath: '/fixture/chats' }))
vi.mock('electron', () => ({ app: { getPath: () => '', getLocale: () => 'en-US' } }))
vi.mock('../defaultChatWorkspace', () => ({
  resolveDefaultChatWorkspacePath: () => host.chatPath,
  isDefaultChatWorkspace: (path: string) => path === host.chatPath,
  ensureDefaultChatWorkspace: vi.fn()
}))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: { fromWebSocket: host.createClient } }))
vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../browserUseManagers', () => ({ browserUseManagers: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: () => ({}) }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ getWorkspaceStatus: () => ({ status: 'ready' }) }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: {} }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))

import { connectViaWebSocket } from '../windows/windowConnections'
import { buildDefaultChatSummary, disposeWorkspaceConnection, getWorkspaceProjectsPayload, refreshConnectionThreadList } from '../windows/windowProjects'
import { createSecondaryWorkspaceConnection, enforceSecondaryWorkspaceBudget, ensureDefaultChatConnection, getManagedAppServerEndpoint, isHubAppServerLifecycleEvent, isSecondaryProjectEvent, refreshSecondaryWorkspaceConnections, startHubEventSubscription, startLocalProjectConnection } from '../windows/windowSecondaryConnections'
import { connectLinkedChats, disconnectedThreadProjectName, listThreadProjects, openThreadProject, threadToolsHoldConnection } from '../windows/windowThreadProjects'
import { ThreadProjectError } from '../threadTasks/threadProjects'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

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
  const store = new ThreadTaskStore(null)
  const tasks = new DesktopThreadTasks({ store, getNewTaskPrompt: () => 'ask' })
  const ctx = {
    getThreadTasks: () => tasks,
    desktopThreadToolOptions: () => ({}),
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
    scheduleSecondaryWorkspaceRefresh: vi.fn(), disposeDeviceProjectConnection: vi.fn(), startHubEventSubscription: vi.fn(),
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
  return { ctx, hub, store, tasks, localCreated, publish: (event: HubEvent) => publish(event) }
}

beforeEach(() => vi.clearAllMocks())

describe('background project connection for thread tools', () => {
  const project = { id: '/projects/library', path: '/projects/library', name: 'Library', running: false }

  function projectFixture() {
    const state = fixture()
    const { ctx } = state
    vi.spyOn(ctx.app.localProjects, 'has').mockReturnValue(true)
    ctx.getManagedAppServerEndpoint = value => getManagedAppServerEndpoint(ctx, value)
    ctx.startLocalProjectConnection = path => startLocalProjectConnection(ctx, path)
    return state
  }

  it('starts through Hub, waits for the new secondary connection, and leaves the foreground untouched', async () => {
    const { ctx, hub, localCreated } = projectFixture()
    const foreground = ctx.wireClient
    const started = openThreadProject(ctx, project)
    expect(openThreadProject(ctx, project)).toBe(started)
    const client = await localCreated
    const entry = ctx.workspaceConnections.get(project.id)!
    expect(hub.ensureAppServer).toHaveBeenCalledTimes(1)
    expect(hub.ensureAppServer).toHaveBeenCalledWith(project.path, expect.anything())
    expect(threadToolsHoldConnection(ctx, entry)).toBe(true)
    expect(entry.connected).toBe(false)
    client.emit('ready', initialized)
    expect(await started).toBe(entry)
    expect(entry).toMatchObject({ role: 'secondary', connected: true })
    expect(ctx.wireClient).toBe(foreground)
    expect(threadToolsHoldConnection(ctx, entry)).toBe(false)
  })

  it('fails the open when the project disconnects before becoming ready', async () => {
    const { ctx, localCreated } = projectFixture()
    const started = openThreadProject(ctx, project)
    const failed = expect(started).rejects.toThrow()
    const client = await localCreated
    await Promise.resolve()
    client.emit('close')
    await failed
  })
})

describe('remote projects as thread tool targets', () => {
  const studio = 'remote:device:studio:app'
  const build = 'remote:ssh:build:api'
  const stack = 'remote:servers:build:stack'

  function remoteEntry(projectId: string, source: string, connected: boolean): WorkspaceConnectionEntry {
    return {
      key: projectId, projectId, kind: 'remote', workspacePath: `/remote/${projectId.split(':').at(-1)}`, role: 'secondary',
      remote: { source, serverName: `${source} machine` }, remoteProject: { name: `${source} project` },
      connected, connecting: false, client: new Client(), threads: [], lastUsedAt: 0
    } as unknown as WorkspaceConnectionEntry
  }

  function remoteFixture() {
    const state = fixture()
    const { ctx, hub } = state
    Object.assign(ctx.app.localProjects, { refresh: vi.fn(async () => undefined) })
    Object.assign(ctx, {
      settings: { ...ctx.settings, remoteHosts: [{ id: 'build', name: 'Build box', source: 'manual', hostname: 'build.example',
        projects: [{ id: 'api', path: '/srv/api', label: 'API' }] }] },
      sshRemoteProjectId: (machine: string, project: string) => `remote:ssh:${machine}:${project}`,
      deviceRemoteProjectId: (computer: string, project: string) => `remote:device:${computer}:${project}`
    })
    for (const entry of [remoteEntry(studio, 'device', true), remoteEntry(build, 'ssh', false), remoteEntry(stack, 'servers', true)]) {
      ctx.workspaceConnections.set(entry.key, entry)
    }
    return { ...state, ctx, hub }
  }

  it('lists only connected SSH and paired-computer projects, with their machine', async () => {
    const { ctx } = remoteFixture()
    const remote = (await listThreadProjects(ctx)).filter(project => project.id.startsWith('remote:'))
    expect(remote).toEqual([
      { id: studio, name: 'device project', path: '/remote/app', running: true, machine: 'device machine' }
    ])
  })

  it('uses the open connection and never opens one for a disconnected remote project', async () => {
    const { ctx, hub } = remoteFixture()
    expect(await openThreadProject(ctx, { id: studio, name: 'device project', path: '/remote/app' }))
      .toBe(ctx.workspaceConnections.get(studio))
    const refused = openThreadProject(ctx, { id: build, name: 'API', path: '/srv/api' })
    await expect(refused).rejects.toBeInstanceOf(ThreadProjectError)
    await expect(refused).rejects.toMatchObject({ code: 'ProjectUnavailable' })
    expect(hub.ensureAppServer).not.toHaveBeenCalled()
    expect(ctx.connectViaWebSocket).not.toHaveBeenCalled()
  })

  it('names saved remote projects so a disconnected one is reported as unavailable', () => {
    const { ctx } = remoteFixture()
    expect(disconnectedThreadProjectName(ctx, build)).toBe('API')
    expect(disconnectedThreadProjectName(ctx, 'remote:ssh:build:gone')).toBeUndefined()
  })
})

describe('local projects and linked chats under a remote foreground', () => {
  const library = '/projects/library'
  const libraryEndpoint = 'ws://127.0.0.1:5555/ws'

  function linkedFixture(needed: string[]) {
    const state = fixture()
    const { ctx, hub } = state
    const linkedChats = {
      needsProjectConnection: vi.fn((projectId: string) => needed.includes(projectId)),
      holdsConnection: vi.fn((connection: { projectId: string }) => needed.includes(connection.projectId)),
      connected: vi.fn(), disconnected: vi.fn(), handleNotification: vi.fn()
    }
    Object.assign(ctx.app, { tasks: { linkedChats } })
    Object.assign(ctx.app.localProjects, { list: () => [{ path: library, displayName: 'Library', running: true }], has: (path: string) => path === library })
    ctx.isRunningHubAppServer = () => true
    hub.listAppServers.mockResolvedValue([{ workspacePath: library, endpoints: { appServerWebSocket: libraryEndpoint } }] as never)
    return { ...state, linkedChats }
  }

  it('keeps a remote secondary with a running linked chat out of the idle budget', () => {
    const { ctx, linkedChats } = linkedFixture(['remote:device:studio:app'])
    const remote = ['remote:device:studio:app', ...Array.from({ length: 9 }, (_, index) => `remote:device:idle:${index}`)]
    remote.forEach((projectId, index) => {
      const entry = {
        key: projectId, projectId, kind: 'remote', workspacePath: `/remote/${index}`, role: 'secondary',
        remote: { source: 'device' }, connected: true, connecting: false, client: new Client(), threads: [], lastUsedAt: index
      } as unknown as WorkspaceConnectionEntry
      ctx.workspaceConnections.set(projectId, entry)
      connectLinkedChats(ctx, entry)
    })
    enforceSecondaryWorkspaceBudget(ctx)
    expect(ctx.workspaceConnections.has('remote:device:studio:app')).toBe(true)
    expect(ctx.workspaceConnections.has('remote:device:idle:0')).toBe(false)
    expect(linkedChats.connected).toHaveBeenCalledTimes(remote.length)
  })

  it('connects running local projects and follows their Hub events after a remote cold start', async () => {
    const { ctx } = linkedFixture([])
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(ctx.startHubEventSubscription).toHaveBeenCalledWith(host.chatPath, expect.anything())
    expect(ctx.workspaceConnections.get(library)).toMatchObject({ role: 'secondary', wsUrl: libraryEndpoint })
    expect(isSecondaryProjectEvent(ctx, { kind: 'appserver.running', workspacePath: library } as HubEvent)).toBe(true)
  })
})

describe('local Recents with a remote foreground', () => {
  it.each(['foreground', 'secondary'] as const)('reconciles a mounted chat after its %s connection reconnects', async (role) => {
    const { ctx, store, tasks } = fixture()
    let entry
    if (role === 'foreground') {
      await ctx.connectViaWebSocket('/workspace', 'ws://fixture/ws')
      entry = ctx.workspaceConnections.get('/workspace')!
    } else {
      entry = ctx.createSecondaryWorkspaceConnection('/workspace', 'ws://fixture/ws')
    }
    const client = entry.client as unknown as Client
    client.emit('ready', initialized)
    entry.threads = [{ id: 'waiting-thread' }, { id: 'idle-thread' }]
    store.setQuestion('waiting-thread', { turnId: 'turn-1', callId: 'call-1' })
    client.emit('close')
    const changed = new Promise<void>((resolve) => store.onChanged((snapshot) => {
      if (snapshot.threadId === 'waiting-thread' && !snapshot.question) resolve()
    }))
    client.sendRequest.mockResolvedValue({ data: [{ id: 'turn-2', status: 'completed' }] } as never)
    client.sendRequest.mockClear()

    client.emit('reconnected', initialized)
    await changed

    expect(tasks.snapshot('waiting-thread').question).toBeUndefined()
    expect(client.sendRequest.mock.calls.filter(([method]) => method === 'thread/turns/list').map(([method, params]) => [method, params])).toEqual([
      ['thread/turns/list', { threadId: 'waiting-thread', limit: 1, sortDirection: 'descending', itemsView: 'none' }]
    ])
  })

  it('publishes a staged question when a mounted secondary chat completed while disconnected', async () => {
    const { ctx, store, tasks } = fixture()
    const entry = ctx.createSecondaryWorkspaceConnection('/workspace', 'ws://fixture/ws')
    const client = entry.client as unknown as Client
    client.emit('ready', initialized)
    entry.threads = [{ id: 'waiting-thread' }]
    tasks.stageQuestion('waiting-thread', 'turn-1', 'call-1')
    client.emit('close')
    const changed = new Promise<void>((resolve) => store.onChanged((snapshot) => {
      if (snapshot.threadId === 'waiting-thread' && snapshot.question) resolve()
    }))
    client.sendRequest.mockResolvedValue({ data: [{ id: 'turn-1', status: 'completed' }] } as never)

    client.emit('reconnected', initialized)
    await changed

    expect(tasks.snapshot('waiting-thread').question).toEqual({ turnId: 'turn-1', callId: 'call-1' })
  })

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

  it('replaces a stopped local Chat endpoint during remote-mode refresh', async () => {
    const { ctx, hub } = fixture()
    const remote = ctx.wireClient
    const old = ctx.createSecondaryWorkspaceConnection(host.chatPath, 'ws://127.0.0.1:1111/ws', { kind: 'chat' })
    old.connecting = false
    await refreshSecondaryWorkspaceConnections(ctx)
    const recovered = ctx.getWorkspaceConnection(host.chatPath)!
    expect(recovered.wsUrl).toBe(localEndpoint)
    expect(old.client.dispose).toHaveBeenCalledOnce()
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
