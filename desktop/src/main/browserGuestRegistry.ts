import { session, webContents, type BrowserWindow, type WebContents } from 'electron'
import type { BrowserHostDescriptor, BrowserHostEvent } from '../shared/viewer/browserHost'

interface GuestEntry {
  descriptor: BrowserHostDescriptor
  ready: Promise<WebContents>
  resolve: (page: WebContents) => void
  reject: (error: Error) => void
  timer: ReturnType<typeof setTimeout>
  page?: WebContents
}

export class BrowserGuestRegistry {
  private windows = new Map<number, Map<string, GuestEntry>>()

  attachWindow(win: BrowserWindow): void {
    win.webContents.on('will-attach-webview', (_event, preferences) => {
      delete preferences.preload
      preferences.nodeIntegration = false
      preferences.contextIsolation = true
      preferences.sandbox = true
      preferences.devTools = true
    })
    win.once('closed', () => this.clear(win))
  }

  request(win: BrowserWindow, tabId: string, partition: string, viewport?: { width: number; height: number }): Promise<WebContents> {
    let entries = this.windows.get(win.id)
    if (!entries) this.windows.set(win.id, entries = new Map())
    const previous = entries.get(tabId)
    if (previous) return previous.ready
    let resolve!: GuestEntry['resolve']
    let reject!: GuestEntry['reject']
    const ready = new Promise<WebContents>((accept, fail) => { resolve = accept; reject = fail })
    const descriptor: BrowserHostDescriptor = {
      tabId, partition, visible: false, automation: viewport !== undefined,
      bounds: { x: 0, y: 0, width: viewport?.width ?? 1280, height: viewport?.height ?? 720 }
    }
    const timer = setTimeout(() => this.remove(win, tabId, 'Browser guest did not become ready.'), 30_000)
    entries.set(tabId, { descriptor, ready, resolve, reject, timer })
    this.emit(win, { type: 'update', host: descriptor })
    return ready
  }

  bind(win: BrowserWindow, tabId: string, id: number): void {
    const entry = this.windows.get(win.id)?.get(tabId)
    const page = webContents.fromId(id)
    if (!entry || !page || page.isDestroyed() || page.hostWebContents !== win.webContents ||
        page.session !== session.fromPartition(entry.descriptor.partition)) {
      throw new Error('Browser guest does not belong to this tab host.')
    }
    if (entry.page) {
      if (entry.page !== page) throw new Error('Browser tab already has a guest.')
      return
    }
    entry.page = page
    clearTimeout(entry.timer)
    page.on('before-mouse-event', (_event, mouse) => {
      if (mouse.type === 'mouseDown') this.emit(win, { type: 'pointer-down', tabId })
    })
    page.once('destroyed', () => this.remove(win, tabId, 'Browser page closed.'))
    entry.resolve(page)
  }

  list(win: BrowserWindow): BrowserHostDescriptor[] {
    return [...this.windows.get(win.id)?.values() ?? []].map(entry => entry.descriptor)
  }

  update(win: BrowserWindow, tabId: string, changes: Partial<BrowserHostDescriptor>): void {
    const entry = this.windows.get(win.id)?.get(tabId)
    if (!entry) return
    entry.descriptor = { ...entry.descriptor, ...changes }
    this.emit(win, { type: 'update', host: entry.descriptor })
  }

  remove(win: BrowserWindow, tabId: string, message = 'Browser page closed.'): void {
    const entries = this.windows.get(win.id)
    const entry = entries?.get(tabId)
    if (!entry) return
    entries!.delete(tabId)
    clearTimeout(entry.timer)
    entry.reject(new Error(message))
    this.emit(win, { type: 'remove', tabId })
    if (entry.page && !entry.page.isDestroyed()) entry.page.close({ waitForBeforeUnload: false })
  }

  clear(win: BrowserWindow): void {
    for (const tabId of this.windows.get(win.id)?.keys() ?? []) this.remove(win, tabId)
    this.windows.delete(win.id)
  }

  private emit(win: BrowserWindow, event: BrowserHostEvent): void {
    if (!win.isDestroyed() && !win.webContents.isDestroyed()) win.webContents.send('viewer:browser:host-event', event)
  }
}
