import type { ConnectionErrorType } from '../../shared/connectionStatus'
import { type RemoteStack } from '../../shared/dockerDeployments'
import { resolveRemoteWebSocketConfig, type ConnectionSettingsDraft } from '../../shared/remoteConnection'
import { type SshMachine } from '../../shared/sshMachines'
import { ensureDefaultChatWorkspace } from '../defaultChatWorkspace'
import type { OpenedPairedProject } from '../pairedComputers/pairedComputersManager'
import { DesktopAppServerClient } from '../DesktopAppServerClient'
import { resolveDotCraftRuntimeTools } from '../ripgrepRuntime'
import { type AppSettings } from '../settings'
import { REMOTE_CONNECTION_PROBE_TIMEOUT_MS } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { RemoteConnectionDiagnostic } from './windowTypes'

export function formatRemoteConnectionError(
  _ctx: DesktopWindowRuntime,
  message: string,
  diagnostic?: RemoteConnectionDiagnostic,
  stage?: string
): string {
  const details: string[] = []
  const resolvedStage = stage ?? diagnostic?.stage
  if (resolvedStage) details.push(`stage=${resolvedStage}`)
  if (diagnostic?.hostName) details.push(`host=${diagnostic.hostName}`)
  if (diagnostic?.stackName) details.push(`stack=${diagnostic.stackName}`)
  if (typeof diagnostic?.localPort === 'number') details.push(`localPort=${diagnostic.localPort}`)
  if (typeof diagnostic?.targetPort === 'number') details.push(`remotePort=${diagnostic.targetPort}`)
  if (typeof diagnostic?.tokenPresent === 'boolean') {
    details.push(`token=${diagnostic.tokenPresent ? 'present' : 'missing'}`)
  }
  return details.length > 0 ? `${message} (${details.join(', ')})` : message
}

export function classifyRemoteInitialError(_ctx: DesktopWindowRuntime, message: string): ConnectionErrorType {
  return /timed out/i.test(message) ? 'handshake-timeout' : 'remote-config-invalid'
}

export function probeRemoteAppServerConnection(_ctx: DesktopWindowRuntime, wsUrl: string): Promise<void> {
  return new Promise((resolve, reject) => {
    let settled = false
    const client = DesktopAppServerClient.fromWebSocket(wsUrl, {
      autoReconnect: false,
      initializeTimeoutMs: REMOTE_CONNECTION_PROBE_TIMEOUT_MS
    })

    function settle(error?: Error): void {
      if (settled) return
      settled = true
      client.removeAllListeners()
      client.dispose()
      if (error) reject(error)
      else resolve()
    }

    client.once('ready', () => settle())
    client.once('reconnect-error', (error) => {
      settle(error instanceof Error ? error : new Error(String(error)))
    })
    client.once('close', () => {
      settle(new Error('Remote AppServer connection closed before initialize completed.'))
    })
  })
}

export async function applyConnectionSettings(
  ctx: DesktopWindowRuntime,
  draft: ConnectionSettingsDraft
): Promise<void> {
  if (!ctx.currentWorkspacePath) {
    throw new Error('Open a workspace before applying connection settings.')
  }
  if (ctx.argv.includes('--remote')) {
    throw new Error(
      'Persistent connection settings cannot be changed while Desktop was launched with --remote.'
    )
  }

  const nextConnectionMode = draft.connectionMode === 'remote' ? 'remote' : 'local'
  if (nextConnectionMode === 'remote') {
    const resolved = resolveRemoteWebSocketConfig(draft.remote)
    if (!resolved.ok) {
      throw new Error(resolved.message)
    }
    await ctx.probeRemoteAppServerConnection(resolved.connectUrl)
    ctx.closeActiveRemoteForwards()
    await ctx.updateSettings({
      ...draft,
      activeRemoteStack: undefined,
      activeRemoteProject: undefined,
      activeRemoteDevice: undefined
    })
    await ctx.connectToAppServer(ctx.currentWorkspacePath)
    return
  }

  await ctx.teardownRuntime('apply local connection settings before reconnect')
  ctx.closeActiveRemoteForwards()
  await ctx.updateSettings({
    ...draft,
    activeRemoteStack: undefined,
    activeRemoteProject: undefined,
    activeRemoteDevice: undefined
  })
  const hubClient = ctx.app.createHubClient(ctx.settings)
  const restarted = await hubClient.restartAppServer(ctx.currentWorkspacePath, resolveDotCraftRuntimeTools())
  await ctx.connectViaWebSocket(ctx.currentWorkspacePath, ctx.getManagedAppServerEndpoint(restarted))
  ctx.startHubEventSubscription(ctx.currentWorkspacePath, hubClient)
}

