import { resolveRemoteWebSocketConfig } from '../../shared/remoteConnection'
import { desktopHubConnectionFailure, resolveDesktopBinarySource, type DesktopHubClient } from '../desktopHub'
import { logDesktopHubFailure, logLiveDesktopHub } from '../desktopHubDiagnostics'
import { resolveDotCraftRuntimeTools } from '../ripgrepRuntime'
import { hasRemoteEndpointArg } from '../workspaceArgs'
import { startWorkspaceConnectionTiming } from '../workspaceConnectionTiming'
import { initializeWorkspace, shouldRouteWorkspaceThroughSetupBeforeAppServerStart } from '../workspaceSetup'
import { REMOTE_INITIALIZE_TIMEOUT_MS } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'

export function prewarmLocalAppServer(ctx: DesktopWindowRuntime, workspacePath: string): void {
  if (ctx.runtimeIsStopping()) return
  const startedAt = Date.now()
  console.info('[desktop] prewarming local AppServer', { workspacePath, startedAt })
  const promise = ctx.app.createHubClient(ctx.settings).ensureAppServer(workspacePath, {
    runtimeTools: resolveDotCraftRuntimeTools()
  })
  void promise.then(
    () =>
      console.info('[desktop] local AppServer prewarm finished', {
        workspacePath,
        elapsedMs: Date.now() - startedAt
      }),
    (error: unknown) => logDesktopHubFailure('prewarm', error, startedAt, workspacePath)
  )
  ctx.prewarmedAppServer = { workspacePath, promise }
}

export function takePrewarmedAppServer(
  ctx: DesktopWindowRuntime,
  workspacePath: string
): ReturnType<DesktopHubClient['ensureAppServer']> | null {
  const prewarmed = ctx.prewarmedAppServer
  ctx.prewarmedAppServer = null
  return prewarmed?.workspacePath === workspacePath ? prewarmed.promise : null
}

