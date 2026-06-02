import { app, BrowserWindow, session, Menu, ipcMain, shell, nativeImage } from 'electron'
import {
  registerViewerScheme,
  installViewerProtocolHandler,
  setViewerWorkspaceRoot
} from './viewerFileProtocol'
import { viewerBrowserManager } from './viewerBrowser'
import { browserUseManager } from './browserUseManager'
import { nodeReplManager } from './nodeReplManager'
import { getGitHubIdentity } from './githubProfile'

// Register the custom viewer scheme as privileged BEFORE app.whenReady().
registerViewerScheme()
import type { IpcMainEvent, MenuItemConstructorOptions } from 'electron'
import { join, basename, resolve as resolvePath } from 'path'
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
  requestWorkspaceActivation,
  startWorkspaceActivationServer,
  type WorkspaceActivationHandle
} from './desktopActivation'
import {
  findWorkspaceOpenDeepLink,
  parseWorkspaceOpenDeepLink,
  type WorkspaceOpenDeepLink
} from './desktopDeepLink'
import { NO_WORKSPACE_ARG, resolveWorkspacePathFromArgs } from './workspaceArgs'
import {
  getWorkspaceStatus,
  runWorkspaceSetup,
  listSetupModels,
  type WorkspaceStatusPayload,
  type WorkspaceSetupRequest,
  type WorkspaceSetupModelListRequest
} from './workspaceSetup'
import { encodeInitialWorkspaceStatusArg } from '../shared/initialWorkspaceStatus'
import type { AddTabMenuRequest } from '../shared/addTabMenu'
import { getEnabledEmbeddedModuleChannelNames } from '../shared/channelModulePersistence'
import {
  popupAddTabMenuWindow,
  registerAddTabPopupWindowIpc,
  warmAddTabPopupWindow,
  type AddTabPopupWindowOptions
} from './addTabPopupWindow'
import { resolveInitialTheme, resolveWindowBackdropOptions } from './windowTheme'
import { WORKSPACE_LOCKED_IPC_PREFIX } from '../shared/workspaceSwitchErrors'
import {
  normalizeLocale,
  translate,
  type AppLocale,
  type TopLevelMenuId
} from '../shared/locales'
import { ensureTrayProcess, openDesktopWindow, runTrayProcess } from './trayManager'
import { configureAppIdentity } from './appIdentity'
import { resolveDotCraftRuntimeTools } from './ripgrepRuntime'
import { WhatsNewCatalog } from './whatsNewCatalog'
import { WhatsNewMediaCache, resolveWhatsNewMediaAssets } from './whatsNewMediaCache'
import type { WhatsNewMediaState } from '../shared/whatsNew'
import { AppUpdateService } from './appUpdate'
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
let lastRemoteStackLocalPort: number | null = null
let connectionGeneration = 0
let activeRemoteReconnectTimer: ReturnType<typeof setTimeout> | null = null
let activeRemoteReconnectAttempt = 0
let isAppQuitting = false
let ipcHandlersRegistered = false
let finalQuitCleanupDone = false
let finalQuitCleanupRunning = false
let hubEventAbortController: AbortController | null = null
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

configureAppIdentity()

function buildAddTabPopupWindowOptions(): AddTabPopupWindowOptions {
  return {
    isDev: import.meta.env.DEV,
    preloadPath: join(__dirname, '../preload/index.js'),
    rendererPopupIndexPath: join(__dirname, '../renderer/add-tab-popup.html'),
    rendererDevUrl: 'http://localhost:5173'
  }
}

function scheduleAddTabPopupWarmup(win: BrowserWindow, theme: 'dark' | 'light'): void {
  setTimeout(() => {
    if (win.isDestroyed()) return
    void warmAddTabPopupWindow(win, buildAddTabPopupWindowOptions(), theme).catch(() => {})
  }, 300)
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
  if (partial.locale !== undefined && normalizeLocale(sharedSettings.locale) !== prevLocale) {
    refreshAppMenu()
  }
}

browserUseManager.setPolicyHost({
  getSettings: () => sharedSettings,
  updateSettings: updateSharedSettings
})

function resolveWorkspacePath(settings: AppSettings): string | null {
  return resolveWorkspacePathFromArgs(settings, process.argv, existsSync)
}

function resolveConnectionMode(settings: AppSettings): ConnectionMode {
  const mode = settings.connectionMode
  return mode === 'remote' ? 'remote' : 'local'
}

