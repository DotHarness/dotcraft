import { BrowserWindow, globalShortcut, screen, type Rectangle } from 'electron'

import {
  COMPUTER_USE_PILL_HEIGHT,
  COMPUTER_USE_PILL_WIDTH,
  computerUseGlowHtml,
  computerUsePillHtml,
  type ComputerUsePillStrings
} from '../../shared/computerUsePill'

const TOP_MARGIN = 12

export class ComputerUseStatusPill {
  private pill: BrowserWindow | null = null
  private glow: BrowserWindow | null = null
  private onEscape: (() => void) | null = null
  private escapeRegistered = false
  private suspended = 0

  show(strings: ComputerUsePillStrings, onEscape: () => void): void {
    this.onEscape = onEscape
    const display = screen.getPrimaryDisplay()
    this.glow = this.present(this.glow, computerUseGlowHtml(), display.bounds)
    const area = display.workArea
    this.pill = this.present(this.pill, computerUsePillHtml(strings), {
      x: Math.round(area.x + (area.width - COMPUTER_USE_PILL_WIDTH) / 2),
      y: area.y + TOP_MARGIN,
      width: COMPUTER_USE_PILL_WIDTH,
      height: COMPUTER_USE_PILL_HEIGHT
    })
    this.registerEscape()
  }

  hide(): void {
    this.onEscape = null
    this.unregisterEscape()
    for (const window of [this.pill, this.glow]) {
      if (window && !window.isDestroyed()) window.hide()
    }
  }

  async suspendEscape<T>(run: () => Promise<T>): Promise<T> {
    this.suspended += 1
    this.unregisterEscape()
    try {
      return await run()
    } finally {
      this.suspended -= 1
      if (this.suspended === 0 && this.onEscape) this.registerEscape()
    }
  }

  dispose(): void {
    this.hide()
    for (const window of [this.pill, this.glow]) {
      if (window && !window.isDestroyed()) window.destroy()
    }
    this.pill = null
    this.glow = null
  }

  private present(existing: BrowserWindow | null, html: string, bounds: Rectangle): BrowserWindow {
    const window = existing && !existing.isDestroyed() ? existing : createOverlayWindow()
    void window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(html)}`)
    window.setBounds(bounds)
    window.showInactive()
    return window
  }

  private registerEscape(): void {
    if (this.escapeRegistered || this.suspended > 0 || !this.onEscape) return
    this.escapeRegistered = globalShortcut.register('Escape', () => this.onEscape?.())
  }

  private unregisterEscape(): void {
    if (!this.escapeRegistered) return
    globalShortcut.unregister('Escape')
    this.escapeRegistered = false
  }
}

function createOverlayWindow(): BrowserWindow {
  const window = new BrowserWindow({
    show: false,
    frame: false,
    transparent: true,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    focusable: false,
    skipTaskbar: true,
    hasShadow: false,
    alwaysOnTop: true,
    webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false, javascript: false }
  })
  window.setAlwaysOnTop(true, 'screen-saver')
  window.setIgnoreMouseEvents(true)
  window.setContentProtection(true)
  return window
}
