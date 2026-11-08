import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { getWorkspaceStatus } from '../workspaceSetup'
import type { DesktopWindowRuntime } from './windowRuntime'

export function handleSshMachineDisconnected(ctx: DesktopWindowRuntime, machineId: string): void {
  const project = ctx.activeRemoteProject
  if (
    !project ||
    (project.source !== 'ssh' && project.source !== 'servers') ||
    project.remote.hostId !== machineId
  )
    return
  void ctx.disconnectActiveRemoteProject({ restorePreviousLocal: true }).catch((error) => {
    console.warn('[desktop] failed to leave the remote project of a disconnected machine', error)
  })
}

export function handlePairedComputerRemoved(ctx: DesktopWindowRuntime, computerId: string): void {
  void ctx.leavePairedComputerProject(computerId).catch((error) => {
    console.warn('[desktop] failed to leave the project of a removed computer', error)
  })
}

export function handleRemoteProjectEndpointChanged(
  ctx: DesktopWindowRuntime,
  machineId: string,
  projectId: string
): void {
  const ref = ctx.settings.activeRemoteProject
  if (ref?.machineId !== machineId || ref.projectId !== projectId) return
  if (ctx.remoteProjectRefreshTimer) clearTimeout(ctx.remoteProjectRefreshTimer)
  ctx.remoteProjectRefreshTimer = setTimeout(() => {
    ctx.remoteProjectRefreshTimer = null
    const current = ctx.settings.activeRemoteProject
    if (ctx.runtimeIsStopping() || current?.machineId !== machineId || current.projectId !== projectId) return
    void ctx.connectToAppServer(ctx.currentWorkspacePath).catch((error) => {
      console.warn('[desktop] remote project reconnect after port change failed', error)
    })
  }, 750)
}

export function getWorkspaceStatusForRenderer(
  ctx: DesktopWindowRuntime,
  workspacePath: string | null | undefined
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
  return (
    ctx.activeRemoteProject?.identityWorkspacePath ||
    ctx.activeRemoteWorkspace?.appServerWorkspacePath?.trim() ||
    ctx.currentWorkspacePath
  )
}

export function clearActiveRemoteReconnectTimer(ctx: DesktopWindowRuntime): void {
  if (ctx.activeRemoteReconnectTimer) {
    clearTimeout(ctx.activeRemoteReconnectTimer)
    ctx.activeRemoteReconnectTimer = null
  }
}

export function hasRecoverableActiveRemoteStack(ctx: DesktopWindowRuntime): boolean {
  if (ctx.runtimeIsStopping() || !ctx.currentWorkspacePath) return false
  if (ctx.resolveConnectionMode(ctx.settings) !== 'remote') return false
  const ref = ctx.settings.activeRemoteStack
  const projectRef = ctx.settings.activeRemoteProject
  const deviceRef = ctx.settings.activeRemoteDevice
  if (deviceRef) {
    const computer = ctx.app.pairedComputers.list().find((entry) => entry.computerId === deviceRef.computerId)
    return Boolean(
      computer &&
        computer.status !== 'identityChanged' &&
        computer.status !== 'updateRequired' &&
        computer.projects.some(
          (project) => project.projectId === deviceRef.projectId && (computer.status !== 'connected' || !project.unavailable)
        )
    )
  }
  return Boolean((ref?.hostId && ref.stackId) || (projectRef?.machineId && projectRef.projectId))
}

export function scheduleActiveRemoteStackReconnect(ctx: DesktopWindowRuntime, reason: string): void {
  if (!ctx.hasRecoverableActiveRemoteStack()) return
  if (ctx.activeRemoteReconnectTimer) return

  const deviceRef = ctx.settings.activeRemoteDevice
  const workspacePath = ctx.currentWorkspacePath
  const delayMs = Math.min(30_000, 1_000 * 2 ** ctx.activeRemoteReconnectAttempt)
  ctx.activeRemoteReconnectAttempt += 1
  console.info(
    `[desktop] active remote project disconnected (${reason}); reconnecting in ${delayMs}ms`
  )
  ctx.activeRemoteReconnectTimer = setTimeout(() => {
    ctx.activeRemoteReconnectTimer = null
    if (!ctx.hasRecoverableActiveRemoteStack()) return
    if (
      deviceRef &&
      (ctx.currentWorkspacePath !== workspacePath ||
        ctx.settings.activeRemoteDevice?.computerId !== deviceRef.computerId ||
        ctx.settings.activeRemoteDevice?.projectId !== deviceRef.projectId)
    ) return
    void ctx.connectToAppServer(ctx.currentWorkspacePath).catch((error) => {
      const message = error instanceof Error ? error.message : String(error)
      console.warn('[desktop] active remote stack reconnect failed', message)
    })
  }, delayMs)
}
