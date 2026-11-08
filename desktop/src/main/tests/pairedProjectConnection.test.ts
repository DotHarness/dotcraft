import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { JsonRpcError, RequestTimeoutError } from '@dotcraft/sdk/wire'
import type { PairedComputer } from '../../shared/pairedComputers'
import type { AppSettings } from '../settings'
import { PairedProjectOpenError, type OpenedPairedProject } from '../pairedComputers/pairedComputersManager'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { ActiveRemoteProject } from '../windows/windowTypes'

const host = vi.hoisted(() => ({ createClient: vi.fn() }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: { fromWebSocket: host.createClient } }))
vi.mock('../workspaceSetup', () => ({ shouldRouteWorkspaceThroughSetupBeforeAppServerStart: () => false }))
vi.mock('../desktopHub', () => ({}))
vi.mock('../desktopHubDiagnostics', () => ({}))
vi.mock('../ripgrepRuntime', () => ({}))
vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../ipcBridge', () => ({}))
vi.mock('../localProjectRecovery', () => ({ bindLocalProjectRecovery: () => {} }))
vi.mock('../voice/voiceIpc', () => ({}))
vi.mock('../threadPanelStateIpc', () => ({}))
import { connectToAppServer } from '../windows/windowForegroundConnection'
import { connectViaWebSocket } from '../windows/windowConnections'
import * as deviceConnections from '../windows/windowDeviceConnections'
import { enforceSecondaryWorkspaceBudget } from '../windows/windowSecondaryConnections'
import { disconnectRemoteProject, leavePairedComputerProject, openPairedComputerProject } from '../windows/windowRemoteActions'
import {
  clearActiveRemoteReconnectTimer,
  hasRecoverableActiveRemoteStack,
  scheduleActiveRemoteStackReconnect
} from '../windows/windowRemoteConnections'

class Client extends EventEmitter {
  onNotification(): void {}
  onServerRequest(): void {}
  dispose = vi.fn()
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function fixture() {
  const clients: Client[] = []
  host.createClient.mockImplementation(() => {
    const client = new Client()
    clients.push(client)
    return client
  })
  const result = {
    computer: { computerId: 'computer-1', name: 'Fixture computer' },
    project: { projectId: 'p1', displayName: 'alpha', path: '/alpha', kind: 'project' },
    bridge: { port: 1234, wsUrl: 'ws://127.0.0.1:1234/ws', close: vi.fn() }
  } as OpenedPairedProject
  const openProject = vi.fn(async () => result)
  const computer: PairedComputer = {
    computerId: 'computer-1', name: 'Fixture computer', status: 'connected', version: '0.10.4',
    requiredVersion: null, lastConnectedAt: null,
    projects: [{ ...result.project, saved: true, unavailable: false }]
  }
  const computers = [computer]
  const ctx = {
    mainWindow: { isDestroyed: () => false },
    argv: [], currentWorkspacePath: '/local', settings: {
      locale: 'en', connectionMode: 'remote', activeRemoteDevice: { computerId: 'computer-1', projectId: 'p1' }
    },
    app: { pairedComputers: { openProject, list: () => computers, find: () => result } },
    runtimeIsStopping: () => false,
    resolveConnectionMode: (settings: AppSettings) => settings.connectionMode,
    resolveActiveRemoteStack: () => null, resolveActiveSshProject: () => null,
    getWorkspaceConnection: () => undefined,
    emitConnectionStatus: vi.fn(), emitCurrentWorkspaceStatus: vi.fn(),
    emitWorkspaceProjects: vi.fn(), resetThreadToolState: () => {},
    deviceRemoteProjectId: (computerId: string, projectId: string) => `remote:device:${computerId}:${projectId}`,
    buildDeviceRemoteProject: (owner: OpenedPairedProject['computer'], project: OpenedPairedProject['project'], _endpoint: undefined, localWorkspacePath: string) => ({
      projectId: `remote:device:${owner.computerId}:${project.projectId}`,
      source: 'device', name: project.displayName, identityWorkspacePath: project.path, displayPath: project.path,
      localWorkspacePath, status: {}, remote: { source: 'device', hostId: owner.computerId, remoteProjectId: project.projectId }
    }),
    deviceOpenGeneration: 0, workspaceConnections: new Map(),
    activeRemoteReconnectTimer: null, activeRemoteReconnectAttempt: 0,
    reregisterIpcForWorkspace: vi.fn(), flushPendingWorkspaceOpenThread: () => {},
    formatRemoteConnectionError: (message: string) => message,
    classifyRemoteInitialError: () => 'remote-config-invalid',
    enforceSecondaryWorkspaceBudget: vi.fn(),
    refreshConnectionThreadList: vi.fn(async () => {}),
    releaseConnectionViewAttachments: vi.fn(),
    remoteForegroundAnchorPath: () => '/local',
    probeRemoteAppServerConnection: vi.fn(async () => {})
  } as unknown as DesktopWindowRuntime
  for (const [name, action] of Object.entries(deviceConnections)) {
    Object.assign(ctx, { [name]: (...args: unknown[]) => (action as (...args: unknown[]) => unknown)(ctx, ...args) })
  }
  ctx.setActiveRemoteProject = project => {
    ctx.activeRemoteProject = project
    ctx.activeRemoteWorkspace = project?.status ?? null
  }
  ctx.prepareRemoteForeground = vi.fn((project: ActiveRemoteProject) => {
    if (ctx.activeRemoteProject?.projectId !== project.projectId) ctx.demoteDeviceProject()
    ctx.setActiveRemoteProject(project)
  })
  ctx.updateSettings = async partial => { ctx.settings = { ...ctx.settings, ...partial } }
  ctx.closeSettingsRemoteForwards = settings => { if (settings.activeRemoteDevice) ctx.demoteDeviceProject() }
  ctx.disposeWorkspaceConnection = entry => {
    ctx.workspaceConnections.delete(entry.key)
    ctx.disposeDeviceProjectConnection(entry)
    entry.client.dispose()
  }
  ctx.connectToAppServer = vi.fn((path) => connectToAppServer(ctx, path))
  ctx.connectViaWebSocket = (...args) => connectViaWebSocket(ctx, ...args)
  ctx.hasRecoverableActiveRemoteStack = () => hasRecoverableActiveRemoteStack(ctx)
  ctx.scheduleActiveRemoteStackReconnect = (reason) => scheduleActiveRemoteStackReconnect(ctx, reason)
  ctx.clearActiveRemoteReconnectTimer = () => clearActiveRemoteReconnectTimer(ctx)
  ctx.leavePairedComputerProject = (...args) => leavePairedComputerProject(ctx, ...args)
  return { ctx, clients, result, openProject, computer, computers }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
})
afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
})

