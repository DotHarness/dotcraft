import { BrowserWindow, Menu, ipcMain } from 'electron'
import type { MenuItemConstructorOptions } from 'electron'
import type {
  AddTabMenuAction,
  AddTabMenuRequest,
  AddTabPopupPayload
} from '../shared/addTabMenu'

const PopupWidth = 210
const PopupItemHeight = 32
const PopupVerticalPadding = 8
const ViewportMargin = 8
const AnchorGap = 4
const PopupPayloadChannel = 'menu:add-tab-popup-payload'

export interface AddTabPopupWindowOptions {
  isDev: boolean
  preloadPath: string
  rendererPopupIndexPath: string
  rendererDevUrl: string
}

interface AddTabPopupRuntime {
  popup: BrowserWindow
  parent: BrowserWindow
  ready: Promise<boolean>
  payload: AddTabPopupPayload | null
  pendingResolve: ((action: AddTabMenuAction | null) => void) | null
  closeOnBlur: boolean
  blurTimer: ReturnType<typeof setTimeout> | null
  cleanupActiveListeners: (() => void) | null
}

const runtimesByParentId = new Map<number, AddTabPopupRuntime>()
let activeRuntime: AddTabPopupRuntime | null = null

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}

function estimatedMenuHeight(payload: AddTabMenuRequest): number {
  return payload.items.length * PopupItemHeight + PopupVerticalPadding
}

export function resolveAddTabPopupPayload(
  payload: AddTabMenuRequest,
  viewport: { width: number; height: number }
): AddTabPopupPayload {
  const anchor = payload.anchor ?? {
    left: payload.x,
    top: payload.y,
    right: payload.x,
    bottom: payload.y
  }
  const height = estimatedMenuHeight(payload)
  const left = clamp(anchor.left, ViewportMargin, viewport.width - PopupWidth - ViewportMargin)
  const belowTop = payload.y
  const aboveTop = anchor.top - height - AnchorGap
  const top =
    belowTop + height > viewport.height - ViewportMargin && aboveTop >= ViewportMargin
      ? aboveTop
      : clamp(belowTop, ViewportMargin, viewport.height - height - ViewportMargin)

  return {
    ...payload,
    position: {
      left,
      top,
      width: PopupWidth
    }
  }
}

function normalizeAction(
  payload: AddTabPopupPayload,
  action: unknown
): AddTabMenuAction | null {
  if (action !== 'openFile' && action !== 'newBrowser' && action !== 'newTerminal') {
    return null
  }
  const item = payload.items.find((candidate) => candidate.action === action)
  return item?.enabled === true ? action : null
}

function finishPopup(runtime: AddTabPopupRuntime, action: AddTabMenuAction | null): void {
  if (runtime.blurTimer) {
    clearTimeout(runtime.blurTimer)
    runtime.blurTimer = null
  }
  runtime.cleanupActiveListeners?.()
  runtime.cleanupActiveListeners = null
  runtime.closeOnBlur = false
  runtime.payload = null
  if (activeRuntime === runtime) {
    activeRuntime = null
  }
  const resolve = runtime.pendingResolve
  runtime.pendingResolve = null
  if (!runtime.popup.isDestroyed() && runtime.popup.isVisible()) {
    runtime.popup.hide()
  }
  resolve?.(action)
}

function destroyRuntime(runtime: AddTabPopupRuntime): void {
  finishPopup(runtime, null)
  runtimesByParentId.delete(runtime.parent.id)
  if (!runtime.popup.isDestroyed()) {
    runtime.popup.destroy()
  }
}

function attachActiveCloseHandlers(runtime: AddTabPopupRuntime): void {
  runtime.cleanupActiveListeners?.()
  const close = (): void => finishPopup(runtime, null)
  const closeAfterBlurArmed = (): void => {
    if (runtime.closeOnBlur) close()
  }
  runtime.parent.on('move', close)
  runtime.parent.on('resize', close)
  runtime.parent.on('blur', closeAfterBlurArmed)
  runtime.popup.on('blur', closeAfterBlurArmed)
  runtime.cleanupActiveListeners = () => {
    runtime.parent.off('move', close)
    runtime.parent.off('resize', close)
    runtime.parent.off('blur', closeAfterBlurArmed)
    runtime.popup.off('blur', closeAfterBlurArmed)
  }
}

