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

describe('shell chrome behavior source guards', () => {
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

  it('gates the first visible frame on renderer paint readiness', () => {
    const appSource = readRendererFile('App.tsx')
    const preloadSource = readPreloadFile('index.ts')
    const preloadTypes = readPreloadFile('api.d.ts')
    const mainSource = readMainFile('index.ts')

    expect(appSource).toContain('window.requestAnimationFrame')
    expect(appSource).toContain('window.api.window.rendererReadyForShow')
    expect(preloadSource).toContain("ipcRenderer.send('window:renderer-ready-for-show')")
    expect(preloadTypes).toContain('rendererReadyForShow(): void')
    expect(mainSource).toContain("ipcMain.on('window:renderer-ready-for-show', handleRendererReadyForShow)")
    expect(mainSource).toContain('rendererReadyForShow = true')
    expect(mainSource).toContain('if (isDev || !electronReadyToShow || !rendererReadyForShow) return')
    expect(mainSource).not.toContain("win.once('ready-to-show', () => {\n      clearShowFallbackTimer()\n      showWindowSafely(win)\n    })")
  })
})
