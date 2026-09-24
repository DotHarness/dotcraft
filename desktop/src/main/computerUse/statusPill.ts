import { BrowserWindow, globalShortcut, screen } from 'electron'

interface StatusPillStrings {
  usingComputer: string
  escToCancel: string
}

const PILL_WIDTH = 380
const PILL_HEIGHT = 44
const TOP_MARGIN = 12

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => `&#${char.charCodeAt(0)};`)
}

function pillHtml(strings: StatusPillStrings): string {
  return `<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;height:100%;background:transparent;overflow:hidden;font:500 13px/1 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;}
.pill{box-sizing:border-box;height:100%;margin:0 auto;display:flex;align-items:center;justify-content:center;gap:10px;padding:0 18px;border-radius:22px;
background:rgba(22,22,30,.92);color:#f4f4f8;border:1px solid rgba(140,130,255,.55);width:max-content;max-width:100%;}
.dot{width:8px;height:8px;border-radius:50%;background:#8b83ff;animation:pulse 1.6s ease-in-out infinite;}
.sep{opacity:.5}.hint{opacity:.72}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.35}}
@media (prefers-reduced-motion:reduce){.dot{animation:none}}
</style></head><body><div class="pill"><span class="dot"></span><span>${escapeHtml(strings.usingComputer)}</span><span class="sep">·</span><span class="hint">${escapeHtml(strings.escToCancel)}</span></div></body></html>`
}

export class ComputerUseStatusPill {
  private window: BrowserWindow | null = null
  private onEscape: (() => void) | null = null
  private escapeRegistered = false
  private suspended = 0

  show(strings: StatusPillStrings, onEscape: () => void): void {
    this.onEscape = onEscape
    const window = this.ensureWindow()
    void window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(pillHtml(strings))}`)
    const area = screen.getPrimaryDisplay().workArea
    window.setBounds({
      x: Math.round(area.x + (area.width - PILL_WIDTH) / 2),
      y: area.y + TOP_MARGIN,
      width: PILL_WIDTH,
      height: PILL_HEIGHT
    })
    window.showInactive()
    this.registerEscape()
  }

  hide(): void {
    this.onEscape = null
    this.unregisterEscape()
    if (this.window && !this.window.isDestroyed()) this.window.hide()
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
    if (this.window && !this.window.isDestroyed()) this.window.destroy()
    this.window = null
  }

  private ensureWindow(): BrowserWindow {
    if (this.window && !this.window.isDestroyed()) return this.window
    const window = new BrowserWindow({
      width: PILL_WIDTH,
      height: PILL_HEIGHT,
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
    this.window = window
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
