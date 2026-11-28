import { app, Menu, MenuItem, nativeImage, shell, Tray, type MenuItemConstructorOptions } from 'electron'
import { basename, join } from 'node:path'
import { existsSync } from 'node:fs'
import { normalizeLocale, translate, type AppLocale } from '../shared/locales'
import { normalizeShowInMenuBar, type AppSettings } from './settings'
import type { DesktopHubClient, HubAppServerResponse } from './desktopHub'
import { resolveDotCraftRuntimeTools } from './ripgrepRuntime'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'
import {
  TrayRecentThreadCatalog,
  DIRECT_RECENT_THREAD_COUNT,
  RECENT_THREAD_TITLE_MAX_LENGTH,
  type TrayRecentThread
} from './trayRecentThreads'
import { showHubNotification } from './trayNotifications'
import { createTrayInlineLabels } from './trayInlineLabels'

export interface DesktopTrayHost {
  getSettings(): AppSettings
  getHubClient(): DesktopHubClient
  openWindow(workspacePath?: string | null, threadId?: string | null): Promise<void>
  newWindow(): void
  isWorkspaceFocused(workspacePath: string): boolean
  isViewingThread(workspacePath: string, threadId: string): boolean
  exit(): Promise<void>
}

export interface DesktopTray {
  readonly visible: boolean
  refresh(): Promise<void>
  dispose(): void
}

export function shouldShowDesktopTray(
  settings: AppSettings,
  platform: NodeJS.Platform = process.platform
): boolean {
  return platform !== 'darwin' || normalizeShowInMenuBar(settings) !== false
}

export function resolveTrayIconPath(platform: NodeJS.Platform = process.platform): string | null {
  const root = app.isPackaged ? process.resourcesPath : join(__dirname, '../../resources')
  const names =
    platform === 'win32'
      ? ['icon.ico', 'icon.png']
      : platform === 'darwin'
        ? ['icon-macTemplate.png', 'icon.png']
        : ['icon.png']
  return names.map((name) => join(root, name)).find(existsSync) ?? null
}

export function trayWorkspaceName(path: string, locale: AppLocale): string {
  return isDefaultChatWorkspace(path) ? translate(locale, 'chatsRail.title') : basename(path) || path
}

function recentItem(
  thread: TrayRecentThread,
  locale: AppLocale,
  host: DesktopTrayHost
): MenuItem {
  const title = Array.from(thread.displayName || translate(locale, 'sidebar.newConversation'))
  return new MenuItem({
    label:
      title.length <= RECENT_THREAD_TITLE_MAX_LENGTH
        ? title.join('')
        : `${title
            .slice(0, RECENT_THREAD_TITLE_MAX_LENGTH - 1)
            .join('')
            .trimEnd()}…`,
    sublabel: isDefaultChatWorkspace(thread.workspacePath)
      ? translate(locale, 'chatsRail.title')
      : thread.workspaceName,
    click: () => {
      void host.openWindow(thread.workspacePath, thread.id)
    }
  })
}

function serverItem(
  server: HubAppServerResponse,
  locale: AppLocale,
  host: DesktopTrayHost,
  refresh: () => void
): MenuItemConstructorOptions {
  const t = (key: string): string => translate(locale, key)
  const workspace = server.canonicalWorkspacePath || server.workspacePath
  const dashboard = server.endpoints.dashboard ?? server.serviceStatus.dashboard?.url
  return {
    label: `${trayWorkspaceName(workspace, locale)} (${server.state})`,
    submenu: [
      {
        label: t('tray.openDesktop'),
        click: () => {
          void host.openWindow(workspace)
        }
      },
      {
        label: t('tray.openDashboard'),
        enabled: Boolean(dashboard),
        click: async () => {
          if (dashboard) await shell.openExternal(dashboard)
        }
      },
      { type: 'separator' },
      {
        label: t('tray.restartAppServer'),
        enabled: Boolean(workspace),
        click: async () => {
          await host.getHubClient().restartAppServer(workspace, resolveDotCraftRuntimeTools())
          refresh()
        }
      },
      {
        label: t('tray.stopAppServer'),
        enabled: server.state === 'running',
        click: async () => {
          await host.getHubClient().stopAppServer(workspace)
          refresh()
        }
      }
    ]
  }
}

