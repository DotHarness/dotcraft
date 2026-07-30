import { app, BrowserWindow, session, Menu, ipcMain, shell, nativeImage, nativeTheme } from 'electron'
import {
  registerViewerScheme,
  installViewerProtocolHandler,
  setViewerWorkspaceRoot
} from './viewerFileProtocol'
import {
  registerPluginFileScheme,
  installPluginFileProtocolHandler
} from './pluginFileProtocol'
import {
  registerMcpAppSandboxScheme,
  installMcpAppSandboxProtocolHandler
} from './mcpAppSandboxProtocol'
import { MCP_APP_SANDBOX_SCHEME } from '../shared/mcpAppSandbox'
import { viewerBrowserManager } from './viewerBrowser'
import { browserUseManager } from './browserUseManager'
import { nodeReplManager } from './nodeReplManager'
import { getGitHubIdentity } from './githubProfile'

// Register the custom viewer scheme as privileged BEFORE app.whenReady().
registerViewerScheme()
registerPluginFileScheme()
registerMcpAppSandboxScheme()
import type { IpcMainEvent, MenuItemConstructorOptions } from 'electron'
import { join, basename } from 'path'
import { existsSync } from 'fs'
import { promises as fs } from 'fs'
import { spawn } from 'child_process'
import net from 'net'
import WebSocket from 'ws'
import { WireProtocolClient, type InitializeResult } from './WireProtocolClient'
import {
  handleDesktopRuntimeThreadToolCall,
  resetDesktopThreadToolBindings
} from './desktopRuntimeThreadTools'
import { HubClient, type HubAppServerResponse, type HubEvent } from './HubClient'
import {
  registerIpcHandlers,
  unregisterIpcHandlers,
  getModuleProcessManager,
  getRemoteServersManager,
  autoStartModuleProcessesByChannelName,
  broadcastConnectionStatus,
  broadcastWorkspaceStatus,
  broadcastNotification,
  broadcastServerRequest,
  createServerRequestBridge,
  sanitizeHttpOrHttpsUrl,
  openExternalHttpUrl,
  type ConnectionErrorType,
  type ConnectionStatusPayload,
  type IpcHandlerCallbacks
} from './ipcBridge'
import {
  loadSettings,
  saveSettings,
  addRecentWorkspace,
  clearRecentWorkspaces,
  getRecentWorkspaces,
  removeRecentWorkspace,
  saveLocalProject,
  type AppSettings,
  type BinarySource,
  type ConnectionMode
} from './settings'
import { mergeUpdatedSettings } from './settingsMerge'
import {
  acquireWorkspaceLock,
  releaseWorkspaceLock,
  updateWorkspaceLockActivation,
  type WorkspaceActivationEndpoint
} from './workspaceLock'
import {
  releaseDesktopActivationLock,
  updateDesktopActivationLock
} from './desktopActivationLock'
import {
  startWorkspaceActivationServer,
  type WorkspaceActivationHandle
} from './desktopActivation'
import {
  findWorkspaceOpenDeepLink,
  parseWorkspaceOpenDeepLink,
  type WorkspaceOpenDeepLink
} from './desktopDeepLink'
import {
  NO_WORKSPACE_ARG,
  hasRemoteEndpointArg,
  resolveStartupWorkspacePath
} from './workspaceArgs'
import {
  ensureDefaultChatWorkspace,
  isDefaultChatWorkspace,
  resolveDefaultChatWorkspacePath
} from './defaultChatWorkspace'
import {
  getWorkspaceStatus,
  shouldRouteWorkspaceThroughSetupBeforeAppServerStart,
  runWorkspaceSetup,
  listSetupModels,
  loginSetupChatGpt,
  type WorkspaceStatusPayload,
  type WorkspaceSetupRequest,
  type WorkspaceSetupModelListRequest
} from './workspaceSetup'
import { encodeInitialWorkspaceStatusArg } from '../shared/initialWorkspaceStatus'
import { getEnabledEmbeddedModuleChannelNames } from '../shared/channelModulePersistence'
import { applyWindowBackdropTheme, resolveInitialTheme, resolveWindowBackdropOptions } from './windowTheme'
import { applyNativeThemeSource } from './nativeThemeSource'
import { resolveThemeMode } from '../shared/theme'
import { normalizeInterfaceZoom } from '../shared/appearance'
import {
  normalizeLocale,
  translate,
  type AppLocale,
  type TopLevelMenuId
} from '../shared/locales'
import { ensureTrayProcess, openDesktopWindow, runTrayProcess, stopTrayProcess } from './trayManager'
import { configureAppIdentity } from './appIdentity'
import { resolveDotCraftRuntimeTools } from './ripgrepRuntime'
import { WhatsNewCatalog } from './whatsNewCatalog'
import { WhatsNewMediaCache, resolveWhatsNewMediaAssets } from './whatsNewMediaCache'
import type { WhatsNewMediaState } from '../shared/whatsNew'
import { AppUpdateService } from './appUpdate'
import {
  retryAppServerConnection,
  type RetryConnectionRequest
} from './appServerRetry'
import type { AppUpdateState } from '../shared/appUpdate'
import {
  resolveRemoteWebSocketConfig,
  type ConnectionSettingsDraft
} from '../shared/remoteConnection'
import {
  effectiveAppServerWorkspacePath,
  effectiveWorkspaceDir,
  normalizeRemoteHosts,
  type RemoteHost,
  type RemoteStack
} from '../shared/remoteServers'
import type {
  WorkspaceProjectKind,
  WorkspaceRemoteProjectMetadata,
  WorkspaceRemoteProjectSource,
  WorkspaceProjectSummary,
  WorkspaceProjectsPayload,
  WorkspaceProjectState
} from '../shared/workspaceProjects'
import {
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../shared/workspaceProjectKey'
import {
  canBridgeRendererInteractiveServerRequest,
  getWorkspaceNotificationForeground,
  isCurrentForegroundWorkspaceConnection,
  isRendererInteractiveServerRequest,
  shouldBridgeWorkspaceServerRequest,
  type WorkspaceConnectionRole
} from './workspaceConnectionRouting'
import {
  applyWorkspaceThreadListRefreshFailure,
  applyWorkspaceThreadListRefreshSuccess,
  applyWorkspaceThreadNotificationToCache
} from './workspaceThreadCache'

// ─── Single-process state ─────────────────────────────────────────────────────
// Each Electron process owns exactly one window and one AppServer connection.
// "New Window" spawns a separate OS process instead of creating another
// BrowserWindow, avoiding the global-IPC-handler conflict that the previous
// multi-window-in-one-process design had.

let mainWindow: BrowserWindow | null = null
let wireClient: WireProtocolClient | null = null
let currentWorkspacePath = ''
/** Last DashBoard URL from a successful initialize (for View menu). */
let lastDashboardUrl: string | null = null
let lastAppServerWsUrl: string | null = null
let lastConnectionStatus: ConnectionStatusPayload = { status: 'disconnected' }
let lastWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false,
  providers: []
}
let activeRemoteWorkspace: WorkspaceStatusPayload['remote'] | null = null
let activeRemoteProject: ActiveRemoteProject | null = null
let previousLocalForegroundWorkspacePath: string | null = null
let lastRemoteStackLocalPort: number | null = null
let connectionGeneration = 0
const SECONDARY_WORKSPACE_CONNECTION_LIMIT = 8

interface WorkspaceConnectionEntry {
  key: string
  projectId: string
  kind: WorkspaceProjectKind
  workspacePath: string
  localWorkspacePath?: string
  displayPath?: string
  remote?: WorkspaceRemoteProjectMetadata
  wsUrl: string
  client: WireProtocolClient
  role: WorkspaceConnectionRole
  connected: boolean
  connecting: boolean
  lastUsedAt: number
  initializeResult?: InitializeResult
  threads: unknown[]
  subscribedThreadId?: string
  errorMessage?: string
}

interface ActiveRemoteProject {
  projectId: string
  source: WorkspaceRemoteProjectSource
  name: string
  identityWorkspacePath: string
  displayPath: string
  endpoint?: string
  localWorkspacePath: string
  remote: WorkspaceRemoteProjectMetadata
  status: NonNullable<WorkspaceStatusPayload['remote']>
}

const workspaceConnections = new Map<string, WorkspaceConnectionEntry>()
let activeRemoteReconnectTimer: ReturnType<typeof setTimeout> | null = null
let activeRemoteReconnectAttempt = 0
let isAppQuitting = false
let ipcHandlersRegistered = false
let finalQuitCleanupDone = false
let finalQuitCleanupRunning = false
let hubEventAbortController: AbortController | null = null
let secondaryRefreshTimer: ReturnType<typeof setTimeout> | null = null
let pendingChromeSettingsDeepLink = process.argv.some(isChromeSettingsDeepLink)
let pendingWorkspaceOpenThreadId = findWorkspaceOpenDeepLink(process.argv)?.threadId ?? null
let chromeSettingsDeepLinkServer: net.Server | null = null
let workspaceActivation:
  | { workspacePath: string; handle: WorkspaceActivationHandle }
  | null = null
let workspaceActivationStartingFor = ''
let workspaceActivationGeneration = 0
let whatsNewMediaCache: WhatsNewMediaCache | null = null
let whatsNewCatalog: WhatsNewCatalog | null = null
let appUpdateService: AppUpdateService | null = null
let initialUpdateCheckStarted = false
const isTrayMode = process.argv.includes('--tray')
const CHROME_SETTINGS_DEEP_LINK_PORT = Number.parseInt(process.env.DOTCRAFT_DESKTOP_DEEPLINK_PORT || '32178', 10)

if (isTrayMode) {
  app.disableHardwareAcceleration()
  app.commandLine.appendSwitch('disable-gpu')
}

configureAppIdentity()

let devProcessGuardsInstalled = false

function isBrokenStdIoError(error: unknown): boolean {
  const code = (error as NodeJS.ErrnoException | undefined)?.code
  if (code === 'EIO' || code === 'EPIPE') return true
  const message = error instanceof Error ? error.message : String(error ?? '')
  return /write EIO|EPIPE/i.test(message)
}

function installDevProcessGuards(): void {
  if (!import.meta.env.DEV || devProcessGuardsInstalled) return
  devProcessGuardsInstalled = true

  const parentPid = process.ppid
  const quitDevApp = (): void => {
    if (isAppQuitting) return
    app.quit()
  }
  const handleStdIoError = (error: unknown): void => {
    if (isBrokenStdIoError(error)) {
      quitDevApp()
    }
  }

  process.stdout.on('error', handleStdIoError)
  process.stderr.on('error', handleStdIoError)

  const watchdog = setInterval(() => {
    if (process.ppid === 1 || process.ppid !== parentPid) {
      clearInterval(watchdog)
      quitDevApp()
    }
  }, 1500)
  watchdog.unref()
}

function normalizeWorkspaceConnectionKey(workspacePath: string): string {
  return normalizeWorkspaceProjectKey(workspacePath)
}

function localProjectId(workspacePath: string): string {
  return normalizeWorkspaceConnectionKey(workspacePath)
}

