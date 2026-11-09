import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { normalizeSshMachines } from '../../shared/sshMachines'
import { getWorkspaceStatus } from '../workspaceSetup'
import { REMOTE_INITIALIZE_TIMEOUT_MS } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function handleSshMachineDisconnected(ctx: DesktopWindowRuntime, machineId: string): void {
  for (const entry of [...ctx.workspaceConnections.values()]) {
    if (entry.remote?.hostId === machineId && (entry.remote.source === 'ssh' || entry.remote.source === 'servers')) {
      ctx.disposeWorkspaceConnection(entry)
      if (ctx.activeRemoteProject?.projectId === entry.projectId && ctx.mainWindow) {
        ctx.emitConnectionStatus(ctx.mainWindow, { status: 'disconnected', connectionKey: entry.key })
      }
    }
  }
}

export function handlePairedComputerRemoved(ctx: DesktopWindowRuntime, computerId: string): void {
  void ctx.leavePairedComputerProject(computerId).catch((error) => {
    console.warn('[desktop] failed to leave the project of a removed computer', error)
  })
}

export function handleRemoteProjectEndpointChanged(
  ctx: DesktopWindowRuntime, machineId: string, projectId: string
): void {
  const entry = ctx.workspaceConnections.get(ctx.sshRemoteProjectId(machineId, projectId))
  if (entry && !entry.connecting) void ctx.reconnectRemoteProject(entry)
}

export function clearRemoteProjectReconnect(_ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  if (entry.reconnectTimer) clearTimeout(entry.reconnectTimer)
  entry.reconnectTimer = null
}

export function scheduleRemoteProjectReconnect(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  if (ctx.runtimeIsStopping() || ctx.workspaceConnections.get(entry.key) !== entry || entry.reconnectTimer ||
    !entry.remoteProject || (entry.remote?.source !== 'ssh' && entry.remote?.source !== 'servers')) return
  entry.connecting = true
  const delay = Math.min(30_000, 1_000 * 2 ** (entry.reconnectAttempt ?? 0))
  entry.reconnectAttempt = (entry.reconnectAttempt ?? 0) + 1
  entry.reconnectTimer = setTimeout(() => {
    entry.reconnectTimer = null
    void ctx.reconnectRemoteProject(entry)
  }, delay)
  ctx.emitWorkspaceProjects()
}

export async function reconnectRemoteProject(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): Promise<void> {
  const project = entry.remoteProject
  if (!project || ctx.runtimeIsStopping() || ctx.workspaceConnections.get(entry.key) !== entry) return
  ctx.clearRemoteProjectReconnect(entry)
  entry.connected = false
  entry.connecting = true
  const previousClient = entry.client
  if (ctx.wireClient === previousClient) ctx.wireClient = null
  previousClient.dispose()
  const current = (): boolean => !ctx.runtimeIsStopping() && ctx.workspaceConnections.get(entry.key) === entry &&
    entry.client === previousClient
  ctx.emitWorkspaceProjects()
  try {
    const manager = ctx.getSshMachinesManager()
    let wsUrl: string
    let nextProject = project
    if (project.source === 'ssh') {
      const opened = await manager.openProject(project.remote.hostId!, project.remote.remoteProjectId!)
      wsUrl = opened.wsUrl
      nextProject = ctx.buildSshRemoteProject(opened.machine, opened.project,
        `ws://127.0.0.1:${opened.localPort}/ws`, project.localWorkspacePath)
    } else if (project.source === 'servers') {
      const machine = normalizeSshMachines(ctx.settings.remoteHosts).find(candidate => candidate.id === project.remote.hostId)
      const stack = machine?.stacks.find(candidate => candidate.id === project.remote.stackId)
      if (!machine || !stack) {
        if (current()) ctx.disposeWorkspaceConnection(entry)
        return
      }
      const opened = await manager.docker.openAppServerTunnel(machine, stack)
      wsUrl = opened.wsUrl
      nextProject = ctx.buildServersRemoteProject(machine, stack,
        `ws://127.0.0.1:${opened.localPort}/ws`, project.localWorkspacePath)
    } else return
    if (!current()) {
      if (!ctx.workspaceConnections.has(entry.key)) {
        if (project.source === 'ssh') manager.closeProjectForward(project.remote.hostId!, project.remote.remoteProjectId!)
        else manager.docker.closeStackTunnels(project.remote.hostId!, project.remote.stackId!)
      }
      return
    }
    if (entry.role === 'foreground') {
      ctx.setActiveRemoteProject(nextProject)
      ctx.emitCurrentWorkspaceStatus(project.localWorkspacePath)
    }
    await ctx.connectViaWebSocket(project.localWorkspacePath, wsUrl, {
      autoReconnect: false, initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS, initialDisconnectIsError: true,
      projectId: project.projectId, projectKind: 'remote', identityWorkspacePath: nextProject.identityWorkspacePath,
      displayPath: nextProject.displayPath, remote: nextProject.remote, remoteProject: nextProject,
      role: entry.role, existingEntry: entry
    })
  } catch (error) {
    if (!current()) return
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = error instanceof Error ? error.message : String(error)
    ctx.emitWorkspaceProjects()
    if (entry.role === 'foreground' && ctx.mainWindow) {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: 'error', connectionKey: entry.key, errorMessage: entry.errorMessage })
    }
    ctx.scheduleRemoteProjectReconnect(entry)
  }
}

