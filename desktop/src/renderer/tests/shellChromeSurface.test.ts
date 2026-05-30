import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8')
}

function readMainFile(path: string): string {
  return readFileSync(resolve(rendererRoot, '..', 'main', path), 'utf8')
}

function readPreloadFile(path: string): string {
  return readFileSync(resolve(rendererRoot, '..', 'preload', path), 'utf8')
}

function readSharedFile(path: string): string {
  return readFileSync(resolve(rendererRoot, '..', 'shared', path), 'utf8')
}

describe('shell chrome surface styling', () => {
  it('defines one shared translucent shell surface for top chrome and sidebar', () => {
    const tokensCss = readRendererFile('styles/tokens.css')

    expect(tokensCss).toContain('--shell-chrome-surface:')
    expect(tokensCss).toContain('color-mix(in srgb, #202020 90%, transparent)')
    expect(tokensCss).toContain('color-mix(in srgb, #181818 88%, transparent)')
    expect(tokensCss).toContain('color-mix(in srgb, #f3f3ee 94%, transparent)')
    expect(tokensCss).toContain('color-mix(in srgb, #ededed 92%, transparent)')
    expect(tokensCss).toContain('--chrome-glass: var(--shell-chrome-surface)')
    expect(tokensCss).toContain('--sidebar-glass: var(--chrome-glass)')
    expect(tokensCss).toContain('--welcome-surface: #202020')
    expect(tokensCss).toContain('--welcome-surface: #f3f3ee')
    expect(tokensCss).toContain('--shell-window-radius: 10px')
    expect(tokensCss).toContain('--shell-chrome-blur: none')
    expect(tokensCss).toContain('--shell-chrome-border:')
  })

  it('keeps native title bar fallback colors aligned to the glass backdrop', () => {
    const titleBarOverlaySource = readSharedFile('titleBarOverlay.ts')

    expect(titleBarOverlaySource).toContain("dark: { color: '#202020', symbolColor: '#eeeeec' }")
    expect(titleBarOverlaySource).toContain("light: { color: '#f3f3ee', symbolColor: '#1a1c1f' }")
  })

  it('keeps shell carriers on shared glass while leaving the main panel separate', () => {
    const appSource = readRendererFile('App.tsx')
    const menuBarSource = readRendererFile('components/layout/CustomMenuBar.tsx')
    const threePanelSource = readRendererFile('components/layout/ThreePanel.tsx')
    const appChromeSource = appSource.slice(
      appSource.indexOf('function AppChrome'),
      appSource.indexOf('function WindowFrame')
    )
    const windowFrameSource = appSource.slice(
      appSource.indexOf('function WindowFrame'),
      appSource.indexOf('/**\n * Root application component.')
    )

    expect(windowFrameSource).toContain("className=\"dotcraft-window-frame\"")
    expect(windowFrameSource).toContain("background: plainSurface ? 'var(--welcome-surface)' : 'var(--chrome-glass)'")
    expect(appSource).toContain('plainSurface={showWelcome || showSetupInterstitial || showSetupFlow}')
    expect(windowFrameSource).toContain("const useRendererRadius = window.api.platform === 'linux'")
    expect(windowFrameSource).toContain("borderRadius: useRendererRadius && !maximized ? 'var(--shell-window-radius)' : 0")
    expect(windowFrameSource).toContain('<AppChrome>{children}</AppChrome>')
    expect(windowFrameSource).toContain('{overlays}')
    expect(appChromeSource).not.toContain("var(--chrome-glass)")
    expect(appChromeSource).not.toContain('borderRadius')
    expect(appSource).not.toContain("backdropFilter: 'var(--shell-chrome-blur)'")
    expect(appSource).not.toContain("WebkitBackdropFilter: 'var(--shell-chrome-blur)'")
    expect(menuBarSource).not.toContain('var(--chrome-glass)')
    expect(threePanelSource).toContain("background: 'transparent'")
    expect(threePanelSource).not.toContain("background: 'var(--shell-chrome-surface)'")
    expect(threePanelSource).toContain("background: 'var(--main-surface)'")
    expect(threePanelSource).not.toContain("backdropFilter: 'var(--glass-blur)'")
  })

  it('supports title-bar double click maximize without changing interactive controls', () => {
    const appSource = readRendererFile('App.tsx')
    const menuBarSource = readRendererFile('components/layout/CustomMenuBar.tsx')
    const hookSource = readRendererFile('hooks/useWindowMaximized.ts')

    expect(appSource).toContain("import { useWindowMaximized } from './hooks/useWindowMaximized'")
    expect(menuBarSource).toContain("import { useWindowMaximized } from '../../hooks/useWindowMaximized'")
    expect(menuBarSource).toContain('onDoubleClick={handleTitleBarDoubleClick}')
    expect(menuBarSource).toContain('void window.api.window.toggleMaximize()')
    expect(menuBarSource).toContain("target.closest('button,a,input,textarea,select,[role=\"button\"]')")
    expect(hookSource).toContain('windowApi.onMaximizedChange')
    expect(hookSource).toContain('windowApi.isMaximized()')
  })

  it('keeps the update available indicator from tinting the title bar background', () => {
    const menuBarSource = readRendererFile('components/layout/CustomMenuBar.tsx')
    const updateButtonSource = menuBarSource.slice(
      menuBarSource.indexOf('function updateButtonStyle'),
      menuBarSource.indexOf('const updateBadgeStyle')
    )

    expect(updateButtonSource).toContain("color: active ? 'var(--accent)' : 'var(--text-secondary)'")
    expect(updateButtonSource).toContain("backgroundColor: 'transparent'")
    expect(updateButtonSource).not.toContain('color-mix(in srgb, var(--accent) 14%, transparent)')
  })

  it('uses glass-aware sidebar state tokens instead of solid panel fills', () => {
    const tokensCss = readRendererFile('styles/tokens.css')
    const sidebarSource = readRendererFile('components/layout/Sidebar.tsx')
    const newThreadSource = readRendererFile('components/sidebar/NewThreadButton.tsx')
    const threadEntrySource = readRendererFile('components/sidebar/ThreadEntry.tsx')

    expect(tokensCss).toContain('--sidebar-control-hover:')
    expect(tokensCss).toContain('--sidebar-control-active:')
    expect(tokensCss).toContain('--sidebar-control-hover: color-mix(in srgb, var(--text-primary) 8%, transparent)')
    expect(tokensCss).toContain('--sidebar-control-active: color-mix(in srgb, var(--text-primary) 13%, transparent)')
    expect(sidebarSource).toContain('var(--sidebar-control-active)')
    expect(sidebarSource).toContain('var(--sidebar-control-hover)')
    expect(newThreadSource).toContain('var(--sidebar-control-hover)')
    expect(threadEntrySource).toContain('var(--sidebar-control-active)')
    expect(threadEntrySource).toContain('var(--sidebar-control-hover)')
  })

  it('uses a tokenized main frame shadow without a left-casting sidebar shadow', () => {
    const tokensCss = readRendererFile('styles/tokens.css')
    const threePanelSource = readRendererFile('components/layout/ThreePanel.tsx')

    expect(tokensCss).toContain('--main-surface-frame-shadow:')
    expect(threePanelSource).toContain("boxShadow: 'var(--main-surface-frame-shadow)'")
    expect(threePanelSource).not.toContain('-18px 0 42px')
    expect(tokensCss).not.toContain('-18px 0 42px')
  })

  it('uses compact resize dividers without adding a thick visible drag bar', () => {
    const tokensCss = readRendererFile('styles/tokens.css')
    const dragHandleSource = readRendererFile('components/layout/DragHandle.tsx')
    const threePanelSource = readRendererFile('components/layout/ThreePanel.tsx')
    const detailPanelSource = readRendererFile('components/layout/DetailPanel.tsx')

    expect(tokensCss).toContain('--resize-divider-hit-width: 8px')
    expect(tokensCss).toContain(
      '--resize-divider-active: color-mix(in srgb, var(--text-primary) 42%, transparent)'
    )
    expect(tokensCss).toContain('--main-surface-left-border: var(--glass-border-strong)')
    expect(tokensCss).toContain('--main-surface-top-border:')
    expect(tokensCss).toContain('inset 1px 0 0 0 var(--main-surface-left-border)')
    expect(tokensCss).toContain('inset 0 1px 0 0 var(--main-surface-top-border)')
    expect(tokensCss).not.toContain('--resize-divider-line-width')
    expect(tokensCss).not.toContain('--resize-divider-idle')
    expect(dragHandleSource).toContain("width: 'var(--resize-divider-hit-width)'")
    expect(dragHandleSource).toContain('onPointerEnter={() => updateHovering(true)}')
    expect(dragHandleSource).toContain('onPointerDown={handlePointerDown}')
    expect(dragHandleSource).toContain('onActiveChange?: (active: boolean) => void')
    expect(dragHandleSource).toContain('onDragStateChange?: (dragging: boolean) => void')
    expect(dragHandleSource).not.toContain('onMouseEnter')
    expect(dragHandleSource).not.toContain('onMouseDown')
    expect(dragHandleSource).not.toContain('drag-handle__line')
    expect(dragHandleSource).not.toContain('idleLineColor')
    expect(dragHandleSource).not.toContain('highlightInsetTop')
    expect(dragHandleSource).not.toContain('highlightRef')
    expect(dragHandleSource).not.toContain("backgroundColor = 'var(--border-active)'")
    expect(threePanelSource).toContain('const RESIZE_HANDLE_HIT_WIDTH = 8')
    expect(threePanelSource).toContain("'--main-surface-left-border': sidebarDividerHighlighted")
    expect(threePanelSource).toContain("'--detail-divider-border': detailDividerHighlighted")
    expect(threePanelSource).toContain("resizingEdge === 'sidebar' ? 'none'")
    expect(threePanelSource).toContain("resizingEdge === 'detail' ? 'none'")
    expect(detailPanelSource).toContain(
      'var(--detail-divider-border, var(--glass-border))'
    )
    expect(threePanelSource).not.toContain('PANEL_DRAG_HANDLE_WIDTH')
    expect(threePanelSource).not.toContain('mainSurfaceWidth - PANEL_DRAG_HANDLE_WIDTH')
  })

  it('gates the first visible frame on renderer paint readiness', () => {
    const appSource = readRendererFile('App.tsx')
    const preloadSource = readPreloadFile('index.ts')
    const preloadTypes = readPreloadFile('api.d.ts')
    const mainSource = readMainFile('index.ts')
    const indexHtml = readRendererFile('index.html')

    expect(indexHtml).toContain('background: #202020')
    expect(indexHtml).toContain('background: #f3f3ee')
    expect(appSource).toContain('window.requestAnimationFrame')
    expect(appSource).toContain('window.api.window.rendererReadyForShow')
    expect(preloadSource).toContain("ipcRenderer.send('window:renderer-ready-for-show')")
    expect(preloadTypes).toContain('rendererReadyForShow(): void')
    expect(mainSource).toContain("ipcMain.on('window:renderer-ready-for-show', handleRendererReadyForShow)")
    expect(mainSource).toContain('rendererReadyForShow = true')
    expect(mainSource).toContain('if (isDev || !electronReadyToShow || !rendererReadyForShow) return')
    expect(mainSource).not.toContain("win.once('ready-to-show', () => {\n      clearShowFallbackTimer()\n      showWindowSafely(win)\n    })")
  })

  it('keeps common light popover shadows on theme tokens instead of hard-coded dark shadows', () => {
    const threadSearchSource = readRendererFile('components/sidebar/ThreadSearch.tsx')
    const composerShellSource = readRendererFile('components/conversation/ComposerShell.tsx')
    const subAgentDockSource = readRendererFile('components/conversation/SubAgentDock.tsx')
    const tokensCss = readRendererFile('styles/tokens.css')

    expect(tokensCss).toContain('--background-activity-dock-shadow:')
    expect(threadSearchSource).toContain("boxShadow: 'var(--glass-shadow-soft)'")
    expect(threadSearchSource).not.toContain('0 28px 80px rgba(0, 0, 0, 0.28)')
    expect(composerShellSource).toContain("boxShadow: 'var(--composer-action-shadow)'")
    expect(composerShellSource).not.toContain('0 4px 10px rgba(0, 0, 0, 0.16)')
    expect(subAgentDockSource).toContain("boxShadow: 'var(--background-activity-dock-shadow)'")
    expect(subAgentDockSource).not.toContain('0 10px 26px rgba(0, 0, 0, 0.14)')
  })
})
