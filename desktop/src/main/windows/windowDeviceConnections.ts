import { JsonRpcError } from '@dotcraft/sdk/wire'
import type { PairedComputer } from '../../shared/pairedComputers'
import type { OpenedPairedProject } from '../pairedComputers/pairedComputersManager'
import { PairedProjectOpenError } from '../pairedComputers/pairedComputersManager'
import { REMOTE_INITIALIZE_TIMEOUT_MS } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { DeviceProjectConnection, WorkspaceConnectionEntry } from './windowTypes'

export function disposeDeviceProjectConnection(_ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const device = entry.device
  if (!device) return
  device.generation += 1
  if (device.retryTimer) clearTimeout(device.retryTimer)
  device.retryTimer = null
  device.bridge?.close()
  device.bridge = null
}

export function handlePairedComputerProjectsChanged(ctx: DesktopWindowRuntime, computers: PairedComputer[]): void {
  for (const entry of [...ctx.workspaceConnections.values()]) {
    if (!entry.device) continue
    const computer = computers.find(candidate => candidate.computerId === entry.remote?.hostId)
    const project = computer?.projects.find(candidate => candidate.projectId === entry.remote?.remoteProjectId)
    if (computer && project?.saved && !(computer.status === 'connected' && project.unavailable)) continue
    void ctx.leavePairedComputerProject(entry.remote!.hostId!, entry.remote!.remoteProjectId!).catch(error => {
      console.warn('[desktop] failed to release a removed paired project', error)
    })
  }
}

function canRecoverDeviceProject(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): boolean {
  if (ctx.runtimeIsStopping() || ctx.workspaceConnections.get(entry.key) !== entry || !entry.device) return false
  const computer = ctx.app.pairedComputers.list().find(candidate => candidate.computerId === entry.remote?.hostId)
  return Boolean(computer && computer.status !== 'identityChanged' && computer.status !== 'updateRequired' &&
    computer.projects.some(project => project.projectId === entry.remote?.remoteProjectId &&
      (computer.status !== 'connected' || !project.unavailable)))
}

export function deviceProjectConnectionReady(_ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const device = entry.device
  if (!device) return
  device.attempt = 0
  if (device.retryTimer) clearTimeout(device.retryTimer)
  device.retryTimer = null
}

export function scheduleDeviceProjectReconnect(
  ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry, error?: unknown
): void {
  const device = entry.device
  if (!device || device.retryTimer || error instanceof JsonRpcError ||
    (error instanceof PairedProjectOpenError && !error.retryable) || !canRecoverDeviceProject(ctx, entry)) return
  entry.connecting = true
  const delay = Math.min(30_000, 1_000 * 2 ** device.attempt++)
  device.retryTimer = setTimeout(() => {
    device.retryTimer = null
    void ctx.reconnectDeviceProject(entry)
  }, delay)
  ctx.emitWorkspaceProjects()
}

export async function reconnectDeviceProject(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): Promise<void> {
  const device = entry.device
  if (!device || !canRecoverDeviceProject(ctx, entry)) {
    entry.connecting = false
    ctx.emitWorkspaceProjects()
    return
  }
  const generation = ++device.generation
  if (ctx.wireClient === entry.client) ctx.wireClient = null
  entry.client.dispose()
  device.bridge?.close()
  device.bridge = null
  const current = (): boolean => !ctx.runtimeIsStopping() &&
    ctx.workspaceConnections.get(entry.key) === entry && device.generation === generation
  try {
    const opened = await ctx.app.pairedComputers.openProject(entry.remote!.hostId!, entry.remote!.remoteProjectId!)
    if (!current()) {
      opened.bridge.close()
      return
    }
    device.bridge = opened.bridge
    await ctx.installDeviceProjectConnection(opened, device.project.localWorkspacePath, entry)
  } catch (error) {
    if (!current()) return
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = error instanceof Error ? error.message : String(error)
    ctx.emitWorkspaceProjects()
    if (entry.role === 'foreground' && ctx.mainWindow) {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: 'error', errorMessage: entry.errorMessage, errorType: 'remote-config-invalid' })
    }
    ctx.scheduleDeviceProjectReconnect(entry, error)
  }
}

