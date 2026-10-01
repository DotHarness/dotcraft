import type { BrowserWindow } from 'electron'
import type { BrowserHostDescriptor } from '../shared/viewer/browserHost'

export const CURSOR_ARRIVAL_TIMEOUT_MS = 1500

interface CursorHost {
  get(win: BrowserWindow, tabId: string): BrowserHostDescriptor | undefined
  update(win: BrowserWindow, tabId: string, changes: Partial<BrowserHostDescriptor>): void
}

interface ArrivalWaiter {
  windowId: number
  tabId: string
  finish: () => void
}

export class BrowserAutomationCursors {
  private readonly waiters = new Map<number, ArrivalWaiter>()
  private nextSequence = 1

  constructor(private readonly host: CursorHost) {}

  activate(win: BrowserWindow, tabId: string): void {
    if (this.host.get(win, tabId)?.cursor?.visible) return
    this.host.update(win, tabId, { cursor: { visible: true } })
  }

  deactivate(win: BrowserWindow, tabId: string): void {
    const cursor = this.host.get(win, tabId)?.cursor
    if (!cursor?.visible) return
    this.finishWaiters(win.id, tabId)
    this.host.update(win, tabId, { cursor: { visible: false, x: cursor.x, y: cursor.y } })
  }

  release(win: BrowserWindow, tabId: string): void {
    this.finishWaiters(win.id, tabId)
    if (this.host.get(win, tabId)?.cursor) this.host.update(win, tabId, { cursor: undefined })
  }

  forget(win: BrowserWindow, tabId: string): void {
    this.finishWaiters(win.id, tabId)
  }

  async move(
    win: BrowserWindow,
    tabId: string,
    point: { x: number; y: number },
    options: { waitForArrival?: boolean } = {}
  ): Promise<void> {
    const host = this.host.get(win, tabId)
    if (!host?.cursor?.visible) return
    const moveSequence = this.nextSequence++
    const animate = win.isFocused() && host.visible && !host.captureSurfaceSize
    const arrival = animate && options.waitForArrival !== false
      ? this.waitForArrival(win.id, tabId, moveSequence)
      : undefined
    this.host.update(win, tabId, { cursor: { visible: true, x: point.x, y: point.y, moveSequence, animate } })
    await arrival
  }

  arrived(win: BrowserWindow, tabId: string, moveSequence: number): void {
    const waiter = this.waiters.get(moveSequence)
    if (waiter?.windowId === win.id && waiter.tabId === tabId) waiter.finish()
  }

  private waitForArrival(windowId: number, tabId: string, moveSequence: number): Promise<void> {
    return new Promise(resolve => {
      const timer = setTimeout(() => finish(), CURSOR_ARRIVAL_TIMEOUT_MS)
      const finish = () => {
        clearTimeout(timer)
        this.waiters.delete(moveSequence)
        resolve()
      }
      this.waiters.set(moveSequence, { windowId, tabId, finish })
    })
  }

  private finishWaiters(windowId: number, tabId: string): void {
    for (const waiter of [...this.waiters.values()]) {
      if (waiter.windowId === windowId && waiter.tabId === tabId) waiter.finish()
    }
  }
}
