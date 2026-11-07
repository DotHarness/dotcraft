import { app } from 'electron'
import { basename } from 'path'
import {
  effectiveAppServerWorkspacePath,
  effectiveWorkspaceDir,
  type RemoteStack
} from '../../shared/dockerDeployments'
import { translate, type AppLocale } from '../../shared/locales'
import { normalizeSshMachines, type RemoteProject, type SshMachine } from '../../shared/sshMachines'
import type { WorkspaceRemoteProjectMetadata } from '../../shared/workspaceProjects'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { isDefaultChatWorkspace } from '../defaultChatWorkspace'
import { type AppSettings, type ConnectionMode } from '../settings'
import type { StoredComputer, StoredProject } from '../pairedComputers/pairedComputersStorage'
import { DOTCRAFT_INSTALL_SCRIPT } from '../sshMachines/installScript'
import { listRemoteProviders } from '../sshMachines/remoteProviderList'
import { SSH_MACHINES_CHANGED_CHANNEL } from '../sshMachines/sshMachinesIpc'
import { SshMachinesManager } from '../sshMachines/sshMachinesManager'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { ActiveRemoteProject } from './windowTypes'

export function isLocalChatsForeground(ctx: DesktopWindowRuntime): boolean {
  return (
    !ctx.activeRemoteProject && !ctx.activeRemoteWorkspace && isDefaultChatWorkspace(ctx.currentWorkspacePath)
  )
}

export function resolveConnectionMode(_ctx: DesktopWindowRuntime, settings: AppSettings): ConnectionMode {
  const mode = settings.connectionMode
  return mode === 'remote' ? 'remote' : 'local'
}

export function workspaceTitleName(
  _ctx: DesktopWindowRuntime,
  workspacePath: string | null | undefined,
  locale: AppLocale
): string {
  if (!workspacePath) return 'DotCraft'
  return isDefaultChatWorkspace(workspacePath)
    ? translate(locale, 'chatsRail.title')
    : basename(workspacePath)
}

export function buildRemoteWorkspaceStatus(
  _ctx: DesktopWindowRuntime,
  host: SshMachine,
  stack: RemoteStack
): NonNullable<WorkspaceStatusPayload['remote']> {
  const projectId = `remote:servers:${host.id}:${stack.id}`
  const displayName = stack.name
  return {
    source: 'servers',
    projectId,
    displayName,
    hostId: host.id,
    stackId: stack.id,
    serverName: host.name,
    stackName: stack.name,
    workspaceDir: effectiveWorkspaceDir(stack),
    appServerWorkspacePath: effectiveAppServerWorkspacePath(stack),
    composeDir: stack.composeDir,
    projectName: stack.name
  }
}

export function buildServersRemoteProject(
  ctx: DesktopWindowRuntime,
  host: SshMachine,
  stack: RemoteStack,
  endpoint?: string,
  localWorkspacePath: string = ctx.currentWorkspacePath
): ActiveRemoteProject {
  const status = ctx.buildRemoteWorkspaceStatus(host, stack)
  const identityWorkspacePath =
    status.appServerWorkspacePath?.trim() || status.workspaceDir?.trim() || ctx.currentWorkspacePath
  const displayPath = status.workspaceDir?.trim() || identityWorkspacePath
  const projectId = status.projectId || `remote:servers:${host.id}:${stack.id}`
  const remote: WorkspaceRemoteProjectMetadata = {
    source: 'servers',
    displayPath,
    ...(endpoint ? { endpoint } : {}),
    hostId: host.id,
    stackId: stack.id,
    serverName: host.name,
    stackName: stack.name,
    workspaceDir: effectiveWorkspaceDir(stack),
    appServerWorkspacePath: effectiveAppServerWorkspacePath(stack),
    composeDir: stack.composeDir,
    projectName: stack.name
  }
  return {
    projectId,
    source: 'servers',
    name: status.displayName || stack.name,
    identityWorkspacePath,
    displayPath,
    ...(endpoint ? { endpoint } : {}),
    localWorkspacePath,
    remote,
    status: {
      ...status,
      projectId,
      displayName: status.displayName || stack.name,
      ...(endpoint ? { endpoint } : {})
    }
  }
}

export function sshRemoteProjectId(_ctx: DesktopWindowRuntime, machineId: string, projectId: string): string {
  return `remote:ssh:${machineId}:${projectId}`
}

