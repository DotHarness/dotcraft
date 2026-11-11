import { normalizeLocale, translate } from '../../shared/locales'
import type { WorkspaceProjectKind } from '../../shared/workspaceProjects'
import { retryAppServerConnection, type RetryConnectionRequest } from '../appServerRetry'
import { readBrowserTurnNotification } from '../browserTabLifecycle'
import { browserUseManager } from '../browserUseManager'
import { computerUseManager } from '../computerUse/runtime'
import {
  ensureDefaultChatWorkspace,
  isDefaultChatWorkspace,
  resolveDefaultChatWorkspacePath
} from '../defaultChatWorkspace'
import { DesktopAppServerClient, type InitializeResult } from '../DesktopAppServerClient'
import { type DesktopHubClient, type HubAppServerResponse, type HubEvent } from '../desktopHub'
import { broadcastNotification } from '../ipcBridge'
import { cancelConnectionServerRequests } from '../ipcServerRequests'
import { resetDesktopThreadToolBindings } from '../desktopRuntimeThreadTools'
import { handleWorkspaceNodeReplNotification, ownsWorkspaceNodeReplThread } from './windowNodeReplRequests'
import { handleWorkspaceThreadPanelDeletion } from './windowThreadPanelDeletion'
import { resolveDotCraftRuntimeTools } from '../ripgrepRuntime'
import { refreshVoiceChatGptAvailability } from '../voice/voiceIpc'
import { hasRemoteEndpointArg } from '../workspaceArgs'
import {
  canBridgeRendererInteractiveServerRequest,
  getWorkspaceNotificationForeground,
  shouldBridgeWorkspaceServerRequest
} from '../workspaceConnectionRouting'
import { getWorkspaceStatus } from '../workspaceSetup'
import { SECONDARY_WORKSPACE_CONNECTION_LIMIT } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function getManagedAppServerEndpoint(
  _ctx: DesktopWindowRuntime,
  response: HubAppServerResponse
): string {
  const endpoint = response.endpoints?.appServerWebSocket
  if (!endpoint?.trim()) {
    throw new Error('Hub did not return an AppServer WebSocket endpoint.')
  }
  return endpoint
}

export async function restartCurrentManagedAppServer(ctx: DesktopWindowRuntime): Promise<void> {
  if (!ctx.currentWorkspacePath) {
    throw new Error('Open a workspace before restarting AppServer.')
  }
  if (ctx.argv.includes('--remote')) {
    throw new Error('Cannot restart AppServer while using a remote WebSocket connection.')
  }
  if (ctx.resolveConnectionMode(ctx.settings) === 'remote') {
    throw new Error('Restart is only available for Hub-managed local AppServers.')
  }
  const hubClient = ctx.app.createHubClient(ctx.settings)
  const restarted = await hubClient.restartAppServer(ctx.currentWorkspacePath, resolveDotCraftRuntimeTools())
  await ctx.connectViaWebSocket(ctx.currentWorkspacePath, ctx.getManagedAppServerEndpoint(restarted))
  ctx.startHubEventSubscription(ctx.currentWorkspacePath, hubClient)
}

export async function retryCurrentAppServerConnection(
  ctx: DesktopWindowRuntime,
  request?: RetryConnectionRequest
): Promise<void> {
  await retryAppServerConnection(request, {
    currentWorkspacePath: ctx.currentWorkspacePath,
    launchedWithRemote: ctx.argv.includes('--remote'),
    connectionMode: ctx.resolveConnectionMode(ctx.settings),
    reconnect: async () => {
      await ctx.connectToAppServer(ctx.currentWorkspacePath)
    },
    restartManaged: ctx.restartCurrentManagedAppServer
  })
}

export function isCurrentWorkspaceEvent(
  ctx: DesktopWindowRuntime,
  event: HubEvent,
  workspacePath: string
): boolean {
  if (!event.workspacePath) return false
  return ctx.isSameWorkspacePath(event.workspacePath, workspacePath)
}

export function isHubAppServerLifecycleEvent(_ctx: DesktopWindowRuntime, event: HubEvent): boolean {
  return event.kind.startsWith('appserver.')
}

