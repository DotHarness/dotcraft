import { contextBridge, ipcRenderer, shell, webUtils } from 'electron'
import { resolveThemeMode, type ThemeMode } from '../shared/theme'
import { readInitialWorkspaceStatusFromArgv } from '../shared/initialWorkspaceStatus'
import type { DesktopProviderProtocol } from '../shared/providerProtocols'
import { localeToHtmlLang, normalizeLocale, type AppLocale } from '../shared/locales'
import type {
  RemoteHost,
  RemoteStack,
  RemoteStackStatus,
  RemoteStackAction,
  SshTestResult,
  OperationResult,
  LocalSshConfigInfo,
  DiscoveredStack
} from '../shared/remoteServers'
import {
  TITLE_BAR_OVERLAY_HEIGHT,
  TITLE_BAR_OVERLAY_RIGHT_RESERVE
} from '../shared/titleBarOverlay'
import type { TopLevelMenuId } from '../shared/locales/types'
import type {
  BrowserUseApprovalRequestPayload,
  BrowserUseClosePayload,
  BrowserUseOpenPayload,
  BrowserEventPayload,
  TerminalDataEventPayload,
  TerminalExitEventPayload
} from '../shared/viewer/types'
import type {
  MarketInstallResult,
  MarketDotCraftInstallPreparation,
  MarketSkillDetail,
  SkillMarketBindDotCraftInstallRequest,
  SkillMarketCleanupDotCraftInstallRequest,
  SkillMarketDetailRequest,
  SkillMarketInstallRequest,
  SkillMarketPrepareDotCraftInstallRequest,
  SkillMarketSearchRequest,
  SkillMarketSearchResult
} from '../shared/skillMarket'
import type { WhatsNewMediaState, WhatsNewRelease } from '../shared/whatsNew'
import type { AppUpdateState } from '../shared/appUpdate'
import type { ConnectionSettingsDraft } from '../shared/remoteConnection'
import type { WorkspaceProjectsPayload } from '../shared/workspaceProjects'
import { TokenMulticastDispatcher } from './notificationDispatcher'

export type UnsubscribeFn = () => void
export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type BrowserUseApprovalMode = 'alwaysAsk' | 'askUnknown' | 'neverAsk'
export type TaskCompletionNotificationMode = 'whenUnfocused' | 'always' | 'never'
export type BrowserUseApprovalResponseAction = 'allowOnce' | 'allowDomain' | 'blockDomain' | 'deny'
export type WorkspaceSetupState = 'no-workspace' | 'needs-setup' | 'ready'
export type WorkspaceBootstrapProfile = 'default' | 'developer' | 'personal-assistant'
export type WorkspaceSetupProviderProtocol = DesktopProviderProtocol
export type WorkspaceSetupProviderMode = 'existing' | 'create' | 'skip'
export type WorkspaceSetupBootstrapImportSourceId = 'codex' | 'claude'
export type EditorId =
  | 'explorer'
  | 'vs'
  | 'cursor'
  | 'vscode'
  | 'rider'
  | 'webstorm'
  | 'idea'
  | 'github-desktop'
  | 'git-bash'
  | 'terminal'

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

function readInitialTheme(): ThemeMode {
  const arg = process.argv.find((value) => value.startsWith('--dotcraft-initial-theme='))
  const raw = arg?.slice('--dotcraft-initial-theme='.length)
  return resolveThemeMode(raw)
}

function readInitialLocale(): AppLocale {
  const arg = process.argv.find((value) => value.startsWith('--dotcraft-initial-locale='))
  const raw = arg?.slice('--dotcraft-initial-locale='.length)
  return normalizeLocale(raw)
}

const initialTheme = readInitialTheme()
const initialLocale = readInitialLocale()
const initialWorkspaceStatus = readInitialWorkspaceStatusFromArgv(process.argv) as WorkspaceStatusPayload

function applyInitialDocumentState(): void {
  const root = document.documentElement
  if (!root) return
  root.setAttribute('data-theme', initialTheme)
  root.lang = localeToHtmlLang(initialLocale)
}

if (typeof document !== 'undefined') {
  applyInitialDocumentState()
  if (!document.documentElement) {
    document.addEventListener('DOMContentLoaded', applyInitialDocumentState, { once: true })
  }
}

export interface NotificationPayload {
  method: string
  params: unknown
  workspacePath?: string
  foreground?: boolean
}

export interface ConnectionStatusPayload {
  status: 'connecting' | 'connected' | 'disconnected' | 'error'
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  dashboardUrl?: string
  errorMessage?: string
  errorType?: 'binary-not-found' | 'handshake-timeout' | 'crash' | 'remote-config-invalid'
  binarySource?: BinarySource
}

export interface ResolvedBinaryPayload {
  source: BinarySource
  path: string | null
}

export interface ChromeSetupStatus {
  extension: unknown
  nativeHost: unknown
  chromeRunning: unknown
  installedBrowsers: unknown
  backend?: unknown
  bridge: unknown
}

export type ConfigReloadBehavior = 'processRestart' | 'subsystemRestart' | 'hot' | string

export interface WorkspaceConfigSchemaField {
  key: string
  displayName?: string
  type: string
  sensitive: boolean
  options?: string[]
  min?: number
  max?: number
  hint?: string
  defaultValue?: unknown
  reload?: ConfigReloadBehavior
  subsystemKey?: string
}

export interface WorkspaceConfigSchemaSection {
  section: string
  order: number
  path?: string[]
  rootKey?: string
  itemFields?: WorkspaceConfigSchemaField[]
  fields: WorkspaceConfigSchemaField[]
}

export interface WorkspaceConfigSchema {
  sections: WorkspaceConfigSchemaSection[]
}

export interface ServerRequestPayload {
  bridgeId: string
  method: string
  params: unknown
}

export interface OpenThreadPayload {
  threadId: string
}

export interface WorkspaceStatusPayload {
  status: WorkspaceSetupState
  workspacePath: string
  hasUserConfig: boolean
  userConfigDefaults?: {
    providerId?: string
    model?: string
  }
  providers: WorkspaceSetupProviderSummary[]
  bootstrapImportSources?: WorkspaceSetupBootstrapImportSource[]
  remote?: RemoteWorkspaceStatusPayload
}