function createPopupRuntime(
  win: BrowserWindow,
  options: AddTabPopupWindowOptions,
  theme: 'dark' | 'light'
): AddTabPopupRuntime | null {
  const contentBounds = win.getContentBounds()
  let popup: BrowserWindow
  try {
    popup = new BrowserWindow({
      parent: win,
      x: contentBounds.x,
      y: contentBounds.y,
      width: contentBounds.width,
      height: contentBounds.height,
      show: false,
      frame: false,
      transparent: true,
      backgroundColor: '#00000000',
      hasShadow: false,
      resizable: false,
      movable: false,
      minimizable: false,
      maximizable: false,
      fullscreenable: false,
      skipTaskbar: true,
      webPreferences: {
        preload: options.preloadPath,
        additionalArguments: [`--dotcraft-initial-theme=${theme}`],
        sandbox: false,
        contextIsolation: true,
        nodeIntegration: false
      }
    })
  } catch {
    return null
  }

  const runtime: AddTabPopupRuntime = {
    popup,
    parent: win,
    ready: Promise.resolve(false),
    payload: null,
    pendingResolve: null,
    closeOnBlur: false,
    blurTimer: null,
    cleanupActiveListeners: null
  }
  runtimesByParentId.set(win.id, runtime)

  const cleanupParent = (): void => {
    destroyRuntime(runtime)
  }
  win.once('closed', cleanupParent)
  popup.once('closed', () => {
    finishPopup(runtime, null)
    runtimesByParentId.delete(win.id)
    win.off('closed', cleanupParent)
  })

  const load = options.isDev
    ? popup.loadURL(`${options.rendererDevUrl}/add-tab-popup.html`)
    : popup.loadFile(options.rendererPopupIndexPath)
  runtime.ready = load
    .then(() => true)
    .catch(() => {
      destroyRuntime(runtime)
      return false
    })

  return runtime
}

function ensurePopupRuntime(
  win: BrowserWindow,
  options: AddTabPopupWindowOptions,
  theme: 'dark' | 'light'
): AddTabPopupRuntime | null {
  const existing = runtimesByParentId.get(win.id)
  if (existing && !existing.popup.isDestroyed()) {
    return existing
  }
  return createPopupRuntime(win, options, theme)
}

export function warmAddTabPopupWindow(
  win: BrowserWindow,
  options: AddTabPopupWindowOptions,
  theme: 'dark' | 'light' = 'dark'
): Promise<boolean> {
  const runtime = ensurePopupRuntime(win, options, theme)
  return runtime?.ready ?? Promise.resolve(false)
}

export function popupAddTabNativeMenu(
  win: BrowserWindow,
  payload: AddTabMenuRequest
): Promise<AddTabMenuAction | null> {
  return new Promise((resolve) => {
    let selected: AddTabMenuAction | null = null
    const template: MenuItemConstructorOptions[] = payload.items.map((item) => ({
      label: item.label,
      enabled: item.enabled,
      accelerator: item.shortcut,
      click: () => {
        selected = item.action
      }
    }))
    const menu = Menu.buildFromTemplate(template)
    menu.popup({
      window: win,
      x: Math.round(payload.x),
      y: Math.round(payload.y),
      callback: () => resolve(selected)
    })
  })
}

export async function popupAddTabMenuWindow(
  win: BrowserWindow,
  payload: AddTabMenuRequest,
  options: AddTabPopupWindowOptions
): Promise<AddTabMenuAction | null> {
  if (activeRuntime) {
    finishPopup(activeRuntime, null)
  }

  const contentBounds = win.getContentBounds()
  const popupPayload = resolveAddTabPopupPayload(payload, {
    width: contentBounds.width,
    height: contentBounds.height
  })
  const runtime = ensurePopupRuntime(win, options, popupPayload.theme)
  if (!runtime) {
    return popupAddTabNativeMenu(win, payload)
  }
  runtime.payload = popupPayload
  const ready = await runtime.ready
  if (!ready || runtime.popup.isDestroyed()) {
    return popupAddTabNativeMenu(win, payload)
  }

  runtime.popup.setBounds(contentBounds)
  runtime.popup.webContents.send(PopupPayloadChannel, popupPayload)

  return new Promise((resolve) => {
    runtime.pendingResolve = resolve
    activeRuntime = runtime
    attachActiveCloseHandlers(runtime)
    runtime.popup.show()
    runtime.popup.focus()
    runtime.blurTimer = setTimeout(() => {
      runtime.closeOnBlur = true
    }, 100)
  })
}

export function registerAddTabPopupWindowIpc(): void {
  ipcMain.removeHandler('menu:add-tab-popup-payload')
  ipcMain.removeHandler('menu:add-tab-popup-result')
  ipcMain.handle('menu:add-tab-popup-payload', (event): AddTabPopupPayload | null => {
    const runtime = [...runtimesByParentId.values()].find((candidate) => (
      candidate.popup.webContents.id === event.sender.id
    ))
    return runtime?.payload ?? null
  })
  ipcMain.handle('menu:add-tab-popup-result', (event, action: unknown): void => {
    const runtime = activeRuntime
    if (!runtime || runtime.popup.webContents.id !== event.sender.id || !runtime.payload) {
      return
    }
    finishPopup(runtime, normalizeAction(runtime.payload, action))
  })
}
