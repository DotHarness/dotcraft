import { hostname, release } from 'os'
import { app, BrowserWindow, nativeTheme } from 'electron'
import { join } from 'node:path'
import { normalizeNewTaskPromptMode } from '../shared/threadTasks'
import { resolveBinaryLocation } from './AppServerManager'
import { AppUpdateService } from './appUpdate'
import { browserUseManager } from './browserUseManager'
import { disposeComputerUse, setComputerUseRuntimeHost } from './computerUse/runtime'
import type { DesktopApplicationServices } from './desktopApplicationServices'
import { createDesktopHubClient, resolveDesktopBinarySource, type DesktopHubClient } from './desktopHub'
import { createDesktopLifecycle } from './desktopLifecycle'
import { refreshDesktopMenu, registerDesktopApplicationIpc } from './desktopMenu'
import { DesktopUpdatePreparation } from './desktopUpdatePreparation'
import { DesktopWindowManager } from './desktopWindowManager'
import { LocalProjectList } from './localProjects'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'
import { isLocalDirectory } from './newThreadDeepLink'
import { restoreRecentProjects } from './recentProjectRestore'
import { getWorkspaceStatus } from './workspaceSetup'
import { disposeCodexRemoteHubBridge } from './codexRemote/codexRemoteHubBridge'
import { disposeMobileHubBridge } from './mobile/mobileHubBridge'
import { broadcastPairedComputerEvent } from './pairedComputers/pairedComputersIpc'
import { PairedComputersManager } from './pairedComputers/pairedComputersManager'
import { createFileStorage, createSafeStorageVault } from './pairedComputers/pairedComputersStorage'
import { openPinnedWebSocket, pinnedRequest } from './pairedComputers/pinnedTransport'
import { applyNativeThemeSource } from './nativeThemeSource'
import { nodeReplManager } from './nodeReplManager'
import { disposeSatellitesHubBridge } from './satellites/satellitesHubBridge'
import { closeAllScreenViews } from './screenView/screenViewManager'
import { loadSettings, saveSettings, type AppSettings } from './settings'
import { mergeUpdatedSettings } from './settingsMerge'
import { createDesktopThreadTasks } from './threadTasks/threadTasksIpc'
import { createDesktopTray, type DesktopTray } from './trayManager'
import { refreshVoiceChatGptAvailability, shutdownVoiceService } from './voice/voiceIpc'
import { WhatsNewCatalog } from './whatsNewCatalog'
import { resolveWhatsNewMediaAssets, WhatsNewMediaCache } from './whatsNewMediaCache'
import { createWindowAttentionNotifier } from './windowAttentionNotifier'
import type { DesktopWindowRuntime } from './windows/windowRuntime'
import { applyNativeChromeTheme, resolveInitialTheme, resolveThemeSurface } from './windowTheme'
import { hasRemoteEndpointArg, NO_WORKSPACE_ARG } from './workspaceArgs'

const WINDOW_CONNECTION_SETTINGS = new Set<keyof AppSettings>([
  'connectionMode',
  'remote',
  'activeRemoteProject',
  'activeRemoteDevice',
  'activeRemoteStack'
])

export class DesktopApplication implements DesktopApplicationServices {
  settings = loadSettings()
  readonly lifecycle = createDesktopLifecycle()
  readonly manager: DesktopWindowManager = new DesktopWindowManager(this)
  readonly windows: Map<number, DesktopWindowRuntime> = this.manager.windows
  readonly notifyWindowAttention = createWindowAttentionNotifier(() =>
    Promise.resolve(this.tray?.visible === true)
  )
  readonly localProjects = new LocalProjectList(
    () => this.createHubClient(this.settings),
    () => {
      for (const window of this.windows.values()) {
        window.emitWorkspaceProjects()
        if (!window.runtimeIsStopping()) window.scheduleSecondaryWorkspaceRefresh()
      }
    },
    { isActive: () => !this.lifecycle.isStopping }
  )
  readonly tasks = createDesktopThreadTasks({
    filePath: join(app.getPath('userData'), 'thread-tasks.json'),
    getNewTaskPrompt: () => normalizeNewTaskPromptMode(this.settings.newTaskPrompt),
    notifyOffer: (task, ownerId) => {
      if (ownerId !== undefined) this.windows.get(ownerId)?.notifyNewTaskOffer(task)
    }
  })
  private readonly hubs = new Map<string, DesktopHubClient>()
  private readonly catalog = new WhatsNewCatalog()
  private media: WhatsNewMediaCache | null = null
  private updater: AppUpdateService | null = null
  private tray: DesktopTray | null = null
  private updateTimer: ReturnType<typeof setTimeout> | null = null
  private pairedComputersManager: PairedComputersManager | null = null
  private disposing: Promise<void> | null = null