function normalizeRemoteEndpointForProjectId(endpoint: string): string {
  const trimmed = endpoint.trim()
  try {
    const parsed = new URL(trimmed)
    parsed.username = ''
    parsed.password = ''
    parsed.search = ''
    parsed.hash = ''
    return parsed.toString().replace(/\/$/u, '').toLowerCase()
  } catch {
    return trimmed.replace(/[?#].*$/u, '').replace(/\/$/u, '').toLowerCase()
  }
}

function remoteEndpointDisplay(endpoint: string): string {
  const trimmed = endpoint.trim()
  try {
    const parsed = new URL(trimmed)
    parsed.username = ''
    parsed.password = ''
    parsed.search = ''
    parsed.hash = ''
    return parsed.toString().replace(/\/$/u, '')
  } catch {
    return trimmed.replace(/[?#].*$/u, '').replace(/\/$/u, '')
  }
}

function remoteProjectId(source: WorkspaceRemoteProjectSource, endpoint: string): string {
  return `remote:${source}:${normalizeRemoteEndpointForProjectId(endpoint)}`
}

function remoteProjectNameFromEndpoint(endpoint: string): string {
  try {
    const parsed = new URL(endpoint)
    return parsed.host || parsed.hostname || 'Remote'
  } catch {
    return endpoint.trim() || 'Remote'
  }
}

function pinnedSettingsKey(key: string): string {
  return normalizeWorkspaceProjectKey(key)
}

function getWorkspaceConnection(workspacePath: string): WorkspaceConnectionEntry | undefined {
  return workspaceConnections.get(normalizeWorkspaceConnectionKey(workspacePath))
}

function isWorkspaceForeground(workspacePath: string): boolean {
  return !activeRemoteProject && Boolean(currentWorkspacePath && isSameWorkspacePath(currentWorkspacePath, workspacePath))
}

function getPinnedThreadIdsForProject(keyOrPath: string): string[] {
  const key = pinnedSettingsKey(keyOrPath)
  if (!key || !sharedSettings.pinnedThreadIdsByWorkspace) return []
  const exact = sharedSettings.pinnedThreadIdsByWorkspace[key]
  if (Array.isArray(exact)) return exact
  const match = Object.entries(sharedSettings.pinnedThreadIdsByWorkspace).find(
    ([candidate]) => normalizeWorkspaceProjectKey(candidate) === key
  )
  return match?.[1] ?? []
}

function getPinnedThreadIdsForWorkspace(workspacePath: string): string[] {
  return getPinnedThreadIdsForProject(workspacePath)
}

function isProjectPinned(keyOrPath: string): boolean {
  const key = pinnedSettingsKey(keyOrPath)
  return Boolean(key && sharedSettings.pinnedProjectIds?.some((candidate) =>
    normalizeWorkspaceProjectKey(candidate) === key
  ))
}

function findWorkspaceConnectionByClient(client: WireProtocolClient): WorkspaceConnectionEntry | undefined {
  return [...workspaceConnections.values()].find((entry) => entry.client === client)
}

function threadIdFromRequestParams(params: unknown): string | null {
  if (!params || typeof params !== 'object') return null
  const threadId = (params as { threadId?: unknown }).threadId
  return typeof threadId === 'string' && threadId.trim() ? threadId.trim() : null
}

function releaseConnectionThreadSubscription(entry: WorkspaceConnectionEntry, reason: string): void {
  const threadId = entry.subscribedThreadId
  if (!threadId) return
  entry.subscribedThreadId = undefined
  entry.client.sendRequest('thread/unsubscribe', { threadId })
    .catch((error: unknown) => {
      if (!isAppQuitting) {
        console.warn(`[desktop] failed to unsubscribe ${threadId} during ${reason}`, error)
      }
    })
}

function observeAppServerRequestCompletion(
  client: WireProtocolClient,
  method: string,
  params: unknown
): void {
  if (method !== 'thread/subscribe' && method !== 'thread/unsubscribe') return
  const entry = findWorkspaceConnectionByClient(client)
  if (!entry) return
  const threadId = threadIdFromRequestParams(params)
  if (!threadId) return

  if (method === 'thread/subscribe') {
    entry.subscribedThreadId = threadId
    if (entry.role !== 'foreground' || wireClient !== client) {
      releaseConnectionThreadSubscription(entry, 'late secondary subscribe')
    }
    return
  }

  if (entry.subscribedThreadId === threadId) {
    entry.subscribedThreadId = undefined
  }
}

function makeThreadListIdentity(workspacePath: string): Record<string, unknown> {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

function isRunningHubAppServer(entry: HubAppServerResponse | undefined): boolean {
  const state = entry?.state?.toLowerCase()
  return state === 'running' || state === 'healthy' || state === 'ready'
}

function emitWorkspaceProjects(): void {
  const win = mainWindow
  if (!win || win.isDestroyed()) return
  win.webContents.send('workspace:projects-changed', getWorkspaceProjectsPayload())
}

// Builds the dedicated `Chats` summary from the default Chat workspace connection.
// Returns undefined in remote mode (the Chat workspace is a local concept and is
// not connected then). The summary reuses WorkspaceProjectSummary so the renderer
// can share thread-row rendering; `kind: 'chat'` keeps it out of the Projects list.
function buildDefaultChatSummary(): WorkspaceProjectSummary | undefined {
  if (activeRemoteProject || activeRemoteWorkspace || resolveConnectionMode(sharedSettings) !== 'local') {
    return undefined
  }
  const chatPath = resolveDefaultChatWorkspacePath()
  const entry = getWorkspaceConnection(chatPath)
  let state: WorkspaceProjectState = 'cold'
  if (isWorkspaceForeground(chatPath)) state = 'foreground'
  else if (entry?.connecting) state = 'connecting'
  else if (entry?.errorMessage) state = 'error'
  else if (entry?.connected) state = 'secondary'
  return {
    projectId: localProjectId(chatPath),
    kind: 'chat',
    path: chatPath,
    identityWorkspacePath: chatPath,
    name: chatPath,
    state,
    running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
    loaded: Boolean(entry?.connected),
    threadCount: entry?.threads.length ?? 0,
    threads: entry?.threads ?? [],
    pinnedThreadIds: getPinnedThreadIdsForWorkspace(chatPath),
    pinned: false,
    ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
  }
}

function getWorkspaceProjectsPayload(): WorkspaceProjectsPayload {
  // Order projects by when they were first added (stable), not by most-recently
  // opened, so switching the active project never reshuffles the sidebar list.
  const recents = [...getRecentWorkspaces(sharedSettings)].sort((a, b) =>
    (a.firstOpenedAt ?? a.lastOpenedAt).localeCompare(b.firstOpenedAt ?? b.lastOpenedAt)
  )
  const projects = recents.map((recent): WorkspaceProjectSummary => {
    const entry = getWorkspaceConnection(recent.path)
    let state: WorkspaceProjectState = 'cold'
    if (isWorkspaceForeground(recent.path)) state = 'foreground'
    else if (entry?.connecting) state = 'connecting'
    else if (entry?.errorMessage) state = 'error'
    else if (entry?.connected) state = 'secondary'
    const projectId = localProjectId(recent.path)
    return {
      projectId,
      kind: 'local',
      path: recent.path,
      identityWorkspacePath: recent.path,
      name: recent.name || basename(recent.path),
      lastOpenedAt: recent.lastOpenedAt,
      state,
      running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
      loaded: Boolean(entry?.connected),
      threadCount: entry?.threads.length ?? 0,
      threads: entry?.threads ?? [],
      pinnedThreadIds: getPinnedThreadIdsForWorkspace(recent.path),
      pinned: isProjectPinned(projectId),
      // Local Projects may attach extra runtime roots beyond the primary folder.
      secondaryFolders: recent.secondaryFolders ?? [],
      ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
    }
  })
  if (activeRemoteProject) {
    const entry = workspaceConnections.get(activeRemoteProject.projectId)
    const state: WorkspaceProjectState = entry?.errorMessage
      ? 'error'
      : entry?.connecting
        ? 'connecting'
        : 'foreground'
    projects.unshift({
      projectId: activeRemoteProject.projectId,
      kind: 'remote',
      path: activeRemoteProject.displayPath,
      identityWorkspacePath: activeRemoteProject.identityWorkspacePath,
      name: activeRemoteProject.name,
      state,
      running: Boolean(entry?.connected || entry?.connecting || state === 'foreground'),
      loaded: Boolean(entry?.connected),
      threadCount: entry?.threads.length ?? 0,
      threads: entry?.threads ?? [],
      pinnedThreadIds: getPinnedThreadIdsForProject(activeRemoteProject.projectId),
      pinned: isProjectPinned(activeRemoteProject.projectId),
      remote: activeRemoteProject.remote,
      ...(entry?.errorMessage ? { errorMessage: entry.errorMessage } : {})
    })
  }
  const chat = buildDefaultChatSummary()
  return {
    foregroundWorkspacePath: currentWorkspacePath,
    foregroundProjectId: activeRemoteProject?.projectId ?? (currentWorkspacePath ? localProjectId(currentWorkspacePath) : ''),
    secondaryLimit: SECONDARY_WORKSPACE_CONNECTION_LIMIT,
    projects,
    ...(chat ? { chat } : {})
  }
}

async function refreshConnectionThreadList(entry: WorkspaceConnectionEntry): Promise<void> {
  if (!entry.connected) return
  try {
    const result = await entry.client.sendRequest<{ data?: unknown[] }>('thread/list', {
      identity: makeThreadListIdentity(entry.workspacePath),
      scope: 'workspace',
      includeSubAgents: true
    })
    applyWorkspaceThreadListRefreshSuccess(entry, result.data)
    emitWorkspaceProjects()
  } catch (error) {
    applyWorkspaceThreadListRefreshFailure(entry, error)
    emitWorkspaceProjects()
  }
}

function applyWorkspaceThreadNotification(entry: WorkspaceConnectionEntry, method: string, params: unknown): void {
  const result = applyWorkspaceThreadNotificationToCache(entry.threads, method, params)
  entry.threads = result.threads
  if (result.changed) {
    emitWorkspaceProjects()
  }
  if (result.refreshThreadList) {
    void refreshConnectionThreadList(entry)
  }
}

function disposeWorkspaceConnection(entry: WorkspaceConnectionEntry): void {
  if (wireClient === entry.client) {
    wireClient = null
  }
  entry.subscribedThreadId = undefined
  entry.client.dispose()
  workspaceConnections.delete(entry.key)
  releaseWorkspaceLock(entry.workspacePath)
  emitWorkspaceProjects()
}

function canActivateWorkspaceInThisWindow(workspacePath: string): boolean {
  return !activeRemoteProject && workspacePath.trim().length > 0
}

function publishWorkspaceActivation(endpoint: WorkspaceActivationEndpoint): void {
  updateDesktopActivationLock(endpoint)
  if (currentWorkspacePath && !activeRemoteProject) {
    updateWorkspaceLockActivation(currentWorkspacePath, endpoint)
  }
}

async function handleServerRequestInMain(method: string, params: unknown): Promise<unknown | undefined> {
  if (!mainWindow || mainWindow.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }

  if (method === 'item/tool/call') {
    const client = wireClient
    if (!client) {
      return undefined
    }
    return handleDesktopRuntimeThreadToolCall(client, params, getProtocolWorkspacePath(), {
      supportsDynamicToolRebind: lastConnectionStatus.capabilities?.dynamicToolRebind === true,
      settingsHost: {
        getSettings: () => sharedSettings,
        updateSettings: updateSharedSettings,
        onPinnedThreadIdsChanged: (workspacePath, threadIds) => {
          broadcastPinnedThreadIdsChanged({ workspacePath, threadIds })
        }
      }
    })
  }

  if (method === 'ext/nodeRepl/evaluate') {
    const p = (params ?? {}) as {
      threadId?: string
      turnId?: string
      evaluationId?: string
      browserSession?: Record<string, unknown>
      code?: string
      timeoutMs?: number
    }
    if (!p.threadId || typeof p.code !== 'string') {
      return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
    }
    return nodeReplManager.evaluate(mainWindow, {
      threadId: p.threadId,
      turnId: p.turnId,
      evaluationId: p.evaluationId,
      browserSession: p.browserSession,
      code: p.code,
      timeoutMs: p.timeoutMs,
      workspacePath: currentWorkspacePath
    })
  }

  if (method === 'ext/nodeRepl/cancel') {
    const p = (params ?? {}) as { threadId?: string; evaluationId?: string }
    return p.threadId && p.evaluationId
      ? nodeReplManager.cancel(p.threadId, p.evaluationId)
      : { ok: false }
  }

  return undefined
}

async function bridgeServerRequestToRenderer(method: string, params: unknown): Promise<unknown> {
  const win = mainWindow
  if (!win || win.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }
  const { bridgeId, promise } = createServerRequestBridge()
  broadcastServerRequest(win, { bridgeId, method, params }, sharedSettings)
  return promise
}

async function handleWorkspaceServerRequest(
  method: string,
  params: unknown,
  canUseForegroundMainHandlers: boolean,
  canBridgeInteractiveToRenderer: boolean
): Promise<unknown> {
  const isInteractive = isRendererInteractiveServerRequest(method)

  if (!canUseForegroundMainHandlers && !isInteractive) {
    throw new Error(`Server request ${method} is not routed to the current foreground workspace connection.`)
  }

  if (canUseForegroundMainHandlers) {
    const handledInMain = await handleServerRequestInMain(method, params)
    if (handledInMain !== undefined) return handledInMain
  }

  if (!canUseForegroundMainHandlers && !canBridgeInteractiveToRenderer) {
    throw new Error('Window is not available to handle interactive server request.')
  }

  return bridgeServerRequestToRenderer(method, params)
}

/** PNG shipped via `build.extraResources` (prod) or repo `resources/` (dev). macOS uses bundle icon. */
function resolveWindowIconPath(): string | null {
  if (process.platform === 'darwin') {
    return null
  }
  const packaged = join(process.resourcesPath, 'icon.png')
  const dev = join(__dirname, '../../resources/icon.png')
  const path = app.isPackaged ? packaged : dev
  return existsSync(path) ? path : null
}

function openWhatsNewFromMenu(): void {
  const win = BrowserWindow.getFocusedWindow() ?? mainWindow
  if (!win || win.isDestroyed()) return
  win.webContents.send('app:open-whats-new')
}

function broadcastWhatsNewMediaState(state: WhatsNewMediaState): void {
  for (const win of BrowserWindow.getAllWindows()) {
    if (!win.isDestroyed()) {
      win.webContents.send('app:whats-new-media-state-changed', state)
    }
  }
}

function broadcastAppUpdateState(state: AppUpdateState): void {
  for (const win of BrowserWindow.getAllWindows()) {
    if (!win.isDestroyed()) {
      win.webContents.send('app:update-state-changed', state)
    }
  }
}

function broadcastPinnedThreadIdsChanged(payload: { workspacePath: string; threadIds: string[] }): void {
  for (const win of BrowserWindow.getAllWindows()) {
    if (!win.isDestroyed()) {
      win.webContents.send('settings:pinned-thread-ids-changed', payload)
    }
  }
}

function getWhatsNewMediaCache(): WhatsNewMediaCache {
  if (!whatsNewMediaCache) {
    whatsNewMediaCache = new WhatsNewMediaCache({
      assetResolver: async (releaseVersions) => (
        resolveWhatsNewMediaAssets(await getWhatsNewCatalog().getReleases(), releaseVersions)
      ),
      onStateChanged: broadcastWhatsNewMediaState
    })
  }
  return whatsNewMediaCache
}

function getWhatsNewCatalog(): WhatsNewCatalog {
  whatsNewCatalog ??= new WhatsNewCatalog()
  return whatsNewCatalog
}

function getAppUpdateService(): AppUpdateService {
  if (!appUpdateService) {
    appUpdateService = new AppUpdateService({
      onStateChanged: broadcastAppUpdateState
    })
  }
  return appUpdateService
}

function scheduleInitialUpdateCheck(): void {
  if (initialUpdateCheckStarted) return
  initialUpdateCheckStarted = true
  setTimeout(() => {
    void getAppUpdateService().checkForUpdates().catch((error) => {
      console.warn('[desktop] failed to check for updates', error)
    })
  }, 1200)
}

// ─── Shared (mutable) settings ────────────────────────────────────────────────

let sharedSettings: AppSettings = {}
const WINDOW_SHOW_FALLBACK_MS = 3000

// ─── Workspace resolution ─────────────────────────────────────────────────────

async function updateSharedSettings(partial: Partial<AppSettings>): Promise<void> {
  const prevLocale = normalizeLocale(sharedSettings.locale)
  const next = mergeUpdatedSettings(sharedSettings, partial)
  Object.assign(sharedSettings, next)
  saveSettings(sharedSettings)
  const localeChanged = partial.locale !== undefined && normalizeLocale(sharedSettings.locale) !== prevLocale
  if (partial.theme !== undefined) {
    applyNativeThemeSource(nativeTheme, sharedSettings)
    refreshAppMenu()
  } else if (localeChanged) {
    refreshAppMenu()
  }
  if (process.platform === 'darwin' && partial.showInMenuBar !== undefined) {
    if (sharedSettings.showInMenuBar === false) {
      await stopTrayProcess()
    } else {
      ensureTrayProcess(sharedSettings)
    }
  }
  if (partial.theme !== undefined) {
    const win = mainWindow
    if (win && !win.isDestroyed()) {
      applyWindowBackdropTheme(win, resolveInitialTheme(sharedSettings, nativeTheme.shouldUseDarkColors))
    }
  }
  // Pin is a Desktop-local, per-workspace setting. Re-push the projects payload so
  // secondary / Chats rows reflect a pin toggle (moving between the pinned section
  // and their project group) without waiting for another workspace event.
  if (partial.pinnedThreadIdsByWorkspace !== undefined || partial.pinnedProjectIds !== undefined) {
    emitWorkspaceProjects()
  }
}

browserUseManager.setPolicyHost({
  getSettings: () => sharedSettings,
  updateSettings: updateSharedSettings
})

function resolveConnectionMode(settings: AppSettings): ConnectionMode {
  const mode = settings.connectionMode
  return mode === 'remote' ? 'remote' : 'local'
}

function resolveInitialWorkspacePath(settings: AppSettings): string | null {
  const workspacePath = resolveStartupWorkspacePath(
    settings,
    process.argv,
    existsSync,
    resolveDefaultChatWorkspacePath(),
    resolveConnectionMode(settings)
  )
  return workspacePath && isDefaultChatWorkspace(workspacePath)
    ? ensureDefaultChatWorkspace()
    : workspacePath
}

function workspaceTitleName(workspacePath: string | null | undefined, locale: AppLocale): string {
  if (!workspacePath) return 'DotCraft'
  return isDefaultChatWorkspace(workspacePath)
    ? translate(locale, 'chatsRail.title')
    : basename(workspacePath)
}

function buildRemoteWorkspaceStatus(
  host: RemoteHost,
  stack: RemoteStack
): WorkspaceStatusPayload['remote'] {
  const projectId = `remote:servers:${host.id}:${stack.id}`
  const displayName = stack.projectName?.trim() || stack.name
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
    ...(stack.projectName ? { projectName: stack.projectName } : {})
  }
}

function buildServersRemoteProject(
  host: RemoteHost,
  stack: RemoteStack,
  endpoint?: string,
  localWorkspacePath: string = currentWorkspacePath
): ActiveRemoteProject {
  const status = buildRemoteWorkspaceStatus(host, stack)
  const identityWorkspacePath = status.appServerWorkspacePath?.trim() || status.workspaceDir?.trim() || currentWorkspacePath
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
    ...(stack.projectName ? { projectName: stack.projectName } : {})
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

function buildEndpointRemoteProject(
  source: 'manual' | 'cli',
  endpoint: string,
  localWorkspacePath: string
): ActiveRemoteProject {
  const projectId = remoteProjectId(source, endpoint)
  const name = remoteProjectNameFromEndpoint(endpoint)
  const safeEndpoint = remoteEndpointDisplay(endpoint)
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

function setActiveRemoteProject(project: ActiveRemoteProject | null): void {
  activeRemoteProject = project
  activeRemoteWorkspace = project?.status ?? null
}

function disposeActiveRemoteConnection(): void {
  if (!activeRemoteProject) return
  const entry = workspaceConnections.get(activeRemoteProject.projectId)
  if (entry) {
    entry.client.dispose()
    workspaceConnections.delete(entry.key)
  }
  if (wireClient === entry?.client) {
    wireClient = null
  }
}

function prepareRemoteForeground(project: ActiveRemoteProject): void {
  if (!previousLocalForegroundWorkspacePath && project.localWorkspacePath) {
    previousLocalForegroundWorkspacePath = project.localWorkspacePath
  }
  const previousLocal = project.localWorkspacePath
    ? getWorkspaceConnection(project.localWorkspacePath)
    : undefined
  if (previousLocal?.role === 'foreground') {
    previousLocal.role = 'secondary'
    previousLocal.lastUsedAt = Date.now()
  }
  if (activeRemoteProject?.projectId !== project.projectId) {
    disposeActiveRemoteConnection()
  }
  setActiveRemoteProject(project)
}

function resolveActiveRemoteStack(
  settings: AppSettings
): { host: RemoteHost; stack: RemoteStack } | null {
  const ref = settings.activeRemoteStack
  if (!ref?.hostId || !ref.stackId) return null
  const host = normalizeRemoteHosts(settings.remoteHosts).find((candidate) => candidate.id === ref.hostId)
  const stack = host?.stacks.find((candidate) => candidate.id === ref.stackId)
  return host && stack ? { host, stack } : null
}

function getWorkspaceStatusForRenderer(workspacePath: string | null | undefined): WorkspaceStatusPayload {
  const status = getWorkspaceStatus(workspacePath)
  return activeRemoteWorkspace ? { ...status, remote: activeRemoteWorkspace } : status
}

function emitCurrentWorkspaceStatus(workspacePath: string): void {
  if (mainWindow && !mainWindow.isDestroyed()) {
    emitWorkspaceStatus(mainWindow, getWorkspaceStatusForRenderer(workspacePath))
  }
}

function getProtocolWorkspacePath(): string {
  return activeRemoteProject?.identityWorkspacePath || activeRemoteWorkspace?.appServerWorkspacePath?.trim() || currentWorkspacePath
}

function clearActiveRemoteReconnectTimer(): void {
  if (activeRemoteReconnectTimer) {
    clearTimeout(activeRemoteReconnectTimer)
    activeRemoteReconnectTimer = null
  }
}

function hasRecoverableActiveRemoteStack(): boolean {
  if (isAppQuitting || !currentWorkspacePath) return false
  if (resolveConnectionMode(sharedSettings) !== 'remote') return false
  const ref = sharedSettings.activeRemoteStack
  return Boolean(ref?.hostId && ref.stackId)
}

function scheduleActiveRemoteStackReconnect(reason: string): void {
  if (!hasRecoverableActiveRemoteStack()) return
  if (activeRemoteReconnectTimer) return

  const delayMs = Math.min(30_000, 1_000 * 2 ** activeRemoteReconnectAttempt)
  activeRemoteReconnectAttempt += 1
  console.info(`[desktop] active remote stack disconnected (${reason}); rebuilding SSH tunnel in ${delayMs}ms`)
  activeRemoteReconnectTimer = setTimeout(() => {
    activeRemoteReconnectTimer = null
    if (!hasRecoverableActiveRemoteStack()) return
    void connectToAppServer(currentWorkspacePath).catch((error) => {
      const message = error instanceof Error ? error.message : String(error)
      console.warn('[desktop] active remote stack reconnect failed', message)
    })
  }, delayMs)
}

function resolveBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') {
    return source
  }
  return settings.appServerBinaryPath?.trim() ? 'custom' : 'bundled'
}

function createHubClient(settings: AppSettings): HubClient {
  return new HubClient({
    binarySource: resolveBinarySource(settings),
    binaryPath: settings.appServerBinaryPath,
    preferDevBuild: import.meta.env.DEV,
    requireDevBuild: import.meta.env.DEV,
    ...(import.meta.env.DEV ? { restartMismatchedHub: true } : {})
  })
}

function releaseCurrentWorkspaceLock(): void {
  if (!currentWorkspacePath) return
  stopWorkspaceActivation()
  releaseWorkspaceLock(currentWorkspacePath)
  currentWorkspacePath = ''
}

function registerDesktopIpcHandlers(
  workspacePath: string,
  getWireClient: () => WireProtocolClient | null
): void {
  if (ipcHandlersRegistered) {
    unregisterIpcHandlers()
    ipcHandlersRegistered = false
  }
  try {
    registerIpcHandlers(null, getWireClient, workspacePath, buildCallbacks())
    ipcHandlersRegistered = true
  } catch (err) {
    ipcHandlersRegistered = false
    console.error('[desktop] failed to register IPC handlers', err)
    throw err
  }
}

function unregisterDesktopIpcHandlers(): boolean {
  if (!ipcHandlersRegistered) {
    return false
  }
  unregisterIpcHandlers()
  ipcHandlersRegistered = false
  return true
}

async function autoStartEnabledModules(): Promise<void> {
  const client = wireClient
  if (!client) {
    return
  }
  try {
    const response = await client.sendRequest<{
      channels?: Array<{
        name?: string
        enabled?: boolean
        transport?: string | null
        builtinModule?: string | null
      }>
    }>(
      'externalChannel/list',
      {}
    )
    const enabledChannelNames = getEnabledEmbeddedModuleChannelNames(response.channels ?? [])
    await autoStartModuleProcessesByChannelName(enabledChannelNames)
  } catch (error) {
    console.warn('[desktop] failed to auto-start persisted modules', error)
  }
}

async function teardownRuntime(
  reason: string,
  options?: {
    releaseWorkspaceLock?: boolean
    clearMainWindow?: boolean
    cleanupIpcHandlers?: boolean
  }
): Promise<void> {
  const moduleManager = getModuleProcessManager()
  const cleanedIpc = options?.cleanupIpcHandlers
    ? unregisterDesktopIpcHandlers()
    : false
  if (options?.cleanupIpcHandlers) {
    getRemoteServersManager().closeAllTunnels()
  }
  const hadWireClient = wireClient !== null
  if (moduleManager) {
    void moduleManager.stopAll({ preserveExternalChannels: true }).catch((error) => {
      console.warn('[desktop] failed to stop channel modules during teardown', error)
    })
  }
  hubEventAbortController?.abort()
  hubEventAbortController = null
  if (secondaryRefreshTimer) {
    clearTimeout(secondaryRefreshTimer)
    secondaryRefreshTimer = null
  }
  clearActiveRemoteReconnectTimer()
  connectionGeneration += 1
  for (const entry of [...workspaceConnections.values()]) {
    entry.subscribedThreadId = undefined
    releaseWorkspaceLock(entry.workspacePath)
    entry.client.dispose()
  }
  workspaceConnections.clear()
  wireClient?.dispose()
  wireClient = null
  setActiveRemoteProject(null)
  previousLocalForegroundWorkspacePath = null
  lastAppServerWsUrl = null
  let releasedWorkspaceLock = false
  if (options?.releaseWorkspaceLock) {
    releasedWorkspaceLock = currentWorkspacePath !== ''
    releaseCurrentWorkspaceLock()
  }
  let clearedMainWindow = false
  if (options?.clearMainWindow) {
    clearedMainWindow = mainWindow !== null
    mainWindow = null
  }
  const changed =
    cleanedIpc ||
    hadWireClient ||
    releasedWorkspaceLock ||
    clearedMainWindow
  if (changed) {
    console.info(`[desktop] teardown runtime: ${reason}`)
  }
}

function showWindowSafely(win: BrowserWindow): void {
  if (win.isDestroyed()) return
  if (win.isMinimized()) {
    win.restore()
  }
  if (!win.isVisible()) {
    win.show()
  }
  win.focus()
}

function isSameWorkspacePath(a: string, b: string): boolean {
  return sameWorkspaceProjectKey(a, b)
}

function isChromeSettingsDeepLink(value: string): boolean {
  try {
    const parsed = new URL(value)
    return (
      parsed.protocol === 'dotcraft:' &&
      parsed.hostname === 'settings' &&
      parsed.pathname.replace(/\/+$/, '') === '/computer-control/chrome'
    )
  } catch {
    return false
  }
}

function sendOpenThread(win: BrowserWindow, threadId: string): void {
  const id = threadId.trim()
  if (!id || win.isDestroyed()) return
  const send = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('app:open-thread', { threadId: id })
    }
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

function flushPendingWorkspaceOpenThread(win: BrowserWindow): void {
  const threadId = pendingWorkspaceOpenThreadId?.trim()
  if (!threadId) return
  pendingWorkspaceOpenThreadId = null
  sendOpenThread(win, threadId)
}

function openCurrentWorkspaceThread(threadId?: string | null): void {
  const win = mainWindow
  if (!win || win.isDestroyed()) {
    if (threadId?.trim()) pendingWorkspaceOpenThreadId = threadId.trim()
    return
  }

  showWindowSafely(win)
  if (!threadId?.trim()) return

  if (lastConnectionStatus.status === 'connected') {
    sendOpenThread(win, threadId)
  } else {
    pendingWorkspaceOpenThreadId = threadId.trim()
  }
}

function sendOpenChromeSettings(win: BrowserWindow): void {
  if (win.isDestroyed()) return
  const send = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('app:open-chrome-settings')
    }
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

function openChromeSettingsFromDeepLink(): void {
  const win = mainWindow
  if (!win || win.isDestroyed()) {
    pendingChromeSettingsDeepLink = true
    return
  }
  pendingChromeSettingsDeepLink = false
  showWindowSafely(win)
  sendOpenChromeSettings(win)
}

function stopWorkspaceActivation(): void {
  workspaceActivationGeneration++
  workspaceActivationStartingFor = ''
  workspaceActivation?.handle.close()
  workspaceActivation = null
  releaseDesktopActivationLock()
}

function ensureWorkspaceActivation(workspacePath: string): void {
  const win = mainWindow
  if (!win || win.isDestroyed()) return
  if (workspaceActivation?.workspacePath === workspacePath) {
    publishWorkspaceActivation(workspaceActivation.handle.endpoint)
    return
  }
  if (workspaceActivationStartingFor === workspacePath) return

  stopWorkspaceActivation()
  const generation = workspaceActivationGeneration
  workspaceActivationStartingFor = workspacePath
  void startWorkspaceActivationServer({
    workspacePath,
    getWindow: () => mainWindow,
    canActivateWorkspace: canActivateWorkspaceInThisWindow,
    isForegroundWorkspace: (candidate) =>
      Boolean(currentWorkspacePath && isSameWorkspacePath(currentWorkspacePath, candidate)),
    onActivate: (request) => {
      if (currentWorkspacePath && isSameWorkspacePath(currentWorkspacePath, request.workspacePath)) {
        openCurrentWorkspaceThread(request.threadId)
        return
      }
      void connectToAppServer(request.workspacePath)
        .then((connected) => {
          if (connected) {
            openCurrentWorkspaceThread(request.threadId)
          }
        })
        .catch((error) => {
          console.warn('[desktop] failed to activate requested workspace', error)
        })
    }
  }).then((handle) => {
    if (generation !== workspaceActivationGeneration || currentWorkspacePath !== workspacePath || isAppQuitting) {
      handle.close()
      return
    }
    workspaceActivation = { workspacePath, handle }
    publishWorkspaceActivation(handle.endpoint)
  }).catch((error) => {
    console.warn('[desktop] failed to start workspace activation server', error)
  }).finally(() => {
    if (workspaceActivationStartingFor === workspacePath) {
      workspaceActivationStartingFor = ''
    }
  })
}

function handleWorkspaceOpenDeepLink(link: WorkspaceOpenDeepLink): void {
  if (canActivateWorkspaceInThisWindow(link.workspacePath)) {
    if (currentWorkspacePath && isSameWorkspacePath(currentWorkspacePath, link.workspacePath)) {
      openCurrentWorkspaceThread(link.threadId)
    } else {
      void connectToAppServer(link.workspacePath)
        .then((connected) => {
          if (connected) {
            openCurrentWorkspaceThread(link.threadId)
          }
        })
        .catch((error) => {
          console.warn('[desktop] failed to open workspace deep link in current window', error)
        })
    }
    return
  }

  void openDesktopWindow(link.workspacePath, link.threadId)
}

function startChromeSettingsDeepLinkServer(): void {
  if (chromeSettingsDeepLinkServer || !Number.isFinite(CHROME_SETTINGS_DEEP_LINK_PORT) || CHROME_SETTINGS_DEEP_LINK_PORT <= 0) {
    return
  }

  const server = net.createServer((socket) => {
    socket.setEncoding('utf8')
    let buffer = ''
    socket.on('data', (chunk) => {
      buffer += chunk
      let newline = buffer.indexOf('\n')
      while (newline >= 0) {
        const line = buffer.slice(0, newline).trim()
        buffer = buffer.slice(newline + 1)
        if (line) {
          try {
            const message = JSON.parse(line) as { type?: unknown }
            if (message.type !== 'openChromeSettings') {
              throw new Error('Unsupported deep link request.')
            }
            openChromeSettingsFromDeepLink()
            socket.write(JSON.stringify({ ok: true }) + '\n', 'utf8')
          } catch (error) {
            socket.write(
              JSON.stringify({ ok: false, error: error instanceof Error ? error.message : String(error) }) + '\n',
              'utf8'
            )
          }
        }
        newline = buffer.indexOf('\n')
      }
    })
    socket.on('error', () => {
      socket.destroy()
    })
  })

  server.on('error', (error) => {
    console.warn('[desktop] failed to start Chrome settings deep link server', error)
    if (chromeSettingsDeepLinkServer === server) {
      chromeSettingsDeepLinkServer = null
    }
  })
  server.listen(CHROME_SETTINGS_DEEP_LINK_PORT, '127.0.0.1')
  chromeSettingsDeepLinkServer = server
}

function stopChromeSettingsDeepLinkServer(): void {
  const server = chromeSettingsDeepLinkServer
  chromeSettingsDeepLinkServer = null
  server?.close()
}

// ─── Window creation ──────────────────────────────────────────────────────────

function createWindow(
  workspacePath: string | null,
  initialWorkspaceStatus: WorkspaceStatusPayload
): BrowserWindow {
  const isMac = process.platform === 'darwin'
  const isDev = import.meta.env.DEV
  const iconPath = resolveWindowIconPath()
  const initialTheme = resolveInitialTheme(sharedSettings, nativeTheme.shouldUseDarkColors)
  // The renderer receives the MODE (incl. `system`) and resolves it via matchMedia so it can
  // also react to OS appearance changes; native chrome below uses the resolved dark/light value.
  const initialThemeMode = resolveThemeMode(sharedSettings.theme)
  const windowBackdrop = resolveWindowBackdropOptions(initialTheme)
  const initialLocale = normalizeLocale(sharedSettings.locale)
  const win = new BrowserWindow({
    width: 1400,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    ...windowBackdrop,
    ...(iconPath
      ? {
          icon: nativeImage.createFromPath(iconPath)
        }
      : {}),
    show: isDev,
    ...(isMac
      ? {
          titleBarStyle: 'hiddenInset'
        }
      : {
          frame: false
        }),
    autoHideMenuBar: !isMac,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      devTools: isDev,
      additionalArguments: [
        `--dotcraft-initial-theme=${initialThemeMode}`,
        `--dotcraft-applied-theme=${initialTheme}`,
        `--dotcraft-initial-locale=${initialLocale}`,
        encodeInitialWorkspaceStatusArg(initialWorkspaceStatus)
      ],
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const workspaceName = workspaceTitleName(workspacePath, initialLocale)
  win.setTitle(translate(initialLocale, 'app.titleWithWorkspace', { name: workspaceName }))

  // Keep native window chrome in sync when the OS appearance changes while in `system` theme
  // mode. Resolution is a no-op for fixed dark/light modes, so this can run unconditionally.
  // Registered once; the handler reads the current foreground window lazily.
  if (nativeTheme.listenerCount('updated') === 0) {
    nativeTheme.on('updated', () => {
      const current = mainWindow
      if (current && !current.isDestroyed()) {
        applyWindowBackdropTheme(current, resolveInitialTheme(sharedSettings, nativeTheme.shouldUseDarkColors))
      }
    })
  }

  let showFallbackTimer: ReturnType<typeof setTimeout> | null = null
  let electronReadyToShow = isDev
  let rendererReadyForShow = isDev
  const clearShowFallbackTimer = (): void => {
    if (showFallbackTimer) {
      clearTimeout(showFallbackTimer)
      showFallbackTimer = null
    }
  }
  const showWhenReady = (): void => {
    if (isDev || !electronReadyToShow || !rendererReadyForShow) return
    clearShowFallbackTimer()
    showWindowSafely(win)
  }
  const forceShow = (): void => {
    clearShowFallbackTimer()
    showWindowSafely(win)
  }
  const handleRendererReadyForShow = (event: IpcMainEvent): void => {
    if (event.sender !== win.webContents) return
    rendererReadyForShow = true
    showWhenReady()
  }
  ipcMain.on('window:renderer-ready-for-show', handleRendererReadyForShow)

  if (!isDev) {
    win.once('ready-to-show', () => {
      electronReadyToShow = true
      showWhenReady()
    })
    showFallbackTimer = setTimeout(() => {
      console.warn('[desktop] ready-to-show timeout; forcing window show fallback')
      forceShow()
    }, WINDOW_SHOW_FALLBACK_MS)
  }

  win.webContents.on(
    'did-fail-load',
    (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
      if (!isMainFrame || errorCode === -3) {
        return
      }
      const message = `Renderer failed to load (${errorCode}): ${errorDescription} (${validatedURL || 'unknown URL'})`
      console.error('[desktop] did-fail-load', message)
      forceShow()
      emitConnectionStatus(win, { status: 'error', errorMessage: message })
    }
  )

  win.webContents.on('render-process-gone', (_event, details) => {
    const message = `Renderer process exited (${details.reason})`
    console.error('[desktop] render-process-gone', details)
    forceShow()
    emitConnectionStatus(win, { status: 'error', errorMessage: message })
  })

  win.webContents.on('unresponsive', () => {
    console.warn('[desktop] renderer became unresponsive')
    forceShow()
  })

  const sendMaximizedState = (): void => {
    if (win.isDestroyed()) return
    win.webContents.send('window:maximized-change', win.isMaximized())
  }
  const sendVisibilityState = (): void => {
    if (win.isDestroyed()) return
    win.webContents.send('window:visibility-changed', {
      minimized: win.isMinimized(),
      visible: win.isVisible(),
      focused: win.isFocused()
    })
  }

  // Re-apply the persisted interface zoom on every load (webContents zoom resets per load),
  // so the UI scales without a flash. Runtime changes go through the renderer (webFrame).
  win.webContents.on('did-finish-load', () => {
    win.webContents.setZoomFactor(normalizeInterfaceZoom(sharedSettings.interfaceZoom))
    sendVisibilityState()
  })

  win.on('maximize', sendMaximizedState)
  win.on('unmaximize', sendMaximizedState)
  win.on('enter-full-screen', sendMaximizedState)
  win.on('leave-full-screen', sendMaximizedState)
  win.on('minimize', sendVisibilityState)
  win.on('restore', sendVisibilityState)
  win.on('show', sendVisibilityState)
  win.on('hide', sendVisibilityState)
  win.on('focus', sendVisibilityState)
  win.on('blur', sendVisibilityState)

  win.on('close', () => {
    viewerBrowserManager.destroyAllTabs(win)
    void teardownRuntime('window close', { releaseWorkspaceLock: true })
  })

  win.on('closed', () => {
    clearShowFallbackTimer()
    ipcMain.removeListener('window:renderer-ready-for-show', handleRendererReadyForShow)
    mainWindow = null
  })

  return win
}

// ─── Spawn a new process for "New Window" ─────────────────────────────────────
// Always spawns without a --workspace argument so the new process shows the
// welcome screen. This prevents two processes from accidentally opening the
// same workspace simultaneously.

function openNewProcess(): void {
  const filteredArgs = stripWorkspaceArgs(process.argv.slice(1))
  filteredArgs.push(NO_WORKSPACE_ARG)
  const child = spawn(process.execPath, filteredArgs, {
    detached: true,
    stdio: 'ignore'
  })
  child.unref()
}

/** Remove any existing --workspace <path> pair from argv so the new process can set its own. */
function stripWorkspaceArgs(argv: string[]): string[] {
  const result: string[] = []
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--workspace') {
      i++ // skip the value too
    } else if (argv[i] === NO_WORKSPACE_ARG || parseWorkspaceOpenDeepLink(argv[i])) {
      continue
    } else {
      result.push(argv[i])
    }
  }
  return result
}

// ─── WebSocket remote connection ─────────────────────────────────────────────

const REMOTE_CONNECTION_PROBE_TIMEOUT_MS = 10_000
const REMOTE_INITIALIZE_TIMEOUT_MS = 15_000

interface RemoteConnectionDiagnostic {
  stage?: string
  hostName?: string
  stackName?: string
  localPort?: number
  targetPort?: number
  tokenPresent?: boolean
}

interface ConnectViaWebSocketOptions {
  autoReconnect?: boolean
  initializeTimeoutMs?: number | null
  initialDisconnectIsError?: boolean
  remoteDiagnostic?: RemoteConnectionDiagnostic
  projectId?: string
  projectKind?: WorkspaceProjectKind
  identityWorkspacePath?: string
  displayPath?: string
  remote?: WorkspaceRemoteProjectMetadata
}

function formatRemoteConnectionError(
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

function classifyRemoteInitialError(message: string): ConnectionErrorType {
  return /timed out/i.test(message) ? 'handshake-timeout' : 'remote-config-invalid'
}

function probeRemoteAppServerConnection(wsUrl: string): Promise<void> {
  return new Promise((resolve, reject) => {
    let settled = false
    const ws = new WebSocket(wsUrl)
    const timer = setTimeout(() => {
      settle(new Error('Remote AppServer did not respond within 10 seconds.'))
    }, REMOTE_CONNECTION_PROBE_TIMEOUT_MS)

    function settle(error?: Error): void {
      if (settled) return
      settled = true
      clearTimeout(timer)
      ws.removeAllListeners()
      try {
        ws.close()
      } catch {
        // Best-effort cleanup after a failed probe.
      }
      if (error) reject(error)
      else resolve()
    }

    ws.on('open', () => {
      ws.send(JSON.stringify({
        jsonrpc: '2.0',
        id: 1,
        method: 'initialize',
        params: {
          clientInfo: {
            name: 'dotcraft-desktop',
            title: 'DotCraft',
            version: process.env.npm_package_version ?? '0.1.0'
          },
          capabilities: {
            approvalSupport: true,
            requestUserInputSupport: true,
            streamingSupport: true,
            commandExecutionStreaming: true,
            toolExecutionLifecycle: true,
            backgroundTerminals: true,
            configChange: true,
            optOutNotificationMethods: [],
            nodeRepl: {
              backend: 'desktop-node'
            },
            browserUse: {
              backend: 'desktop-iab',
              backends: ['desktop-iab'],
              protocolVersion: 2,
              supportsCancel: true,
              browserSessionProtocolVersion: 1,
              defaultCommandTimeoutMs: 10000,
              maxCommandTimeoutMs: 120000,
              supportsTypedFinalize: true
            }
          }
        }
      }))
    })

    ws.on('message', (data) => {
      let response: { id?: unknown; error?: { message?: string; data?: unknown } }
      try {
        response = JSON.parse(data.toString()) as typeof response
      } catch {
        return
      }
      if (response.id !== 1) return
      if (response.error) {
        const message = response.error.message || 'Remote AppServer rejected initialize.'
        settle(new Error(message))
        return
      }
      ws.send(JSON.stringify({ jsonrpc: '2.0', method: 'initialized', params: {} }))
      settle()
    })

    ws.on('error', (error) => {
      settle(error instanceof Error ? error : new Error(String(error)))
    })

    ws.on('close', () => {
      settle(new Error('Remote AppServer connection closed before initialize completed.'))
    })
  })
}

async function applyConnectionSettings(draft: ConnectionSettingsDraft): Promise<void> {
  if (!currentWorkspacePath) {
    throw new Error('Open a workspace before applying connection settings.')
  }
  if (process.argv.includes('--remote')) {
    throw new Error('Persistent connection settings cannot be changed while Desktop was launched with --remote.')
  }

  const nextConnectionMode = draft.connectionMode === 'remote' ? 'remote' : 'local'
  if (nextConnectionMode === 'remote') {
    const resolved = resolveRemoteWebSocketConfig(draft.remote)
    if (!resolved.ok) {
      throw new Error(resolved.message)
    }
    await probeRemoteAppServerConnection(resolved.connectUrl)
    closeActiveRemoteStackTunnels()
    await updateSharedSettings({ ...draft, activeRemoteStack: undefined })
    await connectToAppServer(currentWorkspacePath)
    return
  }

  await teardownRuntime('apply local connection settings before reconnect')
  closeActiveRemoteStackTunnels()
  await updateSharedSettings({ ...draft, activeRemoteStack: undefined })
  const hubClient = createHubClient(sharedSettings)
  const restarted = await hubClient.restartAppServer(currentWorkspacePath, resolveDotCraftRuntimeTools())
  await connectViaWebSocket(currentWorkspacePath, getManagedAppServerEndpoint(restarted))
  startHubEventSubscription(currentWorkspacePath, hubClient)
}

function closeActiveRemoteStackTunnels(settings: AppSettings = sharedSettings): void {
  clearActiveRemoteReconnectTimer()
  activeRemoteReconnectAttempt = 0
  const ref = settings.activeRemoteStack
  if (ref?.hostId && ref.stackId) {
    getRemoteServersManager().closeStackTunnels(ref.hostId, ref.stackId)
  }
  disposeActiveRemoteConnection()
  setActiveRemoteProject(null)
  lastRemoteStackLocalPort = null
}

async function disconnectActiveRemoteProject(options: {
  restorePreviousLocal?: boolean
  targetWorkspacePath?: string
} = {}): Promise<void> {
  const project = activeRemoteProject
  clearActiveRemoteReconnectTimer()
  activeRemoteReconnectAttempt = 0
  if (project?.source === 'servers' && project.remote.hostId && project.remote.stackId) {
    getRemoteServersManager().closeStackTunnels(project.remote.hostId, project.remote.stackId)
  }
  disposeActiveRemoteConnection()
  setActiveRemoteProject(null)
  lastRemoteStackLocalPort = null

  const restorePath =
    options.targetWorkspacePath?.trim() ||
    (options.restorePreviousLocal === false ? '' : previousLocalForegroundWorkspacePath || currentWorkspacePath)
  previousLocalForegroundWorkspacePath = null

  await updateSharedSettings({
    connectionMode: 'local',
    activeRemoteStack: undefined
  })
  emitWorkspaceProjects()

  if (restorePath) {
    await connectToAppServer(restorePath)
  } else if (mainWindow && !mainWindow.isDestroyed()) {
    emitCurrentWorkspaceStatus(currentWorkspacePath)
    emitConnectionStatus(mainWindow, { status: 'disconnected' })
  }
}

async function connectRemoteStackFromServers(
  host: RemoteHost,
  stack: RemoteStack
): Promise<{ localPort?: number }> {
  if (!currentWorkspacePath) {
    throw new Error('Open a workspace before connecting a remote stack.')
  }
  if (process.argv.includes('--remote')) {
    throw new Error('Saved remote stacks cannot be activated while Desktop was launched with --remote.')
  }

  const manager = getRemoteServersManager()
  const previousActive = sharedSettings.activeRemoteStack
  if (
    previousActive?.hostId &&
    previousActive.stackId &&
    (previousActive.hostId !== host.id || previousActive.stackId !== stack.id)
  ) {
    manager.closeStackTunnels(previousActive.hostId, previousActive.stackId)
  }

  const result = await manager.openAppServerTunnel(host, stack)
  try {
    await probeRemoteAppServerConnection(result.wsUrl)
  } catch (error) {
    manager.closeStackTunnels(host.id, stack.id)
    const message = error instanceof Error ? error.message : String(error)
    throw new Error(formatRemoteConnectionError(message, {
      stage: 'probe',
      hostName: host.name,
      stackName: stack.name,
      localPort: result.localPort,
      targetPort: stack.appServerPort,
      tokenPresent: result.tokenPresent
    }))
  }

  await updateSharedSettings({
    connectionMode: 'remote',
    remote: undefined,
    activeRemoteStack: { hostId: host.id, stackId: stack.id }
  })
  await connectToAppServer(currentWorkspacePath)
  return { localPort: lastRemoteStackLocalPort ?? result.localPort }
}

async function disconnectRemoteStackFromServers(hostId: string, stackId: string): Promise<void> {
  const active = sharedSettings.activeRemoteStack
  if (active?.hostId !== hostId || active.stackId !== stackId) {
    getRemoteServersManager().closeStackTunnels(hostId, stackId)
    return
  }

  await disconnectActiveRemoteProject({ restorePreviousLocal: true })
}

async function connectViaWebSocket(
  workspacePath: string,
  wsUrl: string,
  options: ConnectViaWebSocketOptions = {}
): Promise<void> {
  if (isAppQuitting || !mainWindow || mainWindow.isDestroyed()) {
    return
  }
  const win = mainWindow!
  lastAppServerWsUrl = wsUrl
  resetDesktopThreadToolBindings()
  emitConnectionStatus(win, { status: 'connecting' })
  reregisterIpcForWorkspace(workspacePath)

  const generation = ++connectionGeneration
  const client = WireProtocolClient.fromWebSocket(wsUrl, {
    autoReconnect: options.autoReconnect,
    initializeTimeoutMs: options.initializeTimeoutMs
  })
  const projectId = options.projectId ?? localProjectId(workspacePath)
  const connectionKey = projectId
  const identityWorkspacePath = options.identityWorkspacePath ?? workspacePath
  const previousEntry = workspaceConnections.get(connectionKey)
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
  workspaceConnections.set(connectionKey, entry)
  wireClient = client
  const isCurrentConnectAttempt = (): boolean =>
    !isAppQuitting &&
    wireClient === client &&
    connectionGeneration === generation &&
    mainWindow === win &&
    !win.isDestroyed()
  const isCurrentForegroundClient = (): boolean =>
    isCurrentForegroundWorkspaceConnection({
      appQuitting: isAppQuitting,
      mainWindow,
      window: win,
      wireClient,
      client,
      role: entry.role
    })

  client.onNotification((method, params) => {
    const foreground = getWorkspaceNotificationForeground(method, {
      appQuitting: isAppQuitting,
      mainWindow,
      window: win,
      wireClient,
      client,
      role: entry.role
    })
    if (foreground == null) return

    if (mainWindow && !mainWindow.isDestroyed()) {
      applyWorkspaceThreadNotification(entry, method, params)
      broadcastNotification(mainWindow, method, params, sharedSettings, entry.workspacePath, foreground)
    }
  })

  client.onServerRequest(async (method, params) => {
    const routingState = {
      appQuitting: isAppQuitting,
      mainWindow,
      window: win,
      wireClient,
      client,
      role: entry.role
    }
    return handleWorkspaceServerRequest(
      method,
      params,
      shouldBridgeWorkspaceServerRequest(routingState),
      canBridgeRendererInteractiveServerRequest(routingState)
    )
  })
  let connectedOnce = false
  let initialFailureEmitted = false
  const emitInitialConnectionFailure = (
    message: string,
    errorType: ConnectionErrorType,
    stage: string
  ): void => {
    if (initialFailureEmitted || !isCurrentConnectAttempt()) return
    initialFailureEmitted = true
    resetDesktopThreadToolBindings()
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, {
        status: 'error',
        errorMessage: formatRemoteConnectionError(message, options.remoteDiagnostic, stage),
        errorType
      })
    }
    if (wireClient === client) {
      wireClient = null
      lastAppServerWsUrl = null
    }
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = message
    emitWorkspaceProjects()
    client.dispose()
  }
  const emitConnected = (result: InitializeResult): void => {
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result
    entry.lastUsedAt = Date.now()
    if (workspaceActivation) {
      publishWorkspaceActivation(workspaceActivation.handle.endpoint)
    }
    void refreshConnectionThreadList(entry)
    if (entry.role === 'secondary') {
      return
    }
    if (!isCurrentForegroundClient()) return
    connectedOnce = true
    clearActiveRemoteReconnectTimer()
    activeRemoteReconnectAttempt = 0
    resetDesktopThreadToolBindings()
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, {
        status: 'connected',
        serverInfo: result.serverInfo,
        capabilities: result.capabilities as Record<string, unknown>,
        dashboardUrl: result.dashboardUrl
      })
      flushPendingWorkspaceOpenThread(mainWindow)
      emitWorkspaceProjects()
    }
    if (!options.remoteDiagnostic && !activeRemoteWorkspace && resolveConnectionMode(sharedSettings) === 'local') {
      void autoStartEnabledModules()
    }
  }
  client.on('ready', (result: InitializeResult) => emitConnected(result))
  client.on('reconnected', (result: InitializeResult) => emitConnected(result))
  client.on('close', () => {
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = undefined
    emitWorkspaceProjects()
    if (entry.role === 'secondary') return
    if (!isCurrentForegroundClient()) return
    resetDesktopThreadToolBindings()
    if (!connectedOnce && options.initialDisconnectIsError) {
      if (!initialFailureEmitted) {
        emitInitialConnectionFailure(
          'Remote AppServer WebSocket closed before initialize completed.',
          'remote-config-invalid',
          'websocket-close-before-ready'
        )
      }
      return
    }
    if (mainWindow && !mainWindow.isDestroyed()) {
      const loc = normalizeLocale(sharedSettings.locale)
      emitConnectionStatus(mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
    scheduleActiveRemoteStackReconnect('websocket closed')
  })
  client.on('reconnect-error', (err) => {
    entry.errorMessage = err instanceof Error ? err.message : String(err)
    entry.connecting = false
    emitWorkspaceProjects()
    if (entry.role === 'secondary') return
    if (!isCurrentForegroundClient()) return
    const message = err instanceof Error ? err.message : String(err)
    if (!connectedOnce && options.initialDisconnectIsError) {
      emitInitialConnectionFailure(
        message,
        classifyRemoteInitialError(message),
        /timed out/i.test(message) ? 'initialize-timeout' : 'initialize'
      )
      return
    }
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, { status: 'error', errorMessage: message })
    }
  })
}

function getManagedAppServerEndpoint(response: HubAppServerResponse): string {
  const endpoint = response.endpoints?.appServerWebSocket
  if (!endpoint?.trim()) {
    throw new Error('Hub did not return an AppServer WebSocket endpoint.')
  }
  return endpoint
}

async function restartCurrentManagedAppServer(): Promise<void> {
  if (!currentWorkspacePath) {
    throw new Error('Open a workspace before restarting AppServer.')
  }
  if (process.argv.includes('--remote')) {
    throw new Error('Cannot restart AppServer while using a remote WebSocket connection.')
  }
  if (resolveConnectionMode(sharedSettings) === 'remote') {
    throw new Error('Restart is only available for Hub-managed local AppServers.')
  }
  const hubClient = createHubClient(sharedSettings)
  const restarted = await hubClient.restartAppServer(currentWorkspacePath, resolveDotCraftRuntimeTools())
  await connectViaWebSocket(currentWorkspacePath, getManagedAppServerEndpoint(restarted))
  startHubEventSubscription(currentWorkspacePath, hubClient)
}

async function retryCurrentAppServerConnection(request?: RetryConnectionRequest): Promise<void> {
  await retryAppServerConnection(request, {
    currentWorkspacePath,
    launchedWithRemote: process.argv.includes('--remote'),
    connectionMode: resolveConnectionMode(sharedSettings),
    reconnect: async () => {
      await connectToAppServer(currentWorkspacePath)
    },
    restartManaged: restartCurrentManagedAppServer
  })
}

function isCurrentWorkspaceEvent(event: HubEvent, workspacePath: string): boolean {
  if (!event.workspacePath) return false
  return isSameWorkspacePath(event.workspacePath, workspacePath)
}

function isHubAppServerLifecycleEvent(event: HubEvent): boolean {
  return event.kind.startsWith('appserver.')
}

function isRecentSecondaryWorkspaceEvent(event: HubEvent): boolean {
  const workspacePath = event.workspacePath?.trim()
  if (!workspacePath || activeRemoteWorkspace || resolveConnectionMode(sharedSettings) !== 'local') {
    return false
  }
  if (isWorkspaceForeground(workspacePath)) return false
  return getRecentWorkspaces(sharedSettings).some((recent) => isSameWorkspacePath(recent.path, workspacePath))
}

function scheduleSecondaryWorkspaceRefresh(): void {
  if (secondaryRefreshTimer) {
    clearTimeout(secondaryRefreshTimer)
  }
  secondaryRefreshTimer = setTimeout(() => {
    secondaryRefreshTimer = null
    void refreshSecondaryWorkspaceConnections()
  }, 150)
}

function startHubEventSubscription(workspacePath: string, hubClient: HubClient): void {
  hubEventAbortController?.abort()
  const controller = new AbortController()
  hubEventAbortController = controller

  void hubClient.subscribeEvents((event) => {
    if (isHubAppServerLifecycleEvent(event) && isRecentSecondaryWorkspaceEvent(event)) {
      scheduleSecondaryWorkspaceRefresh()
    }

    if (!isCurrentWorkspaceEvent(event, workspacePath)) return

    if (event.kind === 'appserver.exited') {
      wireClient?.dispose()
      wireClient = null
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        emitConnectionStatus(mainWindow, {
          status: 'disconnected',
          errorMessage: translate(loc, 'main.status.reconnecting')
        })
      }
      return
    }

    if (event.kind === 'appserver.running') {
      const data = event.data as { endpoints?: Record<string, string> } | null
      const endpoint = data?.endpoints?.appServerWebSocket
      if (endpoint && currentWorkspacePath === workspacePath && !isAppQuitting) {
        void connectViaWebSocket(workspacePath, endpoint)
      }
    }

    if (event.kind === 'notification.requested' && mainWindow && !mainWindow.isDestroyed()) {
      const data = event.data as { kind?: string; title?: string; body?: string } | null
      broadcastNotification(
        mainWindow,
        data?.kind ?? 'hub/notification',
        data ?? {},
        sharedSettings,
        event.workspacePath ?? workspacePath
      )
    }
  }, controller.signal).catch((error) => {
    if (!controller.signal.aborted) {
      console.warn('[desktop] Hub event subscription ended', error)
    }
  })
}

