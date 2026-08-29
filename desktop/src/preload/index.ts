import { contextBridge, ipcRenderer, shell, webFrame, webUtils } from 'electron'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { resolveThemeMode, type ThemeMode } from '../shared/theme'
import { readInitialWorkspaceStatusFromArgv } from '../shared/initialWorkspaceStatus'
import { INITIAL_CDP_DEBUGGING_ARG } from '../shared/initialCdpDebugging'
import type { ProviderPreferences } from '../shared/modelPreference'
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
  BrowserUseApprovalResponseAction,
  BrowserUseApprovalRequestPayload,
  BrowserUseClosePayload,
  BrowserUseOpenPayload,
  BrowserEventPayload,
  TerminalDataEventPayload,
  TerminalExitEventPayload
} from '../shared/viewer/types'
export type {
  BrowserUseApprovalRequestPayload,
  BrowserUseApprovalResponseAction,
  BrowserUseApprovalResponsePayload,
  BrowserUseClosePayload,
  BrowserUseOpenPayload
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
import type {
  VoiceMicrophonePermissionStatus,
  VoiceRuntimeSnapshot,
  VoiceSessionEvent,
  VoiceTranscriptionInput
} from '../shared/voice'
import type { ConnectionSettingsDraft } from '../shared/remoteConnection'
import type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'
export type {
  BinarySource,
  BrowserUseApprovalMode,
  ConnectionMode,
  TaskCompletionNotificationMode
} from '../shared/desktopSettings'
import type {
  DiscoveredModule,
  ModuleStatusMap,
  QrUpdatePayload
} from '../shared/channelModules'
export type {
  ConfigDescriptorWire,
  ConfigFieldOptionWire,
  ConfigGroupDescriptorWire,
  DiscoveredModule,
  ModuleInterfaceWire,
  ModuleStatusEntry,
  ModuleStatusMap,
  QrUpdatePayload
} from '../shared/channelModules'
import type {
  ConnectionStatusPayload,
  ResolvedBinaryPayload,
  RetryConnectionRequest
} from '../shared/connectionStatus'
export type {
  ConnectionErrorType,
  ConnectionStatus,
  ConnectionStatusPayload,
  ResolvedBinaryPayload,
  RetryConnectionRequest
} from '../shared/connectionStatus'
import type {
  WorkspaceSetupModelListRequest,
  WorkspaceSetupModelListResult,
  WorkspaceSetupRequest,
  WorkspaceSetupRunResult,
  WorkspaceStatusPayload
} from '../shared/workspaceSetup'
export type * from '../shared/workspaceSetup'
import type { WorkspaceProjectsPayload } from '../shared/workspaceProjects'
import type { EditorId, EditorInfo } from '../shared/externalEditors'
export type { EditorId, EditorInfo } from '../shared/externalEditors'
import type { ChromeSetupStatus } from '../shared/chromeSetup'
export type { ChromeSetupCheckStatus, ChromeSetupStatus } from '../shared/chromeSetup'
import type { GitHeadInspection } from '../shared/gitHead'
import type { InlineVisualizationCaptureRect, InlineVisualizationCaptureResult } from '../shared/inlineVisualization'
import type { OratorioApi, OratorioRequest, OratorioResponse, OratorioServiceContext, OratorioServiceEvent } from '../shared/oratorio'
import { TokenMulticastDispatcher } from './notificationDispatcher'
import {
  isKnownServerNotification,
  isKnownServerRequest,
  type KnownNotificationPayload,
  type KnownServerRequestPayload,
  type RawNotificationPayload,
  type RawServerRequestPayload
} from '../shared/appServerBoundary'

export type UnsubscribeFn = () => void

function readInitialTheme(): ThemeMode {
  const arg = process.argv.find((value) => value.startsWith('--dotcraft-initial-theme='))
  const raw = arg?.slice('--dotcraft-initial-theme='.length)
  return resolveThemeMode(raw)
}

/** The dark/light theme main already resolved (incl. `system`), used for a flash-free first paint. */
function readAppliedTheme(): 'dark' | 'light' {
  const arg = process.argv.find((value) => value.startsWith('--dotcraft-applied-theme='))
  return arg?.slice('--dotcraft-applied-theme='.length) === 'dark' ? 'dark' : 'light'
}

function readInitialLocale(): AppLocale {
  const arg = process.argv.find((value) => value.startsWith('--dotcraft-initial-locale='))
  const raw = arg?.slice('--dotcraft-initial-locale='.length)
  return normalizeLocale(raw)
}

const initialTheme = readInitialTheme()
const initialAppliedTheme = readAppliedTheme()
const initialLocale = readInitialLocale()
const initialCdpDebuggingEnabled = process.argv.includes(INITIAL_CDP_DEBUGGING_ARG)
const initialWorkspaceStatus = readInitialWorkspaceStatusFromArgv(process.argv) as WorkspaceStatusPayload

function applyInitialDocumentState(): void {
  const root = document.documentElement
  if (!root) return
  // Use the dark/light value main already resolved (initialTheme is the preference, which may
  // be `system`). The renderer re-applies from the preference and installs an OS listener.
  root.setAttribute('data-theme', initialAppliedTheme)
  root.lang = localeToHtmlLang(initialLocale)
}

if (typeof document !== 'undefined') {
  applyInitialDocumentState()
  if (!document.documentElement) {
    document.addEventListener('DOMContentLoaded', applyInitialDocumentState, { once: true })
  }
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

export interface OpenThreadPayload {
  threadId: string
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

export interface WindowVisibilityState {
  minimized: boolean
  visible: boolean
  focused: boolean
}

// Exactly one ipcRenderer listener per channel, dispatched locally: one listener per
// subscriber accumulates stale listeners across React StrictMode remounts.

// Only notifications multicast; other channels stay single-slot because each has one
// owning surface or responder. contextBridge wraps functions in a new Proxy on every
// call, so subscribers are tracked by monotonic token rather than reference equality.

const notificationDispatcher = new TokenMulticastDispatcher<KnownNotificationPayload>((error) => {
  console.error('appserver:notification subscriber failed:', error)
})
const rawNotificationDispatcher = new TokenMulticastDispatcher<RawNotificationPayload>((error) => {
  console.error('appserver:raw-notification subscriber failed:', error)
})
ipcRenderer.on(
  'appserver:notification',
  (_event: Electron.IpcRendererEvent, payload: RawNotificationPayload) => {
    if (isKnownServerNotification(payload)) {
      notificationDispatcher.dispatch(payload)
    } else {
      rawNotificationDispatcher.dispatch(payload)
    }
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
let activeServerRequestCallback: ((payload: KnownServerRequestPayload) => void) | null = null
let rawServerRequestToken = 0
let activeRawServerRequestCallback: ((payload: RawServerRequestPayload) => void) | null = null
ipcRenderer.on(
  'appserver:server-request',
  (_event: Electron.IpcRendererEvent, payload: RawServerRequestPayload) => {
    if (isKnownServerRequest(payload)) {
      activeServerRequestCallback?.(payload)
    } else {
      activeRawServerRequestCallback?.(payload)
    }
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

let visibilityChangeToken = 0
let activeVisibilityChangeCallback: ((state: WindowVisibilityState) => void) | null = null
ipcRenderer.on('window:visibility-changed', (_event: Electron.IpcRendererEvent, state: WindowVisibilityState) => {
  activeVisibilityChangeCallback?.(state)
})

let oratorioSubscriptionCount = 0

const api = {
  platform: process.platform as 'darwin' | 'win32' | 'linux',

  initialTheme,

  initialLocale,

  initialCdpDebuggingEnabled,

  initialWorkspaceStatus,

  titleBarOverlayHeight: TITLE_BAR_OVERLAY_HEIGHT,

  /** Matches CustomMenuBar / ToastContainer right inset on Windows / Linux. */
  titleBarOverlayRightReserve: TITLE_BAR_OVERLAY_RIGHT_RESERVE,

  menu: {
    popupTopLevel(menuId: TopLevelMenuId, x: number, y: number): Promise<void> {
      return ipcRenderer.invoke('menu:popup-top-level', { menuId, x, y })
    }
  },

  visualization: {
    copyImage(rect: InlineVisualizationCaptureRect): Promise<InlineVisualizationCaptureResult> {
      return ipcRenderer.invoke('visualization:copy-image', rect)
    }
  },

  oratorio: {
    getContext(): Promise<OratorioServiceContext> {
      return ipcRenderer.invoke('oratorio:get-context')
    },
    request<T = unknown>(request: OratorioRequest): Promise<OratorioResponse<T>> {
      return ipcRenderer.invoke('oratorio:request', request)
    },
    retry(): Promise<OratorioServiceContext> {
      return ipcRenderer.invoke('oratorio:retry')
    },
    getPendingHandoff(): Promise<import('../shared/oratorio').OratorioHandoffRequest | null> {
      return ipcRenderer.invoke('oratorio:get-pending-handoff')
    },
    resolveHandoff(requestId: string, approved: boolean): Promise<void> {
      return ipcRenderer.invoke('oratorio:resolve-handoff', { requestId, approved })
    },
    focusRun(runId: string | null): Promise<void> {
      return ipcRenderer.invoke('oratorio:focus-run', runId)
    },
    onEvent(callback: (event: OratorioServiceEvent) => void): () => void {
      const wrapped = (_event: Electron.IpcRendererEvent, payload: OratorioServiceEvent): void => callback(payload)
      oratorioSubscriptionCount += 1
      if (oratorioSubscriptionCount === 1) ipcRenderer.send('oratorio:subscribe')
      ipcRenderer.on('oratorio:event', wrapped)
      return () => {
        ipcRenderer.removeListener('oratorio:event', wrapped)
        oratorioSubscriptionCount = Math.max(0, oratorioSubscriptionCount - 1)
        if (oratorioSubscriptionCount === 0) ipcRenderer.send('oratorio:unsubscribe')
      }
    }
  } satisfies OratorioApi,

  appServer: {
    sendRequest<M extends keyof ClientRequestMethods>(
      method: M,
      params: ClientRequestMethods[M]['params'],
      timeoutMs?: number | null
    ): Promise<ClientRequestMethods[M]['result']> {
      return ipcRenderer.invoke('appserver:send-request', method, params, timeoutMs)
    },

    sendRequestRaw(method: string, params?: unknown, timeoutMs?: number | null): Promise<unknown> {
      return ipcRenderer.invoke('appserver:send-request-raw', method, params, timeoutMs)
    },

    listModels(providerId?: string | null): Promise<unknown> {
      return ipcRenderer.invoke('appserver:model-list', providerId)
    },

    requestWorkspaceConfigSchema(): Promise<WorkspaceConfigSchema | null> {
      return ipcRenderer.invoke('appserver:workspace-config-schema')
    },

    /** Snapshot read so the renderer does not miss status events fired during bootstrap. */
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

    retryConnection(request?: RetryConnectionRequest): Promise<void> {
      return ipcRenderer.invoke('appserver:retry-connection', request)
    },

    applyConnectionSettings(draft: ConnectionSettingsDraft): Promise<void> {
      return ipcRenderer.invoke('appserver:apply-connection-settings', draft)
    },

    onNotification(callback: (payload: KnownNotificationPayload) => void): UnsubscribeFn {
      return notificationDispatcher.subscribe(callback)
    },

    onNotificationRaw(callback: (payload: RawNotificationPayload) => void): UnsubscribeFn {
      return rawNotificationDispatcher.subscribe(callback)
    },

    onConnectionStatus(callback: (status: ConnectionStatusPayload) => void): UnsubscribeFn {
      const token = ++connectionStatusToken
      activeConnectionStatusCallback = callback
      return () => {
        if (connectionStatusToken === token) activeConnectionStatusCallback = null
      }
    },

    /** The payload carries a bridgeId that must be passed back to sendServerResponse. */
    onServerRequest(callback: (payload: KnownServerRequestPayload) => void): UnsubscribeFn {
      const token = ++serverRequestToken
      activeServerRequestCallback = callback
      return () => {
        if (serverRequestToken === token) activeServerRequestCallback = null
      }
    },

    onServerRequestRaw(callback: (payload: RawServerRequestPayload) => void): UnsubscribeFn {
      const token = ++rawServerRequestToken
      activeRawServerRequestCallback = callback
      return () => {
        if (rawServerRequestToken === token) activeRawServerRequestCallback = null
      }
    },

    /** Main forwards this as the JSON-RPC response to the AppServer. */
    sendServerResponse(bridgeId: string, result: unknown): Promise<void> {
      return ipcRenderer.invoke('appserver:server-response', bridgeId, result)
    }
  },

  workspaceConfig: {
    getCore(): Promise<{
      workspace: {
        providerId: string | null
        providerPreferences: ProviderPreferences
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
        providerPreferences: ProviderPreferences
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
    setTitle(title: string): void {
      ipcRenderer.invoke('window:set-title', title)
    },

    /** No-op on macOS, which has no title bar overlay. */
    setTitleBarOverlayTheme(theme: 'dark' | 'light'): Promise<void> {
      return ipcRenderer.invoke('window:set-title-bar-overlay-theme', theme)
    },

    /**
     * Applied immediately via webFrame (1 = 100%); persisting the preference is a
     * separate call, and the main process re-applies it on load.
     */
    setZoomFactor(factor: number): void {
      webFrame.setZoomFactor(factor)
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

    getVisibilityState(): Promise<WindowVisibilityState> {
      return ipcRenderer.invoke('window:get-visibility-state')
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

    onVisibilityChanged(callback: (state: WindowVisibilityState) => void): () => void {
      const token = ++visibilityChangeToken
      activeVisibilityChangeCallback = callback
      return () => {
        if (visibilityChangeToken === token) {
          activeVisibilityChangeCallback = null
        }
      }
    },

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
    openPath(path: string): Promise<string> {
      return shell.openPath(path)
    },

    /** The URL scheme is validated in the main process, not here. */
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
    /** Fetched and cached in the main process; null when the username is invalid or unavailable. */
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
    writeFile(absPath: string, content: string): Promise<void> {
      return ipcRenderer.invoke('file:write', absPath, content)
    },

    /** Returns an empty string when the file does not exist. */
    readFile(absPath: string): Promise<string> {
      return ipcRenderer.invoke('file:read', absPath)
    },

    deleteFile(absPath: string): Promise<void> {
      return ipcRenderer.invoke('file:delete', absPath)
    },

    exists(absPath: string): Promise<boolean> {
      return ipcRenderer.invoke('file:exists', absPath)
    }
  },

  git: {
    commit(workspacePath: string, files: string[], message: string): Promise<string> {
      return ipcRenderer.invoke('git:commit', workspacePath, files, message)
    },
    /** Falls back to the detached short SHA, or null when unavailable. */
    getBranch(workspacePath: string): Promise<string | null> {
      return ipcRenderer.invoke('git:branch', workspacePath)
    },
    inspectHead(workspacePath: string): Promise<GitHeadInspection> {
      return ipcRenderer.invoke('git:inspectHead', workspacePath)
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

  desktopPlugins: {
    registerModule(params: {
      pluginId: string
      version: string
      revision: string
      rootPath: string
    }): Promise<{ entryUrl: string; styleUrls: string[] }> {
      return ipcRenderer.invoke('desktop-plugin:register-module', params)
    },
    removeModule(params: { pluginId: string; revision: string }): Promise<{ ok: boolean }> {
      return ipcRenderer.invoke('desktop-plugin:remove-module', params)
    },
    appSurfaceGetJson(params: {
      appId: string
      surfaceId: string
      relativePath: string
      timeoutMs?: number
    }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-plugin:app-surface-get-json', params)
    },
    appSurfacePostJson(params: {
      appId: string
      surfaceId: string
      relativePath: string
      body?: unknown
      timeoutMs?: number
    }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-plugin:app-surface-post-json', params)
    },
    getAppConnectionStatus(params: { appId: string }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-plugin:app-connection-status', params)
    },
    startAppConnection(params: { appId: string }): Promise<unknown> {
      return ipcRenderer.invoke('desktop-plugin:app-connection-start', params)
    },
    openApp(params: { appId: string; url: string }): Promise<void> {
      return ipcRenderer.invoke('desktop-plugin:app-open', params)
    }
  },

  workspace: {
    /** `title` must already be localized; the picker does not translate it. */
    pickFolder(options?: { title?: string }): Promise<string | null> {
      return ipcRenderer.invoke('workspace:pick-folder', options)
    },

    /** Creates the folder under the user's Documents directory and git-inits it. */
    createLocalProject(params: { name: string }): Promise<{ path: string; gitInitialized: boolean }> {
      return ipcRenderer.invoke('workspace:create-local-project', params)
    },

    /** A `previousPath` that differs from `primaryFolder` reassigns the Project identity. */
    saveLocalProject(params: {
      previousPath?: string
      primaryFolder: string
      secondaryFolders: string[]
      name?: string
    }): Promise<{ path: string }> {
      return ipcRenderer.invoke('workspace:save-local-project', params)
    },

    getPathForFile(file: File): string {
      return webUtils.getPathForFile(file)
    },

    /** Main tears down the current AppServer and spawns a new one. */
    switch(newPath: string): Promise<void> {
      return ipcRenderer.invoke('workspace:switch', newPath)
    },

    clearSelection(): Promise<void> {
      return ipcRenderer.invoke('workspace:clear-selection')
    },

    /** Capped at the 20 most recent. */
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

    restart(path: string): Promise<void> {
      return ipcRenderer.invoke('workspace:restart', path)
    },

    stop(path: string): Promise<void> {
      return ipcRenderer.invoke('workspace:stop', path)
    },

    archiveThread(workspacePath: string, threadId: string): Promise<void> {
      return ipcRenderer.invoke('workspace:archive-thread', { workspacePath, threadId })
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
    loginSetupChatGpt(providerId: string): Promise<{ kind: 'success' | 'error' }> {
      return ipcRenderer.invoke('workspace:login-setup-chatgpt', providerId)
    },

    runSetup(request: WorkspaceSetupRequest): Promise<WorkspaceSetupRunResult> {
      return ipcRenderer.invoke('workspace:run-setup', request)
    },

    openNewWindow(): Promise<void> {
      return ipcRenderer.invoke('workspace:open-new-window')
    },

    /** Locked reports another running DotCraft process, but is a hint rather than a gate. */
    checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }> {
      return ipcRenderer.invoke('workspace:check-lock', wsPath)
    },

    /** Writes into the workspace's `.craft/attachments/images/`, not an OS temp dir. */
    saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }> {
      return ipcRenderer.invoke('workspace:save-image-to-temp', params)
    },

    readImageAsDataUrl(params: { path: string }): Promise<{ dataUrl: string }> {
      return ipcRenderer.invoke('workspace:read-image-as-data-url', params)
    },

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

    viewer: {
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

  voice: {
    getMicrophonePermissionStatus(): Promise<VoiceMicrophonePermissionStatus> {
      return ipcRenderer.invoke('voice:get-microphone-permission-status')
    },
    requestMicrophonePermission(): Promise<VoiceMicrophonePermissionStatus> {
      return ipcRenderer.invoke('voice:request-microphone-permission')
    },
    openMicrophoneSettings(): Promise<void> {
      return ipcRenderer.invoke('voice:open-microphone-settings')
    },
    getSnapshot(): Promise<VoiceRuntimeSnapshot> {
      return ipcRenderer.invoke('voice:get-snapshot')
    },
    installModel(): Promise<void> {
      return ipcRenderer.invoke('voice:install-model')
    },
    cancelModelInstall(): Promise<void> {
      return ipcRenderer.invoke('voice:cancel-model-install')
    },
    removeModel(): Promise<void> {
      return ipcRenderer.invoke('voice:remove-model')
    },
    repairModel(): Promise<void> {
      return ipcRenderer.invoke('voice:repair-model')
    },
    submitTranscription(input: VoiceTranscriptionInput): Promise<{ sessionId: string }> {
      return ipcRenderer.invoke('voice:submit-transcription', input)
    },
    retryTranscription(sessionId: string): Promise<void> {
      return ipcRenderer.invoke('voice:retry-transcription', sessionId)
    },
    discardSession(sessionId: string): Promise<void> {
      return ipcRenderer.invoke('voice:discard-session', sessionId)
    },
    onSnapshot(listener: (snapshot: VoiceRuntimeSnapshot) => void): UnsubscribeFn {
      const wrapped = (_event: Electron.IpcRendererEvent, snapshot: VoiceRuntimeSnapshot): void => listener(snapshot)
      ipcRenderer.on('voice:snapshot', wrapped)
      return () => ipcRenderer.removeListener('voice:snapshot', wrapped)
    },
    onSessionEvent(listener: (event: VoiceSessionEvent) => void): UnsubscribeFn {
      const wrapped = (_event: Electron.IpcRendererEvent, payload: VoiceSessionEvent): void => listener(payload)
      ipcRenderer.on('voice:session-event', wrapped)
      return () => ipcRenderer.removeListener('voice:session-event', wrapped)
    }
  },

  settings: {
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
      theme?: 'system' | 'dark' | 'light'
      accent?: string
      codeFontSize?: number
      diffMarkers?: 'color' | 'sign'
      reduceMotion?: 'system' | 'on' | 'off'
      pointerCursors?: boolean
      interfaceZoom?: number
      translucentSidebar?: boolean
      locale?: AppLocale
      showThinkingContent?: boolean
      projectsSectionCollapsed?: boolean
      pinnedSectionCollapsed?: boolean
      chatsSectionCollapsed?: boolean
      showInMenuBar?: boolean
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
      voice?: {
        deviceId?: string
      }
      pinnedThreadIdsByWorkspace?: Record<string, string[]>
      pinnedProjectIds?: string[]
    }> {
      return ipcRenderer.invoke('settings:get')
    },

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
      theme?: 'system' | 'dark' | 'light'
      accent?: string
      codeFontSize?: number
      diffMarkers?: 'color' | 'sign'
      reduceMotion?: 'system' | 'on' | 'off'
      pointerCursors?: boolean
      interfaceZoom?: number
      translucentSidebar?: boolean
      locale?: AppLocale
      showThinkingContent?: boolean
      projectsSectionCollapsed?: boolean
      pinnedSectionCollapsed?: boolean
      chatsSectionCollapsed?: boolean
      showInMenuBar?: boolean
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
      voice?: {
        deviceId?: string
      }
      pinnedThreadIdsByWorkspace?: Record<string, string[]>
      pinnedProjectIds?: string[]
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
