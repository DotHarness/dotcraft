import { app, Menu, nativeImage, Notification, shell, Tray, type MenuItemConstructorOptions } from 'electron'
import { spawn } from 'child_process'
import { basename, join } from 'path'
import { existsSync } from 'fs'
import { HubClient, type HubAppServerResponse, type HubEvent } from './HubClient'
import {
  getRecentWorkspaces,
  loadSettings,
  normalizeShowInMenuBar,
  resolveTaskCompletionNotificationMode,
  type AppSettings,
  type RecentWorkspace
} from './settings'
import { getTrayLockPid, isProcessAlive, tryAcquireTrayLock, type TrayLockHandle } from './trayLock'
import { DEFAULT_LOCALE, normalizeLocale, translate, type AppLocale } from '../shared/locales'
import { resolveDotCraftRuntimeTools } from './ripgrepRuntime'
import { checkWorkspaceLock } from './workspaceLock'
import { requestWorkspaceActivation, requestWorkspaceWindowState } from './desktopActivation'
import { getDesktopActivationEndpoint } from './desktopActivationLock'
import {
  buildWorkspaceOpenDeepLink,
  parseWorkspaceOpenDeepLink
} from './desktopDeepLink'
import { NO_WORKSPACE_ARG } from './workspaceArgs'

interface TrayState {
  appServers: HubAppServerResponse[]
  recentWorkspaces: RecentWorkspace[]
  locale: AppLocale
}

const REFRESH_INTERVAL_MS = 5_000

export function shouldRunTrayProcess(
  settings: AppSettings,
  platform: NodeJS.Platform = process.platform
): boolean {
  if (platform !== 'darwin') return true
  return normalizeShowInMenuBar(settings) !== false
}

interface HubNotificationPayload {
  kind?: string | null
  workspacePath?: string | null
  threadId?: string | null
  titleKey?: string | null
  bodyKey?: string | null
  params?: unknown
  title?: string
  body?: string | null
  actionUrl?: string | null
  openDesktopOnClick?: boolean | null
}

function notificationVars(value: unknown): Record<string, string | number> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const vars: Record<string, string | number> = {}
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    if (typeof raw === 'string' || typeof raw === 'number') vars[key] = raw
    else if (typeof raw === 'boolean') vars[key] = raw ? 'true' : 'false'
  }
  return Object.keys(vars).length > 0 ? vars : undefined
}

function resolveNotificationText(
  locale: AppLocale,
  key: unknown,
  params: unknown,
  fallback: unknown,
  legacy: unknown
): string | null {
  const fallbackText =
    typeof fallback === 'string'
      ? fallback
      : typeof legacy === 'string'
        ? legacy
        : null
  if (typeof key !== 'string' || key.trim() === '') return fallbackText
  const normalizedKey = key.trim()
  const localized = translate(locale, normalizedKey, notificationVars(params))
  return localized === normalizedKey ? fallbackText : localized
}

export function resolveTrayIconPath(platform: NodeJS.Platform = process.platform): string | null {
  const basePath = app.isPackaged && typeof process.resourcesPath === 'string'
    ? process.resourcesPath
    : join(__dirname, '../../resources')
  const candidates = platform === 'win32'
    ? ['tray-icon.png', 'icon.ico', 'icon.png']
    : platform === 'darwin'
      ? ['tray-icon-macTemplate.png', 'tray-icon.png', 'icon.png']
      : ['tray-icon.png', 'icon.png']

  for (const candidate of candidates) {
    const path = join(basePath, candidate)
    if (existsSync(path)) return path
  }

  return null
}

export function resolveNotificationIconPath(platform: NodeJS.Platform = process.platform): string | null {
  const basePath = app.isPackaged && typeof process.resourcesPath === 'string'
    ? process.resourcesPath
    : join(__dirname, '../../resources')
  const candidates = platform === 'win32'
    ? ['icon.png', 'icon.ico', 'tray-icon.png']
    : ['icon.png', 'tray-icon.png']

  for (const candidate of candidates) {
    const path = join(basePath, candidate)
    if (existsSync(path)) return path
  }

  return null
}

function createTrayIcon(): Electron.NativeImage {
  const iconPath = resolveTrayIconPath()
  if (!iconPath) {
    return nativeImage.createEmpty()
  }

  const icon = nativeImage.createFromPath(iconPath)
  if (process.platform === 'darwin') {
    icon.setTemplateImage(true)
    return icon.resize({ height: 18 })
  }

  return icon
}