function promoteWorkspaceConnection(entry: WorkspaceConnectionEntry): void {
  const previous = currentWorkspacePath ? getWorkspaceConnection(currentWorkspacePath) : undefined
  if (previous && previous.client !== entry.client) {
    previous.role = 'secondary'
    previous.lastUsedAt = Date.now()
    releaseConnectionThreadSubscription(previous, 'workspace demote')
    releaseWorkspaceLock(previous.workspacePath)
  }

  entry.role = 'foreground'
  entry.lastUsedAt = Date.now()
  wireClient = entry.client
  currentWorkspacePath = entry.workspacePath
  lastAppServerWsUrl = entry.wsUrl
  lastDashboardUrl = entry.initializeResult?.dashboardUrl ?? null
  setActiveRemoteProject(null)
  previousLocalForegroundWorkspacePath = null
  lastRemoteStackLocalPort = null
  resetDesktopThreadToolBindings()
  reregisterIpcForWorkspace(entry.workspacePath)
  ensureWorkspaceActivation(entry.workspacePath)
  emitCurrentWorkspaceStatus(entry.workspacePath)
  if (mainWindow && !mainWindow.isDestroyed()) {
    if (entry.initializeResult) {
      emitConnectionStatus(mainWindow, {
        status: 'connected',
        serverInfo: entry.initializeResult.serverInfo,
        capabilities: entry.initializeResult.capabilities as Record<string, unknown>,
        dashboardUrl: entry.initializeResult.dashboardUrl
      })
    } else {
      emitConnectionStatus(mainWindow, { status: entry.connected ? 'connected' : 'connecting' })
    }
    const loc = normalizeLocale(sharedSettings.locale)
    mainWindow.setTitle(
      translate(loc, 'app.titleWithWorkspace', { name: workspaceTitleName(entry.workspacePath, loc) })
    )
  }
  startHubEventSubscription(entry.workspacePath, createHubClient(sharedSettings))
  emitWorkspaceProjects()
}

