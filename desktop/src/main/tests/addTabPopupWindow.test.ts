import { beforeEach, describe, expect, it, vi } from 'vitest'

const electronMock = vi.hoisted(() => {
  let nextWebContentsId = 1
  let nextWindowId = 99
  let throwOnConstruct = false
  const handlers = new Map<string, Function>()
  let createdPopup: any = null

  function makeEmitter() {
    const events = new Map<string, Set<Function>>()
    return {
      on: vi.fn((name: string, handler: Function) => {
        const bucket = events.get(name) ?? new Set<Function>()
        bucket.add(handler)
        events.set(name, bucket)
      }),
      once: vi.fn((name: string, handler: Function) => {
        const wrapped = () => {
          events.get(name)?.delete(wrapped)
          handler()
        }
        const bucket = events.get(name) ?? new Set<Function>()
        bucket.add(wrapped)
        events.set(name, bucket)
      }),
      off: vi.fn((name: string, handler: Function) => {
        events.get(name)?.delete(handler)
      }),
      emit(name: string) {
        for (const handler of events.get(name) ?? []) {
          handler()
        }
      }
    }
  }

  function createParent(bounds = { x: 10, y: 20, width: 600, height: 400 }) {
    const emitter = makeEmitter()
    return {
      ...emitter,
      id: nextWindowId++,
      getContentBounds: vi.fn(() => bounds),
      isDestroyed: vi.fn(() => false)
    }
  }

  function createPopup() {
    const emitter = makeEmitter()
    let destroyed = false
    let visible = false
    return {
      ...emitter,
      webContents: { id: nextWebContentsId++, send: vi.fn() },
      loadURL: vi.fn(async () => undefined),
      loadFile: vi.fn(async () => undefined),
      setBounds: vi.fn(),
      show: vi.fn(() => {
        visible = true
      }),
      hide: vi.fn(() => {
        visible = false
      }),
      focus: vi.fn(),
      isDestroyed: vi.fn(() => destroyed),
      isVisible: vi.fn(() => visible),
      destroy: vi.fn(() => {
        destroyed = true
      })
    }
  }

  const BrowserWindow = vi.fn(() => {
    if (throwOnConstruct) throw new Error('popup failed')
    createdPopup = createPopup()
    return createdPopup
  })

  const Menu = {
    buildFromTemplate: vi.fn((template: Array<{ click?: () => void }>) => ({
      popup: vi.fn((options: { callback?: () => void }) => {
        template[0]?.click?.()
        options.callback?.()
      })
    }))
  }

  const ipcMain = {
    handle: vi.fn((channel: string, handler: Function) => {
      handlers.set(channel, handler)
    }),
    removeHandler: vi.fn((channel: string) => {
      handlers.delete(channel)
    })
  }

  return {
    BrowserWindow,
    Menu,
    ipcMain,
    handlers,
    createParent,
    get createdPopup() {
      return createdPopup
    },
    setThrowOnConstruct(value: boolean) {
      throwOnConstruct = value
    },
    reset() {
      nextWebContentsId = 1
      throwOnConstruct = false
      createdPopup = null
      handlers.clear()
      BrowserWindow.mockClear()
      Menu.buildFromTemplate.mockClear()
      ipcMain.handle.mockClear()
      ipcMain.removeHandler.mockClear()
    }
  }
})

vi.mock('electron', () => ({
  BrowserWindow: electronMock.BrowserWindow,
  Menu: electronMock.Menu,
  ipcMain: electronMock.ipcMain
}))

import {
  popupAddTabMenuWindow,
  registerAddTabPopupWindowIpc,
  resolveAddTabPopupPayload,
  warmAddTabPopupWindow
} from '../addTabPopupWindow'
import type { AddTabMenuRequest } from '../../shared/addTabMenu'

const payload: AddTabMenuRequest = {
  x: 80,
  y: 44,
  anchor: {
    left: 80,
    top: 10,
    right: 108,
    bottom: 40
  },
  theme: 'dark',
  items: [
    { action: 'openFile', label: 'Open File', shortcut: 'Ctrl+P', enabled: true },
    { action: 'newBrowser', label: 'Browser', enabled: true },
    { action: 'newTerminal', label: 'Terminal', enabled: true }
  ]
}

const options = {
  isDev: false,
  preloadPath: 'preload.js',
  rendererPopupIndexPath: 'add-tab-popup.html',
  rendererDevUrl: 'http://localhost:5173'
}