function buildRemoteWorkspaceStatus(
  host: RemoteHost,
  stack: RemoteStack
): WorkspaceStatusPayload['remote'] {
  return {
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
  return activeRemoteWorkspace?.appServerWorkspacePath?.trim() || currentWorkspacePath
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
    restartMismatchedHub: import.meta.env.DEV
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
  clearActiveRemoteReconnectTimer()
  connectionGeneration += 1
  wireClient?.dispose()
  wireClient = null
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
  return resolvePath(a) === resolvePath(b)
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
}

function ensureWorkspaceActivation(workspacePath: string): void {
  const win = mainWindow
  if (!workspacePath || !win || win.isDestroyed()) return
  if (workspaceActivation?.workspacePath === workspacePath) {
    updateWorkspaceLockActivation(workspacePath, workspaceActivation.handle.endpoint)
    return
  }
  if (workspaceActivationStartingFor === workspacePath) return

  stopWorkspaceActivation()
  const generation = workspaceActivationGeneration
  workspaceActivationStartingFor = workspacePath
  void startWorkspaceActivationServer({
    workspacePath,
    getWindow: () => mainWindow,
    onActivate: (request) => {
      openCurrentWorkspaceThread(request.threadId)
    }
  }).then((handle) => {
    if (generation !== workspaceActivationGeneration || currentWorkspacePath !== workspacePath || isAppQuitting) {
      handle.close()
      return
    }
    workspaceActivation = { workspacePath, handle }
    updateWorkspaceLockActivation(workspacePath, handle.endpoint)
  }).catch((error) => {
    console.warn('[desktop] failed to start workspace activation server', error)
  }).finally(() => {
    if (workspaceActivationStartingFor === workspacePath) {
      workspaceActivationStartingFor = ''
    }
  })
}

async function activateExistingWorkspace(
  workspacePath: string,
  threadId: string | null | undefined,
  activation: WorkspaceActivationEndpoint | undefined
): Promise<boolean> {
  if (!activation) return false
  return await requestWorkspaceActivation(activation, {
    workspacePath,
    threadId: threadId ?? null
  })
}

function handleWorkspaceOpenDeepLink(link: WorkspaceOpenDeepLink): void {
  if (currentWorkspacePath && isSameWorkspacePath(currentWorkspacePath, link.workspacePath)) {
    openCurrentWorkspaceThread(link.threadId)
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
  const initialTheme = resolveInitialTheme(sharedSettings)
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
      additionalArguments: [
        `--dotcraft-initial-theme=${initialTheme}`,
        `--dotcraft-initial-locale=${initialLocale}`,
        encodeInitialWorkspaceStatusArg(initialWorkspaceStatus)
      ],
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const workspaceName = workspacePath ? basename(workspacePath) : 'DotCraft'
  win.setTitle(translate(initialLocale, 'app.titleWithWorkspace', { name: workspaceName }))

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
  win.on('maximize', sendMaximizedState)
  win.on('unmaximize', sendMaximizedState)
  win.on('enter-full-screen', sendMaximizedState)
  win.on('leave-full-screen', sendMaximizedState)

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
  activeRemoteWorkspace = null
  lastRemoteStackLocalPort = null
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
  clearActiveRemoteReconnectTimer()
  const manager = getRemoteServersManager()
  manager.closeStackTunnels(hostId, stackId)
  const active = sharedSettings.activeRemoteStack
  if (active?.hostId !== hostId || active.stackId !== stackId) {
    return
  }

  activeRemoteWorkspace = null
  lastRemoteStackLocalPort = null
  await updateSharedSettings({
    connectionMode: 'local',
    activeRemoteStack: undefined
  })
  if (currentWorkspacePath) {
    if (mainWindow && !mainWindow.isDestroyed()) {
      emitCurrentWorkspaceStatus(currentWorkspacePath)
      emitConnectionStatus(mainWindow, { status: 'disconnected' })
    }
    const workspacePath = currentWorkspacePath
    void connectToAppServer(workspacePath).catch((error) => {
      const message = error instanceof Error ? error.message : String(error)
      if (mainWindow && !mainWindow.isDestroyed()) {
        emitConnectionStatus(mainWindow, { status: 'error', errorMessage: message })
      }
    })
  } else if (mainWindow && !mainWindow.isDestroyed()) {
    emitCurrentWorkspaceStatus('')
    emitConnectionStatus(mainWindow, { status: 'disconnected' })
  }
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
  wireClient = client
  const isCurrentClient = (): boolean =>
    !isAppQuitting &&
    wireClient === client &&
    connectionGeneration === generation &&
    mainWindow === win &&
    !win.isDestroyed()

  client.onNotification((method, params) => {
    if (isCurrentClient() && mainWindow && !mainWindow.isDestroyed()) {
      broadcastNotification(mainWindow, method, params, sharedSettings)
    }
  })

  client.onServerRequest(async (method, params) => {
    if (!isCurrentClient()) return undefined
    const handledInMain = await handleServerRequestInMain(method, params)
    if (handledInMain !== undefined) return handledInMain
    const win = mainWindow!
    const { bridgeId, promise } = createServerRequestBridge()
    broadcastServerRequest(win, { bridgeId, method, params }, sharedSettings)
    return promise
  })
  let connectedOnce = false
  let initialFailureEmitted = false
  const emitInitialConnectionFailure = (
    message: string,
    errorType: ConnectionErrorType,
    stage: string
  ): void => {
    if (initialFailureEmitted || !isCurrentClient()) return
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
    client.dispose()
  }
  const emitConnected = (result: InitializeResult): void => {
    if (!isCurrentClient()) return
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
    }
    if (!options.remoteDiagnostic && !activeRemoteWorkspace && resolveConnectionMode(sharedSettings) === 'local') {
      void autoStartEnabledModules()
    }
  }
  client.on('ready', (result: InitializeResult) => emitConnected(result))
  client.on('reconnected', (result: InitializeResult) => emitConnected(result))
  client.on('close', () => {
    if (!isCurrentClient()) return
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
    if (!isCurrentClient()) return
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

function isCurrentWorkspaceEvent(event: HubEvent, workspacePath: string): boolean {
  if (!event.workspacePath) return false
  return resolvePath(event.workspacePath) === resolvePath(workspacePath)
}

function startHubEventSubscription(workspacePath: string, hubClient: HubClient): void {
  hubEventAbortController?.abort()
  const controller = new AbortController()
  hubEventAbortController = controller

  void hubClient.subscribeEvents((event) => {
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
      broadcastNotification(mainWindow, data?.kind ?? 'hub/notification', data ?? {}, sharedSettings)
    }
  }, controller.signal).catch((error) => {
    if (!controller.signal.aborted) {
      console.warn('[desktop] Hub event subscription ended', error)
    }
  })
}

// ─── AppServer connection ─────────────────────────────────────────────────────

function buildCallbacks(): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: async (newPath: string) => {
      if (mainWindow && !mainWindow.isDestroyed()) {
        viewerBrowserManager.destroyAllTabs(mainWindow)
      }
      setViewerWorkspaceRoot(newPath)
      addRecentWorkspace(sharedSettings, newPath)
      saveSettings(sharedSettings)
      const workspaceStatus = getWorkspaceStatus(newPath)
      if (workspaceStatus.status === 'needs-setup') {
        await openWorkspaceWithoutConnection(newPath)
      } else {
        await connectToAppServer(newPath)
      }
      if (mainWindow && !mainWindow.isDestroyed()) {
        const loc = normalizeLocale(sharedSettings.locale)
        mainWindow.setTitle(
          translate(loc, 'app.titleWithWorkspace', { name: basename(newPath) })
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
      return listSetupModels(request)
    },
    onOpenNewWindow: () => {
      openNewProcess()
    },
    onRestartManagedAppServer: async () => {
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
    },
    onApplyConnectionSettings: applyConnectionSettings,
    onConnectRemoteStack: connectRemoteStackFromServers,
    onDisconnectRemoteStack: disconnectRemoteStackFromServers,
    getSettings: () => sharedSettings,
    updateSettings: async (partial) => {
      await updateSharedSettings(partial)
    },
    getAppServerWsConfig: () => lastAppServerWsUrl ? { wsUrl: lastAppServerWsUrl } : null,
    getRecentWorkspaces: () => getRecentWorkspaces(sharedSettings),
    clearRecentWorkspaces: () => {
      clearRecentWorkspaces(sharedSettings)
      saveSettings(sharedSettings)
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

  const lockResult = acquireWorkspaceLock(workspacePath)
  if (!lockResult.ok) {
    const loc = normalizeLocale(sharedSettings.locale)
    throw new Error(
      WORKSPACE_LOCKED_IPC_PREFIX +
        translate(loc, 'main.error.workspaceLocked', { pid: lockResult.pid ?? 0 })
    )
  }

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
  delete sharedSettings.lastWorkspacePath
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

async function connectToAppServer(workspacePath: string): Promise<void> {
  if (isAppQuitting) {
    return
  }
  // Acquire the lock BEFORE tearing anything down so a failure leaves the
  // current connection intact and propagates as an exception to the caller
  // (e.g. the renderer's workspace:switch IPC).
  const lockResult = acquireWorkspaceLock(workspacePath)
  if (!lockResult.ok) {
    const loc = normalizeLocale(sharedSettings.locale)
    throw new Error(
      WORKSPACE_LOCKED_IPC_PREFIX +
        translate(loc, 'main.error.workspaceLocked', { pid: lockResult.pid ?? 0 })
    )
  }

  // Release lock on previous workspace after the new lock is secured
  if (currentWorkspacePath && currentWorkspacePath !== workspacePath) {
    stopWorkspaceActivation()
    releaseWorkspaceLock(currentWorkspacePath)
  }

  // Tear down previous connection
  await teardownRuntime('switch/reconnect before new connect')

  currentWorkspacePath = workspacePath
  ensureWorkspaceActivation(workspacePath)

  const remoteIdx = process.argv.indexOf('--remote')
  const launchedWithRemoteUrl = remoteIdx !== -1 && Boolean(process.argv[remoteIdx + 1])
  const connectionMode = resolveConnectionMode(sharedSettings)
  const activeStack =
    !launchedWithRemoteUrl && connectionMode === 'remote'
      ? resolveActiveRemoteStack(sharedSettings)
      : null
  activeRemoteWorkspace = activeStack ? buildRemoteWorkspaceStatus(activeStack.host, activeStack.stack) : null
  lastRemoteStackLocalPort = null
  emitCurrentWorkspaceStatus(workspacePath)

  // --remote ws://host:port/ws?token=xxx  → skip AppServerManager, connect via WebSocket
  if (launchedWithRemoteUrl) {
    activeRemoteWorkspace = null
    emitCurrentWorkspaceStatus(workspacePath)
    await connectViaWebSocket(workspacePath, process.argv[remoteIdx + 1], {
      initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
      initialDisconnectIsError: true,
      remoteDiagnostic: { stage: 'cli-remote' }
    })
    return
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
        await connectViaWebSocket(workspacePath, result.wsUrl, {
          autoReconnect: false,
          initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
          initialDisconnectIsError: true,
          remoteDiagnostic: {
            stage: 'active-remote-stack',
            hostName: activeStack.host.name,
            stackName: activeStack.stack.name,
            localPort: result.localPort,
            targetPort: activeStack.stack.appServerPort,
            tokenPresent: result.tokenPresent
          }
        })
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
      return
    }

    if (sharedSettings.activeRemoteStack?.hostId || sharedSettings.activeRemoteStack?.stackId) {
      const win = mainWindow!
      emitConnectionStatus(win, {
        status: 'error',
        errorMessage: 'Saved remote stack was not found. Check Servers settings or disconnect this stack.',
        errorType: 'remote-config-invalid'
      })
      return
    }

    activeRemoteWorkspace = null
    emitCurrentWorkspaceStatus(workspacePath)
    const remoteConfig = resolveRemoteWebSocketConfig(sharedSettings.remote)
    if (!remoteConfig.ok) {
      const win = mainWindow!
      emitConnectionStatus(win, {
        status: 'error',
        errorMessage: remoteConfig.message,
        errorType: 'remote-config-invalid'
      })
      return
    }
    await connectViaWebSocket(workspacePath, remoteConfig.connectUrl, {
      initializeTimeoutMs: REMOTE_INITIALIZE_TIMEOUT_MS,
      initialDisconnectIsError: true,
      remoteDiagnostic: { stage: 'manual-remote' }
    })
    return
  }

  const win = mainWindow!
  emitConnectionStatus(win, { status: 'connecting' })

  reregisterIpcForWorkspace(workspacePath)
  try {
    const hubClient = createHubClient(sharedSettings)
    const ensured = await hubClient.ensureAppServer(workspacePath, {
      runtimeTools: resolveDotCraftRuntimeTools()
    })
    if (currentWorkspacePath !== workspacePath || isAppQuitting) return

    startHubEventSubscription(workspacePath, hubClient)
    await connectViaWebSocket(workspacePath, getManagedAppServerEndpoint(ensured))
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
        { role: 'toggleDevTools' },
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
  ipcMain.removeHandler('menu:popup-add-tab')
  ipcMain.removeHandler('app:whats-new-get-releases')
  ipcMain.removeHandler('app:whats-new-get-media-states')
  ipcMain.removeHandler('app:whats-new-prefetch-media')
  ipcMain.removeHandler('app:update-get-state')
  ipcMain.removeHandler('app:update-check')
  ipcMain.removeHandler('app:update-download-and-install')
  registerAddTabPopupWindowIpc()
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
  ipcMain.handle(
    'menu:popup-add-tab',
    async (event, payload: AddTabMenuRequest) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return null
      return popupAddTabMenuWindow(win, payload, buildAddTabPopupWindowOptions())
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
  if (isTrayMode) {
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
  registerMenuPopupIpc()
  sharedSettings = loadSettings()
  refreshAppMenu()
  try {
    ensureTrayProcess()
  } catch (error) {
    console.warn('[desktop] failed to ensure tray process', error)
  }

  if (!import.meta.env.DEV) {
    session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          'Content-Security-Policy': [
            "default-src 'self' dotcraft-viewer:; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: file: dotcraft-viewer:; font-src 'self' data:; connect-src 'self' dotcraft-viewer:"
          ]
        }
      })
    })
  }

  const initialWorkspaceOpenDeepLink = findWorkspaceOpenDeepLink(process.argv)
  let workspacePath = resolveWorkspacePath(sharedSettings)

  // If another process is already using this workspace, start without one
  // so the user sees the welcome screen and can pick a different workspace.
  if (workspacePath) {
    const lockCheck = acquireWorkspaceLock(workspacePath)
    if (!lockCheck.ok) {
      if (
        initialWorkspaceOpenDeepLink &&
        isSameWorkspacePath(initialWorkspaceOpenDeepLink.workspacePath, workspacePath) &&
        await activateExistingWorkspace(workspacePath, initialWorkspaceOpenDeepLink.threadId, lockCheck.activation)
      ) {
        app.quit()
        return
      }
      workspacePath = null
    } else {
      addRecentWorkspace(sharedSettings, workspacePath)
      saveSettings(sharedSettings)
    }
  }

  const initialActiveStack =
    workspacePath && resolveConnectionMode(sharedSettings) === 'remote'
      ? resolveActiveRemoteStack(sharedSettings)
      : null
  activeRemoteWorkspace = initialActiveStack
    ? buildRemoteWorkspaceStatus(initialActiveStack.host, initialActiveStack.stack)
    : null
  const initialWorkspaceStatus = getWorkspaceStatusForRenderer(workspacePath)
  lastWorkspaceStatus = initialWorkspaceStatus
  const win = createWindow(workspacePath, initialWorkspaceStatus)
  mainWindow = win
  currentWorkspacePath = workspacePath ?? ''
  if (workspacePath) {
    ensureWorkspaceActivation(workspacePath)
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
    scheduleAddTabPopupWarmup(win, resolveInitialTheme(sharedSettings))
    if (pendingChromeSettingsDeepLink) {
      openChromeSettingsFromDeepLink()
    }
    if (workspacePath && initialWorkspaceStatus.status === 'ready') {
      connectWorkspaceForLoadedWindow(win, workspacePath)
    } else {
      emitConnectionStatus(win, { status: 'disconnected' })
    }
  })

  app.on('activate', () => {
    const windows = BrowserWindow.getAllWindows()
    if (windows.length === 0) {
      sharedSettings = loadSettings()
      let wsPath = resolveWorkspacePath(sharedSettings)
      if (wsPath) {
        const lockCheck = acquireWorkspaceLock(wsPath)
        if (!lockCheck.ok) {
          wsPath = null
        } else {
          addRecentWorkspace(sharedSettings, wsPath)
          saveSettings(sharedSettings)
        }
      }
      const activeStack =
        wsPath && resolveConnectionMode(sharedSettings) === 'remote'
          ? resolveActiveRemoteStack(sharedSettings)
          : null
      activeRemoteWorkspace = activeStack
        ? buildRemoteWorkspaceStatus(activeStack.host, activeStack.stack)
        : null
      const workspaceStatus = getWorkspaceStatusForRenderer(wsPath)
      lastWorkspaceStatus = workspaceStatus
      const newWin = createWindow(wsPath, workspaceStatus)
      mainWindow = newWin
      currentWorkspacePath = wsPath ?? ''
      if (wsPath) {
        ensureWorkspaceActivation(wsPath)
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
        scheduleAddTabPopupWarmup(newWin, resolveInitialTheme(sharedSettings))
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
