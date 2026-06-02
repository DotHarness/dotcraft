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
import type { DesktopProviderProtocol } from '../shared/providerProtocols'
import type { ConnectionSettingsDraft } from '../shared/remoteConnection'
import type { AppLocale } from '../shared/locales'
import type { AddTabMenuAction, AddTabMenuRequest, AddTabPopupPayload } from '../shared/addTabMenu'

export type UnsubscribeFn = () => void
export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type BrowserUseApprovalMode = 'alwaysAsk' | 'askUnknown' | 'neverAsk'
export type TaskCompletionNotificationMode = 'whenUnfocused' | 'always' | 'never'
export type BrowserUseApprovalResponseAction = 'allowOnce' | 'allowDomain' | 'blockDomain' | 'deny'
export type ThemeMode = 'dark' | 'light'
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

export interface NotificationPayload {
  method: string
  params: unknown
}

export interface PinnedThreadIdsChangedPayload {
  workspacePath: string
  threadIds: string[]
}

export interface BrowserEventPayload {
  tabId: string
  threadId?: string
  type:
    | 'did-start-loading'
    | 'did-stop-loading'
    | 'did-navigate'
    | 'did-fail-load'
    | 'page-title-updated'
    | 'page-favicon-updated'
    | 'blocked-navigation'
    | 'download-blocked'
    | 'request-new-tab'
    | 'crashed'
    | 'update-history-flags'
    | 'external-handoff'
    | 'automation-started'
    | 'automation-updated'
    | 'automation-stopped'
    | 'virtual-cursor'
  url?: string
  title?: string
  faviconDataUrl?: string
  canGoBack?: boolean
  canGoForward?: boolean
  message?: string
  automationActive?: boolean
  sessionName?: string
  action?: string
  x?: number
  y?: number
}

export interface BrowserUseOpenPayload {
  threadId: string
  tabId: string
  initialUrl: string
  title?: string
  focusMode: 'first-open' | 'none'
}

export interface BrowserUseApprovalRequestPayload {
  requestId: string
  threadId: string
  tabId: string
  url: string
  domain: string
  sessionName?: string
}

export interface TerminalDataEventPayload {
  tabId: string
  type: 'data'
  data: string
}

export interface TerminalExitEventPayload {
  tabId: string
  type: 'exit'
  code: number | null
  signal: number | null
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
  hostId: string
  stackId: string
  serverName: string
  stackName: string
  workspaceDir: string
  appServerWorkspacePath?: string
  composeDir: string
  projectName?: string
}

export interface WorkspaceSetupBootstrapImportSource {
  id: WorkspaceSetupBootstrapImportSourceId
  fileName: 'AGENTS.md' | 'CLAUDE.md'
  path: string
  relativePath: string
}

export type ProviderAuthMethod = 'apiKey' | 'chatgptOAuth'

