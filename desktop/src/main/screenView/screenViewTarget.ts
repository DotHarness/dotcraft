import type { BrowserWindow } from 'electron'

export interface ScreenViewTarget {
  readonly id: number
  isVisible(): boolean
  send(channel: string, payload: unknown): void
  onVisibilityChange(listener: () => void): void
  onGone(listener: () => void): void
}

export function browserWindowTarget(win: BrowserWindow): ScreenViewTarget {
  return {
    id: win.id,
    isVisible(): boolean {
      if (win.isDestroyed()) return false
      return win.isVisible() && !win.isMinimized()
    },
    send(channel, payload): void {
      if (win.isDestroyed() || win.webContents.isDestroyed()) return
      win.webContents.send(channel, payload)
    },
    onVisibilityChange(listener): void {
      win.on('hide', listener)
      win.on('show', listener)
      win.on('minimize', listener)
      win.on('restore', listener)
    },
    onGone(listener): void {
      win.once('closed', listener)
      win.webContents.on('did-finish-load', listener)
    }
  }
}