function createSecondaryWorkspaceConnection(
  workspacePath: string,
  wsUrl: string,
  options: { kind?: WorkspaceProjectKind } = {}
): WorkspaceConnectionEntry {
  const key = normalizeWorkspaceConnectionKey(workspacePath)
  const existing = workspaceConnections.get(key)
  if (existing) {
    existing.lastUsedAt = Date.now()
    return existing
  }

  const client = WireProtocolClient.fromWebSocket(wsUrl, {
    initializeProfile: 'secondary'
  })
  const entry: WorkspaceConnectionEntry = {
    key,
    projectId: localProjectId(workspacePath),
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
  workspaceConnections.set(key, entry)
  emitWorkspaceProjects()

  client.onNotification((method, params) => {
    const foreground = getWorkspaceNotificationForeground(method, {
      appQuitting: isAppQuitting,
      mainWindow,
      wireClient,
      client,
      role: entry.role
    })
    if (foreground == null) return

    applyWorkspaceThreadNotification(entry, method, params)
    if (mainWindow && !mainWindow.isDestroyed()) {
      broadcastNotification(mainWindow, method, params, sharedSettings, workspacePath, foreground)
    }
  })

  client.onServerRequest(async (method, params) => {
    const routingState = {
      appQuitting: isAppQuitting,
      mainWindow,
      wireClient,
      client,
      role: entry.role
    }
    return handleWorkspaceServerRequest(
      method,
      params,
      shouldBridgeWorkspaceServerRequest(routingState),
      canBridgeRendererInteractiveServerRequest(routingState)
    )
  })

  const onReady = (result: InitializeResult): void => {
    entry.connected = true
    entry.connecting = false
    entry.errorMessage = undefined
    entry.initializeResult = result
    if (workspaceActivation) {
      publishWorkspaceActivation(workspaceActivation.handle.endpoint)
    }
    void refreshConnectionThreadList(entry)
  }
  client.on('ready', onReady)
  client.on('reconnected', onReady)
  client.on('close', () => {
    entry.connected = false
    entry.connecting = false
    entry.errorMessage = undefined
    emitWorkspaceProjects()
    if (entry.role === 'foreground' && mainWindow && !mainWindow.isDestroyed()) {
      const loc = normalizeLocale(sharedSettings.locale)
      emitConnectionStatus(mainWindow, {
        status: 'disconnected',
        errorMessage: translate(loc, 'main.status.reconnecting')
      })
    }
  })
  client.on('reconnect-error', (error) => {
    entry.errorMessage = error instanceof Error ? error.message : String(error)
    entry.connecting = false
    emitWorkspaceProjects()
    if (entry.role === 'foreground' && mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, { status: 'error', errorMessage: entry.errorMessage })
    }
  })

  return entry
}

