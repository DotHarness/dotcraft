import type { IpcMainEvent } from 'electron'
import { app, BrowserWindow, ipcMain, nativeImage, nativeTheme } from 'electron'
import { randomUUID } from 'node:crypto'
import { join } from 'path'
import { normalizeAccentHex, normalizeInterfaceZoom } from '../../shared/appearance'
import { INITIAL_CDP_DEBUGGING_ARG } from '../../shared/initialCdpDebugging'
import { encodeInitialWorkspaceStatusArg } from '../../shared/initialWorkspaceStatus'
import { normalizeLocale, translate } from '../../shared/locales'
import { resolveThemeMode } from '../../shared/theme'
import { EMPTY_THEME_SEEDS } from '../../shared/themeSeed'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { attachDesktopPet } from '../desktopPet'
import { configureDesktopSession } from '../desktopSession'
import { guardMainWindowNavigation } from '../mainWindowNavigation'
import { viewerBrowserManager } from '../viewerBrowser'
import {
  applyWindowBackdropTheme,
  resolveInitialTheme,
  resolveThemeSurface,
  resolveWindowBackdropOptions,
  resolveWindowsTitleBarOverlay
} from '../windowTheme'
import { WINDOW_SHOW_FALLBACK_MS } from './windowConstants'
import type { DesktopWindowRuntime } from './windowRuntime'