export interface RemoteWorkspaceStatusPayload {
  source?: 'servers' | 'manual' | 'cli'
  projectId?: string
  displayName?: string
  endpoint?: string
  hostId?: string
  stackId?: string
  serverName?: string
  stackName?: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  composeDir?: string
  projectName?: string
}

export interface WorkspaceSetupBootstrapImportSource {
  id: WorkspaceSetupBootstrapImportSourceId
  fileName: 'AGENTS.md' | 'CLAUDE.md'
  path: string
  relativePath: string
}

export interface WorkspaceSetupProviderSummary {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
}

export interface WorkspaceSetupProviderDraft {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  apiKey: string
  endPoint: string
  networkTimeoutSeconds?: number | null
}

export interface WorkspaceSetupRequest {
  model: string
  profile: WorkspaceBootstrapProfile
  providerMode: WorkspaceSetupProviderMode
  providerId?: string
  provider?: WorkspaceSetupProviderDraft
  setAsUserDefault: boolean
  bootstrapImportSourceId?: WorkspaceSetupBootstrapImportSourceId | null
}

export interface WorkspaceSetupRunResult {
  bootstrapImport?: {
    sourceId: WorkspaceSetupBootstrapImportSourceId
    status: 'success' | 'failed'
    warning?: string
  }
}

export type WorkspaceSetupModelListRequest =
  | { providerId: string }
  | { provider: WorkspaceSetupProviderDraft }

export type WorkspaceSetupModelListResult =
  | { kind: 'success'; models: string[] }
  | { kind: 'unsupported' }
  | { kind: 'missing-key' }
  | { kind: 'error' }

export interface ConfigDescriptorWire {
  key: string
  displayLabel: string
  description: string
  localizedDisplayLabel?: Partial<Record<AppLocale, string>>
  localizedDescription?: Partial<Record<AppLocale, string>>
  required: boolean
  dataKind: string
  masked: boolean
  interactiveSetupOnly: boolean
  advanced?: boolean
  defaultValue?: unknown
  enumValues?: string[]
}

export interface ModuleInterfaceWire {
  shortDescription?: string
  localizedShortDescription?: Partial<Record<AppLocale, string>>
  longDescription?: string
  localizedLongDescription?: Partial<Record<AppLocale, string>>
  previewPrompt?: string
  localizedPreviewPrompt?: Partial<Record<AppLocale, string>>
}

export interface DiscoveredModule {
  moduleId: string
  channelName: string
  displayName: string
  localizedDisplayName?: Partial<Record<AppLocale, string>>
  interface?: ModuleInterfaceWire
  packageName: string
  configFileName: string
  supportedTransports: string[]
  requiresInteractiveSetup: boolean
  capabilitySummary?: Record<string, unknown>
  variant: string
  source: 'bundled' | 'user'
  absolutePath: string
  configDescriptors: ConfigDescriptorWire[]
}

export interface ModuleStatusEntry {
  processState: 'starting' | 'running' | 'stopping' | 'stopped' | 'crashed'
  connected: boolean
  restartCount: number
  lastExitCode: number | null
  lastStderrExcerpt?: string[]
  crashHint?: string
}

export type ModuleStatusMap = Record<string, ModuleStatusEntry>

export interface QrUpdatePayload {
  moduleId: string
  qrDataUrl: string | null
  timestamp: number
}

export interface ModulesRescanSummaryPayload {
  addedModuleIds: string[]
  removedModuleIds: string[]
  changedModuleIds: string[]
  changedRunningModuleIds: string[]
}

export interface PinnedThreadIdsChangedPayload {
  workspacePath: string
  threadIds: string[]
}

// ---------------------------------------------------------------------------
// Single-listener dispatchers for notifications and connection status.
//
// Instead of registering one ipcRenderer.on per subscriber (which can
// accumulate stale listeners when React StrictMode mounts/unmounts/remounts
// components), we keep exactly ONE ipcRenderer listener per channel and
// dispatch locally.
// ---------------------------------------------------------------------------

// Notification dispatch supports multiple renderer subscribers. Other channels
// below remain single-slot because they represent one owning surface or responder.
//
// contextBridge wraps functions in new Proxy objects on every call, making
// reference equality (=== or Set/Map) unreliable across the bridge boundary,
// so registrations are tracked with monotonically-increasing tokens.

const notificationDispatcher = new TokenMulticastDispatcher<NotificationPayload>((error) => {
  console.error('appserver:notification subscriber failed:', error)
})
ipcRenderer.on(
  'appserver:notification',
  (_event: Electron.IpcRendererEvent, payload: NotificationPayload) => {
    notificationDispatcher.dispatch(payload)
  }
)

let connectionStatusToken = 0
let activeConnectionStatusCallback: ((status: ConnectionStatusPayload) => void) | null = null
ipcRenderer.on(
  'appserver:connection-status',
  (_event: Electron.IpcRendererEvent, status: ConnectionStatusPayload) => {
    activeConnectionStatusCallback?.(status)
  }
)

let serverRequestToken = 0
let activeServerRequestCallback: ((payload: ServerRequestPayload) => void) | null = null
ipcRenderer.on(
  'appserver:server-request',
  (_event: Electron.IpcRendererEvent, payload: ServerRequestPayload) => {
    activeServerRequestCallback?.(payload)
  }
)

let workspaceStatusToken = 0
let activeWorkspaceStatusCallback: ((status: WorkspaceStatusPayload) => void) | null = null
ipcRenderer.on(
  'workspace:status-changed',
  (_event: Electron.IpcRendererEvent, status: WorkspaceStatusPayload) => {
    activeWorkspaceStatusCallback?.(status)
  }
)

let workspaceProjectsToken = 0
let activeWorkspaceProjectsCallback: ((payload: WorkspaceProjectsPayload) => void) | null = null
ipcRenderer.on(
  'workspace:projects-changed',
  (_event: Electron.IpcRendererEvent, payload: WorkspaceProjectsPayload) => {
    activeWorkspaceProjectsCallback?.(payload)
  }
)

let moduleStatusToken = 0
let activeModuleStatusCallback: ((statusMap: ModuleStatusMap) => void) | null = null
ipcRenderer.on(
  'modules:status-changed',
  (_event: Electron.IpcRendererEvent, statusMap: ModuleStatusMap) => {
    activeModuleStatusCallback?.(statusMap)
  }
)