export function closeSettingsRemoteForwards(ctx: DesktopWindowRuntime, settings: AppSettings): void {
  const ref = settings.activeRemoteStack
  if (ref?.hostId && ref.stackId) {
    ctx.sshMachinesManager?.docker.closeStackTunnels(ref.hostId, ref.stackId)
  }
  const projectRef = settings.activeRemoteProject
  if (projectRef?.machineId && projectRef.projectId) {
    ctx.sshMachinesManager?.closeProjectForward(projectRef.machineId, projectRef.projectId)
  }
  if (settings.activeRemoteDevice) ctx.demoteDeviceProject()
}

export function closeActiveRemoteForwards(
  ctx: DesktopWindowRuntime,
  settings: AppSettings = ctx.settings
): void {
  ctx.clearPendingDeviceOpenRetry()
  ctx.pendingDeviceOpenAttempt = 0
  ctx.closeSettingsRemoteForwards(settings)
  ctx.disposeActiveRemoteConnection()
  ctx.setActiveRemoteProject(null)
  ctx.lastRemoteStackLocalPort = null
}

export async function disconnectActiveRemoteProject(
  ctx: DesktopWindowRuntime,
  options: {
    restorePreviousLocal?: boolean
    targetWorkspacePath?: string
  } = {}
): Promise<void> {
  ctx.clearPendingDeviceOpenRetry()
  ctx.pendingDeviceOpenAttempt = 0
  ctx.deviceOpenGeneration += 1
  ctx.disposeActiveRemoteConnection()
  ctx.setActiveRemoteProject(null)
  ctx.lastRemoteStackLocalPort = null

  const restorePath =
    options.targetWorkspacePath?.trim() ||
    (options.restorePreviousLocal === false
      ? ''
      : ctx.previousLocalForegroundWorkspacePath || ctx.currentWorkspacePath)
  ctx.previousLocalForegroundWorkspacePath = null

  await ctx.updateSettings({
    connectionMode: 'local',
    activeRemoteStack: undefined,
    activeRemoteProject: undefined,
    activeRemoteDevice: undefined
  })
  ctx.emitWorkspaceProjects()

  if (restorePath) {
    await ctx.connectToAppServer(restorePath)
  } else if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    ctx.emitCurrentWorkspaceStatus(ctx.currentWorkspacePath)
    ctx.emitConnectionStatus(ctx.mainWindow, { status: 'disconnected' })
  }
}

export function remoteForegroundAnchorPath(ctx: DesktopWindowRuntime): string {
  return ctx.currentWorkspacePath || ensureDefaultChatWorkspace()
}

export async function connectDockerDeployment(
  ctx: DesktopWindowRuntime,
  host: SshMachine,
  stack: RemoteStack
): Promise<{ localPort?: number }> {
  if (ctx.argv.includes('--remote')) {
    throw new Error('Saved remote stacks cannot be activated while Desktop was launched with --remote.')
  }

  const existing = ctx.workspaceConnections.get(ctx.buildServersRemoteProject(host, stack).projectId)
  if (existing?.connected) {
    await ctx.promoteWorkspaceConnection(existing)
    return {}
  }
  const manager = ctx.getSshMachinesManager()

  const result = await manager.docker.openAppServerTunnel(host, stack)
  try {
    await ctx.probeRemoteAppServerConnection(result.wsUrl)
  } catch (error) {
    manager.docker.closeStackTunnels(host.id, stack.id)
    const message = error instanceof Error ? error.message : String(error)
    throw new Error(
      ctx.formatRemoteConnectionError(message, {
        stage: 'probe',
        hostName: host.name,
        stackName: stack.name,
        localPort: result.localPort,
        targetPort: stack.appServerPort,
        tokenPresent: result.tokenPresent
      })
    )
  }

  await ctx.updateSettings({
    connectionMode: 'remote',
    remote: undefined,
    activeRemoteStack: { hostId: host.id, stackId: stack.id },
    activeRemoteProject: undefined,
    activeRemoteDevice: undefined
  })
  await ctx.connectToAppServer(ctx.remoteForegroundAnchorPath())
  return { localPort: ctx.lastRemoteStackLocalPort ?? result.localPort }
}

export async function disconnectDockerDeployment(
  ctx: DesktopWindowRuntime,
  hostId: string,
  stackId: string
): Promise<void> {
  const active = ctx.settings.activeRemoteStack
  if (active?.hostId !== hostId || active.stackId !== stackId) {
    const entry = ctx.workspaceConnections.get(`remote:servers:${hostId}:${stackId}`)
    if (entry) ctx.disposeWorkspaceConnection(entry)
    else ctx.sshMachinesManager?.docker.closeStackTunnels(hostId, stackId)
    return
  }

  await ctx.disconnectActiveRemoteProject({ restorePreviousLocal: true })
}

export async function leaveSshRemoteProject(
  ctx: DesktopWindowRuntime,
  machineId: string,
  projectId: string
): Promise<void> {
  const key = ctx.sshRemoteProjectId(machineId, projectId)
  if (ctx.activeRemoteProject?.projectId === key) {
    await ctx.disconnectActiveRemoteProject({ restorePreviousLocal: true })
  } else {
    const entry = ctx.workspaceConnections.get(key)
    if (entry) ctx.disposeWorkspaceConnection(entry)
  }
}