export function createWindow(
  ctx: DesktopWindowRuntime,
  workspacePath: string | null,
  initialWorkspaceStatus: WorkspaceStatusPayload
): BrowserWindow {
  const isMac = process.platform === 'darwin'
  const isDev = import.meta.env.DEV
  const packagedIcon = join(process.resourcesPath, 'icon.png')
  const developmentIcon = join(__dirname, '../../resources/icon.png')
  const iconPath = process.platform === 'darwin' ? null : app.isPackaged ? packagedIcon : developmentIcon
  const initialTheme = resolveInitialTheme(ctx.settings, nativeTheme.shouldUseDarkColors)
  const initialThemeMode = resolveThemeMode(ctx.settings.theme)
  const initialSurface = resolveThemeSurface(ctx.settings, initialTheme)
  const windowBackdrop = resolveWindowBackdropOptions(initialTheme, process.platform, initialSurface)
  const initialAppearance = {
    accent: normalizeAccentHex(ctx.settings.accent),
    themeSeeds: ctx.settings.themeSeeds ?? EMPTY_THEME_SEEDS
  }
  const initialLocale = normalizeLocale(ctx.settings.locale)
  const cdpDebuggingEnabled = app.commandLine.hasSwitch('remote-debugging-port')
  const win = new BrowserWindow({
    width: 1400,
    height: 800,
    minWidth: process.platform === 'win32' ? 480 : 900,
    minHeight: 600,
    ...windowBackdrop,
    ...(iconPath
      ? {
          icon: nativeImage.createFromPath(iconPath)
        }
      : {}),
    show: isDev,
    ...(isMac
      ? {
          titleBarStyle: 'hiddenInset'
        }
      : process.platform === 'win32'
        ? {
            titleBarStyle: 'hidden',
            titleBarOverlay: resolveWindowsTitleBarOverlay(initialSurface)
          }
        : {
            frame: false
          }),
    autoHideMenuBar: !isMac,
    webPreferences: {
      partition: `dotcraft-window-${randomUUID()}`,
      webviewTag: true,
      preload: join(__dirname, '../preload/index.js'),
      devTools: isDev,
      additionalArguments: [
        `--dotcraft-initial-theme=${initialThemeMode}`,
        `--dotcraft-applied-theme=${initialTheme}`,
        `--dotcraft-appearance=${encodeURIComponent(JSON.stringify(initialAppearance))}`,
        `--dotcraft-initial-locale=${initialLocale}`,
        ...(cdpDebuggingEnabled ? [INITIAL_CDP_DEBUGGING_ARG] : []),
        encodeInitialWorkspaceStatusArg(initialWorkspaceStatus)
      ],
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  const ownerId = win.webContents.id
  configureDesktopSession(win.webContents.session)
  const workspaceName = ctx.workspaceTitleName(workspacePath, initialLocale)
  guardMainWindowNavigation(win.webContents)
  viewerBrowserManager.hosts.attachWindow(win)
  attachDesktopPet(win)
  win.setTitle(translate(initialLocale, 'app.titleWithWorkspace', { name: workspaceName }))

  const updateTheme = (): void => {
    if (win.isDestroyed()) return
    const theme = resolveInitialTheme(ctx.settings, nativeTheme.shouldUseDarkColors)
    applyWindowBackdropTheme(win, theme, process.platform, resolveThemeSurface(ctx.settings, theme))
  }
  nativeTheme.on('updated', updateTheme)
  ctx.disposers.push(() => nativeTheme.removeListener('updated', updateTheme))

  let showFallbackTimer: ReturnType<typeof setTimeout> | null = null
  let electronReadyToShow = isDev
  let rendererReadyForShow = isDev
  const clearShowFallbackTimer = (): void => {
    if (showFallbackTimer) {
      clearTimeout(showFallbackTimer)
      showFallbackTimer = null
    }
  }
  const showWhenReady = (): void => {
    if (isDev || !electronReadyToShow || !rendererReadyForShow) return
    clearShowFallbackTimer()
    ctx.showWindowSafely(win)
  }
  const forceShow = (): void => {
    clearShowFallbackTimer()
    ctx.showWindowSafely(win)
  }
  const handleRendererReadyForShow = (event: IpcMainEvent): void => {
    if (event.sender !== win.webContents) return
    rendererReadyForShow = true
    showWhenReady()
  }
  ipcMain.on('window:renderer-ready-for-show', handleRendererReadyForShow)

  if (!isDev) {
    win.once('ready-to-show', () => {
      electronReadyToShow = true
      showWhenReady()
    })
    showFallbackTimer = setTimeout(() => {
      console.warn('[desktop] ready-to-show timeout; forcing window show fallback')
      forceShow()
    }, WINDOW_SHOW_FALLBACK_MS)
  }

  win.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
    if (!isMainFrame || errorCode === -3) {
      return
    }
    const message = `Renderer failed to load (${errorCode}): ${errorDescription} (${validatedURL || 'unknown URL'})`
    console.error('[desktop] did-fail-load', message)
    forceShow()
    ctx.emitConnectionStatus(win, { status: 'error', errorMessage: message })
  })

  win.webContents.on('render-process-gone', (_event, details) => {
    const message = `Renderer process exited (${details.reason})`
    console.error('[desktop] render-process-gone', details)
    forceShow()
    ctx.emitConnectionStatus(win, { status: 'error', errorMessage: message })
  })

  win.webContents.on('unresponsive', () => {
    console.warn('[desktop] renderer became unresponsive')
    forceShow()
  })

  const sendMaximizedState = (): void => {
    if (win.isDestroyed()) return
    win.webContents.send('window:maximized-change', win.isMaximized())
  }
  const sendVisibilityState = (): void => {
    if (win.isDestroyed()) return
    win.webContents.send('window:visibility-changed', {
      minimized: win.isMinimized(),
      visible: win.isVisible(),
      focused: win.isFocused()
    })
  }
  const handleWindowFocus = (): void => {
    ctx.userInputAutoResolution.setWindowFocused(true)
    sendVisibilityState()
  }
  const handleWindowBlur = (): void => {
    ctx.userInputAutoResolution.setWindowFocused(false)
    sendVisibilityState()
  }

  // webContents zoom resets on reload, so restore the saved value before showing the page.
  win.webContents.on('did-finish-load', () => {
    win.webContents.setZoomFactor(normalizeInterfaceZoom(ctx.settings.interfaceZoom))
    sendVisibilityState()
  })

  win.on('maximize', sendMaximizedState)
  win.on('unmaximize', sendMaximizedState)
  win.on('enter-full-screen', sendMaximizedState)
  win.on('leave-full-screen', sendMaximizedState)
  win.on('minimize', sendVisibilityState)
  win.on('restore', sendVisibilityState)
  win.on('show', sendVisibilityState)
  win.on('hide', sendVisibilityState)
  win.on('focus', handleWindowFocus)
  win.on('blur', handleWindowBlur)
  ctx.userInputAutoResolution.setWindowFocused(win.isFocused())

  win.on('close', () => {
    ctx.disposed = true
    viewerBrowserManager.destroyAllTabs(win)
    void ctx
      .teardownRuntime('window close', { clearWorkspace: true, cleanupIpcHandlers: true })
      .catch((error) => console.warn('[desktop] window cleanup failed', error))
  })

  win.on('closed', () => {
    clearShowFallbackTimer()
    ipcMain.removeListener('window:renderer-ready-for-show', handleRendererReadyForShow)
    for (const dispose of ctx.disposers.splice(0)) {
      try {
        dispose()
      } catch (error) {
        console.warn('[desktop] window resource cleanup failed', error)
      }
    }
    ctx.app.windows.delete(ownerId)
    ctx.mainWindow = null
  })

  return win
}
