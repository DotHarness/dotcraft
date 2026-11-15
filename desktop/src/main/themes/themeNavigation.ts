import type { BrowserWindow, Event, WebFrameMain } from 'electron'
import { MCP_APP_SANDBOX_PROXY_URL } from '../../shared/mcpAppSandbox'

interface FrameNavigation extends Event {
  url: string
  frame: WebFrameMain | null
}

export function isThemeFrame(frame: Pick<WebFrameMain, 'name' | 'parent'> | null): boolean {
  let current = frame
  while (current) {
    if (current.name === 'dotcraft-themes') return true
    current = current.parent
  }
  return false
}

export function installThemeNavigationGuard(win: BrowserWindow): () => void {
  const guard = (event: FrameNavigation): void => {
    if (!isThemeFrame(event.frame)) return
    if (event.url !== MCP_APP_SANDBOX_PROXY_URL && event.url !== 'about:blank' && event.url !== 'about:srcdoc') event.preventDefault()
  }
  win.webContents.on('will-frame-navigate', guard)
  win.webContents.on('will-redirect', guard)
  return () => {
    if (win.webContents.isDestroyed()) return
    win.webContents.removeListener('will-frame-navigate', guard)
    win.webContents.removeListener('will-redirect', guard)
  }
}
