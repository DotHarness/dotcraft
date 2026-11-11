import type { ConnectionErrorType } from '../../shared/connectionStatus'
import { normalizeLocale, translate } from '../../shared/locales'
import { readBrowserTurnNotification } from '../browserTabLifecycle'
import { browserUseManager } from '../browserUseManager'
import { computerUseManager } from '../computerUse/runtime'
import { DesktopAppServerClient, type InitializeResult } from '../DesktopAppServerClient'
import { broadcastNotification } from '../ipcBridge'
import { cancelConnectionServerRequests } from '../ipcServerRequests'
import { resetDesktopThreadToolBindings } from '../desktopRuntimeThreadTools'
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
import { clearRemoteProjectReconnect } from './windowRemoteConnections'
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
  const win = ctx.mainWindow
  const role = options.role ?? 'foreground'
  if (role === 'foreground') {
    ctx.lastAppServerWsUrl = wsUrl
    ctx.emitConnectionStatus(win, { status: 'connecting' })
    ctx.reregisterIpcForWorkspace(workspacePath)
  }

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
    ctx.workspaceConnections.delete(connectionKey)
    clearRemoteProjectReconnect(ctx, previousEntry)
    previousEntry.client.dispose()
    if (previousEntry.device && previousEntry.device !== options.device) {
      ctx.disposeDeviceProjectConnection(previousEntry)
    }
  }
  const entry: WorkspaceConnectionEntry = {
    ...options.existingEntry,
    key: connectionKey,
    projectId,
    kind: options.projectKind ?? 'local',
    workspacePath: identityWorkspacePath,
    localWorkspacePath: workspacePath,
    displayPath: options.displayPath ?? workspacePath,
    remote: options.remote,
    wsUrl,
    client,
    role,
    connected: false,
    connecting: true,
    lastUsedAt: options.existingEntry?.lastUsedAt ?? Date.now(),
    threads: options.existingEntry?.threads ?? [],
    device: options.device,
    remoteProject: options.remoteProject ?? options.existingEntry?.remoteProject,
    reconnectTimer: null,
    reconnectAttempt: options.existingEntry?.reconnectAttempt ?? 0
  }
  ctx.workspaceConnections.set(connectionKey, entry)
  ctx.startupProjectStates.delete(connectionKey)
  if (role === 'foreground') ctx.wireClient = client
  const deviceGeneration = entry.device?.generation
  const isCurrentConnectAttempt = (): boolean =>
    !ctx.runtimeIsStopping() &&
    ctx.workspaceConnections.get(connectionKey) === entry &&
    (!entry.device || entry.device.generation === deviceGeneration) &&
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
    if (!isCurrentConnectAttempt()) return
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
    const foreground = getWorkspaceNotificationForeground(method, {
      appQuitting: ctx.runtimeIsStopping(),
      mainWindow: ctx.mainWindow,
      window: win,
      wireClient: ctx.wireClient,
      client,
      role: entry.role
    }, params)
    if (foreground == null) return
    if (foreground && method === 'provider/auth/usage/updated') refreshVoiceChatGptAvailability()

    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.applyWorkspaceThreadNotification(entry, method, params)
      broadcastNotification(ctx.mainWindow, method, params, ctx.settings, entry.workspacePath, foreground,
        client.connectionId ? { connectionKey: entry.key, connectionId: client.connectionId } : undefined)
    }
  })

  client.onServerRequest(async (method, params, requestId, signal) => {
    if (!isCurrentConnectAttempt()) throw new Error('Workspace connection is no longer available.')
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
      canBridgeRendererInteractiveServerRequest(routingState),
      requestId,
      signal
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
    if (entry.device) {
      entry.connected = false
      entry.connecting = false
      entry.errorMessage = error instanceof Error ? error.message : String(error)
      entry.device.generation += 1
      client.dispose()
      entry.device.bridge?.close()
      entry.device.bridge = null
      if (isCurrentForegroundClient()) {
        ctx.emitConnectionStatus(win, { status: 'error', errorMessage: entry.errorMessage, errorType })
      }
      ctx.emitWorkspaceProjects()
      ctx.scheduleDeviceProjectReconnect(entry, error)
      return
    }
    const message = error instanceof Error ? error.message : String(error)
    if (isCurrentForegroundClient() && ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
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
    if (entry.remote?.source === 'ssh' || entry.remote?.source === 'servers') ctx.scheduleRemoteProjectReconnect(entry)
  }
  const emitConnected = (result: InitializeResult): void => {
    if (!isCurrentConnectAttempt()) return
    if (entry.device) ctx.deviceProjectConnectionReady(entry)
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result
    clearRemoteProjectReconnect(ctx, entry)
    entry.reconnectAttempt = 0
    const firstReady = !connectedOnce
    connectedOnce = true

    if (entry.role === 'secondary') {
      ctx.emitWorkspaceProjects()
      void ctx.refreshConnectionThreadList(entry)
      return
    }
    if (!isCurrentForegroundClient()) return
    if (entry.kind === 'remote') void ctx.refreshSecondaryWorkspaceConnections()
    if (firstReady) options.onReady?.()
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'connected',
        serverInfo: result.serverInfo,
        capabilities: result.capabilities as Record<string, unknown>,
        dashboardUrl: result.dashboardUrl,
        connectionKey: entry.key,
        ...(client.connectionId ? { connectionId: client.connectionId } : {})
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
  client.on('disposed', (connectionId: string | null) => {
    if (connectionId) cancelConnectionServerRequests(connectionId)
    resetDesktopThreadToolBindings(client)
  })
  client.on('close', (connectionId: string | null) => {
    if (connectionId) cancelConnectionServerRequests(connectionId)
    resetDesktopThreadToolBindings(client)
    if (!isCurrentConnectAttempt() || initialFailureEmitted) return
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = undefined
    ctx.emitWorkspaceProjects()
    if (!ctx.runtimeIsStopping() && entry.kind !== 'remote') ctx.scheduleSecondaryWorkspaceRefresh()
    if (entry.device) {
      if (!connectedOnce && options.initialDisconnectIsError) {
        emitInitialConnectionFailure(new Error('Remote AppServer WebSocket closed before initialize completed.'), 'remote-config-invalid', 'websocket-close-before-ready')
      } else {
        if (isCurrentForegroundClient()) ctx.emitConnectionStatus(win, { status: 'disconnected' })
        ctx.scheduleDeviceProjectReconnect(entry)
      }
      return
    }
    if (entry.remote?.source === 'ssh' || entry.remote?.source === 'servers') {
      ctx.scheduleRemoteProjectReconnect(entry)
    }
    if (entry.role === 'secondary') return
    if (!isCurrentForegroundClient()) return
    if (!connectedOnce && options.initialDisconnectIsError) {
      emitInitialConnectionFailure(
        new Error('Remote AppServer WebSocket closed before initialize completed.'),
        'remote-config-invalid',
        'websocket-close-before-ready'
      )
      return
    }
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      const loc = normalizeLocale(ctx.settings.locale)
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
  })
  client.on('reconnect-error', (err) => {
    if (!isCurrentConnectAttempt()) return
    if (entry.device) {
      emitInitialConnectionFailure(err, ctx.classifyRemoteInitialError(err instanceof Error ? err.message : String(err)), 'initialize')
      return
    }
    entry.errorMessage = err instanceof Error ? err.message : String(err)
    entry.connecting = false
    ctx.emitWorkspaceProjects()
    if (entry.remote?.source === 'ssh' || entry.remote?.source === 'servers') {
      if (!connectedOnce && options.initialDisconnectIsError) {
        emitInitialConnectionFailure(err, ctx.classifyRemoteInitialError(entry.errorMessage), 'initialize')
      } else ctx.scheduleRemoteProjectReconnect(entry)
    }
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
