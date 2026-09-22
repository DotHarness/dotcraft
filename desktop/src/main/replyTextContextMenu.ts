import { BrowserWindow, Menu, type MenuItemConstructorOptions, type WebContents } from 'electron'
import { translate, type AppLocale } from '../shared/locales'
import type { TextContextMenuRequest } from '../shared/textContextMenu'

export function showReplyTextContextMenu(
  sender: WebContents,
  request: TextContextMenuRequest,
  locale: AppLocale,
  openExternal: (url: string) => Promise<void>,
): Promise<void> {
  if (!request || !Number.isFinite(request.x) || !Number.isFinite(request.y) ||
    typeof request.selectionText !== 'string') return Promise.reject(new Error('Invalid text menu request'))
  if (sender.isDestroyed()) return Promise.resolve()
  const window = BrowserWindow.fromWebContents(sender)
  if (!window || window.isDestroyed() || window.webContents !== sender) return Promise.resolve()
  const items: MenuItemConstructorOptions[] = []
  let action: Promise<void> = Promise.resolve()
  let actionError: unknown
  if (request.selectionText.length > 0) {
    items.push({
      label: translate(locale, 'conversation.selection.searchGoogle'),
      click: () => {
        const url = new URL('https://www.google.com/search')
        url.searchParams.set('q', request.selectionText)
        action = openExternal(url.toString()).catch((error: unknown) => { actionError = error })
      },
    }, { type: 'separator' }, {
      label: translate(locale, 'conversation.selection.copy'),
      accelerator: 'CmdOrCtrl+C',
      registerAccelerator: false,
      click: () => { if (!sender.isDestroyed()) sender.copy() },
    })
  }
  if (process.platform !== 'darwin') items.push({
    label: translate(locale, 'conversation.selectAll'),
    click: () => { if (!sender.isDestroyed()) sender.selectAll() },
  })
  if (items.length === 0) return Promise.resolve()
  const menu = Menu.buildFromTemplate(items)
  return new Promise<void>((resolve, reject) => {
    const finish = (): void => {
      sender.removeListener('destroyed', finish)
      void action.then(() => { if (actionError) reject(actionError); else resolve() })
    }
    sender.once('destroyed', finish)
    try {
      menu.popup({ window, x: Math.round(request.x), y: Math.round(request.y), callback: finish })
    } catch (error) {
      sender.removeListener('destroyed', finish)
      reject(error)
    }
  })
}