function stripArgPair(argv: string[], name: string): string[] {
  const result: string[] = []
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === name) {
      i++
      continue
    }
    result.push(argv[i])
  }
  return result
}

function baseDesktopArgs(): string[] {
  return stripArgPair(
    stripArgPair(
      process.argv.slice(1).filter((arg) => (
        arg !== '--tray' &&
        arg !== NO_WORKSPACE_ARG &&
        !parseWorkspaceOpenDeepLink(arg)
      )),
      '--workspace'
    ),
    '--remote'
  )
}

export function spawnDesktopWindow(workspacePath?: string, threadId?: string | null): void {
  const args = baseDesktopArgs()
  if (workspacePath && threadId?.trim()) {
    args.push(buildWorkspaceOpenDeepLink(workspacePath, threadId))
  } else if (workspacePath) {
    args.push('--workspace', workspacePath)
  } else {
    args.push(NO_WORKSPACE_ARG)
  }
  const child = spawn(process.execPath, args, {
    detached: true,
    stdio: 'ignore'
  })
  child.unref()
}

export async function openDesktopWindow(workspacePath?: string | null, threadId?: string | null): Promise<void> {
  const path = workspacePath?.trim()
  if (path) {
    if (process.platform === 'darwin') {
      const desktopActivation = getDesktopActivationEndpoint()
      if (desktopActivation) {
        const activated = await requestWorkspaceActivation(desktopActivation, {
          workspacePath: path,
          threadId: threadId ?? null
        })
        if (activated) return
      }
    }

    const lock = checkWorkspaceLock(path)
    if (lock.activation) {
      const activated = await requestWorkspaceActivation(lock.activation, {
        workspacePath: path,
        threadId: threadId ?? null
      })
      if (activated) return
    }
  }

  spawnDesktopWindow(path || undefined, threadId)
}

export async function openMostRecentWorkspaceFromTray(settings: AppSettings = loadSettings()): Promise<boolean> {
  const recentPath = getRecentWorkspaces(settings).find((workspace) => workspace.path.trim())?.path.trim()
  const workspacePath = recentPath || settings.lastWorkspacePath?.trim()
  if (!workspacePath) return false

  await openDesktopWindow(workspacePath)
  return true
}

async function openNotificationAction(payload: HubNotificationPayload): Promise<void> {
  const actionUrl = payload.actionUrl?.trim()
  if (actionUrl) {
    const workspaceOpen = parseWorkspaceOpenDeepLink(actionUrl)
    if (workspaceOpen) {
      if (payload.openDesktopOnClick === false) return
      await openDesktopWindow(workspaceOpen.workspacePath, workspaceOpen.threadId)
      return
    }

    await shell.openExternal(actionUrl)
    return
  }

  if (payload.openDesktopOnClick === false) return
  await openDesktopWindow(payload.workspacePath ?? undefined, payload.threadId ?? undefined)
}

function displayWorkspaceName(path: string): string {
  return basename(path) || path
}

function dashboardUrlOf(server: HubAppServerResponse): string | null {
  return server.endpoints.dashboard ?? server.serviceStatus.dashboard?.url ?? null
}

function buildAppServerMenu(
  server: HubAppServerResponse,
  hubClient: HubClient,
  refresh: () => void,
  locale: AppLocale
): MenuItemConstructorOptions {
  const L = (key: string) => translate(locale, key)
  const workspacePath = server.canonicalWorkspacePath || server.workspacePath
  const dashboardUrl = dashboardUrlOf(server)
  const running = server.state === 'running'
  return {
    label: `${displayWorkspaceName(workspacePath)} (${server.state})`,
    submenu: [
      {
        label: L('tray.openDesktop'),
        click: () => {
          void openDesktopWindow(workspacePath)
        }
      },
      {
        label: L('tray.openDashboard'),
        enabled: Boolean(dashboardUrl),
        click: async () => {
          if (dashboardUrl) await shell.openExternal(dashboardUrl)
        }
      },
      { type: 'separator' },
      {
        label: L('tray.restartAppServer'),
        enabled: Boolean(workspacePath),
        click: async () => {
          await hubClient.restartAppServer(workspacePath, resolveDotCraftRuntimeTools())
          refresh()
        }
      },
      {
        label: L('tray.stopAppServer'),
        enabled: running,
        click: async () => {
          await hubClient.stopAppServer(workspacePath)
          refresh()
        }
      }
    ]
  }
}