let moduleQrUpdateToken = 0
let activeModuleQrUpdateCallback: ((payload: QrUpdatePayload) => void) | null = null
ipcRenderer.on(
  'modules:qr-update',
  (_event: Electron.IpcRendererEvent, payload: QrUpdatePayload) => {
    activeModuleQrUpdateCallback?.(payload)
  }
)

let moduleRescanSummaryToken = 0
let activeModuleRescanSummaryCallback: ((payload: ModulesRescanSummaryPayload) => void) | null = null
ipcRenderer.on(
  'modules:rescan-summary',
  (_event: Electron.IpcRendererEvent, payload: ModulesRescanSummaryPayload) => {
    activeModuleRescanSummaryCallback?.(payload)
  }
)

let openChromeSettingsToken = 0
let activeOpenChromeSettingsCallback: (() => void) | null = null
ipcRenderer.on('app:open-chrome-settings', () => {
  activeOpenChromeSettingsCallback?.()
})

let openWhatsNewToken = 0
let activeOpenWhatsNewCallback: (() => void) | null = null
ipcRenderer.on('app:open-whats-new', () => {
  activeOpenWhatsNewCallback?.()
})

let whatsNewMediaStateToken = 0
let activeWhatsNewMediaStateCallback: ((state: WhatsNewMediaState) => void) | null = null
ipcRenderer.on('app:whats-new-media-state-changed', (_event: Electron.IpcRendererEvent, state: WhatsNewMediaState) => {
  activeWhatsNewMediaStateCallback?.(state)
})

let appUpdateStateToken = 0
let activeAppUpdateStateCallback: ((state: AppUpdateState) => void) | null = null
ipcRenderer.on('app:update-state-changed', (_event: Electron.IpcRendererEvent, state: AppUpdateState) => {
  activeAppUpdateStateCallback?.(state)
})

let openThreadToken = 0
let activeOpenThreadCallback: ((payload: OpenThreadPayload) => void) | null = null
ipcRenderer.on('app:open-thread', (_event: Electron.IpcRendererEvent, payload: OpenThreadPayload) => {
  activeOpenThreadCallback?.(payload)
})

let pinnedThreadIdsChangedToken = 0
let activePinnedThreadIdsChangedCallback: ((payload: PinnedThreadIdsChangedPayload) => void) | null = null
ipcRenderer.on(
  'settings:pinned-thread-ids-changed',
  (_event: Electron.IpcRendererEvent, payload: PinnedThreadIdsChangedPayload) => {
    activePinnedThreadIdsChangedCallback?.(payload)
  }
)

let maximizedChangeToken = 0
let activeMaximizedChangeCallback: ((maximized: boolean) => void) | null = null
ipcRenderer.on('window:maximized-change', (_event: Electron.IpcRendererEvent, maximized: boolean) => {
  activeMaximizedChangeCallback?.(maximized)
})

/**
 * Typed API exposed to the Renderer via contextBridge.
 * The Renderer accesses this as `window.api`.
 */