export function buildSshRemoteProject(
  ctx: DesktopWindowRuntime,
  machine: SshMachine,
  project: RemoteProject,
  endpoint?: string,
  localWorkspacePath: string = ctx.currentWorkspacePath
): ActiveRemoteProject {
  const projectId = ctx.sshRemoteProjectId(machine.id, project.id)
  const remote: WorkspaceRemoteProjectMetadata = {
    source: 'ssh',
    displayPath: project.path,
    ...(endpoint ? { endpoint } : {}),
    hostId: machine.id,
    serverName: machine.name,
    appServerWorkspacePath: project.path,
    projectName: project.label,
    remoteProjectId: project.id
  }
  return {
    projectId,
    source: 'ssh',
    name: project.label,
    identityWorkspacePath: project.path,
    displayPath: project.path,
    ...(endpoint ? { endpoint } : {}),
    localWorkspacePath,
    remote,
    status: {
      source: 'ssh',
      projectId,
      displayName: project.label,
      ...(endpoint ? { endpoint } : {}),
      hostId: machine.id,
      serverName: machine.name,
      appServerWorkspacePath: project.path,
      projectName: project.label,
      remoteProjectId: project.id
    }
  }
}

export function deviceRemoteProjectId(_ctx: DesktopWindowRuntime, computerId: string, projectId: string): string {
  return `remote:device:${computerId}:${projectId}`
}

export function buildDeviceRemoteProject(
  ctx: DesktopWindowRuntime,
  computer: Pick<StoredComputer, 'computerId' | 'name'>,
  project: StoredProject,
  endpoint?: string,
  localWorkspacePath: string = ctx.currentWorkspacePath
): ActiveRemoteProject {
  const projectId = ctx.deviceRemoteProjectId(computer.computerId, project.projectId)
  const remote: WorkspaceRemoteProjectMetadata = {
    source: 'device',
    displayPath: project.path,
    ...(endpoint ? { endpoint } : {}),
    hostId: computer.computerId,
    serverName: computer.name,
    appServerWorkspacePath: project.path,
    projectName: project.displayName,
    remoteProjectId: project.projectId
  }
  return {
    projectId,
    source: 'device',
    name: project.displayName,
    identityWorkspacePath: project.path,
    displayPath: project.path,
    ...(endpoint ? { endpoint } : {}),
    localWorkspacePath,
    remote,
    status: {
      source: 'device',
      projectId,
      displayName: project.displayName,
      ...(endpoint ? { endpoint } : {}),
      hostId: computer.computerId,
      serverName: computer.name,
      appServerWorkspacePath: project.path,
      projectName: project.displayName,
      remoteProjectId: project.projectId
    }
  }
}

export function buildEndpointRemoteProject(
  ctx: DesktopWindowRuntime,
  source: 'manual' | 'cli',
  endpoint: string,
  localWorkspacePath: string
): ActiveRemoteProject {
  const projectId = ctx.remoteProjectId(source, endpoint)
  const name = ctx.remoteProjectNameFromEndpoint(endpoint)
  const safeEndpoint = ctx.remoteEndpointDisplay(endpoint)
  const identityWorkspacePath = localWorkspacePath || name
  const remote: WorkspaceRemoteProjectMetadata = {
    source,
    displayPath: safeEndpoint,
    endpoint: safeEndpoint
  }
  return {
    projectId,
    source,
    name,
    identityWorkspacePath,
    displayPath: safeEndpoint,
    endpoint: safeEndpoint,
    localWorkspacePath,
    remote,
    status: {
      source,
      projectId,
      displayName: name,
      endpoint: safeEndpoint,
      workspaceDir: localWorkspacePath,
      appServerWorkspacePath: identityWorkspacePath
    }
  }
}

export function setActiveRemoteProject(ctx: DesktopWindowRuntime, project: ActiveRemoteProject | null): void {
  ctx.activeRemoteProject = project
  ctx.activeRemoteWorkspace = project?.status ?? null
}

export function disposeActiveRemoteConnection(ctx: DesktopWindowRuntime): void {
  if (!ctx.activeRemoteProject) return
  const entry = ctx.workspaceConnections.get(ctx.activeRemoteProject.projectId)
  if (entry) {
    entry.client.dispose()
    ctx.workspaceConnections.delete(entry.key)
  }
  if (ctx.wireClient === entry?.client) {
    ctx.wireClient = null
  }
}