export async function openSshRemoteProject(
  ctx: DesktopWindowRuntime,
  machineId: string,
  projectId: string
): Promise<void> {
  if (ctx.argv.includes('--remote')) {
    throw new Error('Remote projects cannot be opened while Desktop was launched with --remote.')
  }
  const existing = ctx.workspaceConnections.get(ctx.sshRemoteProjectId(machineId, projectId))
  if (existing?.connected) {
    await ctx.promoteWorkspaceConnection(existing)
    return
  }
  const manager = ctx.getSshMachinesManager()
  const opened = await manager.openProject(machineId, projectId)
  try {
    await ctx.probeRemoteAppServerConnection(opened.wsUrl)
  } catch (error) {
    manager.closeProjectForward(machineId, projectId)
    const message = error instanceof Error ? error.message : String(error)
    throw new Error(
      ctx.formatRemoteConnectionError(message, {
        stage: 'probe',
        hostName: opened.machine.name,
        localPort: opened.localPort,
        targetPort: opened.remotePort,
        tokenPresent: opened.tokenPresent
      })
    )
  }

  await ctx.updateSettings({
    connectionMode: 'remote',
    remote: undefined,
    activeRemoteStack: undefined,
    activeRemoteProject: { machineId, projectId },
    activeRemoteDevice: undefined
  })
  await ctx.connectToAppServer(ctx.remoteForegroundAnchorPath())
}

export async function openPairedComputerProject(
  ctx: DesktopWindowRuntime,
  computerId: string,
  projectId: string
): Promise<void> {
  if (ctx.argv.includes('--remote')) {
    throw new Error('Remote projects cannot be opened while Desktop was launched with --remote.')
  }
  const generation = ++ctx.deviceOpenGeneration
  ctx.clearPendingDeviceOpenRetry()
  const current = (): boolean => !ctx.runtimeIsStopping() && ctx.deviceOpenGeneration === generation &&
    Boolean(ctx.app.pairedComputers.find(computerId, projectId))
  const key = ctx.deviceRemoteProjectId(computerId, projectId)
  const existing = ctx.workspaceConnections.get(key)
  if (existing?.connected && existing.device && ctx.activeRemoteProject?.projectId === key) return
  let opened: OpenedPairedProject | null = null
  try {
    if (!existing?.device || !existing.initializeResult) {
      opened = await ctx.app.pairedComputers.openProject(computerId, projectId)
      if (!current()) throw new Error('Project opening was cancelled.')
      await ctx.probeRemoteAppServerConnection(opened.bridge.wsUrl)
    }
    if (!current()) throw new Error('Project opening was cancelled.')
    await ctx.updateSettings({
      connectionMode: 'remote',
      remote: undefined,
      activeRemoteStack: undefined,
      activeRemoteProject: undefined,
      activeRemoteDevice: { computerId, projectId }
    })
    if (!current()) throw new Error('Project opening was cancelled.')
    const target = ctx.workspaceConnections.get(key)
    if (target?.device && target.initializeResult) {
      ctx.promoteDeviceProject(target)
    } else if (opened) {
      await ctx.installDeviceProjectConnection(opened, ctx.remoteForegroundAnchorPath())
    } else {
      if (!await ctx.connectDeviceProject(computerId, projectId, ctx.remoteForegroundAnchorPath())) {
        throw new Error('Project opening was cancelled.')
      }
      return
    }
    if (!current()) throw new Error('Project opening was cancelled.')
  } finally {
    const bridge = opened?.bridge
    if (bridge && ctx.workspaceConnections.get(key)?.device?.bridge !== bridge) {
      bridge.close()
    }
  }
}

export async function leavePairedComputerProject(
  ctx: DesktopWindowRuntime,
  computerId: string,
  projectId?: string
): Promise<void> {
  const ref = ctx.settings.activeRemoteDevice
  const foregroundMatches = ref?.computerId === computerId && (!projectId || ref.projectId === projectId)
  if (foregroundMatches) ctx.deviceOpenGeneration += 1
  for (const entry of [...ctx.workspaceConnections.values()]) {
    if (entry.remote?.source === 'device' && entry.remote.hostId === computerId &&
      (!projectId || entry.remote.remoteProjectId === projectId) && entry.projectId !== ctx.activeRemoteProject?.projectId) {
      ctx.disposeWorkspaceConnection(entry)
    }
  }
  if (foregroundMatches) await ctx.disconnectActiveRemoteProject({ restorePreviousLocal: true })
}

export async function disconnectRemoteProject(ctx: DesktopWindowRuntime, projectId?: string): Promise<void> {
  if (!projectId || projectId === ctx.activeRemoteProject?.projectId) {
    await ctx.disconnectActiveRemoteProject({ restorePreviousLocal: true })
    return
  }
  const entry = ctx.workspaceConnections.get(projectId)
  if (entry?.kind === 'remote') ctx.disposeWorkspaceConnection(entry)
}