// Ensures the default Chat workspace (`~/.craft/workspaces/chats`) has a live,
// secondary-style connection so the sidebar `Chats` group can list its threads
// without the user opening it as a project. Skips when Chat is already the
// foreground workspace (its connection is owned by connectViaWebSocket then), or
// when it is already connected/connecting. Mirrors the backend default Chat helper:
// ensure the workspace skeleton, then go through the existing Hub ensure flow.
async function ensureDefaultChatConnection(): Promise<void> {
  if (isAppQuitting) return
  const chatPath = resolveDefaultChatWorkspacePath()
  if (isWorkspaceForeground(chatPath)) return
  const existing = getWorkspaceConnection(chatPath)
  if (existing?.connected || existing?.connecting) return

  try {
    ensureDefaultChatWorkspace()
    if (getWorkspaceStatus(chatPath).status !== 'ready') return

    const ensured = await createHubClient(sharedSettings).ensureAppServer(chatPath, {
      runtimeTools: resolveDotCraftRuntimeTools()
    })
    if (isAppQuitting || isWorkspaceForeground(chatPath)) return
    const endpoint = ensured.endpoints?.appServerWebSocket
    if (endpoint?.trim()) {
      createSecondaryWorkspaceConnection(chatPath, endpoint, { kind: 'chat' })
    }
  } catch (error) {
    console.warn('[desktop] failed to ensure default chat workspace connection', error)
  }
}