function buildRecentMenu(recent: RecentWorkspace[], locale: AppLocale): MenuItemConstructorOptions {
  const L = (key: string) => translate(locale, key)
  const items = recent.slice(0, 8).map((workspace) => ({
    label: workspace.name || displayWorkspaceName(workspace.path),
    click: () => {
      void openDesktopWindow(workspace.path)
    }
  }))
  return {
    label: L('tray.recent'),
    enabled: items.length > 0,
    submenu: items.length > 0 ? items : [{ label: L('tray.noRecentWorkspaces'), enabled: false }]
  }
}

function buildTrayMenu(
  state: TrayState,
  hubClient: HubClient,
  refresh: () => void,
  exitAll: () => Promise<void>
): Menu {
  const L = (key: string) => translate(state.locale, key)
  const appServerItems = state.appServers.length > 0
    ? state.appServers.map((server) => buildAppServerMenu(server, hubClient, refresh, state.locale))
    : [{ label: L('tray.noManagedAppServers'), enabled: false } satisfies MenuItemConstructorOptions]

  const template: MenuItemConstructorOptions[] = [
    { label: L('tray.hub'), enabled: false },
    { type: 'separator' },
    {
      label: L('tray.newChat'),
      click: () => spawnDesktopWindow()
    },
    buildRecentMenu(state.recentWorkspaces, state.locale),
    { type: 'separator' },
    {
      label: L('tray.appServers'),
      submenu: appServerItems
    },
    { type: 'separator' },
    {
      label: L('tray.refresh'),
      click: refresh
    },
    {
      label: L('tray.exit'),
      click: () => {
        void exitAll()
      }
    }
  ]

  return Menu.buildFromTemplate(template)
}

export function parseHubNotificationPayload(
  event: HubEvent,
  locale: AppLocale = DEFAULT_LOCALE
): HubNotificationPayload | null {
  if (event.kind !== 'notification.requested' || !event.data || typeof event.data !== 'object') {
    return null
  }

  const data = event.data as Record<string, unknown>
  const title = (resolveNotificationText(
    locale,
    data.titleKey,
    data.params,
    data.fallbackTitle,
    data.title
  ) ?? '').trim()
  if (!title) return null
  const body = resolveNotificationText(
    locale,
    data.bodyKey,
    data.params,
    data.fallbackBody,
    data.body
  )

  return {
    kind: typeof data.kind === 'string' ? data.kind : null,
    workspacePath: typeof data.workspacePath === 'string' ? data.workspacePath : event.workspacePath,
    threadId: typeof data.threadId === 'string' ? data.threadId : null,
    titleKey: typeof data.titleKey === 'string' ? data.titleKey : null,
    bodyKey: typeof data.bodyKey === 'string' ? data.bodyKey : null,
    params: data.params,
    title,
    body,
    actionUrl: typeof data.actionUrl === 'string' ? data.actionUrl : null,
    openDesktopOnClick: typeof data.openDesktopOnClick === 'boolean' ? data.openDesktopOnClick : null
  }
}

function isTurnResultNotification(payload: HubNotificationPayload): boolean {
  return payload.kind === 'turnCompleted' || payload.kind === 'turnFailed'
}

async function shouldShowTurnResultNotification(
  payload: HubNotificationPayload,
  settings?: AppSettings
): Promise<boolean> {
  const mode = resolveTaskCompletionNotificationMode(settings)
  if (mode === 'never') return false
  if (mode === 'always') return true

  const workspacePath = payload.workspacePath?.trim()
  if (!workspacePath) return true

  const lock = checkWorkspaceLock(workspacePath)
  if (!lock.activation) return true

  const state = await requestWorkspaceWindowState(lock.activation, workspacePath)
  return state?.focused !== true
}

async function shouldShowHubNotification(
  payload: HubNotificationPayload,
  settings?: AppSettings
): Promise<boolean> {
  if (!isTurnResultNotification(payload)) return true
  return await shouldShowTurnResultNotification(payload, settings)
}

function showHubNotificationPayload(payload: HubNotificationPayload): boolean {
  if (!payload.title || !Notification.isSupported()) return false

  const notification = new Notification({
    title: payload.title,
    body: payload.body ?? undefined,
    icon: resolveNotificationIconPath() ?? undefined
  })

  notification.on('click', () => {
    void openNotificationAction(payload)
  })
  notification.show()
  return true
}