export async function connectToAppServer(ctx: DesktopWindowRuntime, workspacePath: string): Promise<boolean> {
  if (ctx.runtimeIsStopping()) {
    return false
  }
  const recordConnectionTiming = startWorkspaceConnectionTiming(workspacePath)
  const remoteIdx = ctx.argv.indexOf('--remote')
  const launchedWithRemoteUrl = hasRemoteEndpointArg(ctx.argv)
  const connectionMode = ctx.resolveConnectionMode(ctx.settings)
  const usingRemoteConnection = launchedWithRemoteUrl || connectionMode === 'remote'
  const preserveLocalConnections = !usingRemoteConnection && connectionMode === 'local'

  if (shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspacePath, { usingRemoteConnection })) {
    await ctx.openWorkspaceWithoutConnection(workspacePath)
    return false
  }
  if (!usingRemoteConnection && workspacePath) {
    await initializeWorkspace(workspacePath, ctx.settings)
  }

  if (usingRemoteConnection) {
    if (!ctx.previousLocalForegroundWorkspacePath && workspacePath) {
      ctx.previousLocalForegroundWorkspacePath = workspacePath
    }

    const previous = ctx.getWorkspaceConnection(workspacePath)
    if (previous?.role === 'foreground') {
      previous.role = 'secondary'
      previous.lastUsedAt = Date.now()
      ctx.releaseConnectionViewAttachments(previous, 'remote foreground connect')
    }
  } else if (preserveLocalConnections) {
    const existingConnection = ctx.getWorkspaceConnection(workspacePath)
    if (existingConnection?.connected) {
      ctx.promoteWorkspaceConnection(existingConnection)
      recordConnectionTiming('connection-ready', 'existing-connection')
      void ctx.refreshSecondaryWorkspaceConnections()
      return true
    }
    if (ctx.currentWorkspacePath && ctx.currentWorkspacePath !== workspacePath) {
      const previous = ctx.getWorkspaceConnection(ctx.currentWorkspacePath)
      if (previous) {
        previous.role = 'secondary'
        previous.lastUsedAt = Date.now()
        ctx.releaseConnectionViewAttachments(previous, 'workspace demote')
      }
    } else if (ctx.currentWorkspacePath === workspacePath) {
      await ctx.teardownRuntime('reconnect current local workspace before new connect')
    }
  } else {
    await ctx.teardownRuntime('switch/reconnect before new connect')
  }

  ctx.wireClient = null
  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    ctx.emitConnectionStatus(ctx.mainWindow, { status: 'connecting' })
  }
  ctx.currentWorkspacePath = workspacePath

  const activeStack =
    !launchedWithRemoteUrl && connectionMode === 'remote' ? ctx.resolveActiveRemoteStack(ctx.settings) : null
  const activeSshProject =
    !launchedWithRemoteUrl && connectionMode === 'remote' ? ctx.resolveActiveSshProject(ctx.settings) : null
  ctx.lastRemoteStackLocalPort = null

  if (launchedWithRemoteUrl) {
    const endpoint = ctx.argv[remoteIdx + 1]
    const project = ctx.buildEndpointRemoteProject('cli', endpoint, workspacePath)
    ctx.prepareRemoteForeground(project)
    ctx.emitCurrentWorkspaceStatus(workspacePath)
    await ctx.connectViaWebSocket(workspacePath, endpoint, {
      initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
      initialDisconnectIsError: true,
      remoteDiagnostic: { stage: 'cli-remote' },
      projectId: project.projectId,
      projectKind: 'remote',
      identityWorkspacePath: project.identityWorkspacePath,
      displayPath: project.displayPath,
      remote: project.remote
    })
    return true
  }

  if (connectionMode === 'remote') {
    if (activeSshProject) {
      const win = ctx.mainWindow!
      const { machine, project: remoteProject } = activeSshProject
      ctx.emitConnectionStatus(win, { status: 'connecting' })
      try {
        const opened = await ctx.getSshMachinesManager().openProject(machine.id, remoteProject.id)
        if (ctx.runtimeIsStopping() || ctx.currentWorkspacePath !== workspacePath) return false
        ctx.lastRemoteStackLocalPort = opened.localPort
        const project = ctx.buildSshRemoteProject(
          opened.machine,
          opened.project,
          `ws://127.0.0.1:${opened.localPort}/ws`,
          workspacePath
        )
        ctx.prepareRemoteForeground(project)
        ctx.emitCurrentWorkspaceStatus(workspacePath)
        await ctx.connectViaWebSocket(workspacePath, opened.wsUrl, {
          autoReconnect: false,
          initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
          initialDisconnectIsError: true,
          projectId: project.projectId,
          projectKind: 'remote',
          identityWorkspacePath: project.identityWorkspacePath,
          displayPath: project.displayPath,
          remote: project.remote,
          remoteDiagnostic: {
            stage: 'active-remote-project',
            hostName: opened.machine.name,
            localPort: opened.localPort,
            targetPort: opened.remotePort,
            tokenPresent: opened.tokenPresent
          }
        })
        return true
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        ctx.emitConnectionStatus(win, {
          status: 'error',
          errorMessage: ctx.formatRemoteConnectionError(message, {
            stage: 'open-remote-project',
            hostName: machine.name
          }),
          errorType: 'remote-config-invalid'
        })
      }
      return false
    }

    if (ctx.settings.activeRemoteProject?.machineId || ctx.settings.activeRemoteProject?.projectId) {
      const win = ctx.mainWindow!
      ctx.emitConnectionStatus(win, {
        status: 'error',
        errorMessage: 'Saved remote project was not found. Check SSH settings or disconnect this project.',
        errorType: 'remote-config-invalid'
      })
      return false
    }

    if (activeStack) {
      const win = ctx.mainWindow!
      ctx.emitConnectionStatus(win, { status: 'connecting' })
      try {
        const result = await ctx
          .getSshMachinesManager()
          .docker.openAppServerTunnel(activeStack.host, activeStack.stack, { forceNew: true })
        ctx.lastRemoteStackLocalPort = result.localPort
        const tunnelEndpoint = `ws://127.0.0.1:${result.localPort}/ws`
        const project = ctx.buildServersRemoteProject(
          activeStack.host,
          activeStack.stack,
          tunnelEndpoint,
          workspacePath
        )
        ctx.prepareRemoteForeground(project)
        ctx.emitCurrentWorkspaceStatus(workspacePath)
        await ctx.connectViaWebSocket(workspacePath, result.wsUrl, {
          autoReconnect: false,
          initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
          initialDisconnectIsError: true,
          projectId: project.projectId,
          projectKind: 'remote',
          identityWorkspacePath: project.identityWorkspacePath,
          displayPath: project.displayPath,
          remote: project.remote,
          remoteDiagnostic: {
            stage: 'active-remote-stack',
            hostName: activeStack.host.name,
            stackName: activeStack.stack.name,
            localPort: result.localPort,
            targetPort: activeStack.stack.appServerPort,
            tokenPresent: result.tokenPresent
          }
        })
        return true
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        ctx.emitConnectionStatus(win, {
          status: 'error',
          errorMessage: ctx.formatRemoteConnectionError(message, {
            stage: 'open-appserver-tunnel',
            hostName: activeStack.host.name,
            stackName: activeStack.stack.name,
            targetPort: activeStack.stack.appServerPort
          }),
          errorType: 'remote-config-invalid'
        })
      }
      return false
    }

    if (ctx.settings.activeRemoteStack?.hostId || ctx.settings.activeRemoteStack?.stackId) {
      const win = ctx.mainWindow!
      ctx.emitConnectionStatus(win, {
        status: 'error',
        errorMessage: 'Saved remote stack was not found. Check Servers settings or disconnect this stack.',
        errorType: 'remote-config-invalid'
      })
      return false
    }

    const remoteConfig = resolveRemoteWebSocketConfig(ctx.settings.remote)
    if (!remoteConfig.ok) {
      const win = ctx.mainWindow!
      ctx.emitConnectionStatus(win, {
        status: 'error',
        errorMessage: remoteConfig.message,
        errorType: 'remote-config-invalid'
      })
      return false
    }
    const project = ctx.buildEndpointRemoteProject('manual', remoteConfig.connectUrl, workspacePath)
    ctx.prepareRemoteForeground(project)
    ctx.emitCurrentWorkspaceStatus(workspacePath)
    await ctx.connectViaWebSocket(workspacePath, remoteConfig.connectUrl, {
      initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
      initialDisconnectIsError: true,
      remoteDiagnostic: { stage: 'manual-remote' },
      projectId: project.projectId,
      projectKind: 'remote',
      identityWorkspacePath: project.identityWorkspacePath,
      displayPath: project.displayPath,
      remote: project.remote
    })
    return true
  }

  ctx.setActiveRemoteProject(null)
  ctx.emitCurrentWorkspaceStatus(workspacePath)

  ctx.reregisterIpcForWorkspace(workspacePath)
  const hubStartedAt = Date.now()
  try {
    const hubClient = ctx.app.createHubClient(ctx.settings)
    const ensured = await (ctx.takePrewarmedAppServer(workspacePath) ??
      hubClient.ensureAppServer(workspacePath, { runtimeTools: resolveDotCraftRuntimeTools() }))
    if (ctx.currentWorkspacePath !== workspacePath || ctx.runtimeIsStopping()) return false

    recordConnectionTiming('hub-ready', 'ensure-and-connect')
    ctx.startHubEventSubscription(workspacePath, hubClient)
    logLiveDesktopHub(hubClient)
    await ctx.connectViaWebSocket(workspacePath, ctx.getManagedAppServerEndpoint(ensured), {
      onReady: () => recordConnectionTiming('connection-ready', 'ensure-and-connect')
    })
    return true
  } catch (err) {
    if (ctx.runtimeIsStopping() || ctx.currentWorkspacePath !== workspacePath) return false
    logDesktopHubFailure('foreground-connect', err, hubStartedAt, workspacePath)
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(
        ctx.mainWindow,
        desktopHubConnectionFailure(err, resolveDesktopBinarySource(ctx.settings))
      )
    }
    return false
  }
}
