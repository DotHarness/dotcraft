import { BrowserWindow, dialog, ipcMain, Menu, shell, type MenuItemConstructorOptions } from 'electron'
import { normalizeLocale, translate, type AppLocale, type TopLevelMenuId } from '../shared/locales'
import type { DesktopApplicationServices } from './desktopApplicationServices'
import { getGitHubIdentity } from './githubProfile'
import { openExternalHttpUrl } from './ipcBridge'

export function refreshDesktopMenu(services: DesktopApplicationServices): void {
  const locale = normalizeLocale(services.settings.locale)
  const t = (key: string): string => translate(locale, key)
  const isMac = process.platform === 'darwin'
  const focused = services.focusedWindow()
  const template: MenuItemConstructorOptions[] = [
    ...(isMac ? [{ role: 'appMenu' } as MenuItemConstructorOptions] : []),
    {
      id: 'file',
      label: t('menu.file'),
      submenu: [
        { label: t('menu.newWindow'), accelerator: 'CmdOrCtrl+Shift+N', click: services.openNewWindow },
        { type: 'separator' },
        { role: isMac ? 'close' : 'quit' }
      ]
    },
    {
      id: 'edit',
      label: t('menu.edit'),
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
      label: t('menu.view'),
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        ...(import.meta.env.DEV ? [{ role: 'toggleDevTools' } as MenuItemConstructorOptions] : []),
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        {
          label: t('menu.openDashboard'),
          accelerator: 'CmdOrCtrl+Shift+D',
          enabled: Boolean(focused?.lastDashboardUrl),
          click: async () => {
            const url = services.focusedWindow()?.lastDashboardUrl
            if (url) await openExternalHttpUrl(url)
          }
        },
        { type: 'separator' },
        { role: 'togglefullscreen' }
      ]
    },
    {
      id: 'window',
      label: t('menu.window'),
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
      label: t('menu.help'),
      submenu: [
        {
          label: t('menu.whatsNew'),
          click: () => services.focusedWindow()?.mainWindow?.webContents.send('app:open-whats-new')
        },
        { type: 'separator' },
        {
          label: t('menu.documentation'),
          click: () => {
            void shell.openExternal('https://github.com/DotHarness/dotcraft')
          }
        },
        ...(services.getAppUpdateService().getState().status === 'unsupported'
          ? []
          : ([
              { type: 'separator' },
              {
                label: t('menu.checkForUpdates'),
                click: () => {
                  void checkForUpdates(services, locale)
                }
              }
            ] as MenuItemConstructorOptions[]))
      ]
    }
  ]
  Menu.setApplicationMenu(Menu.buildFromTemplate(template))
}

async function checkForUpdates(services: DesktopApplicationServices, locale: AppLocale): Promise<void> {
  const win = services.focusedWindow()?.mainWindow
  const state = await services.getAppUpdateService().checkForUpdates()
  if (!win || win.isDestroyed()) return
  if (state.status === 'not-available')
    await dialog.showMessageBox(win, { type: 'info', message: translate(locale, 'update.upToDate') })
  else if (state.status === 'error')
    await dialog.showMessageBox(win, {
      type: 'error',
      message: translate(locale, 'update.checkFailed'),
      detail: state.error
    })
  else if (state.update) win.webContents.send('app:open-update-dialog')
}

export function registerDesktopApplicationIpc(services: DesktopApplicationServices): void {
  ipcMain.handle(
    'menu:popup-top-level',
    (event, payload: { menuId: TopLevelMenuId; x: number; y: number }) => {
      const owner = BrowserWindow.fromWebContents(event.sender)
      if (!owner || !services.windowFor(owner)) return
      Menu.getApplicationMenu()
        ?.items.find((item) => item.id === payload.menuId)
        ?.submenu?.popup({ window: owner, x: Math.round(payload.x), y: Math.round(payload.y) })
    }
  )
  ipcMain.handle('menu:popup-all', (event, payload: { x: number; y: number }) => {
    const owner = BrowserWindow.fromWebContents(event.sender)
    if (owner && services.windowFor(owner))
      Menu.getApplicationMenu()?.popup({ window: owner, x: Math.round(payload.x), y: Math.round(payload.y) })
  })
  ipcMain.handle('app:whats-new-get-releases', () => services.getWhatsNewCatalog().getReleases())
  ipcMain.handle('app:whats-new-get-media-states', (_event, versions: string[]) =>
    services.getWhatsNewMediaCache().getMediaStates(Array.isArray(versions) ? versions : [])
  )
  ipcMain.handle('app:whats-new-prefetch-media', (_event, versions: string[]) =>
    services.getWhatsNewMediaCache().prefetchMedia(Array.isArray(versions) ? versions : [])
  )
  ipcMain.handle('app:update-get-state', () => services.getAppUpdateService().getState())
  ipcMain.handle('app:update-download', () => services.getAppUpdateService().download())
  ipcMain.handle('app:update-install', () => services.getAppUpdateService().install())
  ipcMain.handle('profile:get-github-identity', (_event, username: string) =>
    getGitHubIdentity(typeof username === 'string' ? username : '')
  )
}
