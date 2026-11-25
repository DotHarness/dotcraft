import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { createDesktopLifecycle } from '../desktopLifecycle'

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
      hide = vi.fn()
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
const originalPlatform = process.platform
afterEach(() => {
  mocks.windows.length = 0
  mocks.destroyTabs.mockClear()
  mocks.ipc.removeAllListeners()
  mocks.theme.removeAllListeners()
  Object.defineProperty(process, 'resourcesPath', { value: originalResources, configurable: true })
  Object.defineProperty(process, 'platform', { value: originalPlatform, configurable: true })
})

function fixture(tray = true) {
  Object.defineProperty(process, 'resourcesPath', { value: '/tmp/dotcraft-resources', configurable: true })
  const owners = new Map<number, DesktopWindowRuntime>()
  const lifecycle = createDesktopLifecycle()
  const make = (workspace: string): DesktopWindowRuntime => {
    const context = {
      app: { windows: owners, lifecycle, hasTray: () => tray },
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
  return { make, owners, lifecycle }
}

describe('BrowserWindow lifecycle ownership', () => {
  it.each(['win32', 'darwin'] as const)('hides the last window without releasing its runtime on %s', platform => {
    Object.defineProperty(process, 'platform', { value: platform, configurable: true })
    const { make, owners } = fixture(platform === 'win32')
    const context = make('/workspace')
    const win = context.mainWindow!
    const event = { preventDefault: vi.fn() }
    win.emit('close', event)
    expect(event.preventDefault).toHaveBeenCalledOnce()
    expect(win.hide).toHaveBeenCalledOnce()
    expect(context.disposed).toBe(false)
    expect(context.teardownRuntime).not.toHaveBeenCalled()
    expect(mocks.destroyTabs).not.toHaveBeenCalled()
    expect(owners.get(win.webContents.id)).toBe(context)
  })

  it.each(['no-tray', 'application-exit'] as const)('destroys the last window for %s', reason => {
    Object.defineProperty(process, 'platform', { value: 'win32', configurable: true })
    const { make, lifecycle } = fixture(reason !== 'no-tray')
    const context = make('/workspace')
    const win = context.mainWindow!
    if (reason === 'application-exit') lifecycle.stop()
    const event = { preventDefault: vi.fn() }
    win.emit('close', event)
    expect(event.preventDefault).not.toHaveBeenCalled()
    expect(win.hide).not.toHaveBeenCalled()
    expect(context.disposed).toBe(true)
    expect(context.teardownRuntime).toHaveBeenCalledOnce()
    expect(mocks.destroyTabs).toHaveBeenCalledWith(win)
    win.emit('closed')
  })

  it('closing one window releases only its resources and preserves its sibling', async () => {
    const { make, owners } = fixture()
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

  it('passes initial nativeTheme dark state and forwards updates to the renderer', () => {
    mocks.theme.shouldUseDarkColors = true
    const { make } = fixture()
    const context = make('/workspace')
    const win = context.mainWindow!
    const args = (mocks.windows[0].options as { webPreferences: { additionalArguments: string[] } }).webPreferences
      .additionalArguments
    expect(args).toContain('--dotcraft-native-dark=true')

    mocks.theme.shouldUseDarkColors = false
    mocks.theme.emit('updated')
    expect(win.webContents.send).toHaveBeenCalledWith('window:native-theme-updated', false)
  })
})