export function isSecondaryProjectEvent(ctx: DesktopWindowRuntime, event: HubEvent): boolean {
  const workspacePath = event.workspacePath?.trim()
  if (!workspacePath || ctx.isWorkspaceForeground(workspacePath)) return false
  if (isDefaultChatWorkspace(workspacePath)) return true
  return !ctx.activeRemoteWorkspace && ctx.resolveConnectionMode(ctx.settings) === 'local' &&
    ctx.app.localProjects.has(workspacePath)
}

export function scheduleSecondaryWorkspaceRefresh(ctx: DesktopWindowRuntime): void {
  if (ctx.runtimeIsStopping()) return
  if (ctx.secondaryRefreshTimer) {
    clearTimeout(ctx.secondaryRefreshTimer)
  }
  ctx.secondaryRefreshTimer = setTimeout(() => {
    ctx.secondaryRefreshTimer = null
    void ctx.refreshSecondaryWorkspaceConnections()
  }, 150)
}

export function startHubEventSubscription(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  hubClient: DesktopHubClient
): void {
  if (ctx.runtimeIsStopping()) return
  ctx.hubEventAbortController?.abort()
  const controller = new AbortController()
  ctx.hubEventAbortController = controller

  void hubClient
    .subscribeEvents((event) => {
      if (ctx.isHubAppServerLifecycleEvent(event) && ctx.isSecondaryProjectEvent(event)) {
        ctx.scheduleSecondaryWorkspaceRefresh()
      }

      if (!ctx.isCurrentWorkspaceEvent(event, workspacePath)) return
      if (ctx.isHubAppServerLifecycleEvent(event) && !ctx.isWorkspaceForeground(workspacePath)) return

      if (event.kind === 'appserver.exited') {
        ctx.wireClient?.dispose()
        ctx.wireClient = null
        if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
          const loc = normalizeLocale(ctx.settings.locale)
          ctx.emitConnectionStatus(ctx.mainWindow, {
            status: 'disconnected',
            errorMessage: translate(loc, 'main.status.reconnecting')
          })
        }
        return
      }

      if (event.kind === 'appserver.running') {
        const data = event.data as { endpoints?: Record<string, string> } | null
        const endpoint = data?.endpoints?.appServerWebSocket
        if (endpoint && ctx.currentWorkspacePath === workspacePath && !ctx.runtimeIsStopping()) {
          void ctx.connectViaWebSocket(workspacePath, endpoint)
        }
      }

      if (event.kind === 'notification.requested' && ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
        const data = event.data as { kind?: string; title?: string; body?: string } | null
        broadcastNotification(
          ctx.mainWindow,
          data?.kind ?? 'hub/notification',
          data ?? {},
          ctx.settings,
          event.workspacePath ?? workspacePath
        )
      }
    }, controller.signal)
    .catch((error) => {
      if (!controller.signal.aborted) {
        console.warn('[desktop] Hub event subscription ended', error)
      }
    })
}

