import { BrowserWindow, dialog, ipcMain, Menu, shell, type MenuItem, type MenuItemConstructorOptions } from 'electron'
import type { ApplicationMenuEntry, ApplicationMenuSnapshot } from '../shared/applicationMenu'
import { normalizeLocale, translate, TOP_LEVEL_MENU_IDS, type AppLocale } from '../shared/locales'
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
          label: t('menu.documentation'),
          click: () => {
            void shell.openExternal('https://www.dotcraft.net/')
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

function readApplicationMenu(): ApplicationMenuSnapshot {
  const items = Menu.getApplicationMenu()?.items ?? []
  return Object.fromEntries(
    TOP_LEVEL_MENU_IDS.map((id) => [id, readMenuEntries(items.find((item) => item.id === id)?.submenu?.items ?? [])])
  ) as ApplicationMenuSnapshot
}

function readMenuEntries(items: MenuItem[]): ApplicationMenuEntry[] {
  return items.flatMap((item): ApplicationMenuEntry[] => {
    if (!item.visible || item.type === 'submenu') return []
    if (item.type === 'separator') return [{ type: 'separator' }]
    return [{
      type: 'item',
      commandId: item.commandId,
      label: item.label,
      enabled: item.enabled,
      accelerator: item.accelerator ? formatAccelerator(item.accelerator) : undefined
    }]
  })
}

function formatAccelerator(accelerator: string): string {
  return accelerator
    .split('+')
    .map((part) => /^(CommandOrControl|CmdOrCtrl|Control)$/i.test(part) ? 'Ctrl' : part)
    .join('+')
}

function findMenuItem(menu: Menu | null, commandId: number): MenuItem | null {
  for (const item of menu?.items ?? []) {
    if (item.commandId === commandId) return item
    const nested = findMenuItem(item.submenu ?? null, commandId)
    if (nested) return nested
  }
  return null
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
  ipcMain.handle('menu:get-application-menu', () => readApplicationMenu())
  ipcMain.handle('menu:invoke-item', (event, commandId: number) => {
    const owner = BrowserWindow.fromWebContents(event.sender)
    if (!owner || !services.windowFor(owner)) return
    const item = findMenuItem(Menu.getApplicationMenu(), commandId)
    if (item?.enabled) item.click(undefined, owner, event.sender)
  })
  ipcMain.handle('app:update-get-state', () => services.getAppUpdateService().getState())
  ipcMain.handle('app:update-download', () => services.getAppUpdateService().download())
  ipcMain.handle('app:update-install', () => services.getAppUpdateService().install())
  ipcMain.handle('profile:get-github-identity', (_event, username: string) =>
    getGitHubIdentity(typeof username === 'string' ? username : '')
  )
}