export function showHubNotification(event: HubEvent, locale: AppLocale = DEFAULT_LOCALE): boolean {
  const payload = parseHubNotificationPayload(event, locale)
  return payload ? showHubNotificationPayload(payload) : false
}

export async function showHubNotificationForSettings(
  event: HubEvent,
  settings?: AppSettings,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<boolean> {
  const payload = parseHubNotificationPayload(event, locale)
  if (!payload) return false
  if (!await shouldShowHubNotification(payload, settings)) return false
  return showHubNotificationPayload(payload)
}

export async function runTrayProcess(): Promise<void> {
  if (!shouldRunTrayProcess(loadSettings())) {
    app.quit()
    return
  }

  const lock = tryAcquireTrayLock()
  if (!lock) {
    app.quit()
    return
  }

  let tray: Tray | null = new Tray(createTrayIcon())
  let settings: AppSettings = loadSettings()
  const hubClient = new HubClient({
    binarySource: settings.binarySource,
    binaryPath: settings.appServerBinaryPath,
    preferDevBuild: import.meta.env.DEV,
    requireDevBuild: import.meta.env.DEV,
    ...(import.meta.env.DEV ? { restartMismatchedHub: true } : {})
  })
  let eventAbortController: AbortController | null = null
  let refreshTimer: ReturnType<typeof setInterval> | null = null
  let disposed = false
  let openRecentInFlight = false

  tray.on('click', () => {
    if (disposed || openRecentInFlight) return
    openRecentInFlight = true
    void openMostRecentWorkspaceFromTray(loadSettings()).catch(() => {
      // Non-fatal: tray clicks should not crash the background process.
    }).finally(() => {
      openRecentInFlight = false
    })
  })

  const setMenu = (state: TrayState): void => {
    if (!tray) return
    tray.setToolTip(translate(state.locale, 'tray.hub'))
    tray.setContextMenu(buildTrayMenu(state, hubClient, () => {
      void refresh()
    }, exitAll))
  }

  const refresh = async (): Promise<void> => {
    if (disposed) return
    settings = loadSettings()
    if (!shouldRunTrayProcess(settings)) {
      app.quit()
      return
    }
    try {
      const [, appServers] = await Promise.all([
        hubClient.getStatus(),
        hubClient.listAppServers()
      ])
      setMenu({
        appServers,
        recentWorkspaces: getRecentWorkspaces(settings),
        locale: normalizeLocale(settings.locale)
      })
      subscribeEvents()
    } catch {
      setMenu({
        appServers: [],
        recentWorkspaces: getRecentWorkspaces(settings),
        locale: normalizeLocale(settings.locale)
      })
    }
  }

  const subscribeEvents = (): void => {
    if (eventAbortController) return
    const controller = new AbortController()
    eventAbortController = controller
    void hubClient.subscribeEvents((event: HubEvent) => {
      void showHubNotificationForSettings(event, settings, normalizeLocale(settings.locale))
      void refresh()
    }, controller.signal).then(() => {
      eventAbortController = null
    }).catch(() => {
      eventAbortController = null
    })
  }

  async function exitAll(): Promise<void> {
    disposed = true
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
    eventAbortController?.abort()
    eventAbortController = null
    try {
      await hubClient.shutdownHub()
    } catch {
      // Hub may already be stopped.
    }
    app.quit()
  }

  const cleanup = (lockHandle: TrayLockHandle): void => {
    disposed = true
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
    eventAbortController?.abort()
    eventAbortController = null
    tray?.destroy()
    tray = null
    lockHandle.release()
  }

  app.on('before-quit', () => cleanup(lock))

  setMenu({
    appServers: [],
    recentWorkspaces: getRecentWorkspaces(settings),
    locale: normalizeLocale(settings.locale)
  })
  await refresh()
  refreshTimer = setInterval(() => {
    void refresh()
  }, REFRESH_INTERVAL_MS)
}

export function stopTrayProcess(): void {
  const pid = getTrayLockPid()
  if (pid == null || pid === process.pid || !isProcessAlive(pid)) {
    return
  }

  try {
    process.kill(pid)
  } catch {
    // Ignore shutdown races.
  }
}

export function ensureTrayProcess(settings: AppSettings = loadSettings()): void {
  if (process.argv.includes('--tray')) return
  if (!shouldRunTrayProcess(settings)) return

  const args = baseDesktopArgs()
  args.push('--tray')
  const child = spawn(process.execPath, args, {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  })
  child.unref()
}
