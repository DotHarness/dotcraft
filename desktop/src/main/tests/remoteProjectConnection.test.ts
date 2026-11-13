import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { ActiveRemoteProject, WorkspaceConnectionEntry } from '../windows/windowTypes'

vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../defaultChatWorkspace', () => ({ isDefaultChatWorkspace: () => false }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../desktopRuntimeThreadTools', () => ({ resetDesktopThreadToolBindings: vi.fn() }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: vi.fn() }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ getWorkspaceStatus: vi.fn() }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: {} }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))
vi.mock('../viewerBrowser', () => ({ viewerBrowserManager: {} }))
vi.mock('../windowFileAccess', () => ({ invalidateForegroundFileAccess: vi.fn() }))
import { promoteWorkspaceConnection } from '../windows/windowSecondaryConnections'
import { switchForegroundWorkspace } from '../windows/windowWorkspaceActions'
import { demoteWorkspaceConnection, disposeWorkspaceConnection } from '../windows/windowProjects'
import { clearRemoteProjectReconnect, handleRemoteProjectEndpointChanged, reconnectRemoteProject, scheduleRemoteProjectReconnect } from '../windows/windowRemoteConnections'

function fixture(source: 'ssh' | 'servers') {
  const project: ActiveRemoteProject = {
    projectId: `remote:${source}:machine:project`, source, name: 'Project A', identityWorkspacePath: '/a', displayPath: '/a',
    localWorkspacePath: '/local', remote: { source, hostId: 'machine', remoteProjectId: 'project', stackId: 'stack' },
    status: { source }
  }
  const client = { connectionId: 'connection-a', dispose: vi.fn(), releaseViewAttachments: vi.fn(async () => {}) }
  const entry = {
    key: project.projectId, projectId: project.projectId, kind: 'remote', remote: project.remote, remoteProject: project,
    workspacePath: '/a', localWorkspacePath: '/local', role: 'secondary', connected: true, connecting: false,
    client, wsUrl: 'ws://127.0.0.1:1234/ws', threads: [], lastUsedAt: 1,
    initializeResult: { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} }
  } as unknown as WorkspaceConnectionEntry
  const previous = { key: '/b', role: 'foreground', client: { dispose: vi.fn() } } as unknown as WorkspaceConnectionEntry
  const manager = {
    openProject: vi.fn(async () => ({ machine: { id: 'machine' }, project: { id: 'project' }, localPort: 4321, wsUrl: 'ws://ssh/ws' })),
    closeProjectForward: vi.fn(),
    docker: { openAppServerTunnel: vi.fn(async () => ({ localPort: 4321, wsUrl: 'ws://docker/ws' })), closeStackTunnels: vi.fn() }
  }
  const ctx = {
    deviceOpenGeneration: 0, currentWorkspacePath: '/b', settings: { locale: 'en', remoteHosts: [{
      id: 'machine', name: 'Box', source: 'manual', hostname: 'box', autoConnect: true, projects: [],
      stacks: [{ id: 'stack', name: 'Stack', composeDir: '/compose', workspaceDir: '/a', appServerPort: 9100, dashboardPort: 9101, oratorioPort: 5087 }]
    }] },
    workspaceConnections: new Map([[entry.key, entry], [previous.key, previous]]), wireClient: previous.client,
    mainWindow: { isDestroyed: () => false, setTitle: vi.fn() }, runtimeIsStopping: () => false,
    findWorkspaceConnectionByClient: (value: unknown) => value === previous.client ? previous : entry,
    releaseConnectionViewAttachments: vi.fn(), updateSettings: vi.fn(async () => {}),
    refreshConnectionThreadList: vi.fn(async () => {}),
    setActiveRemoteProject: vi.fn(), reregisterIpcForWorkspace: vi.fn(), emitCurrentWorkspaceStatus: vi.fn(),
    emitConnectionStatus: vi.fn(), emitWorkspaceProjects: vi.fn(), workspaceTitleName: () => 'Project',
    getSshMachinesManager: () => manager, sshMachinesManager: manager,
    buildSshRemoteProject: () => project, buildServersRemoteProject: () => project,
    connectViaWebSocket: vi.fn(async () => {}), disposeDeviceProjectConnection: vi.fn(),
    sshRemoteProjectId: () => entry.key
  } as unknown as DesktopWindowRuntime
  ctx.demoteWorkspaceConnection = value => demoteWorkspaceConnection(ctx, value)
  ctx.clearRemoteProjectReconnect = value => clearRemoteProjectReconnect(ctx, value)
  ctx.scheduleRemoteProjectReconnect = value => scheduleRemoteProjectReconnect(ctx, value)
  ctx.reconnectRemoteProject = value => reconnectRemoteProject(ctx, value)
  return { ctx, entry, previous, client, manager, project }
}

afterEach(() => { vi.useRealTimers(); vi.clearAllMocks() })