export async function promoteWorkspaceConnection(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): Promise<boolean> {
  const generation = ++ctx.deviceOpenGeneration
  const project = entry.remoteProject ?? entry.device?.project
  if (project) {
    await ctx.updateSettings({
      connectionMode: 'remote',
      activeRemoteStack: project.source === 'servers' ? { hostId: project.remote.hostId!, stackId: project.remote.stackId! } : undefined,
      activeRemoteProject: project.source === 'ssh' ? { machineId: project.remote.hostId!, projectId: project.remote.remoteProjectId! } : undefined,
      activeRemoteDevice: project.source === 'device' ? { computerId: project.remote.hostId!, projectId: project.remote.remoteProjectId! } : undefined,
      remote: project.source === 'manual' ? { url: entry.wsUrl } : undefined
    })
  } else {
    await ctx.updateSettings({ connectionMode: 'local', activeRemoteStack: undefined, activeRemoteProject: undefined, activeRemoteDevice: undefined })
  }
  if (ctx.deviceOpenGeneration !== generation || ctx.workspaceConnections.get(entry.key) !== entry) return false
  const previous = ctx.wireClient ? ctx.findWorkspaceConnectionByClient(ctx.wireClient)
    : ctx.activeRemoteProject ? ctx.workspaceConnections.get(ctx.activeRemoteProject.projectId) : undefined
  if (previous && previous.client !== entry.client) ctx.demoteWorkspaceConnection(previous)

  entry.role = 'foreground'
  entry.lastUsedAt = Date.now()
  ctx.wireClient = entry.client
  const workspacePath = project?.localWorkspacePath ?? entry.workspacePath
  ctx.currentWorkspacePath = workspacePath
  ctx.lastAppServerWsUrl = entry.wsUrl
  ctx.lastDashboardUrl = entry.initializeResult?.dashboardUrl ?? null
  ctx.setActiveRemoteProject(project ?? null)
  if (!project) ctx.previousLocalForegroundWorkspacePath = null
  ctx.lastRemoteStackLocalPort = null
  ctx.reregisterIpcForWorkspace(workspacePath)

  ctx.emitCurrentWorkspaceStatus(workspacePath)
  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    if (entry.initializeResult) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'connected',
        serverInfo: entry.initializeResult.serverInfo,
        capabilities: entry.initializeResult.capabilities as Record<string, unknown>,
        dashboardUrl: entry.initializeResult.dashboardUrl,
        connectionKey: entry.key,
        ...(entry.client.connectionId ? { connectionId: entry.client.connectionId } : {})
      })
    } else {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: entry.connected ? 'connected' : 'connecting' })
    }
    const loc = normalizeLocale(ctx.settings.locale)
    ctx.mainWindow.setTitle(
      translate(loc, 'app.titleWithWorkspace', { name: ctx.workspaceTitleName(entry.workspacePath, loc) })
    )
  }
  if (entry.kind !== 'remote') ctx.startHubEventSubscription(entry.workspacePath, ctx.app.createHubClient(ctx.settings))
  ctx.emitWorkspaceProjects()
  return true
}

export function createSecondaryWorkspaceConnection(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  wsUrl: string,
  options: { kind?: WorkspaceProjectKind } = {}
): WorkspaceConnectionEntry {
  const key = ctx.normalizeWorkspaceConnectionKey(workspacePath)
  const existing = ctx.workspaceConnections.get(key)
  if (existing) return existing

  const client = DesktopAppServerClient.fromWebSocket(wsUrl)
  const entry: WorkspaceConnectionEntry = {
    key,
    projectId: ctx.localProjectId(workspacePath),
    kind: options.kind ?? 'local',
    workspacePath,
    localWorkspacePath: workspacePath,
    displayPath: workspacePath,
    wsUrl,
    client,
    role: 'secondary',
    connected: false,
    connecting: true,
    lastUsedAt: Date.now(),
    threads: []
  }
  ctx.workspaceConnections.set(key, entry)
  ctx.startupProjectStates.delete(key)
  ctx.emitWorkspaceProjects()

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
      wireClient: ctx.wireClient,
      client,
      role: entry.role
    }, params)
    if (foreground == null) return
    if (foreground && method === 'provider/auth/usage/updated') refreshVoiceChatGptAvailability()

    ctx.applyWorkspaceThreadNotification(entry, method, params)
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      broadcastNotification(ctx.mainWindow, method, params, ctx.settings, workspacePath, foreground,
        client.connectionId ? { connectionKey: entry.key, connectionId: client.connectionId } : undefined)
    }
  })

  client.onServerRequest(async (method, params, requestId, signal) => {
    const routingState = {
      appQuitting: ctx.runtimeIsStopping(),
      mainWindow: ctx.mainWindow,
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

  const onReady = (result: InitializeResult): void => {
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result
    ctx.emitWorkspaceProjects()
    if (entry.role === 'foreground' && ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'connected', serverInfo: result.serverInfo, capabilities: result.capabilities,
        dashboardUrl: result.dashboardUrl, connectionKey: entry.key,
        ...(client.connectionId ? { connectionId: client.connectionId } : {})
      })
    }
    void ctx.refreshConnectionThreadList(entry)
  }
  client.on('ready', onReady)
  client.on('reconnected', onReady)
  client.on('disposed', (connectionId: string | null) => {
    if (connectionId) cancelConnectionServerRequests(connectionId)
    resetDesktopThreadToolBindings(client)
  })
  client.on('close', (connectionId: string | null) => {
    if (connectionId) cancelConnectionServerRequests(connectionId)
    resetDesktopThreadToolBindings(client)
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = undefined
    ctx.emitWorkspaceProjects()
    if (!ctx.runtimeIsStopping() && entry.kind !== 'remote') ctx.scheduleSecondaryWorkspaceRefresh()
    if (entry.role === 'foreground' && ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      const loc = normalizeLocale(ctx.settings.locale)
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
  })
  client.on('reconnect-error', (error) => {
    entry.errorMessage = error instanceof Error ? error.message : String(error)
    entry.connecting = false
    ctx.emitWorkspaceProjects()
    if (entry.role === 'foreground' && ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: 'error', errorMessage: entry.errorMessage })
    }
  })

  return entry
}