export function createDesktopTray(host: DesktopTrayHost): DesktopTray {
  let tray: Tray | null = null
  let disposed = false
  let refreshPromise: Promise<void> | null = null
  let events: AbortController | null = null
  let timer: ReturnType<typeof setInterval> | null = null
  const recent = new TrayRecentThreadCatalog()
  const updateInlineLabels = createTrayInlineLabels()
  const destroyTray = (): void => {
    tray?.destroy()
    tray = null
    updateInlineLabels([])
  }
  const buildMenu = (servers: HubAppServerResponse[], threads: TrayRecentThread[], online: number): void => {
    if (!tray || disposed) return
    const locale = normalizeLocale(host.getSettings().locale)
    const t = (key: string): string => translate(locale, key)
    const recentItems = threads.map((thread) => recentItem(thread, locale, host))
    const items: (MenuItemConstructorOptions | MenuItem)[] = []
    if (recentItems.length) {
      items.push({ label: t('tray.recent'), enabled: false }, ...recentItems.slice(0, DIRECT_RECENT_THREAD_COUNT))
      if (recentItems.length > DIRECT_RECENT_THREAD_COUNT)
        items.push({
          label: t('tray.more'),
          submenu: Menu.buildFromTemplate(recentItems.slice(DIRECT_RECENT_THREAD_COUNT))
        })
      items.push({ type: 'separator' })
    }
    items.push(
      { label: t('tray.newChat'), click: host.newWindow },
      {
        label: t('tray.appServers'),
        submenu: servers.length
          ? servers.map((server) =>
              serverItem(server, locale, host, () => {
                void refresh()
              })
            )
          : [{ label: t('tray.noManagedAppServers'), enabled: false }]
      }
    )
    if (online)
      items.push({
        label: translate(locale, online === 1 ? 'tray.satellitesOnline.one' : 'tray.satellitesOnline.other', {
          count: online
        }),
        enabled: false
      })
    items.push(
      { type: 'separator' },
      {
        label: t('tray.exit'),
        click: () => {
          void host.exit()
        }
      }
    )
    const menu = Menu.buildFromTemplate(items)
    updateInlineLabels(recentItems.filter((item) => item.sublabel).map((item) => item.commandId))
    tray.setToolTip(t('tray.hub'))
    tray.setContextMenu(menu)
  }
  const subscribe = (): void => {
    if (disposed || events) return
    const controller = new AbortController()
    events = controller
    void host
      .getHubClient()
      .subscribeEvents((event) => {
        if (disposed || !tray) return
        showHubNotification(event, {
          locale: normalizeLocale(host.getSettings().locale),
          settings: host.getSettings(),
          isWorkspaceFocused: host.isWorkspaceFocused,
          isViewingThread: host.isViewingThread,
          openWorkspace: host.openWindow,
          openExternal: (url) => void shell.openExternal(url)
        })
        void refresh()
      }, controller.signal)
      .catch(() => {})
      .finally(() => {
        if (events === controller) events = null
      })
  }
  const refresh = (): Promise<void> => {
    if (disposed) return Promise.resolve()
    if (refreshPromise) return refreshPromise
    refreshPromise = (async () => {
      if (!shouldShowDesktopTray(host.getSettings())) {
        events?.abort()
        events = null
        destroyTray()
        return
      }
      if (!tray) {
        const path = resolveTrayIconPath()
        const icon = path ? nativeImage.createFromPath(path) : nativeImage.createEmpty()
        if (process.platform === 'darwin') icon.setTemplateImage(true)
        tray = new Tray(icon)
        tray.on('click', () => {
          void host
            .getHubClient()
            .listProjects()
            .then((projects) => host.openWindow(projects[0]?.path ?? host.getSettings().lastWorkspacePath))
            .catch(() => host.openWindow())
        })
        buildMenu([], [], 0)
      }
      const hub = host.getHubClient()
      const [servers, satellites] = await Promise.allSettled([hub.listAppServers(), hub.listSatellites()])
      if (disposed || !tray) return
      const appServers = servers.status === 'fulfilled' ? servers.value : []
      const threads = await recent.refresh(appServers)
      if (disposed || !tray) return
      buildMenu(
        appServers,
        threads,
        satellites.status === 'fulfilled' ? satellites.value.filter((item) => item.online).length : 0
      )
      if (servers.status === 'fulfilled') subscribe()
    })()
      .catch((error) => {
        console.warn('[desktop] tray refresh failed', error)
        destroyTray()
      })
      .finally(() => {
        refreshPromise = null
      })
    return refreshPromise
  }
  timer = setInterval(() => {
    void refresh()
  }, 5_000)
  timer.unref()
  return {
    get visible() {
      return tray !== null
    },
    refresh,
    dispose() {
      disposed = true
      if (timer) clearInterval(timer)
      timer = null
      events?.abort()
      events = null
      recent.dispose()
      destroyTray()
    }
  }
}