async function refreshSecondaryWorkspaceConnections(): Promise<void> {
  if (isAppQuitting || activeRemoteWorkspace || resolveConnectionMode(sharedSettings) !== 'local') {
    return
  }
  await ensureDefaultChatConnection()
  const recents = getRecentWorkspaces(sharedSettings)
    .filter((recent) => recent.path && !isWorkspaceForeground(recent.path))
  if (recents.length === 0) {
    emitWorkspaceProjects()
    return
  }

  let liveAppServers: HubAppServerResponse[] = []
  try {
    liveAppServers = await createHubClient(sharedSettings).listAppServers()
  } catch (error) {
    console.warn('[desktop] failed to list Hub appservers for secondary workspaces', error)
    emitWorkspaceProjects()
    return
  }

  const liveByKey = new Map<string, HubAppServerResponse>()
  for (const appServer of liveAppServers) {
    if (!isRunningHubAppServer(appServer)) continue
    const workspacePath = appServer.workspacePath || appServer.canonicalWorkspacePath
    if (!workspacePath) continue
    liveByKey.set(normalizeWorkspaceConnectionKey(workspacePath), appServer)
    if (appServer.canonicalWorkspacePath) {
      liveByKey.set(normalizeWorkspaceConnectionKey(appServer.canonicalWorkspacePath), appServer)
    }
  }

  const allowedKeys = new Set<string>()
  for (const recent of recents) {
    if (allowedKeys.size >= SECONDARY_WORKSPACE_CONNECTION_LIMIT) break
    const key = normalizeWorkspaceConnectionKey(recent.path)
    const live = liveByKey.get(key)
    const endpoint = live?.endpoints?.appServerWebSocket
    if (endpoint?.trim()) {
      allowedKeys.add(key)
      createSecondaryWorkspaceConnection(recent.path, endpoint)
    }
  }

  for (const entry of [...workspaceConnections.values()]) {
    if (entry.role !== 'secondary') continue
    // The default Chat connection is managed by ensureDefaultChatConnection, not the
    // recents-driven secondary set, so it must survive this prune.
    if (isDefaultChatWorkspace(entry.workspacePath)) continue
    if (!allowedKeys.has(entry.key)) {
      disposeWorkspaceConnection(entry)
    }
  }
  emitWorkspaceProjects()
}

// ─── AppServer connection ─────────────────────────────────────────────────────