  constructor() {
    browserUseManager.setPolicyHost({
      getSettings: () => this.settings,
      updateSettings: this.updateSharedSettings
    })
    setComputerUseRuntimeHost({ getSettings: () => this.settings, updateSettings: this.updateSharedSettings })
  }

  async start(argv: string[]): Promise<void> {
    registerDesktopApplicationIpc(this)
    applyNativeThemeSource(nativeTheme, this.settings)
    this.refreshAppMenu()
    this.tray = createDesktopTray({
      getSettings: () => this.settings,
      getHubClient: () => this.createHubClient(this.settings),
      openWindow: this.openDesktopWindow,
      newWindow: this.openNewWindow,
      isWorkspaceFocused: (workspace) =>
        [...this.windows.values()].some(
          (window) => window.currentWorkspacePath === workspace && window.mainWindow?.isFocused()
        ),
      exit: this.exitFromTray
    })
    await this.manager.routeLaunch(argv, true)
    if (!hasRemoteEndpointArg(argv)) this.localProjects.start()
    void this.tray.refresh()
    this.updateTimer = setTimeout(() => this.getAppUpdateService().start(), 1_200)
    this.updateTimer.unref()
  }

  async restoreRecentProjects(window: DesktopWindowRuntime): Promise<void> {
    if (window !== this.manager.initialWindow || window.startupProjectRestore !== 'pending' ||
      window.lastConnectionStatus.status !== 'connected') return
    window.startupProjectRestore = 'running'
    const isActive = (): boolean => window.startupProjectRestore === 'running' &&
      !window.runtimeIsStopping() && Boolean(window.currentWorkspacePath) &&
      !hasRemoteEndpointArg(window.argv) && !window.argv.includes(NO_WORKSPACE_ARG) &&
      !window.activeRemoteWorkspace && window.resolveConnectionMode(window.settings) === 'local'
    try {
      await restoreRecentProjects({
        foregroundPath: window.currentWorkspacePath,
        isChatWorkspace: isDefaultChatWorkspace,
        isActive,
        projects: this.localProjects,
        isEligible: (path) => isLocalDirectory(path) && getWorkspaceStatus(path).status === 'ready',
        startProject: (path) => window.startLocalProjectConnection(path, isActive),
        setProjectState: (path, state) => {
          const key = window.normalizeWorkspaceConnectionKey(path)
          if (state) window.startupProjectStates.set(key, state)
          else window.startupProjectStates.delete(key)
          if (!window.runtimeIsStopping()) window.emitWorkspaceProjects()
        }
      })
    } catch (error) {
      console.warn('[desktop] recent project startup restore failed', error)
    } finally {
      window.startupProjectRestore = undefined
    }
  }

  get pairedComputers(): PairedComputersManager {
    this.pairedComputersManager ??= new PairedComputersManager({
      storage: createFileStorage(),
      vault: createSafeStorageVault(),
      native: { request: pinnedRequest },
      openSocket: openPinnedWebSocket,
      ownComputerId: async () => (await this.createHubClient(this.settings).getMobile()).computerId ?? null,
      appVersion: app.getVersion(),
      enforceVersion: app.isPackaged,
      device: {
        displayName: hostname(),
        platform: process.platform === 'win32' ? 'windows' : process.platform === 'darwin' ? 'macos' : 'linux',
        osVersion: release()
      },
      onEvent: (event) => {
        broadcastPairedComputerEvent(event)
        if (event.kind !== 'changed') return
        for (const window of this.windows.values()) {
          window.handlePairedComputerProjectsChanged(event.computers)
          window.emitWorkspaceProjects()
        }
      },
      onRemoved: (computerId) => {
        for (const window of this.windows.values()) window.handlePairedComputerRemoved(computerId)
      }
    })
    return this.pairedComputersManager
  }

  createHubClient = (settings: AppSettings): DesktopHubClient => {
    const key = JSON.stringify([settings.binarySource, settings.appServerBinaryPath])
    let hub = this.hubs.get(key)
    if (!hub) {
      hub = createDesktopHubClient(settings, {
        preferDevBuild: import.meta.env.DEV,
        requireDevBuild: import.meta.env.DEV,
        lifecycle: this.lifecycle
      })
      this.hubs.set(key, hub)
    }
    return hub
  }

