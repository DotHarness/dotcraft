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
  if (!workspacePath || ctx.activeRemoteWorkspace || ctx.resolveConnectionMode(ctx.settings) !== 'local') {
    return false
  }
  if (ctx.isWorkspaceForeground(workspacePath)) return false
  return ctx.app.localProjects.has(workspacePath)
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

export function promoteWorkspaceConnection(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const previous = ctx.currentWorkspacePath ? ctx.getWorkspaceConnection(ctx.currentWorkspacePath) : undefined
  if (previous && previous.client !== entry.client) {
    previous.role = 'secondary'
    previous.lastUsedAt = Date.now()
    ctx.releaseConnectionViewAttachments(previous, 'workspace demote')
  }

  entry.role = 'foreground'
  entry.lastUsedAt = Date.now()
  ctx.wireClient = entry.client
  ctx.currentWorkspacePath = entry.workspacePath
  ctx.lastAppServerWsUrl = entry.wsUrl
  ctx.lastDashboardUrl = entry.initializeResult?.dashboardUrl ?? null
  ctx.setActiveRemoteProject(null)
  ctx.previousLocalForegroundWorkspacePath = null
  ctx.lastRemoteStackLocalPort = null
  ctx.reregisterIpcForWorkspace(entry.workspacePath)

  ctx.emitCurrentWorkspaceStatus(entry.workspacePath)
  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    if (entry.initializeResult) {
      ctx.emitConnectionStatus(ctx.mainWindow, {
        status: 'connected',
        serverInfo: entry.initializeResult.serverInfo,
        capabilities: entry.initializeResult.capabilities as Record<string, unknown>,
        dashboardUrl: entry.initializeResult.dashboardUrl
      })
    } else {
      ctx.emitConnectionStatus(ctx.mainWindow, { status: entry.connected ? 'connected' : 'connecting' })
    }
    const loc = normalizeLocale(ctx.settings.locale)
    ctx.mainWindow.setTitle(
      translate(loc, 'app.titleWithWorkspace', { name: ctx.workspaceTitleName(entry.workspacePath, loc) })
    )
  }
  ctx.startHubEventSubscription(entry.workspacePath, ctx.app.createHubClient(ctx.settings))
  ctx.emitWorkspaceProjects()
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

  const client = DesktopAppServerClient.fromWebSocket(wsUrl, {
    initializeProfile: 'secondary'
  })
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
    })
    if (foreground == null) return
    if (foreground && method === 'provider/auth/usage/updated') refreshVoiceChatGptAvailability()

    ctx.applyWorkspaceThreadNotification(entry, method, params)
    if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
      broadcastNotification(ctx.mainWindow, method, params, ctx.settings, workspacePath, foreground)
    }
  })

  client.onServerRequest(async (method, params) => {
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
      canBridgeRendererInteractiveServerRequest(routingState)
    )
  })

  const onReady = (result: InitializeResult): void => {
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result

    void ctx.refreshConnectionThreadList(entry)
  }
  client.on('ready', onReady)
  client.on('reconnected', onReady)
  client.on('close', () => {
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
      ctx.createSecondaryWorkspaceConnection(chatPath, endpoint, { kind: 'chat' })
    }
  } catch (error) {
    console.warn('[desktop] failed to ensure default chat workspace connection', error)
  }
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
  if (ctx.runtimeIsStopping()) return
  enforceSecondaryWorkspaceBudget(ctx)
  if (ctx.activeRemoteWorkspace || ctx.resolveConnectionMode(ctx.settings) !== 'local') return
  if (ctx.secondaryRefreshTimer) clearTimeout(ctx.secondaryRefreshTimer)
  ctx.secondaryRefreshTimer = null
  await ctx.ensureDefaultChatConnection()
  if (ctx.runtimeIsStopping()) return

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
  const devices = [...ctx.workspaceConnections.values()]
    .filter((entry) => entry.role === 'secondary' && entry.remote?.source === 'device')
    .map((entry) => ({ key: entry.key, entry, workspacePath: entry.workspacePath,
      endpoint: undefined, lastUsedAt: entry.lastUsedAt }))
  const ranked = [...candidates, ...devices].sort((left, right) => right.lastUsedAt - left.lastUsedAt)
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