async function flushPopupOpen(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

beforeEach(() => {
  electronMock.reset()
  registerAddTabPopupWindowIpc()
})

describe('resolveAddTabPopupPayload', () => {
  it('clamps and flips the popup inside the parent content area', () => {
    const resolved = resolveAddTabPopupPayload(
      {
        ...payload,
        x: 500,
        y: 390,
        anchor: { left: 500, right: 528, top: 360, bottom: 386 }
      },
      { width: 600, height: 400 }
    )

    expect(resolved.position.left).toBe(382)
    expect(resolved.position.top).toBe(252)
  })
})

describe('popupAddTabMenuWindow', () => {
  it('creates a transparent child window and resolves the selected action', async () => {
    const parent = electronMock.createParent()
    const result = popupAddTabMenuWindow(parent as never, payload, options)
    await flushPopupOpen()

    const popup = electronMock.createdPopup
    expect(electronMock.BrowserWindow).toHaveBeenCalledWith(expect.objectContaining({
      parent,
      transparent: true,
      frame: false,
      skipTaskbar: true
    }))
    expect(popup.loadFile).toHaveBeenCalledWith('add-tab-popup.html')

    const getPayload = electronMock.handlers.get('menu:add-tab-popup-payload')!
    const popupPayload = await getPayload({ sender: { id: popup.webContents.id } })
    expect(popupPayload).toEqual(expect.objectContaining({
      theme: 'dark',
      position: expect.objectContaining({ width: 210 })
    }))

    const choose = electronMock.handlers.get('menu:add-tab-popup-result')!
    await choose({ sender: { id: popup.webContents.id } }, 'openFile')
    await expect(result).resolves.toBe('openFile')
    expect(popup.hide).toHaveBeenCalled()
    expect(popup.destroy).not.toHaveBeenCalled()
  })

  it('reuses the warmed child window on subsequent opens', async () => {
    const parent = electronMock.createParent()
    await expect(warmAddTabPopupWindow(parent as never, options, 'dark')).resolves.toBe(true)
    const popup = electronMock.createdPopup
    expect(popup.loadFile).toHaveBeenCalledTimes(1)

    const first = popupAddTabMenuWindow(parent as never, payload, options)
    await flushPopupOpen()
    const choose = electronMock.handlers.get('menu:add-tab-popup-result')!
    await choose({ sender: { id: popup.webContents.id } }, 'openFile')
    await expect(first).resolves.toBe('openFile')

    const second = popupAddTabMenuWindow(parent as never, payload, options)
    await flushPopupOpen()

    expect(electronMock.BrowserWindow).toHaveBeenCalledTimes(1)
    expect(popup.loadFile).toHaveBeenCalledTimes(1)
    expect(popup.webContents.send).toHaveBeenCalledWith('menu:add-tab-popup-payload', expect.objectContaining({
      theme: 'dark'
    }))
    await choose({ sender: { id: popup.webContents.id } }, 'newTerminal')
    await expect(second).resolves.toBe('newTerminal')
  })

  it('closes with null when the parent moves or resizes', async () => {
    const parent = electronMock.createParent()
    const result = popupAddTabMenuWindow(parent as never, payload, options)
    await flushPopupOpen()

    parent.emit('resize')

    await expect(result).resolves.toBeNull()
    expect(electronMock.createdPopup.hide).toHaveBeenCalled()
    expect(electronMock.createdPopup.destroy).not.toHaveBeenCalled()
  })

  it('ignores disabled actions returned by the popup window', async () => {
    const parent = electronMock.createParent()
    const result = popupAddTabMenuWindow(parent as never, {
      ...payload,
      items: payload.items.map((item) => (
        item.action === 'newBrowser' ? { ...item, enabled: false } : item
      ))
    }, options)
    await flushPopupOpen()

    const popup = electronMock.createdPopup
    const choose = electronMock.handlers.get('menu:add-tab-popup-result')!
    await choose({ sender: { id: popup.webContents.id } }, 'newBrowser')

    await expect(result).resolves.toBeNull()
  })

  it('falls back to the native menu if the popup window cannot be created', async () => {
    electronMock.setThrowOnConstruct(true)
    const parent = electronMock.createParent()

    await expect(popupAddTabMenuWindow(parent as never, payload, options)).resolves.toBe('openFile')
    expect(electronMock.Menu.buildFromTemplate).toHaveBeenCalled()
  })
})