  updateSharedSettings = async (
    partial: Partial<AppSettings>,
    owner?: DesktopWindowRuntime
  ): Promise<void> => {
    Object.assign(this.settings, mergeUpdatedSettings(this.settings, partial))
    saveSettings(this.settings)
    const draftOnly = Object.keys(partial).length === 1 && partial.composerTextDrafts !== undefined
    for (const window of this.windows.values()) {
      const update =
        window === owner
          ? partial
          : Object.fromEntries(
              Object.entries(partial).filter(
                ([key]) => !WINDOW_CONNECTION_SETTINGS.has(key as keyof AppSettings)
              )
            )
      Object.assign(window.settings, mergeUpdatedSettings(window.settings, update))
      const win = window.mainWindow
      if (win && !win.isDestroyed() && (partial.theme !== undefined || partial.themeSeeds !== undefined)) {
        const theme = resolveInitialTheme(this.settings, nativeTheme.shouldUseDarkColors)
        applyNativeChromeTheme(win, theme, resolveThemeSurface(this.settings, theme))
      }
      if (!draftOnly) window.emitWorkspaceProjects()
    }
    if (draftOnly) return
    if (partial.theme !== undefined) applyNativeThemeSource(nativeTheme, this.settings)
    if (partial.voice !== undefined) refreshVoiceChatGptAvailability()
    if (!this.lifecycle.isStopping) void this.tray?.refresh()
    this.refreshAppMenu()
  }

  persistWindowSettings = (settings: AppSettings): void => {
    Object.assign(this.settings, settings)
    saveSettings(this.settings)
    for (const window of this.windows.values()) {
      if (window.settings === settings) continue
      const shared = Object.fromEntries(
        Object.entries(this.settings).filter(
          ([key]) => !WINDOW_CONNECTION_SETTINGS.has(key as keyof AppSettings)
        )
      )
      Object.assign(window.settings, shared)
    }
  }

  hasTray(): boolean {
    return this.tray?.visible === true
  }

  focusedWindow = (): DesktopWindowRuntime | undefined => this.manager.focused()
  windowFor = (window: BrowserWindow): DesktopWindowRuntime | undefined =>
    this.windows.get(window.webContents.id)
  openNewWindow = (): void => {
    this.manager.open([NO_WORKSPACE_ARG])
  }
  openDesktopWindow = (workspacePath?: string | null, threadId?: string | null): Promise<void> =>
    this.manager.openWorkspace(workspacePath, threadId)
  refreshAppMenu = (): void => refreshDesktopMenu(this)
  getWhatsNewCatalog = (): WhatsNewCatalog => this.catalog
  getWhatsNewMediaCache = (): WhatsNewMediaCache =>
    (this.media ??= new WhatsNewMediaCache({
      assetResolver: async (versions) =>
        resolveWhatsNewMediaAssets(await this.catalog.getReleases(), versions),
      onStateChanged: (state) => this.broadcast('app:whats-new-media-state-changed', state)
    }))
  broadcastPinnedThreadIdsChanged = (payload: { workspacePath: string; threadIds: string[] }): void =>
    this.broadcast('settings:pinned-thread-ids-changed', payload)

  getAppUpdateService = (): AppUpdateService =>
    (this.updater ??= new AppUpdateService({
      onStateChanged: (state) => this.broadcast('app:update-state-changed', state),
      beforeInstall: async () => {
        const runtimeExecutable =
          resolveDesktopBinarySource(this.settings) === 'bundled'
            ? (resolveBinaryLocation({ binarySource: 'bundled' }).path ?? undefined)
            : undefined
        await new DesktopUpdatePreparation({
          lifecycle: this.lifecycle,
          runtimeExecutable,
          stopBackgroundWork: this.stopBackgroundWork,
          disposeRuntime: () => this.manager.dispose()
        }).prepare()
      }
    }))

  private broadcast(channel: string, payload: unknown): void {
    for (const window of this.windows.values())
      if (window.mainWindow && !window.mainWindow.isDestroyed())
        window.mainWindow.webContents.send(channel, payload)
  }

  private stopBackgroundWork = (): void => {
    if (this.updateTimer) clearTimeout(this.updateTimer)
    this.updateTimer = null
    this.tray?.dispose()
    this.localProjects.stop()
    disposeCodexRemoteHubBridge()
    disposeMobileHubBridge()
    disposeSatellitesHubBridge()
    this.pairedComputersManager?.dispose()
  }

  private exitFromTray = async (): Promise<void> => {
    this.lifecycle.stop()
    this.stopBackgroundWork()
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([
        Promise.allSettled([this.manager.dispose(), createDesktopHubClient(this.settings).shutdownHub()]),
        new Promise<void>((resolve) => {
          timer = setTimeout(resolve, 5_000)
        })
      ])
    } finally {
      if (timer) clearTimeout(timer)
      app.quit()
    }
  }

  dispose(): Promise<void> {
    return (this.disposing ??= (async () => {
      this.lifecycle.stop()
      this.stopBackgroundWork()
      await Promise.allSettled([
        this.manager.dispose(),
        shutdownVoiceService(),
        nodeReplManager.disposeAll(),
        disposeComputerUse()
      ])
      closeAllScreenViews()
    })())
  }
}
