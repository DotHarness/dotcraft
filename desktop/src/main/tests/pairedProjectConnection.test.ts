import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { JsonRpcError, RequestTimeoutError } from '@dotcraft/sdk/wire'
import type { PairedComputer } from '../../shared/pairedComputers'
import type { AppSettings } from '../settings'
import { PairedProjectOpenError, type OpenedPairedProject } from '../pairedComputers/pairedComputersManager'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

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
    computer: { computerId: 'computer-1', name: 'Studio' },
    project: { projectId: 'p1', displayName: 'alpha', path: '/alpha', kind: 'project' },
    bridge: { port: 1234, wsUrl: 'ws://127.0.0.1:1234/ws', close: vi.fn() }
  } as OpenedPairedProject
  const openProject = vi.fn(async () => result)
  const computer: PairedComputer = {
    computerId: 'computer-1', name: 'Studio', status: 'connected', version: '0.10.4',
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
    emitWorkspaceProjects: () => {}, resetThreadToolState: () => {},
    buildDeviceRemoteProject: () => ({
      projectId: 'device:computer-1:p1', identityWorkspacePath: '/alpha', displayPath: '/alpha', remote: {}
    }),
    prepareRemoteForeground: vi.fn(),
    connectionGeneration: 0, workspaceConnections: new Map(),
    activeRemoteReconnectTimer: null, activeRemoteReconnectAttempt: 0,
    reregisterIpcForWorkspace: () => {}, flushPendingWorkspaceOpenThread: () => {},
    formatRemoteConnectionError: (message: string) => message,
    classifyRemoteInitialError: () => 'remote-config-invalid'
  } as unknown as DesktopWindowRuntime
  ctx.connectToAppServer = vi.fn((path) => connectToAppServer(ctx, path))
  ctx.connectViaWebSocket = (...args) => connectViaWebSocket(ctx, ...args)
  ctx.hasRecoverableActiveRemoteStack = () => hasRecoverableActiveRemoteStack(ctx)
  ctx.scheduleActiveRemoteStackReconnect = (reason) => scheduleActiveRemoteStackReconnect(ctx, reason)
  ctx.clearActiveRemoteReconnectTimer = () => clearActiveRemoteReconnectTimer(ctx)
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

describe('paired project foreground recovery', () => {
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

  it.each(['local', 'other-project', 'removed', 'identityChanged'] as const)(
    'does not reopen the old project after %s', async (target) => {
      const { ctx, clients, openProject, computer, computers } = fixture()
      await ctx.connectToAppServer('/local')
      clients[0].emit('ready', initialized)
      clients[0].emit('close')
      if (target === 'local') ctx.settings = { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
      if (target === 'other-project') {
        computer.projects.push({ ...computer.projects[0], projectId: 'p2' })
        ctx.settings = { ...ctx.settings, activeRemoteDevice: { computerId: 'computer-1', projectId: 'p2' } }
      }
      if (target === 'removed') computers.length = 0
      if (target === 'identityChanged') computer.status = 'identityChanged'
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

  it.each(['local', 'other-project'] as const)('discards old open results after selecting %s at the same local path', async (target) => {
    for (const outcome of ['success', 'failure'] as const) {
      const { ctx, result, openProject } = fixture()
      const pending = deferred<OpenedPairedProject>()
      openProject.mockReturnValueOnce(pending.promise)
      const connecting = ctx.connectToAppServer('/local')
      ctx.settings = target === 'local'
        ? { ...ctx.settings, connectionMode: 'local', activeRemoteDevice: undefined }
        : { ...ctx.settings, activeRemoteDevice: { computerId: 'computer-2', projectId: 'p2' } }
      const currentBridge = { port: 5678, wsUrl: 'ws://127.0.0.1:5678/ws', close: vi.fn() }
      ctx.activeDeviceBridge = currentBridge
      vi.mocked(ctx.emitConnectionStatus).mockClear()
      if (outcome === 'success') pending.resolve(result)
      else pending.reject(new PairedProjectOpenError('late failure', true))

      expect(await connecting).toBe(false)
      expect(ctx.prepareRemoteForeground).not.toHaveBeenCalled()
      expect(ctx.emitConnectionStatus).not.toHaveBeenCalled()
      expect(currentBridge.close).not.toHaveBeenCalled()
      if (outcome === 'success') expect(result.bridge.close).toHaveBeenCalledOnce()
      await vi.advanceTimersByTimeAsync(60000)
      expect(openProject).toHaveBeenCalledTimes(1)
    }
  })
})