export async function ensureDefaultChatConnection(ctx: DesktopWindowRuntime): Promise<void> {
  if (ctx.runtimeIsStopping()) return
  const chatPath = resolveDefaultChatWorkspacePath()
  if (ctx.isWorkspaceForeground(chatPath)) return
  const existing = ctx.getWorkspaceConnection(chatPath)
  if (existing?.connected || existing?.connecting) return

  try {
    ensureDefaultChatWorkspace()
    if (getWorkspaceStatus(chatPath).status !== 'ready') return

    const ensured = await ctx.app.createHubClient(ctx.settings).ensureAppServer(chatPath, {
      runtimeTools: resolveDotCraftRuntimeTools()
    })
    if (ctx.runtimeIsStopping() || ctx.isWorkspaceForeground(chatPath)) return
    const endpoint = ensured.endpoints?.appServerWebSocket
    if (endpoint?.trim()) {
      if (existing && existing.wsUrl !== endpoint) ctx.disposeWorkspaceConnection(existing)
      ctx.createSecondaryWorkspaceConnection(chatPath, endpoint, { kind: 'chat' })
    }
  } catch (error) {
    console.warn('[desktop] failed to ensure default chat workspace connection', error)
  }
}

export async function startLocalProjectConnection(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  canContinue: () => boolean = () => true
): Promise<void> {
  const path = workspacePath.trim()
  if (!path || ctx.runtimeIsStopping() || !canContinue() || ctx.isWorkspaceForeground(path)) return
  if (!ctx.app.localProjects.has(path)) throw new Error('Only saved local projects can be started.')
  const existing = ctx.getWorkspaceConnection(path)
  if (existing?.connected || existing?.connecting) return

  const ensured = await ctx.app.createHubClient(ctx.settings).ensureAppServer(path, {
    runtimeTools: resolveDotCraftRuntimeTools()
  })
  if (ctx.runtimeIsStopping() || !canContinue() || ctx.isWorkspaceForeground(path) ||
    !ctx.app.localProjects.has(path)) return
  const endpoint = ctx.getManagedAppServerEndpoint(ensured)
  const current = ctx.getWorkspaceConnection(path)
  if (current?.connected || current?.connecting) return
  if (current) ctx.disposeWorkspaceConnection(current)
  const entry = ctx.createSecondaryWorkspaceConnection(path, endpoint)
  entry.lastUsedAt = Date.now()
}

function isIdleSecondary(entry: WorkspaceConnectionEntry): boolean {
  return entry.role === 'secondary' && entry.kind !== 'chat' &&
    !(entry.kind !== 'remote' && isDefaultChatWorkspace(entry.workspacePath)) &&
    !entry.client.hasRetainedThreadAttachments
}

export function enforceSecondaryWorkspaceBudget(ctx: DesktopWindowRuntime): void {
  const idle = [...ctx.workspaceConnections.values()]
    .filter(isIdleSecondary)
    .sort((left, right) => right.lastUsedAt - left.lastUsedAt)
  for (const entry of idle.slice(SECONDARY_WORKSPACE_CONNECTION_LIMIT)) {
    ctx.disposeWorkspaceConnection(entry)
  }
}