describe('remote project connection lifetime', () => {
  it('promotes a remote project without closing its source or the previous connection', async () => {
    const { ctx, entry, previous, client, manager } = fixture('ssh')
    await expect(promoteWorkspaceConnection(ctx, entry)).resolves.toBe(true)
    expect(ctx.wireClient).toBe(client)
    expect(previous.role).toBe('secondary')
    expect(previous.client.dispose).not.toHaveBeenCalled()
    expect(manager.closeProjectForward).not.toHaveBeenCalled()
    expect(manager.docker.closeStackTunnels).not.toHaveBeenCalled()
    expect(ctx.emitConnectionStatus).toHaveBeenCalledWith(ctx.mainWindow, expect.objectContaining({
      status: 'connected', connectionKey: entry.key, connectionId: 'connection-a'
    }))
  })

  it.each([false, true])('refreshes a demoted connection only if its directory is incomplete (loaded: %s)', (loaded) => {
    const { ctx, entry } = fixture('ssh')
    entry.role = 'foreground'
    entry.threadListLoaded = loaded
    ctx.wireClient = entry.client
    demoteWorkspaceConnection(ctx, entry)
    if (loaded) expect(ctx.refreshConnectionThreadList).not.toHaveBeenCalled()
    else expect(ctx.refreshConnectionThreadList).toHaveBeenCalledExactlyOnceWith(entry)
  })

  it('does not promote an older selection after settings persistence completes late', async () => {
    const { ctx, entry, previous } = fixture('ssh')
    let finish!: () => void
    ctx.updateSettings = () => new Promise<void>(resolve => { finish = resolve })
    const pending = promoteWorkspaceConnection(ctx, entry)
    ctx.deviceOpenGeneration += 1
    finish()
    await expect(pending).resolves.toBe(false)
    expect(ctx.wireClient).toBe(previous.client)
    expect(ctx.emitConnectionStatus).not.toHaveBeenCalled()
  })

  it.each(['ssh', 'servers'] as const)('keeps %s recovery secondary after switching to a local workspace', async source => {
    const { ctx, entry, previous, client, manager, project } = fixture(source)
    const tunnel = { machine: { id: 'machine' }, project: { id: 'project' }, localPort: 4321, wsUrl: `ws://${source}/ws` }
    let finishTunnel!: () => void
    const pendingTunnel = new Promise<typeof tunnel>(resolve => { finishTunnel = () => resolve(tunnel) })
    manager.openProject.mockReturnValue(pendingTunnel)
    manager.docker.openAppServerTunnel.mockReturnValue(pendingTunnel)
    entry.role = 'foreground'
    Object.assign(previous, { kind: 'local', workspacePath: '/b', role: 'secondary', connected: true,
      initializeResult: { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} } })
    ctx.wireClient = client as unknown as WorkspaceConnectionEntry['client']
    ctx.activeRemoteProject = project
    ctx.currentWorkspacePath = '/local'
    Object.assign(ctx.mainWindow!, { webContents: { id: 1 } })
    ctx.updateSettings = async changes => { Object.assign(ctx.settings, changes) }
    ctx.setActiveRemoteProject = value => { ctx.activeRemoteProject = value; ctx.activeRemoteWorkspace = value?.status ?? null }
    ctx.clearPendingDeviceOpenRetry = vi.fn()
    ctx.recordForegroundWorkspace = vi.fn()
    ctx.startHubEventSubscription = vi.fn()
    ctx.app = { createHubClient: () => ({}) } as unknown as DesktopWindowRuntime['app']
    ctx.connectToAppServer = () => promoteWorkspaceConnection(ctx, previous)

    const recovery = reconnectRemoteProject(ctx, entry)
    await switchForegroundWorkspace(ctx, '/b')
    finishTunnel()
    await recovery

    expect(entry.role).toBe('secondary')
    expect(ctx.currentWorkspacePath).toBe('/b')
    expect(ctx.activeRemoteProject).toBeNull()
    expect(ctx.wireClient).toBe(previous.client)
    expect(ctx.reregisterIpcForWorkspace).toHaveBeenCalledExactlyOnceWith('/b')
    expect(ctx.connectViaWebSocket).toHaveBeenCalledWith('/local', tunnel.wsUrl, expect.objectContaining({
      projectId: project.projectId, role: 'secondary', existingEntry: entry
    }))
  })

  it('cancels a scheduled recovery and closes only the disposed source forward', async () => {
    vi.useFakeTimers()
    const { ctx, entry, manager, previous } = fixture('ssh')
    scheduleRemoteProjectReconnect(ctx, entry)
    disposeWorkspaceConnection(ctx, entry)
    await vi.advanceTimersByTimeAsync(30_000)
    expect(manager.openProject).not.toHaveBeenCalled()
    expect(manager.closeProjectForward).toHaveBeenCalledExactlyOnceWith('machine', 'project')
    expect(manager.docker.closeStackTunnels).not.toHaveBeenCalled()
    expect(ctx.workspaceConnections.has(previous.key)).toBe(true)
  })

  it('routes endpoint changes to the retained source rather than the foreground workspace', async () => {
    const { ctx, entry } = fixture('ssh')
    ctx.reconnectRemoteProject = vi.fn(async () => {})
    handleRemoteProjectEndpointChanged(ctx, 'machine', 'project')
    expect(ctx.reconnectRemoteProject).toHaveBeenCalledExactlyOnceWith(entry)
  })
})