function buildCallbacks(): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: async (newPath: string) => {
      if (activeRemoteProject) {
        await disconnectActiveRemoteProject({ restorePreviousLocal: false })
      }
      if (mainWindow && !mainWindow.isDestroyed()) {
        viewerBrowserManager.destroyAllTabs(mainWindow)
      }
      setViewerWorkspaceRoot(newPath)
      // The default Chat workspace is surfaced as the `Chats` group, never as a
      // Project, so opening one of its threads must not add it to recent projects.
      // Ensure its skeleton first so it never diverts into the setup wizard.
      if (isDefaultChatWorkspace(newPath)) {
        ensureDefaultChatWorkspace()
        sharedSettings.lastForegroundEntry = 'chats'
        saveSettings(sharedSettings)
      } else {
        addRecentWorkspace(sharedSettings, newPath)
        saveSettings(sharedSettings)
      }
      emitWorkspaceProjects()
      await connectToAppServer(newPath)
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        mainWindow.setTitle(
          translate(loc, 'app.titleWithWorkspace', { name: workspaceTitleName(newPath, loc) })
        )
      }
    },
    onClearWorkspaceSelection: async () => {
      await clearWorkspaceSelection()
    },
    onRunWorkspaceSetup: async (request: WorkspaceSetupRequest) => {
      if (!currentWorkspacePath) {
        throw new Error('Open a workspace before running setup.')
      }
      const result = await runWorkspaceSetup(currentWorkspacePath, request, sharedSettings)
      emitCurrentWorkspaceStatus(currentWorkspacePath)
      await connectToAppServer(currentWorkspacePath)
      return result
    },
    onListSetupModels: async (request: WorkspaceSetupModelListRequest) => {
      return listSetupModels(request, { settings: sharedSettings })
    },
    onLoginSetupChatGpt: async (providerId: string) => loginSetupChatGpt(providerId, sharedSettings),
    onOpenNewWindow: () => {
      openNewProcess()
    },
    onRestartManagedAppServer: restartCurrentManagedAppServer,
    onRetryAppServerConnection: retryCurrentAppServerConnection,
    onApplyConnectionSettings: applyConnectionSettings,
    onConnectRemoteStack: connectRemoteStackFromServers,
    onDisconnectRemoteStack: disconnectRemoteStackFromServers,
    onDisconnectRemoteProject: () => disconnectActiveRemoteProject({ restorePreviousLocal: true }),
    getSettings: () => sharedSettings,
    updateSettings: async (partial) => {
      await updateSharedSettings(partial)
    },
    getAppServerWsConfig: () => lastAppServerWsUrl ? { wsUrl: lastAppServerWsUrl } : null,
    getRecentWorkspaces: () => getRecentWorkspaces(sharedSettings),
    getWorkspaceProjects: getWorkspaceProjectsPayload,
    removeRecentWorkspace: (workspacePath: string) => {
      if (isWorkspaceForeground(workspacePath)) {
        throw new Error('Cannot remove the foreground project from Projects.')
      }
      const entry = getWorkspaceConnection(workspacePath)
      if (entry?.role === 'secondary') {
        disposeWorkspaceConnection(entry)
      }
      removeRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
      emitWorkspaceProjects()
    },
    saveLocalProject: (params) => {
      saveLocalProject(sharedSettings, params)
      saveSettings(sharedSettings)
      emitWorkspaceProjects()
    },
    clearRecentWorkspaces: () => {
      clearRecentWorkspaces(sharedSettings)
      saveSettings(sharedSettings)
      emitWorkspaceProjects()
    },
    restartWorkspace: async (workspacePath: string) => {
      const hubClient = createHubClient(sharedSettings)
      await hubClient.restartAppServer(workspacePath, resolveDotCraftRuntimeTools())
    },
    stopWorkspace: async (workspacePath: string) => {
      const hubClient = createHubClient(sharedSettings)
      await hubClient.stopAppServer(workspacePath)
    },
    archiveThreadInWorkspace: async (workspacePath: string, threadId: string) => {
      const id = threadId.trim()
      if (!id) throw new Error('A thread id is required to archive.')
      const entry = getWorkspaceConnection(workspacePath)
      if (!entry || !entry.connected) {
        throw new Error('Workspace connection is not available for archiving.')
      }
      await entry.client.sendRequest('thread/archive', { threadId: id })
      // Re-fetch the connection's thread list so the archived row drops out of the
      // secondary project group immediately (thread/list omits archived threads).
      await refreshConnectionThreadList(entry)
    },
    onAppServerRequestCompleted: (client, method, params) => {
      observeAppServerRequestCompletion(client, method, params)
    },
    getConnectionStatus: () => lastConnectionStatus,
    getWorkspaceStatus: () => getWorkspaceStatusForRenderer(currentWorkspacePath)
  }
}

/** Re-register IPC handlers with the current workspace path (used on workspace switch). */
function reregisterIpcForWorkspace(workspacePath: string): void {
  registerDesktopIpcHandlers(workspacePath, () => wireClient)
}

async function openWorkspaceWithoutConnection(workspacePath: string): Promise<void> {
  if (isAppQuitting) {
    return
  }

  acquireWorkspaceLock(workspacePath)

  if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
    stopWorkspaceActivation()
    releaseWorkspaceLock(currentWorkspacePath)
  }

  await teardownRuntime('switch to setup-required workspace')
  closeActiveRemoteStackTunnels()
  currentWorkspacePath = workspacePath
  ensureWorkspaceActivation(workspacePath)
  reregisterIpcForWorkspace(workspacePath)

  const win = mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  emitWorkspaceStatus(win, getWorkspaceStatusForRenderer(workspacePath))
  emitConnectionStatus(win, { status: 'disconnected' })
}

async function clearWorkspaceSelection(): Promise<void> {
  if (currentWorkspacePath) {
    await teardownRuntime('clear workspace selection', { releaseWorkspaceLock: true })
  }
  closeActiveRemoteStackTunnels()

  if (mainWindow && !mainWindow.isDestroyed()) {
    viewerBrowserManager.destroyAllTabs(mainWindow)
  }
  setViewerWorkspaceRoot('')
  currentWorkspacePath = ''
  ensureWorkspaceActivation('')
  sharedSettings.lastForegroundEntry = 'welcome'
  saveSettings(sharedSettings)

  const win = mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  reregisterIpcForWorkspace('')
  const loc = normalizeLocale(sharedSettings.locale)
  win.setTitle(translate(loc, 'app.brandSubtitle'))
  emitWorkspaceStatus(win, getWorkspaceStatusForRenderer(''))
  emitConnectionStatus(win, { status: 'disconnected' })
}

