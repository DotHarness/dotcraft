import type { ConnectionErrorType } from '../../shared/connectionStatus'
import { normalizeLocale, translate } from '../../shared/locales'
import { readBrowserTurnNotification } from '../browserTabLifecycle'
import { browserUseManager } from '../browserUseManager'
import { computerUseManager } from '../computerUse/runtime'
import { DesktopAppServerClient, type InitializeResult } from '../DesktopAppServerClient'
import { broadcastNotification } from '../ipcBridge'
import { bindLocalProjectRecovery } from '../localProjectRecovery'
import { handleWorkspaceNodeReplNotification, ownsWorkspaceNodeReplThread } from './windowNodeReplRequests'
import { handleWorkspaceThreadPanelDeletion } from './windowThreadPanelDeletion'
import { refreshVoiceChatGptAvailability } from '../voice/voiceIpc'
import {
  canBridgeRendererInteractiveServerRequest,
  getWorkspaceNotificationForeground,
  isCurrentForegroundWorkspaceConnection,
  shouldBridgeWorkspaceServerRequest
} from '../workspaceConnectionRouting'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { ConnectViaWebSocketOptions, WorkspaceConnectionEntry } from './windowTypes'

export async function connectViaWebSocket(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  wsUrl: string,
  options: ConnectViaWebSocketOptions = {}
): Promise<void> {
  if (ctx.runtimeIsStopping() || !ctx.mainWindow || ctx.mainWindow.isDestroyed()) {
    return
  }
  const win = ctx.mainWindow!
  ctx.lastAppServerWsUrl = wsUrl
  ctx.emitConnectionStatus(win, { status: 'connecting' })
  ctx.reregisterIpcForWorkspace(workspacePath)

  const generation = ++ctx.connectionGeneration
  const client = DesktopAppServerClient.fromWebSocket(wsUrl, {
    autoReconnect: options.autoReconnect,
    initializeTimeoutMs: options.initializeTimeoutMs,
    remoteHost: options.projectKind === 'remote'
  })
  const projectId = options.projectId ?? ctx.localProjectId(workspacePath)
  const connectionKey = projectId
  const identityWorkspacePath = options.identityWorkspacePath ?? workspacePath
  const previousEntry = ctx.workspaceConnections.get(connectionKey)
  if (previousEntry && previousEntry.client !== client) {
    previousEntry.client.dispose()
  }
  const entry: WorkspaceConnectionEntry = {
    key: connectionKey,
    projectId,
    kind: options.projectKind ?? 'local',
    workspacePath: identityWorkspacePath,
    localWorkspacePath: workspacePath,
    displayPath: options.displayPath ?? workspacePath,
    remote: options.remote,
    wsUrl,
    client,
    role: 'foreground',
    connected: false,
    connecting: true,
    lastUsedAt: Date.now(),
    threads: []
  }
  ctx.workspaceConnections.set(connectionKey, entry)
  ctx.wireClient = client
  const isCurrentConnectAttempt = (): boolean =>
    !ctx.runtimeIsStopping() &&
    ctx.wireClient === client &&
    ctx.connectionGeneration === generation &&
    ctx.mainWindow === win &&
    !win.isDestroyed()
  const isCurrentForegroundClient = (): boolean =>
    isCurrentForegroundWorkspaceConnection({
      appQuitting: ctx.runtimeIsStopping(),
      mainWindow: ctx.mainWindow,
      window: win,
      wireClient: ctx.wireClient,
      client,
      role: entry.role
    })

  client.onNotification((method, params) => {
    if (!ctx.runtimeIsStopping() && ctx.workspaceConnections.get(entry.key) === entry) {
      const turn = readBrowserTurnNotification(method, params)
      const ownsThread = turn && ownsWorkspaceNodeReplThread(entry, turn.threadId)
      handleWorkspaceThreadPanelDeletion(entry, method, params, ctx.mainWindow)
      handleWorkspaceNodeReplNotification(entry, method, params)
      ctx.getThreadTasks().handleNotification(method, params)
      if (ownsThread) {
        if (turn.terminal) computerUseManager.handleTurnEnded(turn.threadId, turn.turnId)
        browserUseManager.handleTurnNotification(method, params, entry.localWorkspacePath ?? entry.workspacePath)
      }
      if (turn?.terminal) {
        void client.releaseBrowserTurn(turn.threadId, turn.turnId).catch(console.warn).finally(() => {
          if (!ctx.runtimeIsStopping()) ctx.scheduleSecondaryWorkspaceRefresh()
        })
      }
    }
    const foreground = getWorkspaceNotificationForeground(method, {
      appQuitting: ctx.runtimeIsStopping(),
      mainWindow: ctx.mainWindow,
      window: win,
      wireClient: ctx.wireClient,
      client,
      role: entry.role
    })
    if (foreground == null) return
    if (foreground && method === 'provider/auth/usage/updated') refreshVoiceChatGptAvailability()

    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.applyWorkspaceThreadNotification(entry, method, params)
      broadcastNotification(ctx.mainWindow, method, params, ctx.settings, entry.workspacePath, foreground)
    }
  })

  client.onServerRequest(async (method, params) => {
    const routingState = {
      appQuitting: ctx.runtimeIsStopping(),
      mainWindow: ctx.mainWindow,
      window: win,
      wireClient: ctx.wireClient,
      client,
      role: entry.role
    }
    return ctx.handleWorkspaceServerRequest(
      method,
      params,
      entry,
      shouldBridgeWorkspaceServerRequest(routingState),
      canBridgeRendererInteractiveServerRequest(routingState)
    )
  })
  let connectedOnce = false
  let initialFailureEmitted = false
  const emitInitialConnectionFailure = (
    error: unknown,
    errorType: ConnectionErrorType,
    stage: string
  ): void => {
    if (initialFailureEmitted || !isCurrentConnectAttempt()) return
    initialFailureEmitted = true
    const message = error instanceof Error ? error.message : String(error)
    ctx.resetThreadToolState()
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'error',
        errorMessage: ctx.formatRemoteConnectionError(message, options.remoteDiagnostic, stage),
        errorType
      })
    }
    if (ctx.wireClient === client) {
      ctx.wireClient = null
      ctx.lastAppServerWsUrl = null
    }
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = message
    ctx.emitWorkspaceProjects()
    client.dispose()
    options.onInitialFailure?.(error)
  }
  const emitConnected = (result: InitializeResult): void => {
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result
    entry.lastUsedAt = Date.now()

    if (entry.role === 'secondary') {
      void ctx.refreshConnectionThreadList(entry)
      return
    }
    if (!isCurrentForegroundClient()) return
    if (!connectedOnce) options.onReady?.()
    connectedOnce = true
    ctx.clearActiveRemoteReconnectTimer()
    ctx.activeRemoteReconnectAttempt = 0
    ctx.resetThreadToolState()
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'connected',
        serverInfo: result.serverInfo,
        capabilities: result.capabilities as Record<string, unknown>,
        dashboardUrl: result.dashboardUrl
      })
      ctx.flushPendingWorkspaceOpenThread(ctx.mainWindow)
      ctx.emitWorkspaceProjects()
    }
    if (
      entry.kind !== 'remote' &&
      !options.remoteDiagnostic &&
      !ctx.activeRemoteWorkspace &&
      ctx.resolveConnectionMode(ctx.settings) === 'local'
    ) {
      void ctx.restoreEnabledModuleTracking()
    }
  }
  client.on('ready', (result: InitializeResult) => emitConnected(result))
  client.on('reconnected', (result: InitializeResult) => emitConnected(result))
  bindLocalProjectRecovery(client, {
    refreshProjects: () => ctx.app.localProjects.refresh(),
    refreshSecondaryConnections: ctx.refreshSecondaryWorkspaceConnections,
    isCurrent: () =>
      !ctx.runtimeIsStopping() &&
      isCurrentForegroundClient() &&
      entry.kind !== 'remote' &&
      entry.connected &&
      !ctx.activeRemoteWorkspace &&
      ctx.resolveConnectionMode(ctx.settings) === 'local'
  })
  client.on('close', () => {
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = undefined
    ctx.emitWorkspaceProjects()
    if (!ctx.runtimeIsStopping() && entry.kind !== 'remote') ctx.scheduleSecondaryWorkspaceRefresh()
    if (entry.role === 'secondary') return
    if (!isCurrentForegroundClient()) return
    ctx.resetThreadToolState()
    if (!connectedOnce && options.initialDisconnectIsError) {
      if (!initialFailureEmitted) {
        emitInitialConnectionFailure(
          new Error('Remote AppServer WebSocket closed before initialize completed.'),
          'remote-config-invalid',
          'websocket-close-before-ready'
        )
      }
      return
    }
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      const loc = normalizeLocale(ctx.settings.locale)
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
    ctx.scheduleActiveRemoteStackReconnect('websocket closed')
  })
  client.on('reconnect-error', (err) => {
    entry.errorMessage = err instanceof Error ? err.message : String(err)
    entry.connecting = false
    ctx.emitWorkspaceProjects()
    if (entry.role === 'secondary') return
    if (!isCurrentForegroundClient()) return
    const message = err instanceof Error ? err.message : String(err)
    if (!connectedOnce && options.initialDisconnectIsError) {
      emitInitialConnectionFailure(
        err,
        ctx.classifyRemoteInitialError(message),
        /timed out/i.test(message) ? 'initialize-timeout' : 'initialize'
      )
      return
    }
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: 'error', errorMessage: message })
    }
  })
}