export function prepareRemoteForeground(ctx: DesktopWindowRuntime, project: ActiveRemoteProject): void {
  if (!ctx.previousLocalForegroundWorkspacePath && project.localWorkspacePath) {
    ctx.previousLocalForegroundWorkspacePath = project.localWorkspacePath
  }
  const previousLocal = project.localWorkspacePath
    ? ctx.getWorkspaceConnection(project.localWorkspacePath)
    : undefined
  if (previousLocal?.role === 'foreground') {
    previousLocal.role = 'secondary'
    previousLocal.lastUsedAt = Date.now()
  }
  if (ctx.activeRemoteProject?.projectId !== project.projectId) {
    ctx.disposeActiveRemoteConnection()
  }
  ctx.setActiveRemoteProject(project)
}

export function resolveActiveRemoteStack(
  _ctx: DesktopWindowRuntime,
  settings: AppSettings
): { host: SshMachine; stack: RemoteStack } | null {
  const ref = settings.activeRemoteStack
  if (!ref?.hostId || !ref.stackId) return null
  const host = normalizeSshMachines(settings.remoteHosts).find((candidate) => candidate.id === ref.hostId)
  const stack = host?.stacks.find((candidate) => candidate.id === ref.stackId)
  return host && stack ? { host, stack } : null
}

export function resolveActiveSshProject(
  _ctx: DesktopWindowRuntime,
  settings: AppSettings
): { machine: SshMachine; project: RemoteProject } | null {
  const ref = settings.activeRemoteProject
  if (!ref?.machineId || !ref.projectId) return null
  const machine = normalizeSshMachines(settings.remoteHosts).find(
    (candidate) => candidate.id === ref.machineId
  )
  const project = machine?.projects.find((candidate) => candidate.id === ref.projectId)
  return machine && project ? { machine, project } : null
}

export function buildInitialRemoteProject(
  ctx: DesktopWindowRuntime,
  settings: AppSettings,
  localWorkspacePath: string
): ActiveRemoteProject | null {
  if (ctx.resolveConnectionMode(settings) !== 'remote') return null
  const sshProject = ctx.resolveActiveSshProject(settings)
  if (sshProject)
    return ctx.buildSshRemoteProject(sshProject.machine, sshProject.project, undefined, localWorkspacePath)
  const device = settings.activeRemoteDevice
    ? ctx.app.pairedComputers.find(settings.activeRemoteDevice.computerId, settings.activeRemoteDevice.projectId)
    : null
  if (device) return ctx.buildDeviceRemoteProject(device.computer, device.project, undefined, localWorkspacePath)
  const stack = ctx.resolveActiveRemoteStack(settings)
  return stack ? ctx.buildServersRemoteProject(stack.host, stack.stack, undefined, localWorkspacePath) : null
}

export function getSshMachinesManager(ctx: DesktopWindowRuntime): SshMachinesManager {
  if (!ctx.sshMachinesManager) {
    ctx.sshMachinesManager = new SshMachinesManager({
      loadMachines: () => normalizeSshMachines(ctx.settings.remoteHosts),
      saveMachines: (machines) =>
        ctx.updateSettings({ remoteHosts: machines.length > 0 ? machines : undefined }),
      installScript: () => DOTCRAFT_INSTALL_SCRIPT,
      appVersion: app.getVersion(),
      packaged: app.isPackaged,
      checkProviders: listRemoteProviders,
      onChanged: (payload) => {
        const win = ctx.mainWindow
        if (win && !win.isDestroyed()) win.webContents.send(SSH_MACHINES_CHANGED_CHANNEL, payload)
        ctx.emitWorkspaceProjects()
      },
      onMachineDisconnected: ctx.handleSshMachineDisconnected,
      onProjectEndpointChanged: ctx.handleRemoteProjectEndpointChanged,
      getForegroundProject: () => {
        const ref = ctx.settings.activeRemoteProject
        return ctx.activeRemoteProject?.source === 'ssh' && ref
          ? { machineId: ref.machineId, projectId: ref.projectId }
          : null
      }
    })
  }
  return ctx.sshMachinesManager
}