export interface WorkspaceSetupProviderSummary {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  hasApiKey: boolean
  endPoint: string
  networkTimeoutSeconds?: number | null
  authMethod?: ProviderAuthMethod
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

export interface WorkspaceSetupProviderDraft {
  id: string
  displayName: string
  protocol: WorkspaceSetupProviderProtocol
  apiKey: string
  endPoint: string
  networkTimeoutSeconds?: number | null
  authMethod?: ProviderAuthMethod
  chatGptAccountId?: string | null
  chatGptPlanType?: string | null
}

/** Status returned by auth/openai/* JSON-RPC methods. */
export interface OpenAiAuthStatus {
  loggedIn: boolean
  accountId?: string | null
  planType?: string | null
  email?: string | null
  lastRefresh?: string | null
  accessTokenExpiresAt?: string | null
  providerId?: string | null
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

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

declare global {
  interface Window {
    api: {
      platform: 'darwin' | 'win32' | 'linux'
      initialTheme: ThemeMode
      initialLocale: AppLocale
      initialWorkspaceStatus: WorkspaceStatusPayload
      titleBarOverlayHeight: number
      titleBarOverlayRightReserve: number
      menu: {
        popupTopLevel(
          menuId: 'file' | 'edit' | 'view' | 'window' | 'help',
          x: number,
          y: number
        ): Promise<void>
        popupAddTabMenu(request: AddTabMenuRequest): Promise<AddTabMenuAction | null>
        getAddTabMenuPayload(): Promise<AddTabPopupPayload | null>
        onAddTabMenuPayload(callback: (payload: AddTabPopupPayload) => void): UnsubscribeFn
        resolveAddTabMenu(action: AddTabMenuAction | null): Promise<void>
      }
      appServer: {
        sendRequest(method: string, params?: unknown, timeoutMs?: number): Promise<unknown>
        listModels(): Promise<unknown>
        requestWorkspaceConfigSchema(): Promise<WorkspaceConfigSchema | null>
        getConnectionStatus(): Promise<ConnectionStatusPayload>
        getResolvedBinary(request?: {
          binarySource?: BinarySource
          binaryPath?: string
        }): Promise<ResolvedBinaryPayload>
        pickBinary(): Promise<string | null>
        restartManaged(): Promise<void>
        applyConnectionSettings(draft: ConnectionSettingsDraft): Promise<void>
        onNotification(callback: (payload: NotificationPayload) => void): UnsubscribeFn
        onConnectionStatus(
          callback: (status: ConnectionStatusPayload) => void
        ): UnsubscribeFn
        onServerRequest(callback: (payload: ServerRequestPayload) => void): UnsubscribeFn
        sendServerResponse(bridgeId: string, result: unknown): void
      }
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
        }>
      }
      skillMarket: {
        search(request: SkillMarketSearchRequest): Promise<SkillMarketSearchResult>
        detail(request: SkillMarketDetailRequest): Promise<MarketSkillDetail>
        install(request: SkillMarketInstallRequest): Promise<MarketInstallResult>
        prepareDotCraftInstall(
          request: SkillMarketPrepareDotCraftInstallRequest
        ): Promise<MarketDotCraftInstallPreparation>
        bindDotCraftInstall(request: SkillMarketBindDotCraftInstallRequest): Promise<void>
        cleanupDotCraftInstall(request: SkillMarketCleanupDotCraftInstallRequest): Promise<void>
      }
      window: {
        setTitle(title: string): void
        setTitleBarOverlayTheme(theme: 'dark' | 'light'): Promise<void>
        minimize(): Promise<void>
        toggleMaximize(): Promise<boolean>
        close(): Promise<void>
        isMaximized(): Promise<boolean>
        rendererReadyForShow(): void
        onMaximizedChange(callback: (maximized: boolean) => void): () => void
        getWorkspacePath(): Promise<string>
        onOpenChromeSettings(callback: () => void): () => void
        onOpenWhatsNew(callback: () => void): () => void
        onOpenThread(callback: (payload: { threadId: string }) => void): () => void
      }
      shell: {
        openPath(path: string): Promise<string>
        /** Opens allowed URLs in the OS default handler (validated in main process). */
        openExternal(url: string): Promise<void>
        /** Opens an App Binding handoff, silently invoking loopback HTTP handoffs in main process. */
        openAppHandoff(url: string): Promise<void>
        getProtocolHandlerName(protocol: string): Promise<string>
        listEditors(): Promise<EditorInfo[]>
        launchEditor(id: EditorId, targetPath: string): Promise<void>
        launchLocalPathInEditor(id: EditorId, targetPath: string): Promise<void>
        openLocalPath(path: string): Promise<void>
        revealLocalPath(path: string): Promise<void>
        showItemInFolder(path: string): Promise<void>
      }
      profile: {
        getGithubIdentity(
          username: string
        ): Promise<{ login: string; name: string | null; avatarDataUrl: string | null } | null>
      }
      chrome: {
        checkSetup(): Promise<{
          extension: unknown
          nativeHost: unknown
          chromeRunning: unknown
          installedBrowsers: unknown
          backend?: unknown
          bridge: unknown
        }>
        installNativeHost(): Promise<unknown>
        openChrome(params?: { url?: string }): Promise<unknown>
      }
      file: {
        writeFile(absPath: string, content: string): Promise<void>
        readFile(absPath: string): Promise<string>
        deleteFile(absPath: string): Promise<void>
        exists(absPath: string): Promise<boolean>
      }
      git: {
        commit(workspacePath: string, files: string[], message: string): Promise<string>
        getBranch(workspacePath: string): Promise<string | null>
      }
      workspace: {
        pickFolder(): Promise<string | null>
        /** Opens the native file picker and returns selected local file paths, including files outside the workspace. */
        pickFiles(): Promise<Array<{ path: string; fileName: string }>>
        /** Returns the absolute local path for a dragged or picked Electron-backed File. */
        getPathForFile(file: File): string
        switch(newPath: string): Promise<void>
        clearSelection(): Promise<void>
        getRecent(): Promise<Array<{ path: string; name: string; lastOpenedAt: string }>>
        clearRecent(): Promise<void>
        getStatus(): Promise<WorkspaceStatusPayload>
        onStatusChange(
          callback: (status: WorkspaceStatusPayload) => void
        ): UnsubscribeFn
        listSetupModels(
          request: WorkspaceSetupModelListRequest
        ): Promise<WorkspaceSetupModelListResult>
        runSetup(request: WorkspaceSetupRequest): Promise<WorkspaceSetupRunResult>
        openNewWindow(): Promise<void>
        checkLock(wsPath: string): Promise<{ locked: boolean; pid?: number }>
        saveImageToTemp(params: { dataUrl: string; fileName?: string }): Promise<{ path: string }>
        readImageAsDataUrl(params: { path: string }): Promise<{ dataUrl: string }>
        searchFiles(params: {
          query: string
          workspacePath: string
          limit?: number
        }): Promise<{
          files: Array<{ name: string; relativePath: string; dir: string }>
          indexStatus?: 'empty' | 'building' | 'ready'
          indexedCount?: number
          stale?: boolean
        }>
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
          }>
          listDir(params: { dirPath?: string }): Promise<{
            dirPath: string
            entries: Array<{
              name: string
              relativePath: string
              absolutePath: string
              isDir: boolean
            }>
          }>
          classify(params: {
            absolutePath: string
          }): Promise<{
            contentClass: 'text' | 'image' | 'pdf' | 'unsupported'
            mime: string
            sizeBytes: number
          }>
          readText(params: {
            absolutePath: string
            limitBytes?: number
          }): Promise<{ text: string; truncated: boolean; encoding: string }>
          authorizeFile(params: { absolutePath: string }): Promise<{ absolutePath: string }>
          toViewerUrl(params: { absolutePath: string }): Promise<{ url: string }>
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
            }>
            destroy(params: { tabId: string }): Promise<void>
            navigate(params: { tabId: string; url: string }): Promise<void>
            back(params: { tabId: string }): Promise<void>
            forward(params: { tabId: string }): Promise<void>
            reload(params: { tabId: string }): Promise<void>
            stop(params: { tabId: string }): Promise<void>
            setBounds(params: {
              tabId: string
              x: number
              y: number
              width: number
              height: number
            }): Promise<void>
            setVisible(params: { tabId: string; visible: boolean }): Promise<void>
            setActive(params: { tabId: string }): Promise<void>
            openExternal(params: { tabId: string }): Promise<void>
            snapshot(params: { tabId: string }): Promise<{
              tabId: string
              currentUrl: string
              title: string
              faviconDataUrl?: string
              canGoBack: boolean
              canGoForward: boolean
              loading: boolean
              } | null>
              onEvent(callback: (event: BrowserEventPayload) => void): UnsubscribeFn
            }
            browserUse: {
              onOpen(callback: (event: BrowserUseOpenPayload) => void): UnsubscribeFn
              onApprovalRequest(callback: (event: BrowserUseApprovalRequestPayload) => void): UnsubscribeFn
              sendApprovalResponse(params: {
                requestId: string
                action: BrowserUseApprovalResponseAction
              }): Promise<void>
              clearCookies(): Promise<{ ok: boolean }>
            }
            terminal: {
            create(params: {
              tabId: string
              threadId: string
              workspacePath: string
              cols: number
              rows: number
            }): Promise<{ tabId: string; pid: number; shell: string; cwd: string }>
            attach(params: { tabId: string }): Promise<{
              tabId: string
              pid: number
              shell: string
              cwd: string
              buffer: string
              exited?: { code: number | null; signal: number | null }
            }>
            write(params: { tabId: string; data: string }): Promise<void>
            resize(params: { tabId: string; cols: number; rows: number }): Promise<void>
            dispose(params: { tabId: string }): Promise<void>
            onData(callback: (event: TerminalDataEventPayload) => void): UnsubscribeFn
            onExit(callback: (event: TerminalExitEventPayload) => void): UnsubscribeFn
          }
        }
      }
      modules: {
        list(): Promise<DiscoveredModule[]>
        userDirectory(): Promise<{ path: string }>
        checkDirectory(path: string): Promise<{ exists: boolean }>
        openFolder(): Promise<{ ok: boolean; error?: string }>
        pickDirectory(): Promise<string | null>
        rescan(): Promise<DiscoveredModule[]>
        setActiveVariant(params: {
          channelName: string
          moduleId: string
        }): Promise<{ ok: boolean; error?: string }>
        readConfig(params: {
          configFileName: string
        }): Promise<{ exists: boolean; config: Record<string, unknown> | null }>
        writeConfig(params: {
          configFileName: string
          config: Record<string, unknown>
        }): Promise<{ ok: boolean }>
        start(params: {
          moduleId: string
        }): Promise<{ ok: boolean; error?: string; missingFields?: string[] }>
        stop(params: { moduleId: string }): Promise<{ ok: boolean; error?: string }>
        running(): Promise<ModuleStatusMap>
        getLogs(moduleId: string): Promise<{ lines: string[] }>
        qrStatus(moduleId: string): Promise<{ active: boolean; qrDataUrl: string | null }>
        onStatusChanged(callback: (statusMap: ModuleStatusMap) => void): UnsubscribeFn
        onQrUpdate(callback: (payload: QrUpdatePayload) => void): UnsubscribeFn
        onRescanSummary(
          callback: (payload: ModulesRescanSummaryPayload) => void
        ): UnsubscribeFn
      }
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
          theme?: 'dark' | 'light'
          locale?: AppLocale
          showThinkingContent?: boolean
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
        }>
        set(
          partial: {
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
          }
        ): Promise<void>
        onPinnedThreadIdsChanged(callback: (payload: PinnedThreadIdsChangedPayload) => void): UnsubscribeFn
      }
      whatsNew: {
        getReleases(): Promise<WhatsNewRelease[]>
        getMediaStates(releaseVersions: string[]): Promise<WhatsNewMediaState[]>
        prefetchMedia(releaseVersions: string[]): Promise<WhatsNewMediaState[]>
        onMediaStateChanged(callback: (state: WhatsNewMediaState) => void): UnsubscribeFn
      }
      updates: {
        getState(): Promise<AppUpdateState>
        check(): Promise<AppUpdateState>
        downloadAndInstall(): Promise<AppUpdateState>
        onStateChanged(callback: (state: AppUpdateState) => void): UnsubscribeFn
      }
      remoteServers: {
        list(): Promise<RemoteHost[]>
        sshConfig(): Promise<LocalSshConfigInfo>
        create(input: {
          name: string
          sshTarget: string
          identityFile?: string
          stacks?: RemoteStack[]
        }): Promise<RemoteHost>
        update(id: string, patch: Partial<Omit<RemoteHost, 'id'>>): Promise<RemoteHost>
        delete(id: string): Promise<{ ok: boolean }>
        test(input: {
          id?: string
          draft?: { name?: string; sshTarget?: string; identityFile?: string }
        }): Promise<SshTestResult>
        listStacks(hostId: string): Promise<RemoteStack[]>
        discoverStacks(hostId: string): Promise<DiscoveredStack[]>
        status(hostId: string, stackId: string): Promise<RemoteStackStatus>
        logs(
          hostId: string,
          stackId: string,
          options?: { service?: string; tail?: number }
        ): Promise<{ text: string; service?: string; tail: number }>
        action(
          hostId: string,
          stackId: string,
          action: RemoteStackAction
        ): Promise<OperationResult>
        openInDesktop(
          hostId: string,
          stackId: string
        ): Promise<{ ok: boolean; hostId: string; stackId: string; localPort: number }>
        openDashboard(hostId: string, stackId: string): Promise<{ ok: boolean; localPort: number }>
        disconnect(hostId: string, stackId: string): Promise<{ ok: boolean }>
      }
    }
  }
}

export {}