const initialized = { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} }

describe('paired project connection ownership', () => {
  it('keeps recovering through open and initialize failures, then stops on ready', async () => {
    const { ctx, clients, openProject } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    openProject.mockRejectedValueOnce(new PairedProjectOpenError('asleep', true))
    clients[0].emit('close')

    await vi.advanceTimersByTimeAsync(1000)
    expect(openProject).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(2000)
    expect(openProject).toHaveBeenCalledTimes(3)
    clients[1].emit('close')
    await vi.advanceTimersByTimeAsync(4000)
    expect(openProject).toHaveBeenCalledTimes(4)
    clients[2].emit('reconnect-error', new RequestTimeoutError('initialize', 1000))
    await vi.advanceTimersByTimeAsync(8000)
    expect(openProject).toHaveBeenCalledTimes(5)
    clients[3].emit('ready', initialized)
    await vi.advanceTimersByTimeAsync(60000)
    expect(openProject).toHaveBeenCalledTimes(5)
    expect(ctx.emitConnectionStatus).toHaveBeenLastCalledWith(ctx.mainWindow, expect.objectContaining({ status: 'connected' }))
  })

  it.each(['removed', 'identityChanged', 'updateRequired'] as const)(
    'does not recover an unavailable computer after %s', async (target) => {
      const { ctx, clients, openProject, computer, computers } = fixture()
      await ctx.connectToAppServer('/local')
      clients[0].emit('ready', initialized)
      clients[0].emit('close')
      if (target === 'removed') computers.length = 0
      else computer.status = target
      await vi.advanceTimersByTimeAsync(60000)
      expect(openProject).toHaveBeenCalledTimes(1)
    }
  )

  it('does not retry a project that cannot open or a rejected initialize', async () => {
    const { ctx, clients, openProject } = fixture()
    openProject.mockRejectedValueOnce(new PairedProjectOpenError('project removed', false))
    await ctx.connectToAppServer('/local')
    await vi.advanceTimersByTimeAsync(60000)
    expect(openProject).toHaveBeenCalledTimes(1)

    await ctx.connectToAppServer('/local')
    clients[0].emit('reconnect-error', new JsonRpcError(-32602, 'Rejected initialize'))
    await vi.advanceTimersByTimeAsync(60000)
    expect(openProject).toHaveBeenCalledTimes(2)
  })

  it.each(['local', 'other-project'] as const)('discards old initial open results after selecting %s', async (target) => {
    for (const outcome of ['success', 'failure'] as const) {
      const { ctx, result, openProject } = fixture()
      const pending = deferred<OpenedPairedProject>()
      openProject.mockReturnValueOnce(pending.promise)
      const connecting = ctx.connectToAppServer('/local')
      ctx.settings = target === 'local'
        ? { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
        : { ...ctx.settings, activeRemoteDevice: { computerId: 'computer-2', projectId: 'p2' } }
      vi.mocked(ctx.emitConnectionStatus).mockClear()
      if (outcome === 'success') pending.resolve(result)
      else pending.reject(new PairedProjectOpenError('late failure', true))

      expect(await connecting).toBe(false)
      expect(ctx.prepareRemoteForeground).not.toHaveBeenCalled()
      expect(ctx.emitConnectionStatus).not.toHaveBeenCalled()
      if (outcome === 'success') expect(result.bridge.close).toHaveBeenCalledOnce()
      await vi.advanceTimersByTimeAsync(60000)
      expect(openProject).toHaveBeenCalledTimes(1)
    }
  })

  it('retains a demoted connection and reuses it when its project is opened again', async () => {
    const { ctx, clients, result, openProject } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    const entry = [...ctx.workspaceConnections.values()][0]
    entry.threads = [{ id: 'remote-thread' }]
    ctx.demoteDeviceProject()
    ctx.settings = { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }

    expect(entry.role).toBe('secondary')
    expect(clients[0].dispose).not.toHaveBeenCalled()
    expect(result.bridge.close).not.toHaveBeenCalled()
    await openPairedComputerProject(ctx, 'computer-1', 'p1')
    expect(ctx.wireClient).toBe(clients[0])
    expect(entry.role).toBe('foreground')
    expect(entry.threads).toEqual([{ id: 'remote-thread' }])
    expect(openProject).toHaveBeenCalledTimes(1)
    expect(ctx.probeRemoteAppServerConnection).not.toHaveBeenCalled()
  })

  it('uses the latest local foreground as the anchor when returning to a device project', async () => {
    const { ctx, clients } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    ctx.demoteDeviceProject()
    ctx.currentWorkspacePath = '/other-local'
    ctx.remoteForegroundAnchorPath = () => ctx.currentWorkspacePath
    await openPairedComputerProject(ctx, 'computer-1', 'p1')
    const entry = [...ctx.workspaceConnections.values()][0]
    expect(entry.localWorkspacePath).toBe('/other-local')
    expect(ctx.prepareRemoteForeground).toHaveBeenLastCalledWith(expect.objectContaining({ localWorkspacePath: '/other-local' }))
  })

  it('does not report success for an explicit opening cancelled while its probe is pending', async () => {
    const { ctx, result } = fixture()
    const probe = deferred<void>()
    vi.mocked(ctx.probeRemoteAppServerConnection).mockReturnValueOnce(probe.promise)
    const opening = openPairedComputerProject(ctx, 'computer-1', 'p1')
    await Promise.resolve()
    expect(ctx.probeRemoteAppServerConnection).toHaveBeenCalled()
    ctx.deviceOpenGeneration += 1
    const rejected = expect(opening).rejects.toThrow('cancelled')
    probe.resolve()
    await rejected
    expect(result.bridge.close).toHaveBeenCalledOnce()
    expect(ctx.prepareRemoteForeground).not.toHaveBeenCalled()
    expect(ctx.workspaceConnections.size).toBe(0)
  })

  it('demotes one device while opening another device at the same remote path', async () => {
    const { ctx, clients, result, openProject } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    const old = [...ctx.workspaceConnections.values()][0]
    const nextBridge = { ...result.bridge, close: vi.fn() }
    openProject.mockResolvedValueOnce({
      ...result, computer: { ...result.computer, computerId: 'computer-2' }, bridge: nextBridge
    })
    await openPairedComputerProject(ctx, 'computer-2', 'p1')
    clients[1].emit('ready', initialized)
    expect(ctx.workspaceConnections.size).toBe(2)
    expect(old.role).toBe('secondary')
    expect(old.workspacePath).toBe([...ctx.workspaceConnections.values()][1].workspacePath)
    expect(result.bridge.close).not.toHaveBeenCalled()
    expect(ctx.wireClient).toBe(clients[1])
  })

  it('reuses the oldest target connection when the shared secondary budget is full', async () => {
    const { ctx, clients, result, openProject } = fixture()
    ctx.enforceSecondaryWorkspaceBudget = () => enforceSecondaryWorkspaceBudget(ctx)
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    const target = [...ctx.workspaceConnections.values()][0]
    ctx.demoteDeviceProject()
    target.lastUsedAt = 0
    for (let index = 1; index <= 7; index++) {
      const key = `/other/${index}`
      ctx.workspaceConnections.set(key, {
        key, kind: 'local', workspacePath: key, role: 'secondary', lastUsedAt: index,
        client: { hasRetainedThreadAttachments: false }
      } as never)
    }
    openProject.mockResolvedValueOnce({
      ...result, computer: { ...result.computer, computerId: 'computer-2' },
      bridge: { ...result.bridge, close: vi.fn() }
    })
    await openPairedComputerProject(ctx, 'computer-2', 'p1')
    clients[1].emit('ready', initialized)

    await openPairedComputerProject(ctx, 'computer-1', 'p1')

    expect(ctx.workspaceConnections.get(target.key)).toBe(target)
    expect(ctx.wireClient).toBe(clients[0])
    expect(openProject).toHaveBeenCalledTimes(2)
    expect(result.bridge.close).not.toHaveBeenCalled()
  })

  it('cleans a secondary project removed from another window without disturbing foreground', async () => {
    const { ctx, clients, result, computer } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    ctx.demoteDeviceProject()
    ctx.settings = { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
    ctx.disconnectActiveRemoteProject = vi.fn(async () => {})
    computer.projects[0].saved = false
    ctx.handlePairedComputerProjectsChanged([computer])
    expect(ctx.workspaceConnections.size).toBe(0)
    expect(result.bridge.close).toHaveBeenCalledOnce()
    expect(ctx.disconnectActiveRemoteProject).not.toHaveBeenCalled()
  })

  it('recovers a secondary connection without replacing the foreground or its status', async () => {
    const { ctx, clients, openProject } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    const entry = [...ctx.workspaceConnections.values()][0]
    entry.threads = [{ id: 'cached' }]
    ctx.demoteDeviceProject()
    const lastUsedAt = entry.lastUsedAt
    ctx.settings = { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
    const foreground = new Client()
    ctx.wireClient = foreground as never
    vi.mocked(ctx.emitConnectionStatus).mockClear()
    clients[0].emit('close')
    await vi.advanceTimersByTimeAsync(1000)
    clients[1].emit('ready', initialized)

    expect(openProject).toHaveBeenCalledTimes(2)
    expect(ctx.wireClient).toBe(foreground)
    expect(ctx.emitConnectionStatus).not.toHaveBeenCalled()
    const recovered = ctx.workspaceConnections.get(entry.key)!
    expect(recovered.role).toBe('secondary')
    expect(recovered.threads).toEqual([{ id: 'cached' }])
    expect(recovered.lastUsedAt).toBe(lastUsedAt)
    expect(ctx.refreshConnectionThreadList).toHaveBeenCalledWith(recovered)
  })

  it('releases a late recovery bridge after its background project is disconnected', async () => {
    const { ctx, clients, result, openProject } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    const entry = [...ctx.workspaceConnections.values()][0]
    ctx.demoteDeviceProject()
    const pending = deferred<OpenedPairedProject>()
    openProject.mockReturnValueOnce(pending.promise)
    clients[0].emit('close')
    await vi.advanceTimersByTimeAsync(1000)
    await disconnectRemoteProject(ctx, entry.projectId)
    const lateBridge = { ...result.bridge, close: vi.fn() }
    pending.resolve({ ...result, bridge: lateBridge })
    await vi.advanceTimersByTimeAsync(60000)

    expect(lateBridge.close).toHaveBeenCalledOnce()
    expect(ctx.workspaceConnections.size).toBe(0)
    expect(openProject).toHaveBeenCalledTimes(2)
  })

  it('removing a computer closes its secondary projects without leaving another foreground', async () => {
    const { ctx, clients, result } = fixture()
    await ctx.connectToAppServer('/local')
    clients[0].emit('ready', initialized)
    ctx.demoteDeviceProject()
    ctx.settings = { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
    ctx.disconnectActiveRemoteProject = vi.fn(async () => {})
    await leavePairedComputerProject(ctx, 'computer-1')
    expect(result.bridge.close).toHaveBeenCalledOnce()
    expect(ctx.workspaceConnections.size).toBe(0)
    expect(ctx.disconnectActiveRemoteProject).not.toHaveBeenCalled()
  })
})