async function connectToAppServer(workspacePath: string): Promise<boolean> {
  if (isAppQuitting) {
    return false
  }
  const remoteIdx = process.argv.indexOf('--remote')
  const launchedWithRemoteUrl = hasRemoteEndpointArg()
  const connectionMode = resolveConnectionMode(sharedSettings)
  const usingRemoteConnection = launchedWithRemoteUrl || connectionMode === 'remote'
  const preserveLocalConnections = !usingRemoteConnection && connectionMode === 'local'

  if (shouldRouteWorkspaceThroughSetupBeforeAppServerStart(workspacePath, { usingRemoteConnection })) {
    await openWorkspaceWithoutConnection(workspacePath)
    return false
  }

  if (!usingRemoteConnection) {
    acquireWorkspaceLock(workspacePath)
  }

  if (usingRemoteConnection) {
    if (!previousLocalForegroundWorkspacePath && workspacePath) {
      previousLocalForegroundWorkspacePath = workspacePath
    }
    stopWorkspaceActivation()
    releaseWorkspaceLock(workspacePath)
    const previous = getWorkspaceConnection(workspacePath)
    if (previous?.role === 'foreground') {
      previous.role = 'secondary'
      previous.lastUsedAt = Date.now()
      releaseConnectionThreadSubscription(previous, 'remote foreground connect')
      releaseWorkspaceLock(previous.workspacePath)
    }
  } else if (preserveLocalConnections) {
    const existingConnection = getWorkspaceConnection(workspacePath)
    if (existingConnection?.connected) {
      promoteWorkspaceConnection(existingConnection)
      void refreshSecondaryWorkspaceConnections()
      return true
    }
    if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
      const previous = getWorkspaceConnection(currentWorkspacePath)
      if (previous) {
        previous.role = 'secondary'
        previous.lastUsedAt = Date.now()
        releaseConnectionThreadSubscription(previous, 'workspace demote')
        releaseWorkspaceLock(previous.workspacePath)
      }
      stopWorkspaceActivation()
    } else if (currentWorkspacePath === workspacePath) {
      await teardownRuntime('reconnect current local workspace before new connect')
    }
  } else {
    if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
      stopWorkspaceActivation()
      releaseWorkspaceLock(currentWorkspacePath)
    }
    await teardownRuntime('switch/reconnect before new connect')
  }

  currentWorkspacePath = workspacePath
  if (!usingRemoteConnection) {
    ensureWorkspaceActivation(workspacePath)
  }

  const activeStack =
    !launchedWithRemoteUrl && connectionMode === 'remote'
      ? resolveActiveRemoteStack(sharedSettings)
      : null
  lastRemoteStackLocalPort = null

  // --remote ws://host:port/ws?token=xxx  → skip AppServerManager, connect via WebSocket
  if (launchedWithRemoteUrl) {
    const endpoint = process.argv[remoteIdx + 1]
    const project = buildEndpointRemoteProject('cli', endpoint, workspacePath)
    prepareRemoteForeground(project)
    emitCurrentWorkspaceStatus(workspacePath)
    await connectViaWebSocket(workspacePath, endpoint, {
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
    if (activeStack) {
      const win = mainWindow!
      emitConnectionStatus(win, { status: 'connecting' })
      try {
        const result = await getRemoteServersManager().openAppServerTunnel(
          activeStack.host,
          activeStack.stack,
          { forceNew: true }
        )
        lastRemoteStackLocalPort = result.localPort
        const tunnelEndpoint = `ws://127.0.0.1:${result.localPort}/ws`
        const project = buildServersRemoteProject(activeStack.host, activeStack.stack, tunnelEndpoint, workspacePath)
        prepareRemoteForeground(project)
        emitCurrentWorkspaceStatus(workspacePath)
        await connectViaWebSocket(workspacePath, result.wsUrl, {
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
        emitConnectionStatus(win, {
          status: 'error',
          errorMessage: formatRemoteConnectionError(message, {
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

    if (sharedSettings.activeRemoteStack?.hostId || sharedSettings.activeRemoteStack?.stackId) {
      const win = mainWindow!
      emitConnectionStatus(win, {
        status: 'error',
        errorMessage: 'Saved remote stack was not found. Check Servers settings or disconnect this stack.',
        errorType: 'remote-config-invalid'
      })
      return false
    }

    const remoteConfig = resolveRemoteWebSocketConfig(sharedSettings.remote)
    if (!remoteConfig.ok) {
      const win = mainWindow!
      emitConnectionStatus(win, {
        status: 'error',
        errorMessage: remoteConfig.message,
        errorType: 'remote-config-invalid'
      })
      return false
    }
    const project = buildEndpointRemoteProject('manual', remoteConfig.connectUrl, workspacePath)
    prepareRemoteForeground(project)
    emitCurrentWorkspaceStatus(workspacePath)
    await connectViaWebSocket(workspacePath, remoteConfig.connectUrl, {
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

  setActiveRemoteProject(null)
  emitCurrentWorkspaceStatus(workspacePath)
  const win = mainWindow!
  emitConnectionStatus(win, { status: 'connecting' })

  reregisterIpcForWorkspace(workspacePath)
  try {
    const hubClient = createHubClient(sharedSettings)
    const ensured = await hubClient.ensureAppServer(workspacePath, {
      runtimeTools: resolveDotCraftRuntimeTools()
    })
    if (currentWorkspacePath !== workspacePath || isAppQuitting) return false

    startHubEventSubscription(workspacePath, hubClient)
    await connectViaWebSocket(workspacePath, getManagedAppServerEndpoint(ensured))
    void refreshSecondaryWorkspaceConnections()
    return true
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err)
    const isBinaryError =
      message.includes('binary') || message.includes('not found') || message.includes('ENOENT')
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitConnectionStatus(mainWindow, {
        status: 'error',
        errorMessage: message,
        ...(isBinaryError ? { binarySource: resolveBinarySource(sharedSettings) } : {}),
        ...(isBinaryError ? { errorType: 'binary-not-found' } : {})
      } as ConnectionStatusPayload)
    }
    return false
  }
}

// ─── App menu ─────────────────────────────────────────────────────────────────

function buildAppMenu(locale: AppLocale): Menu {
  const isMac = process.platform === 'darwin'
  const L = (key: string) => translate(locale, key)
  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? ([{ role: 'appMenu' }] as MenuItemConstructorOptions[]) : []),
    {
      id: 'file',
      label: L('menu.file'),
      submenu: [
        {
          label: L('menu.newWindow'),
          accelerator: 'CmdOrCtrl+Shift+N',
          click: () => {
            openNewProcess()
          }
        },
        { type: 'separator' },
        isMac ? { role: 'close' } : { role: 'quit' }
      ]
    },
    {
      id: 'edit',
      label: L('menu.edit'),
      submenu: [
        { role: 'undo' },
        { role: 'redo' },
        { type: 'separator' },
        { role: 'cut' },
        { role: 'copy' },
        { role: 'paste' },
        { role: 'selectAll' }
      ]
    },
    {
      id: 'view',
      label: L('menu.view'),
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        ...(import.meta.env.DEV ? ([{ role: 'toggleDevTools' }] as MenuItemConstructorOptions[]) : []),
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        {
          label: L('menu.openDashboard'),
          accelerator: 'CmdOrCtrl+Shift+D',
          enabled: Boolean(lastDashboardUrl),
          click: async () => {
            if (lastDashboardUrl) await openExternalHttpUrl(lastDashboardUrl)
          }
        },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      id: 'window',
      label: L('menu.window'),
      submenu: [
        { role: 'minimize' },
        { role: 'zoom' },
        ...(isMac
          ? ([{ type: 'separator' }, { role: 'front' }] as MenuItemConstructorOptions[])
          : ([{ role: 'close' }] as MenuItemConstructorOptions[]))
      ]
    },
    {
      id: 'help',
      label: L('menu.help'),
      submenu: [
        {
          label: L('menu.whatsNew'),
          click: () => {
            openWhatsNewFromMenu()
          }
        },
        { type: 'separator' },
        {
          label: L('menu.documentation'),
          click: async () => {
            await shell.openExternal('https://github.com/DotHarness/dotcraft')
          }
        }
      ]
    }
  ]
  return Menu.buildFromTemplate(template)
}

function refreshAppMenu(): void {
  Menu.setApplicationMenu(buildAppMenu(normalizeLocale(sharedSettings.locale)))
}

function connectWorkspaceForLoadedWindow(win: BrowserWindow, workspacePath: string): void {
  void connectToAppServer(workspacePath).catch((error) => {
    const message = error instanceof Error ? error.message : String(error)
    console.warn('[desktop] failed to connect restored workspace', message)
    if (!win.isDestroyed()) {
      emitConnectionStatus(win, { status: 'error', errorMessage: message })
    }
  })
}

function emitConnectionStatus(win: BrowserWindow, payload: ConnectionStatusPayload): void {
  if (payload.status === 'connected') {
    const sanitized = sanitizeHttpOrHttpsUrl(payload.dashboardUrl)
    lastConnectionStatus = {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    }
    lastDashboardUrl = sanitized
    broadcastConnectionStatus(win, {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    })
  } else {
    lastConnectionStatus = { ...payload, dashboardUrl: undefined }
    lastDashboardUrl = null
    broadcastConnectionStatus(win, payload)
  }
  refreshAppMenu()
}

function emitWorkspaceStatus(win: BrowserWindow, payload: WorkspaceStatusPayload): void {
  lastWorkspaceStatus = payload
  broadcastWorkspaceStatus(win, payload)
}

function registerMenuPopupIpc(): void {
  ipcMain.removeHandler('menu:popup-top-level')
  ipcMain.removeHandler('app:whats-new-get-releases')
  ipcMain.removeHandler('app:whats-new-get-media-states')
  ipcMain.removeHandler('app:whats-new-prefetch-media')
  ipcMain.removeHandler('app:update-get-state')
  ipcMain.removeHandler('app:update-check')
  ipcMain.removeHandler('app:update-download-and-install')
  ipcMain.handle(
    'menu:popup-top-level',
    (event, payload: { menuId: TopLevelMenuId; x: number; y: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      const appMenu = Menu.getApplicationMenu()
      if (!appMenu) return
      const item = appMenu.items.find((i) => i.id === payload.menuId)
      if (!item?.submenu) return
      item.submenu.popup({
        window: win,
        x: Math.round(payload.x),
        y: Math.round(payload.y)
      })
    }
  )
  ipcMain.handle('app:whats-new-get-media-states', (_event, releaseVersions: string[]) => (
    getWhatsNewMediaCache().getMediaStates(Array.isArray(releaseVersions) ? releaseVersions : [])
  ))
  ipcMain.handle('app:whats-new-get-releases', () => getWhatsNewCatalog().getReleases())
  ipcMain.handle('app:whats-new-prefetch-media', (_event, releaseVersions: string[]) => (
    getWhatsNewMediaCache().prefetchMedia(Array.isArray(releaseVersions) ? releaseVersions : [])
  ))
  ipcMain.handle('app:update-get-state', () => getAppUpdateService().getState())
  ipcMain.handle('app:update-check', () => getAppUpdateService().checkForUpdates())
  ipcMain.handle('app:update-download-and-install', () => (
    getAppUpdateService().downloadAndInstall()
  ))
  ipcMain.handle('profile:get-github-identity', (_event, username: string) =>
    getGitHubIdentity(typeof username === 'string' ? username : '')
  )
}

// ─── App lifecycle ────────────────────────────────────────────────────────────

app.on('open-url', (event, url) => {
  const workspaceOpen = parseWorkspaceOpenDeepLink(url)
  if (workspaceOpen) {
    event.preventDefault()
    handleWorkspaceOpenDeepLink(workspaceOpen)
    return
  }

  if (!isChromeSettingsDeepLink(url)) return
  event.preventDefault()
  openChromeSettingsFromDeepLink()
})

app.whenReady().then(async () => {
  isAppQuitting = false
  installDevProcessGuards()
  if (isTrayMode) {
    if (process.platform === 'darwin') {
      app.dock.hide()
    }
    Menu.setApplicationMenu(null)
    void runTrayProcess().catch((error) => {
      console.error('[desktop-tray] failed to start tray process', error)
      app.quit()
    })
    return
  }

  try {
    app.setAsDefaultProtocolClient('dotcraft')
  } catch (error) {
    console.warn('[desktop] failed to register dotcraft protocol handler', error)
  }
  startChromeSettingsDeepLinkServer()

  installViewerProtocolHandler()
  installPluginFileProtocolHandler()
  installMcpAppSandboxProtocolHandler()
  registerMenuPopupIpc()
  sharedSettings = loadSettings()
  applyNativeThemeSource(nativeTheme, sharedSettings)
  refreshAppMenu()
  try {
    ensureTrayProcess(sharedSettings)
  } catch (error) {
    console.warn('[desktop] failed to ensure tray process', error)
  }

  if (!import.meta.env.DEV) {
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
      // The fixed proxy must not inherit the renderer's script-src. Its sandboxed inner srcdoc
      // receives the separately generated MCP App CSP instead.
      if (details.url.startsWith(`${MCP_APP_SANDBOX_SCHEME}:`)) {
        callback({ responseHeaders: details.responseHeaders })
        return
      }
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          'Content-Security-Policy': [
            `default-src 'self' dotcraft-viewer: dotcraft-plugin:; script-src 'self' dotcraft-plugin:; style-src 'self' 'unsafe-inline' dotcraft-plugin:; img-src 'self' data: blob: file: dotcraft-viewer: dotcraft-plugin:; font-src 'self' data: dotcraft-plugin:; connect-src 'self' dotcraft-viewer:; frame-src 'self' ${MCP_APP_SANDBOX_SCHEME}:`
          ]
        }
      })
    })
  }

  let workspacePath = resolveInitialWorkspacePath(sharedSettings)

  const initialActiveStack =
    workspacePath && resolveConnectionMode(sharedSettings) === 'remote'
      ? resolveActiveRemoteStack(sharedSettings)
      : null

  // Publish best-effort activation metadata only for a local foreground project.
  // This is not an exclusive workspace gate; AppServer supports multiple Desktop clients.
  if (workspacePath) {
    if (!initialActiveStack) {
      acquireWorkspaceLock(workspacePath)
    }
    if (isDefaultChatWorkspace(workspacePath)) {
      sharedSettings.lastForegroundEntry = 'chats'
      saveSettings(sharedSettings)
    } else {
      addRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
    }
  }
  setActiveRemoteProject(initialActiveStack
    ? buildServersRemoteProject(initialActiveStack.host, initialActiveStack.stack, undefined, workspacePath ?? '')
    : null)
  const initialWorkspaceStatus = getWorkspaceStatusForRenderer(workspacePath)
  lastWorkspaceStatus = initialWorkspaceStatus
  const win = createWindow(workspacePath, initialWorkspaceStatus)
  mainWindow = win
  currentWorkspacePath = workspacePath ?? ''
  if (!initialActiveStack) {
    ensureWorkspaceActivation(workspacePath ?? '')
  }
  setViewerWorkspaceRoot(workspacePath ?? '')

  registerDesktopIpcHandlers(workspacePath ?? '', () => wireClient)

  if (import.meta.env.DEV) {
    win.loadURL('http://localhost:5173')
    win.webContents.once('did-finish-load', () => {
      win.webContents.openDevTools()
    })
  } else {
    const rendererPath = join(__dirname, '../renderer/index.html')
    win.loadFile(rendererPath)
  }

  win.webContents.once('did-finish-load', () => {
    emitWorkspaceStatus(win, initialWorkspaceStatus)
    scheduleInitialUpdateCheck()
    if (pendingChromeSettingsDeepLink) {
      openChromeSettingsFromDeepLink()
    }
    if (workspacePath && initialWorkspaceStatus.status === 'ready') {
      connectWorkspaceForLoadedWindow(win, workspacePath)
    } else {
      emitConnectionStatus(win, { status: 'disconnected' })
    }
  })

  win.on('focus', () => {
    if (workspaceActivation) {
      publishWorkspaceActivation(workspaceActivation.handle.endpoint)
    }
  })

  app.on('activate', () => {
    const windows = BrowserWindow.getAllWindows()
    if (windows.length === 0) {
      sharedSettings = loadSettings()
      let wsPath = resolveInitialWorkspacePath(sharedSettings)
      const activeStack =
        wsPath && resolveConnectionMode(sharedSettings) === 'remote'
          ? resolveActiveRemoteStack(sharedSettings)
          : null
      if (wsPath) {
        if (!activeStack) {
          acquireWorkspaceLock(wsPath)
        }
        if (isDefaultChatWorkspace(wsPath)) {
          sharedSettings.lastForegroundEntry = 'chats'
          saveSettings(sharedSettings)
        } else {
          addRecentWorkspace(sharedSettings, wsPath)
          saveSettings(sharedSettings)
        }
      }
      setActiveRemoteProject(activeStack
        ? buildServersRemoteProject(activeStack.host, activeStack.stack, undefined, wsPath ?? '')
        : null)
      const workspaceStatus = getWorkspaceStatusForRenderer(wsPath)
      lastWorkspaceStatus = workspaceStatus
      const newWin = createWindow(wsPath, workspaceStatus)
      mainWindow = newWin
      currentWorkspacePath = wsPath ?? ''
      if (!activeStack) {
        ensureWorkspaceActivation(wsPath ?? '')
      }

      if (wsPath) {
        reregisterIpcForWorkspace(wsPath)
      } else {
        registerDesktopIpcHandlers('', () => null)
      }

      if (import.meta.env.DEV) {
        newWin.loadURL('http://localhost:5173')
      } else {
        newWin.loadFile(join(__dirname, '../renderer/index.html'))
      }

      newWin.webContents.once('did-finish-load', () => {
        emitWorkspaceStatus(newWin, workspaceStatus)
        scheduleInitialUpdateCheck()
        if (pendingChromeSettingsDeepLink) {
          openChromeSettingsFromDeepLink()
        }
        if (wsPath && workspaceStatus.status === 'ready') {
          connectWorkspaceForLoadedWindow(newWin, wsPath)
        } else {
          emitConnectionStatus(newWin, { status: 'disconnected' })
        }
      })
    } else {
      showWindowSafely(windows[0]!)
    }
  })
})

app.on('window-all-closed', () => {
  if (isTrayMode) {
    return
  }

  if (process.platform === 'darwin') {
    void teardownRuntime('window-all-closed', {
      releaseWorkspaceLock: true,
      clearMainWindow: true,
      cleanupIpcHandlers: true
    })
    return
  }
  // Non-macOS exits via app.quit() -> before-quit for final cleanup.
  if (!isAppQuitting) {
    app.quit()
  }
})

app.on('before-quit', (event) => {
  if (isTrayMode) {
    return
  }

  isAppQuitting = true
  if (import.meta.env.DEV) {
    void stopTrayProcess()
  }
  stopChromeSettingsDeepLinkServer()
  if (mainWindow && !mainWindow.isDestroyed()) {
    viewerBrowserManager.destroyAllTabs(mainWindow)
  }
  if (finalQuitCleanupDone) {
    return
  }
  if (finalQuitCleanupRunning) {
    event.preventDefault()
    return
  }
  event.preventDefault()
  finalQuitCleanupRunning = true
  void teardownRuntime('before-quit', {
    releaseWorkspaceLock: true,
    clearMainWindow: true,
    cleanupIpcHandlers: true
  })
    .catch((error) => {
      console.warn('[desktop] failed to finish runtime cleanup before quit', error)
    })
    .finally(() => {
      finalQuitCleanupDone = true
      finalQuitCleanupRunning = false
      app.quit()
    })
})