export async function installDeviceProjectConnection(
  ctx: DesktopWindowRuntime,
  opened: OpenedPairedProject,
  workspacePath: string,
  existingEntry?: WorkspaceConnectionEntry
): Promise<void> {
  const project = ctx.buildDeviceRemoteProject(opened.computer, opened.project, undefined, workspacePath)
  const device: DeviceProjectConnection = existingEntry?.device ?? {
    bridge: opened.bridge, retryTimer: null, attempt: 0, generation: 0, project
  }
  const role = existingEntry?.role ?? 'foreground'
  if (role === 'foreground') {
    ctx.prepareRemoteForeground(project)
    ctx.lastRemoteStackLocalPort = opened.bridge.port
    ctx.emitCurrentWorkspaceStatus(workspacePath)
  }
  await ctx.connectViaWebSocket(workspacePath, opened.bridge.wsUrl, {
    autoReconnect: false,
    initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
    initialDisconnectIsError: true,
    projectId: project.projectId, projectKind: 'remote',
    identityWorkspacePath: project.identityWorkspacePath, displayPath: project.displayPath, remote: project.remote,
    remoteDiagnostic: { stage: 'paired-computer', hostName: opened.computer.name, localPort: opened.bridge.port },
    role, device, existingEntry, remoteProject: project
  })
  ctx.enforceSecondaryWorkspaceBudget()
}

export function demoteDeviceProject(ctx: DesktopWindowRuntime): boolean {
  const project = ctx.activeRemoteProject
  if (project?.source !== 'device') return false
  const entry = ctx.workspaceConnections.get(project.projectId)
  if (entry?.device && entry.initializeResult) {
    entry.role = 'secondary'
    entry.lastUsedAt = Date.now()
    ctx.releaseConnectionViewAttachments(entry, 'device project demote')
    if (ctx.wireClient === entry.client) ctx.wireClient = null
    void ctx.refreshConnectionThreadList(entry)
  } else if (entry) {
    ctx.disposeWorkspaceConnection(entry)
  }
  ctx.clearPendingDeviceOpenRetry()
  ctx.pendingDeviceOpenAttempt = 0
  ctx.setActiveRemoteProject(null)
  ctx.enforceSecondaryWorkspaceBudget()
  return true
}

export function promoteDeviceProject(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const device = entry.device!
  device.project = { ...device.project, localWorkspacePath: ctx.remoteForegroundAnchorPath() }
  entry.localWorkspacePath = device.project.localWorkspacePath
  entry.role = 'foreground'
  ctx.prepareRemoteForeground(device.project)
  entry.lastUsedAt = Date.now()
  ctx.wireClient = entry.client
  ctx.lastAppServerWsUrl = entry.wsUrl
  ctx.lastDashboardUrl = entry.initializeResult?.dashboardUrl ?? null
  ctx.lastRemoteStackLocalPort = device.bridge?.port ?? null
  ctx.reregisterIpcForWorkspace(device.project.localWorkspacePath)
  ctx.emitCurrentWorkspaceStatus(device.project.localWorkspacePath)
  if (ctx.mainWindow) {
    const result = entry.initializeResult
    ctx.emitConnectionStatus(ctx.mainWindow, entry.connected && result ? {
      status: 'connected', serverInfo: result.serverInfo,
      capabilities: result.capabilities as Record<string, unknown>, dashboardUrl: result.dashboardUrl,
      connectionKey: entry.key, ...(entry.client.connectionId ? { connectionId: entry.client.connectionId } : {})
    } : { status: 'connecting' })
  }
  if (!entry.connected && !entry.connecting) ctx.scheduleDeviceProjectReconnect(entry)
  ctx.enforceSecondaryWorkspaceBudget()
  ctx.emitWorkspaceProjects()
}

export async function connectDeviceProject(
  ctx: DesktopWindowRuntime, computerId: string, projectId: string, workspacePath: string
): Promise<boolean> {
  const generation = ++ctx.deviceOpenGeneration
  const current = (): boolean => !ctx.runtimeIsStopping() && ctx.deviceOpenGeneration === generation &&
    ctx.resolveConnectionMode(ctx.settings) === 'remote' &&
    ctx.settings.activeRemoteDevice?.computerId === computerId &&
    ctx.settings.activeRemoteDevice?.projectId === projectId
  const key = ctx.deviceRemoteProjectId(computerId, projectId)
  const existing = ctx.workspaceConnections.get(key)
  if (existing?.device && existing.initializeResult) {
    ctx.promoteDeviceProject(existing)
    return true
  }
  try {
    const opened = await ctx.app.pairedComputers.openProject(computerId, projectId)
    if (!current() || !ctx.app.pairedComputers.find(computerId, projectId)) {
      opened.bridge.close()
      return false
    }
    await ctx.installDeviceProjectConnection(opened, workspacePath)
    return true
  } catch (error) {
    if (!current()) return false
    if (ctx.mainWindow) ctx.emitConnectionStatus(ctx.mainWindow, {
      status: 'error',
      errorMessage: ctx.formatRemoteConnectionError(error instanceof Error ? error.message : String(error), { stage: 'open-paired-computer' }),
      errorType: 'remote-config-invalid'
    })
    if (error instanceof PairedProjectOpenError && error.retryable) {
      ctx.schedulePendingDeviceOpenRetry()
    }
    return false
  }
}