const api = {
  platform: process.platform as 'darwin' | 'win32' | 'linux',

  initialTheme,

  initialLocale,

  initialWorkspaceStatus,

  titleBarOverlayHeight: TITLE_BAR_OVERLAY_HEIGHT,

  /** Matches CustomMenuBar / ToastContainer right inset on Windows / Linux. */
  titleBarOverlayRightReserve: TITLE_BAR_OVERLAY_RIGHT_RESERVE,

  menu: {
    popupTopLevel(menuId: TopLevelMenuId, x: number, y: number): Promise<void> {
      return ipcRenderer.invoke('menu:popup-top-level', { menuId, x, y })
    }
  },

  appServer: {
    /**
     * Sends a JSON-RPC request to the AppServer via Main Process.
     * Returns the result or throws on error.
     */
    sendRequest(method: string, params?: unknown, timeoutMs?: number): Promise<unknown> {
      return ipcRenderer.invoke('appserver:send-request', method, params, timeoutMs)
    },

    listModels(): Promise<unknown> {
      return ipcRenderer.invoke('appserver:model-list')
    },

    requestWorkspaceConfigSchema(): Promise<WorkspaceConfigSchema | null> {
      return ipcRenderer.invoke('appserver:workspace-config-schema')
    },

    /**
     * Returns the latest connection status snapshot from Main Process.
     * This avoids missing early status events during renderer bootstrap.
     */
    getConnectionStatus(): Promise<ConnectionStatusPayload> {
      return ipcRenderer.invoke('appserver:get-connection-status')
    },

    getResolvedBinary(request?: {
      binarySource?: BinarySource
      binaryPath?: string
    }): Promise<ResolvedBinaryPayload> {
      return ipcRenderer.invoke('appserver:resolved-binary', request)
    },

    pickBinary(): Promise<string | null> {
      return ipcRenderer.invoke('appserver:pick-binary')
    },

    restartManaged(): Promise<void> {
      return ipcRenderer.invoke('appserver:restart-managed')
    },

    applyConnectionSettings(draft: ConnectionSettingsDraft): Promise<void> {
      return ipcRenderer.invoke('appserver:apply-connection-settings', draft)
    },

    /**
     * Subscribes to Wire Protocol notifications forwarded from Main.
     * Returns an unsubscribe function.
     */
    onNotification(callback: (payload: NotificationPayload) => void): UnsubscribeFn {
      return notificationDispatcher.subscribe(callback)
    },

    /**
     * Subscribes to connection status changes.
     * Returns an unsubscribe function.
     */
    onConnectionStatus(callback: (status: ConnectionStatusPayload) => void): UnsubscribeFn {
      const token = ++connectionStatusToken
      activeConnectionStatusCallback = callback
      return () => {
        if (connectionStatusToken === token) activeConnectionStatusCallback = null
      }
    },

    /**
     * Subscribes to server-initiated requests (e.g. item/approval/request).
     * The callback receives a bridgeId that must be passed to sendServerResponse.
     */
    onServerRequest(callback: (payload: ServerRequestPayload) => void): UnsubscribeFn {
      const token = ++serverRequestToken
      activeServerRequestCallback = callback
      return () => {
        if (serverRequestToken === token) activeServerRequestCallback = null
      }
    },

    /**
     * Sends the user's decision for a server-initiated request back to Main.
     * Main will forward this as the JSON-RPC response to AppServer.
     */
    sendServerResponse(bridgeId: string, result: unknown): Promise<void> {
      return ipcRenderer.invoke('appserver:server-response', bridgeId, result)
    }
  },

  workspaceConfig: {
    getCore(): Promise<{
      workspace: {
        providerId: string | null
        model: string | null
        welcomeSuggestionsEnabled: boolean | null
        skillsSelfLearningEnabled: boolean | null
        memoryAutoConsolidateEnabled: boolean | null
        dreamsEnabled: boolean | null
        dreamsInterval: string | null
        dreamsThreadLookbackCount: number | null
        dreamsAutoApply: boolean | null
        defaultApprovalPolicy: 'default' | 'autoApprove' | null
      }
      userDefaults: {
        providerId: string | null
        model: string | null
        welcomeSuggestionsEnabled: boolean | null
        skillsSelfLearningEnabled: boolean | null
        memoryAutoConsolidateEnabled: boolean | null
        dreamsEnabled: boolean | null
        dreamsInterval: string | null
        dreamsThreadLookbackCount: number | null
        dreamsAutoApply: boolean | null
        defaultApprovalPolicy: 'default' | 'autoApprove' | null
      }
    }> {
      return ipcRenderer.invoke('workspace-config:get-core')
    }
  },

  skillMarket: {
    search(request: SkillMarketSearchRequest): Promise<SkillMarketSearchResult> {
      return ipcRenderer.invoke('skill-market:search', request)
    },
    detail(request: SkillMarketDetailRequest): Promise<MarketSkillDetail> {
      return ipcRenderer.invoke('skill-market:detail', request)
    },
    install(request: SkillMarketInstallRequest): Promise<MarketInstallResult> {
      return ipcRenderer.invoke('skill-market:install', request)
    },
    prepareDotCraftInstall(
      request: SkillMarketPrepareDotCraftInstallRequest
    ): Promise<MarketDotCraftInstallPreparation> {
      return ipcRenderer.invoke('skill-market:prepare-dotcraft-install', request)
    },
    bindDotCraftInstall(request: SkillMarketBindDotCraftInstallRequest): Promise<void> {
      return ipcRenderer.invoke('skill-market:bind-dotcraft-install', request)
    },
    cleanupDotCraftInstall(request: SkillMarketCleanupDotCraftInstallRequest): Promise<void> {
      return ipcRenderer.invoke('skill-market:cleanup-dotcraft-install', request)
    }
  },

  window: {
    /**
     * Sets the window title (rendered in the OS title bar).
     */
    setTitle(title: string): void {
      ipcRenderer.invoke('window:set-title', title)
    },

    /**
     * Updates native title bar overlay colors to match app theme (no-op on macOS).
     */
    setTitleBarOverlayTheme(theme: 'dark' | 'light'): Promise<void> {
      return ipcRenderer.invoke('window:set-title-bar-overlay-theme', theme)
    },

    minimize(): Promise<void> {
      return ipcRenderer.invoke('window:minimize')
    },

    toggleMaximize(): Promise<boolean> {
      return ipcRenderer.invoke('window:toggle-maximize')
    },

    close(): Promise<void> {
      return ipcRenderer.invoke('window:close')
    },

    isMaximized(): Promise<boolean> {
      return ipcRenderer.invoke('window:is-maximized')
    },

    rendererReadyForShow(): void {
      ipcRenderer.send('window:renderer-ready-for-show')
    },

    onMaximizedChange(callback: (maximized: boolean) => void): () => void {
      const token = ++maximizedChangeToken
      activeMaximizedChangeCallback = callback
      return () => {
        if (maximizedChangeToken === token) {
          activeMaximizedChangeCallback = null
        }
      }
    },

    /**
     * Returns the workspace path for this window.
     */
    getWorkspacePath(): Promise<string> {
      return ipcRenderer.invoke('window:get-workspace-path')
    },

    onOpenChromeSettings(callback: () => void): () => void {
      const token = ++openChromeSettingsToken
      activeOpenChromeSettingsCallback = callback
      return () => {
        if (openChromeSettingsToken === token) {
          activeOpenChromeSettingsCallback = null
        }
      }
    },

    onOpenWhatsNew(callback: () => void): () => void {
      const token = ++openWhatsNewToken
      activeOpenWhatsNewCallback = callback
      return () => {
        if (openWhatsNewToken === token) {
          activeOpenWhatsNewCallback = null
        }
      }
    },

    onOpenThread(callback: (payload: OpenThreadPayload) => void): () => void {
      const token = ++openThreadToken
      activeOpenThreadCallback = callback
      return () => {
        if (openThreadToken === token) {
          activeOpenThreadCallback = null
        }
      }
    }
  },

  shell: {
    /**
     * Opens the given path in the system file explorer.
     */
    openPath(path: string): Promise<string> {
      return shell.openPath(path)
    },

    /**
     * Opens an allowed URL in the OS default handler (validated in the main process).
     */
    openExternal(url: string): Promise<void> {
      return ipcRenderer.invoke('shell:open-external', url)
    },

    openAppHandoff(url: string): Promise<void> {
      return ipcRenderer.invoke('shell:open-app-handoff', url)
    },

    getProtocolHandlerName(protocol: string): Promise<string> {
      return ipcRenderer.invoke('shell:get-protocol-handler-name', protocol)
    },

    listEditors(): Promise<EditorInfo[]> {
      return ipcRenderer.invoke('editors:list')
    },

    launchEditor(id: EditorId, targetPath: string): Promise<void> {
      return ipcRenderer.invoke('editors:launch', id, targetPath)
    },

    launchLocalPathInEditor(id: EditorId, targetPath: string): Promise<void> {
      return ipcRenderer.invoke('editors:launch-local-path', id, targetPath)
    },

    openLocalPath(path: string): Promise<void> {
      return ipcRenderer.invoke('shell:open-local-path', path)
    },

    revealLocalPath(path: string): Promise<void> {
      return ipcRenderer.invoke('shell:reveal-local-path', path)
    },

    showItemInFolder(path: string): Promise<void> {
      return ipcRenderer.invoke('shell:show-item-in-folder', path)
    }
  },

  profile: {
    /**
     * Resolves a public GitHub identity (display name + avatar data URL) for the
     * Profile page, fetched and cached in the main process. Returns null when the
     * username is invalid or unavailable.
     */
    getGithubIdentity(
      username: string
    ): Promise<{ login: string; name: string | null; avatarDataUrl: string | null } | null> {
      return ipcRenderer.invoke('profile:get-github-identity', username)
    }
  },

  chrome: {
    checkSetup(): Promise<ChromeSetupStatus> {
      return ipcRenderer.invoke('chrome:check-setup')
    },

    installNativeHost(): Promise<unknown> {
      return ipcRenderer.invoke('chrome:install-native-host')
    },

    openChrome(params?: { url?: string }): Promise<unknown> {
      return ipcRenderer.invoke('chrome:open', params)
    }
  },

  file: {
    /**
     * Writes content to the given absolute path (within workspace).
     */
    writeFile(absPath: string, content: string): Promise<void> {
      return ipcRenderer.invoke('file:write', absPath, content)
    },

    /**
     * Reads UTF-8 text from the given absolute path (within workspace).
     * Returns empty string if the file does not exist.
     */
    readFile(absPath: string): Promise<string> {
      return ipcRenderer.invoke('file:read', absPath)
    },

    /**
     * Deletes the file at the given absolute path (within workspace).
     */
    deleteFile(absPath: string): Promise<void> {
      return ipcRenderer.invoke('file:delete', absPath)
    },

    /**
     * Returns true when the given absolute path exists within the workspace.
     */
    exists(absPath: string): Promise<boolean> {
      return ipcRenderer.invoke('file:exists', absPath)
    }
  },

  git: {
    /**
     * Stages the given files and creates a commit with the provided message.
     * Returns the git output on success.
     */
    commit(workspacePath: string, files: string[], message: string): Promise<string> {
      return ipcRenderer.invoke('git:commit', workspacePath, files, message)
    },
    /**
     * Returns current branch name, detached short SHA, or null when unavailable.
     */
    getBranch(workspacePath: string): Promise<string | null> {
      return ipcRenderer.invoke('git:branch', workspacePath)
    },
    listBranches(workspacePath: string): Promise<{
      current: string | null
      detachedHead: string | null
      branches: Array<{ name: string; current: boolean }>
    }> {
      return ipcRenderer.invoke('git:listBranches', workspacePath)
    },
    checkoutBranch(workspacePath: string, branchName: string): Promise<void> {
      return ipcRenderer.invoke('git:checkoutBranch', workspacePath, branchName)
    },
    createAndCheckoutBranch(workspacePath: string, branchName: string): Promise<void> {
      return ipcRenderer.invoke('git:createAndCheckoutBranch', workspacePath, branchName)
    }
  },

  desktopExtensions: {
    authorizeExtension(params: { pluginId: string; rootPath: string; extensionId: string }): Promise<{ grantId: string }> {
      return ipcRenderer.invoke('desktop-extension:authorize-extension', params)
    },
    revokeExtension(params: { grantId: string }): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('desktop-extension:revoke-extension', params)
    },
    toPluginUrl(pluginId: string, absolutePath: string): Promise<{ url: string }> {
      return ipcRenderer.invoke('desktop-extension:to-plugin-url', { pluginId, absolutePath })
    },
    fetchJson(params: { grantId: string; url: string; timeoutMs?: number }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-extension:fetch-json', params)
    },
    postJson(params: { grantId: string; url: string; body?: unknown; timeoutMs?: number }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-extension:post-json', params)
    },
    getAppConnectionStatus(params: { grantId: string; appId: string }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-extension:app-connection-status', params)
    },
    startAppConnection(params: { grantId: string; appId: string }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-extension:app-connection-start', params)
    },
    openApp(params: { grantId: string; appId: string; url: string }): Promise<void> {
      return ipcRenderer.invoke('desktop-extension:app-open', params)
    }
  },

  workspace: {
    /**
     * Opens the native folder picker dialog.
     * Returns the selected path, or null if cancelled.
     */
    pickFolder(): Promise<string | null> {
      return ipcRenderer.invoke('workspace:pick-folder')
    },

    /**
     * Creates a brand-new local project folder under the user's Documents
     * directory, initializes it as a git repository, and returns its absolute
     * path. The renderer then switches to it, which runs the setup wizard.
     */
    createLocalProject(params: { name: string }): Promise<{ path: string; gitInitialized: boolean }> {
      return ipcRenderer.invoke('workspace:create-local-project', params)
    },

    /**
     * Opens the native file picker and returns selected local file paths,
     * including files outside the workspace.
     */
    pickFiles(): Promise<Array<{ path: string; fileName: string }>> {
      return ipcRenderer.invoke('workspace:pick-files')
    },

    getPathForFile(file: File): string {
      return webUtils.getPathForFile(file)
    },

    /**
     * Triggers a full workspace switch to the given path.
     * The Main process tears down the current AppServer and spawns a new one.
     */
    switch(newPath: string): Promise<void> {
      return ipcRenderer.invoke('workspace:switch', newPath)
    },

    clearSelection(): Promise<void> {
      return ipcRenderer.invoke('workspace:clear-selection')
    },

    /**
     * Returns the list of recently opened workspaces (up to 20).
     */
    getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>> {
      return ipcRenderer.invoke('workspace:get-recent')
    },

    getProjects(): Promise<WorkspaceProjectsPayload> {
      return ipcRenderer.invoke('workspace:get-projects')
    },

    removeRecent(path: string): Promise<void> {
      return ipcRenderer.invoke('workspace:remove-recent', path)
    },

    disconnectRemote(): Promise<void> {
      return ipcRenderer.invoke('workspace:disconnect-remote')
    },

    onProjectsChange(callback: (payload: WorkspaceProjectsPayload) => void): UnsubscribeFn {
      const token = ++workspaceProjectsToken
      activeWorkspaceProjectsCallback = callback
      return () => {
        if (workspaceProjectsToken === token) activeWorkspaceProjectsCallback = null
      }
    },

    clearRecent(): Promise<void> {
      return ipcRenderer.invoke('workspace:clear-recent')
    },

    getStatus(): Promise<WorkspaceStatusPayload> {
      return ipcRenderer.invoke('workspace:get-status')
    },

    onStatusChange(callback: (status: WorkspaceStatusPayload) => void): UnsubscribeFn {
      const token = ++workspaceStatusToken
      activeWorkspaceStatusCallback = callback
      return () => {
        if (workspaceStatusToken === token) activeWorkspaceStatusCallback = null
      }
    },

    listSetupModels(request: WorkspaceSetupModelListRequest): Promise<WorkspaceSetupModelListResult> {
      return ipcRenderer.invoke('workspace:list-setup-models', request)
    },

    runSetup(request: WorkspaceSetupRequest): Promise<WorkspaceSetupRunResult> {
      return ipcRenderer.invoke('workspace:run-setup', request)
    },

    /**
     * Opens a new independent application window.
     */
    openNewWindow(): Promise<void> {
      return ipcRenderer.invoke('workspace:open-new-window')
    },

    /**
     * Checks whether the given workspace path is currently locked by another
     * running DotCraft process.
     * Returns { locked: true, pid } if occupied, or { locked: false } if free.
     */
    checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }> {
      return ipcRenderer.invoke('workspace:check-lock', wsPath)
    },

    /**
     * Writes a data URL image to `.craft/attachments/images/` and returns the absolute path for `localImage`.
     */
    saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }> {
      return ipcRenderer.invoke('workspace:save-image-to-temp', params)
    },

    /**
     * Reads an attached image from disk and returns a data URL for UI rehydration.
     */
    readImageAsDataUrl(params: { path: string }): Promise<{ dataUrl: string }> {
      return ipcRenderer.invoke('workspace:read-image-as-data-url', params)
    },

    /**
     * Fuzzy filename search within the workspace for @ file autocomplete.
     */
    searchFiles(params: {
      query: string
      workspacePath: string
      limit?: number
    }): Promise<{
      files: Array<{ name: string; relativePath: string; dir: string }>
      indexStatus?: 'empty' | 'building' | 'ready'
      indexedCount?: number
      stale?: boolean
    }> {
      return ipcRenderer.invoke('workspace:search-files', params)
    },

    /** Viewer panel IPC — exposed as `window.api.workspace.viewer.*` */
    viewer: {
      /** Lists workspace files for the Quick-Open dialog. */
      listFiles(params: {
        workspacePath: string
        query: string
        limit: number
      }): Promise<{
        files: Array<{ name: string; relativePath: string; dir: string }>
        indexStatus?: 'empty' | 'building' | 'ready'
        indexedCount?: number
        stale?: boolean
      }> {
        return ipcRenderer.invoke('workspace:viewer:list-files', params)
      },

      /** Lists immediate children of a workspace directory for the explorer tree. */
      listDir(params: { dirPath?: string }): Promise<{
        dirPath: string
        entries: Array<{
          name: string
          relativePath: string
          absolutePath: string
          isDir: boolean
        }>
      }> {
        return ipcRenderer.invoke('workspace:viewer:list-dir', params)
      },

      /** Classifies a file into text / image / pdf / unsupported. */
      classify(params: {
        absolutePath: string
      }): Promise<{
        contentClass: 'text' | 'image' | 'pdf' | 'unsupported'
        mime: string
        sizeBytes: number
      }> {
        return ipcRenderer.invoke('workspace:viewer:classify', params)
      },

      /** Reads a text file with an optional size limit (default 5 MB). */
      readText(params: {
        absolutePath: string
        limitBytes?: number
      }): Promise<{ text: string; truncated: boolean; encoding: string }> {
        return ipcRenderer.invoke('workspace:viewer:read-text', params)
      },

      authorizeFile(params: { absolutePath: string }): Promise<{ absolutePath: string }> {
        return ipcRenderer.invoke('workspace:viewer:authorize-file', params)
      },

      toViewerUrl(params: { absolutePath: string }): Promise<{ url: string }> {
        return ipcRenderer.invoke('workspace:viewer:to-viewer-url', params)
      },

        browser: {
        create(params: {
          tabId: string
          threadId?: string
          workspacePath: string
          initialUrl?: string
        }): Promise<{
          tabId: string
          currentUrl: string
          title: string
          faviconDataUrl?: string
          canGoBack: boolean
          canGoForward: boolean
          loading: boolean
        }> {
          return ipcRenderer.invoke('viewer:browser:create', params)
        },
        destroy(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:destroy', params)
        },
        navigate(params: { tabId: string; url: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:navigate', params)
        },
        back(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:back', params)
        },
        forward(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:forward', params)
        },
        reload(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:reload', params)
        },
        stop(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:stop', params)
        },
        setBounds(params: {
          tabId: string
          x: number
          y: number
          width: number
          height: number
        }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:set-bounds', params)
        },
        setVisible(params: { tabId: string; visible: boolean }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:set-visible', params)
        },
        setActive(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:set-active', params)
        },
        openExternal(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:browser:open-external', params)
        },
        snapshot(params: { tabId: string }): Promise<{
          tabId: string
          currentUrl: string
          title: string
          faviconDataUrl?: string
          canGoBack: boolean
          canGoForward: boolean
          loading: boolean
        } | null> {
          return ipcRenderer.invoke('viewer:browser:snapshot', params)
        },
          onEvent(listener: (event: BrowserEventPayload) => void): UnsubscribeFn {
            const wrapped = (_evt: Electron.IpcRendererEvent, payload: BrowserEventPayload) => listener(payload)
            ipcRenderer.on('viewer:browser:event', wrapped)
            return () => ipcRenderer.removeListener('viewer:browser:event', wrapped)
          }
        },
        browserUse: {
          onOpen(listener: (event: BrowserUseOpenPayload) => void): UnsubscribeFn {
            const wrapped = (_evt: Electron.IpcRendererEvent, payload: BrowserUseOpenPayload) => listener(payload)
            ipcRenderer.on('viewer:browser:open', wrapped)
            return () => ipcRenderer.removeListener('viewer:browser:open', wrapped)
          },
          onClose(listener: (event: BrowserUseClosePayload) => void): UnsubscribeFn {
            const wrapped = (_evt: Electron.IpcRendererEvent, payload: BrowserUseClosePayload) => listener(payload)
            ipcRenderer.on('viewer:browser:close', wrapped)
            return () => ipcRenderer.removeListener('viewer:browser:close', wrapped)
          },
          onApprovalRequest(listener: (event: BrowserUseApprovalRequestPayload) => void): UnsubscribeFn {
            const wrapped = (_evt: Electron.IpcRendererEvent, payload: BrowserUseApprovalRequestPayload) => listener(payload)
            ipcRenderer.on('viewer:browser:approval-request', wrapped)
            return () => ipcRenderer.removeListener('viewer:browser:approval-request', wrapped)
          },
          sendApprovalResponse(params: {
            requestId: string
            action: BrowserUseApprovalResponseAction
          }): Promise<void> {
            return ipcRenderer.invoke('viewer:browser:approval-response', params)
          },
          clearCookies(): Promise<{ ok: boolean }> {
            return ipcRenderer.invoke('viewer:browser:clear-cookies')
          }
        },
          terminal: {
        create(params: {
          tabId: string
          threadId: string
          workspacePath: string
          cols: number
          rows: number
        }): Promise<{ tabId: string; pid: number; shell: string; cwd: string }> {
          return ipcRenderer.invoke('viewer:terminal:create', params)
        },
        attach(params: { tabId: string }): Promise<{
          tabId: string
          pid: number
          shell: string
          cwd: string
          buffer: string
          exited?: { code: number | null; signal: number | null }
        }> {
          return ipcRenderer.invoke('viewer:terminal:attach', params)
        },
        write(params: { tabId: string; data: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:terminal:write', params)
        },
        resize(params: { tabId: string; cols: number; rows: number }): Promise<void> {
          return ipcRenderer.invoke('viewer:terminal:resize', params)
        },
        dispose(params: { tabId: string }): Promise<void> {
          return ipcRenderer.invoke('viewer:terminal:dispose', params)
        },
        onData(listener: (event: TerminalDataEventPayload) => void): UnsubscribeFn {
          const wrapped = (
            _evt: Electron.IpcRendererEvent,
            payload: { tabId: string; data: string }
          ) => listener({ ...payload, type: 'data' })
          ipcRenderer.on('viewer:terminal:data', wrapped)
          return () => ipcRenderer.removeListener('viewer:terminal:data', wrapped)
        },
        onExit(listener: (event: TerminalExitEventPayload) => void): UnsubscribeFn {
          const wrapped = (
            _evt: Electron.IpcRendererEvent,
            payload: { tabId: string; code: number | null; signal: number | null }
          ) => listener({ ...payload, type: 'exit' })
          ipcRenderer.on('viewer:terminal:exit', wrapped)
          return () => ipcRenderer.removeListener('viewer:terminal:exit', wrapped)
        }
      }
    }
  },

  modules: {
    list(): Promise<DiscoveredModule[]> {
      return ipcRenderer.invoke('modules:list')
    },
    userDirectory(): Promise<{ path: string }> {
      return ipcRenderer.invoke('modules:user-directory')
    },
    checkDirectory(path: string): Promise<{ exists: boolean }> {
      return ipcRenderer.invoke('modules:check-directory', { path })
    },
    openFolder(): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:open-folder')
    },
    pickDirectory(): Promise<string | null> {
      return ipcRenderer.invoke('modules:pick-directory')
    },
    rescan(): Promise<DiscoveredModule[]> {
      return ipcRenderer.invoke('modules:rescan')
    },
    setActiveVariant(params: {
      channelName: string
      moduleId: string
    }): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:set-active-variant', params)
    },
    readConfig(params: {
      configFileName: string
    }): Promise<{ exists: boolean; config: Record<string, unknown> | null }> {
      return ipcRenderer.invoke('modules:read-config', params)
    },
    writeConfig(params: {
      configFileName: string
      config: Record<string, unknown>
    }): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('modules:write-config', params)
    },
    start(params: {
      moduleId: string
    }): Promise<{ ok: boolean; error?: string; missingFields?: string[] }> {
      return ipcRenderer.invoke('modules:start', params)
    },
    stop(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }> {
      return ipcRenderer.invoke('modules:stop', params)
    },
    running(): Promise<ModuleStatusMap> {
      return ipcRenderer.invoke('modules:running')
    },
    getLogs(moduleId: string): Promise<{ lines: string[] }> {
      return ipcRenderer.invoke('modules:get-logs', { moduleId })
    },
    qrStatus(moduleId: string): Promise<{ active: boolean; qrDataUrl: string | null }> {
      return ipcRenderer.invoke('modules:qr-status', { moduleId })
    },
    onStatusChanged(callback: (statusMap: ModuleStatusMap) => void): UnsubscribeFn {
      const token = ++moduleStatusToken
      activeModuleStatusCallback = callback
      return () => {
        if (moduleStatusToken === token) activeModuleStatusCallback = null
      }
    },
    onQrUpdate(callback: (payload: QrUpdatePayload) => void): UnsubscribeFn {
      const token = ++moduleQrUpdateToken
      activeModuleQrUpdateCallback = callback
      return () => {
        if (moduleQrUpdateToken === token) activeModuleQrUpdateCallback = null
      }
    },
    onRescanSummary(callback: (payload: ModulesRescanSummaryPayload) => void): UnsubscribeFn {
      const token = ++moduleRescanSummaryToken
      activeModuleRescanSummaryCallback = callback
      return () => {
        if (moduleRescanSummaryToken === token) activeModuleRescanSummaryCallback = null
      }
    }
  },

  settings: {
    /**
     * Returns the current application settings.
     */
    get(): Promise<{
      binarySource?: BinarySource
      appServerBinaryPath?: string
      lastWorkspacePath?: string
      connectionMode?: ConnectionMode
      webSocket?: {
        host?: string
        port?: number
      }
      remote?: {
        url?: string
        token?: string
      }
      activeRemoteStack?: {
        hostId: string
        stackId: string
      }
      modulesDirectory?: string
      activeModuleVariants?: Record<string, string>
      theme?: 'dark' | 'light'
      locale?: AppLocale
      showThinkingContent?: boolean
      showInMenuBar?: boolean
      visibleChannels?: string[]
      lastOpenEditorId?: EditorId
      lastSeenWhatsNewVersion?: string
      browserUse?: {
        approvalMode?: BrowserUseApprovalMode
        blockedDomains?: string[]
        allowedDomains?: string[]
      }
      notifications?: {
        taskCompletionMode?: TaskCompletionNotificationMode
      }
      profile?: {
        githubUsername?: string
      }
      pinnedThreadIdsByWorkspace?: Record<string, string[]>
    }> {
      return ipcRenderer.invoke('settings:get')
    },

    /**
     * Merges and persists partial settings updates.
     */
    set(partial: {
      binarySource?: BinarySource
      appServerBinaryPath?: string
      connectionMode?: ConnectionMode
      webSocket?: {
        host?: string
        port?: number
      }
      remote?: {
        url?: string
        token?: string
      }
      activeRemoteStack?: {
        hostId: string
        stackId: string
      }
      modulesDirectory?: string
      activeModuleVariants?: Record<string, string>
      theme?: 'dark' | 'light'
      locale?: AppLocale
      showThinkingContent?: boolean
      showInMenuBar?: boolean
      visibleChannels?: string[]
      lastOpenEditorId?: EditorId
      lastSeenWhatsNewVersion?: string
      browserUse?: {
        approvalMode?: BrowserUseApprovalMode
        blockedDomains?: string[]
        allowedDomains?: string[]
      }
      notifications?: {
        taskCompletionMode?: TaskCompletionNotificationMode
      }
      profile?: {
        githubUsername?: string
      }
      pinnedThreadIdsByWorkspace?: Record<string, string[]>
    }): Promise<void> {
      return ipcRenderer.invoke('settings:set', partial)
    },

    onPinnedThreadIdsChanged(callback: (payload: PinnedThreadIdsChangedPayload) => void): UnsubscribeFn {
      const token = ++pinnedThreadIdsChangedToken
      activePinnedThreadIdsChangedCallback = callback
      return () => {
        if (pinnedThreadIdsChangedToken === token) activePinnedThreadIdsChangedCallback = null
      }
    }
  },

  whatsNew: {
    getReleases(): Promise<WhatsNewRelease[]> {
      return ipcRenderer.invoke('app:whats-new-get-releases')
    },
    getMediaStates(releaseVersions: string[]): Promise<WhatsNewMediaState[]> {
      return ipcRenderer.invoke('app:whats-new-get-media-states', releaseVersions)
    },
    prefetchMedia(releaseVersions: string[]): Promise<WhatsNewMediaState[]> {
      return ipcRenderer.invoke('app:whats-new-prefetch-media', releaseVersions)
    },
    onMediaStateChanged(callback: (state: WhatsNewMediaState) => void): UnsubscribeFn {
      const token = ++whatsNewMediaStateToken
      activeWhatsNewMediaStateCallback = callback
      return () => {
        if (whatsNewMediaStateToken === token) {
          activeWhatsNewMediaStateCallback = null
        }
      }
    }
  },

  updates: {
    getState(): Promise<AppUpdateState> {
      return ipcRenderer.invoke('app:update-get-state')
    },
    check(): Promise<AppUpdateState> {
      return ipcRenderer.invoke('app:update-check')
    },
    downloadAndInstall(): Promise<AppUpdateState> {
      return ipcRenderer.invoke('app:update-download-and-install')
    },
    onStateChanged(callback: (state: AppUpdateState) => void): UnsubscribeFn {
      const token = ++appUpdateStateToken
      activeAppUpdateStateCallback = callback
      return () => {
        if (appUpdateStateToken === token) {
          activeAppUpdateStateCallback = null
        }
      }
    }
  },

  /** Remote DotCraft Docker stack management over SSH (the "Servers" surface). */
  remoteServers: {
    list(): Promise<RemoteHost[]> {
      return ipcRenderer.invoke('remoteHosts:list')
    },
    sshConfig(): Promise<LocalSshConfigInfo> {
      return ipcRenderer.invoke('remoteHosts:ssh-config')
    },
    create(input: {
      name: string
      sshTarget: string
      identityFile?: string
      stacks?: RemoteStack[]
    }): Promise<RemoteHost> {
      return ipcRenderer.invoke('remoteHosts:create', input)
    },
    update(id: string, patch: Partial<Omit<RemoteHost, 'id'>>): Promise<RemoteHost> {
      return ipcRenderer.invoke('remoteHosts:update', { id, patch })
    },
    delete(id: string): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('remoteHosts:delete', { id })
    },
    test(input: {
      id?: string
      draft?: { name?: string; sshTarget?: string; identityFile?: string }
    }): Promise<SshTestResult> {
      return ipcRenderer.invoke('remoteHosts:test', input)
    },
    listStacks(hostId: string): Promise<RemoteStack[]> {
      return ipcRenderer.invoke('remoteStacks:list', { hostId })
    },
    discoverStacks(hostId: string): Promise<DiscoveredStack[]> {
      return ipcRenderer.invoke('remoteStacks:discover', { hostId })
    },
    status(hostId: string, stackId: string): Promise<RemoteStackStatus> {
      return ipcRenderer.invoke('remoteStacks:status', { hostId, stackId })
    },
    logs(
      hostId: string,
      stackId: string,
      options?: { service?: string; tail?: number }
    ): Promise<{ text: string; service?: string; tail: number }> {
      return ipcRenderer.invoke('remoteStacks:logs', { hostId, stackId, ...options })
    },
    action(hostId: string, stackId: string, action: RemoteStackAction): Promise<OperationResult> {
      return ipcRenderer.invoke('remoteStacks:action', { hostId, stackId, action })
    },
    openInDesktop(
      hostId: string,
      stackId: string
    ): Promise<{ ok: boolean; hostId: string; stackId: string; localPort: number }> {
      return ipcRenderer.invoke('remoteStacks:open-app-server-tunnel', { hostId, stackId })
    },
    openDashboard(hostId: string, stackId: string): Promise<{ ok: boolean; localPort: number }> {
      return ipcRenderer.invoke('remoteStacks:open-dashboard-tunnel', { hostId, stackId })
    },
    disconnect(hostId: string, stackId: string): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('remoteStacks:disconnect', { hostId, stackId })
    }
  }
}

contextBridge.exposeInMainWorld('api', api)

export type Api = typeof api
