import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

const mocks = vi.hoisted(() => ({
  windows: [] as any[],
  destroyTabs: vi.fn(),
  ipc: null as any,
  theme: null as any
}))
vi.mock('electron', async () => {
  const { EventEmitter } = await import('node:events')
  mocks.ipc = new EventEmitter()
  mocks.theme = Object.assign(new EventEmitter(), { shouldUseDarkColors: false })
  return {
    app: { isPackaged: false, commandLine: { hasSwitch: () => false } },
    ipcMain: mocks.ipc,
    nativeTheme: mocks.theme,
    nativeImage: { createFromPath: vi.fn() },
    BrowserWindow: class extends EventEmitter {
      id = mocks.windows.length + 1
      webContents = Object.assign(new EventEmitter(), {
        id: this.id,
        session: {},
        send: vi.fn(),
        isDestroyed: () => false,
        setZoomFactor: vi.fn()
      })
      setTitle = vi.fn()
      isFocused = () => false
      isDestroyed = () => false
      constructor(readonly options: unknown) {
        super()
        mocks.windows.push(this)
      }
    }
  }
})
vi.mock('../desktopPet', () => ({ attachDesktopPet: vi.fn() }))
vi.mock('../desktopSession', () => ({ configureDesktopSession: vi.fn() }))
vi.mock('../mainWindowNavigation', () => ({ guardMainWindowNavigation: vi.fn() }))
vi.mock('../viewerBrowser', () => ({
  viewerBrowserManager: { hosts: { attachWindow: vi.fn() }, destroyAllTabs: mocks.destroyTabs }
}))
vi.mock('../windowTheme', () => ({
  resolveInitialTheme: () => 'light',
  resolveThemeSurface: () => ({}),
  resolveWindowBackdropOptions: () => ({}),
  resolveWindowsTitleBarOverlay: () => ({}),
  applyWindowBackdropTheme: vi.fn()
}))
import { createWindow } from '../windows/windowFrame'

const originalResources = process.resourcesPath
afterEach(() => {
  mocks.windows.length = 0
  mocks.destroyTabs.mockClear()
  mocks.ipc.removeAllListeners()
  mocks.theme.removeAllListeners()
  Object.defineProperty(process, 'resourcesPath', { value: originalResources, configurable: true })
})

describe('BrowserWindow lifecycle ownership', () => {
  it('closing one window releases only its resources and preserves its sibling', async () => {
    Object.defineProperty(process, 'resourcesPath', { value: '/tmp/dotcraft-resources', configurable: true })
    const owners = new Map<number, DesktopWindowRuntime>()
    const make = (workspace: string): DesktopWindowRuntime => {
      const context = {
        app: { windows: owners },
        settings: {},
        currentWorkspacePath: workspace,
        disposed: false,
        mainWindow: null,
        disposers: [],
        showWindowSafely: vi.fn(),
        workspaceTitleName: () => workspace,
        userInputAutoResolution: { setWindowFocused: vi.fn() },
        emitConnectionStatus: vi.fn(),
        teardownRuntime: vi.fn(async () => {})
      } as unknown as DesktopWindowRuntime
      const window = createWindow(context, workspace, { status: 'ready', workspacePath: workspace } as never)
      context.mainWindow = window
      owners.set(window.webContents.id, context)
      return context
    }
    const first = make('/one')
    const second = make('/two')
    const released = vi.fn()
    first.disposers.push(() => {
      throw new Error('cleanup failure')
    }, released)
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      first.mainWindow!.emit('close')
      first.mainWindow!.emit('closed')
      await Promise.resolve()
      expect(first.disposed).toBe(true)
      expect(first.teardownRuntime).toHaveBeenCalledOnce()
      expect(mocks.destroyTabs).toHaveBeenCalledWith(mocks.windows[0])
      expect(released).toHaveBeenCalledOnce()
      expect(owners.has(second.mainWindow!.webContents.id)).toBe(true)
      expect(second.teardownRuntime).not.toHaveBeenCalled()
      expect(second.disposed).toBe(false)
      expect(mocks.windows[0].options.webPreferences.partition).not.toBe(
        mocks.windows[1].options.webPreferences.partition
      )
    } finally {
      warn.mockRestore()
    }
  })
})