export function getWorkspaceStatusForRenderer(
  ctx: DesktopWindowRuntime, workspacePath: string | null | undefined
): WorkspaceStatusPayload {
  const status = getWorkspaceStatus(workspacePath)
  return ctx.activeRemoteWorkspace ? { ...status, remote: ctx.activeRemoteWorkspace } : status
}

export function emitCurrentWorkspaceStatus(ctx: DesktopWindowRuntime, workspacePath: string): void {
  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    ctx.emitWorkspaceStatus(ctx.mainWindow, ctx.getWorkspaceStatusForRenderer(workspacePath))
  }
}

export function getProtocolWorkspacePath(ctx: DesktopWindowRuntime): string {
  return ctx.activeRemoteProject?.identityWorkspacePath || ctx.activeRemoteWorkspace?.appServerWorkspacePath?.trim() ||
    ctx.currentWorkspacePath
}

export function clearPendingDeviceOpenRetry(ctx: DesktopWindowRuntime): void {
  if (ctx.pendingDeviceOpenTimer) clearTimeout(ctx.pendingDeviceOpenTimer)
  ctx.pendingDeviceOpenTimer = null
}

export function canRetryPendingDeviceOpen(ctx: DesktopWindowRuntime): boolean {
  const ref = ctx.settings.activeRemoteDevice
  if (ctx.runtimeIsStopping() || !ref) return false
  const computer = ctx.app.pairedComputers.list().find(entry => entry.computerId === ref.computerId)
  return Boolean(computer && computer.status !== 'identityChanged' && computer.status !== 'updateRequired' &&
    computer.projects.some(project => project.projectId === ref.projectId &&
      (computer.status !== 'connected' || !project.unavailable)))
}

export function schedulePendingDeviceOpenRetry(ctx: DesktopWindowRuntime): void {
  if (!ctx.canRetryPendingDeviceOpen() || ctx.pendingDeviceOpenTimer) return
  const ref = ctx.settings.activeRemoteDevice!
  ctx.pendingDeviceOpenTimer = setTimeout(() => {
    ctx.pendingDeviceOpenTimer = null
    if (ctx.settings.activeRemoteDevice?.computerId === ref.computerId &&
      ctx.settings.activeRemoteDevice?.projectId === ref.projectId) {
      void ctx.connectToAppServer(ctx.currentWorkspacePath).catch(console.warn)
    }
  }, Math.min(30_000, 1_000 * 2 ** ctx.pendingDeviceOpenAttempt++))
}