export async function refreshSecondaryWorkspaceConnections(ctx: DesktopWindowRuntime): Promise<void> {
  if (ctx.runtimeIsStopping() || hasRemoteEndpointArg(ctx.argv)) return
  enforceSecondaryWorkspaceBudget(ctx)
  if (ctx.secondaryRefreshTimer) clearTimeout(ctx.secondaryRefreshTimer)
  ctx.secondaryRefreshTimer = null
  await ctx.ensureDefaultChatConnection()
  if (ctx.runtimeIsStopping() || ctx.activeRemoteWorkspace || ctx.resolveConnectionMode(ctx.settings) !== 'local') return

  let liveAppServers: HubAppServerResponse[]
  try {
    liveAppServers = await ctx.app.createHubClient(ctx.settings).listAppServers()
  } catch (error) {
    console.warn('[desktop] failed to list Hub appservers for secondary workspaces', error)
    ctx.emitWorkspaceProjects()
    return
  }
  if (ctx.runtimeIsStopping() || ctx.activeRemoteWorkspace || ctx.resolveConnectionMode(ctx.settings) !== 'local') return

  const liveByKey = new Map<string, HubAppServerResponse>()
  for (const appServer of liveAppServers) {
    if (!ctx.isRunningHubAppServer(appServer)) continue
    const workspacePath = appServer.workspacePath || appServer.canonicalWorkspacePath
    if (!workspacePath) continue
    liveByKey.set(ctx.normalizeWorkspaceConnectionKey(workspacePath), appServer)
    if (appServer.canonicalWorkspacePath) {
      liveByKey.set(ctx.normalizeWorkspaceConnectionKey(appServer.canonicalWorkspacePath), appServer)
    }
  }

  const candidates = ctx.app.localProjects.list()
    .filter((project) => !ctx.isWorkspaceForeground(project.path))
    .flatMap((project) => {
      const key = ctx.normalizeWorkspaceConnectionKey(project.path)
      const entry = ctx.workspaceConnections.get(key)
      const endpoint = liveByKey.get(key)?.endpoints?.appServerWebSocket
      if (!endpoint?.trim() && !entry?.client.hasRetainedThreadAttachments) return []
      return [{ key, entry, workspacePath: project.path, endpoint,
        lastUsedAt: entry?.lastUsedAt ?? (project.lastOpenedAt ? Date.parse(project.lastOpenedAt) || 0 : 0) }]
    })
  const remoteConnections = [...ctx.workspaceConnections.values()]
    .filter((entry) => entry.role === 'secondary' && entry.kind === 'remote')
    .map((entry) => ({ key: entry.key, entry, workspacePath: entry.workspacePath,
      endpoint: undefined, lastUsedAt: entry.lastUsedAt }))
  const ranked = [...candidates, ...remoteConnections].sort((left, right) => right.lastUsedAt - left.lastUsedAt)
  const allowedKeys = new Set<string>()
  let idleCount = 0
  for (const candidate of ranked) {
    const protectedEntry = candidate.entry?.client.hasRetainedThreadAttachments
    if (!protectedEntry && idleCount >= SECONDARY_WORKSPACE_CONNECTION_LIMIT) continue
    allowedKeys.add(candidate.key)
    if (!protectedEntry) idleCount += 1
    if (candidate.endpoint && !protectedEntry) {
      const entry = ctx.createSecondaryWorkspaceConnection(candidate.workspacePath, candidate.endpoint)
      if (!candidate.entry) entry.lastUsedAt = candidate.lastUsedAt
    }
  }

  for (const entry of [...ctx.workspaceConnections.values()]) {
    if (isIdleSecondary(entry) && !allowedKeys.has(entry.key)) ctx.disposeWorkspaceConnection(entry)
  }
  ctx.emitWorkspaceProjects()
}

export function recordForegroundWorkspace(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  addProject: boolean
): void {
  if (isDefaultChatWorkspace(workspacePath)) {
    ctx.settings.lastForegroundEntry = 'chats'
  } else {
    ctx.settings.lastWorkspacePath = workspacePath
    ctx.settings.lastForegroundEntry = 'workspace'
    if (addProject && !hasRemoteEndpointArg(ctx.argv)) {
      void ctx.app.localProjects
        .open(workspacePath)
        .catch((error) => console.warn('[desktop] failed to add project to Hub', error))
    }
  }
  ctx.app.persistWindowSettings(ctx.settings)
}
