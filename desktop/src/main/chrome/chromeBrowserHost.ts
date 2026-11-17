import type { BrowserWindow } from 'electron'
import { CURSOR_ARRIVAL_TIMEOUT_MS } from '../browserAutomationCursor'
import type { BrowserUseViewerHost } from '../browserUseManager'
import { ChromeRelayClient, type ChromeRelay } from './chromeRelayClient'
import { ChromeTabPage, type ChromeTabSnapshot } from './chromeTabPage'

const USER_TAB_PREFIX = 'chrome-tab-'
const DEFAULT_GROUP_TITLE = 'DotCraft'
const DEFAULT_LAYOUT = { width: 1280, height: 720 }

interface OverlayState { active: boolean; sessionName?: string }

function isNavigationAbort(message: string): boolean {
  return message.includes('ERR_ABORTED')
}

export class ChromeBrowserHost implements BrowserUseViewerHost {
  private readonly pages = new Map<string, ChromeTabPage>()
  private readonly hostIds = new Map<number, string>()
  private readonly agentTabs = new Set<string>()
  private readonly tabThreads = new Map<string, string>()
  private readonly controllers = new Map<string, string>()
  private readonly threadSessionNames = new Map<string, string>()
  private readonly threadsWithTabs = new Set<string>()
  private readonly overlays = new Map<string, OverlayState>()
  private readonly cursorWaiters = new Map<string, { sequence: number; finish: () => void }>()
  private nextCursorSequence = 1

  constructor(private readonly relay: ChromeRelay = new ChromeRelayClient()) {
    relay.on('event', (name, params) => this.handleEvent(name, params))
    relay.on('disconnected', () => {
      for (const page of this.pages.values()) page.handleDetached('bridge_disconnected')
      this.overlays.clear()
      for (const waiter of this.cursorWaiters.values()) waiter.finish()
    })
  }

  async createAutomationTab(_win: BrowserWindow, params: { tabId: string; threadId?: string; temporary?: boolean; sessionName?: string }): Promise<void> {
    const threadId = params.threadId ?? ''
    if (params.sessionName) this.threadSessionNames.set(threadId, params.sessionName)
    const activate = params.temporary !== true && !this.threadsWithTabs.has(threadId)
    const snapshot = await this.relay.request<ChromeTabSnapshot>('tabs.create', {
      url: 'about:blank',
      active: activate,
      groupKey: threadId,
      groupTitle: this.threadSessionNames.get(threadId) || DEFAULT_GROUP_TITLE
    })
    if (params.temporary !== true) this.threadsWithTabs.add(threadId)
    this.agentTabs.add(params.tabId)
    this.tabThreads.set(params.tabId, threadId)
    this.register(params.tabId, snapshot)
  }

  getTabWebContents(_win: BrowserWindow, tabId: string): Electron.WebContents | null {
    const page = this.pages.get(tabId)
    return page && !page.isDestroyed() ? page as unknown as Electron.WebContents : null
  }

  async ensureTabWebContents(win: BrowserWindow, tabId: string): Promise<Electron.WebContents> {
    const page = this.getTabWebContents(win, tabId)
    if (!page) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return page
  }

  async refreshAutomationTargetTabs(): Promise<void> {
    const seen = new Set<string>()
    for (const snapshot of await this.relay.request<ChromeTabSnapshot[]>('tabs.list')) {
      const existing = this.hostIds.get(snapshot.tabId)
      if (existing && this.agentTabs.has(existing)) continue
      const hostId = existing ?? `${USER_TAB_PREFIX}${snapshot.tabId}`
      seen.add(hostId)
      const page = this.pages.get(hostId)
      if (page) page.handleTabUpdate(snapshot)
      else this.register(hostId, snapshot)
    }
    for (const [hostId, page] of this.pages) {
      if (!this.agentTabs.has(hostId) && !seen.has(hostId)) page.handleRemoved()
    }
  }

  listAutomationTargetTabs(): Array<{ tabId: string; currentUrl: string; title: string; loading: boolean }> {
    return [...this.pages.entries()]
      .filter(([hostId, page]) => !this.agentTabs.has(hostId) && !page.isDestroyed())
      .map(([hostId, page]) => ({ tabId: hostId, currentUrl: page.getURL(), title: page.getTitle(), loading: page.isLoading() }))
  }

  async loadAutomationUrl(_win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const page = this.requirePage(params.tabId)
    try {
      await page.loadURL(params.url)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      if (isNavigationAbort(message)) return
      if (/^[A-Za-z]+:/.test(message) && !message.startsWith('net::')) throw error
      throw new Error(`NavigationFailed: ${message}`)
    }
  }

  destroyTab(_win: BrowserWindow, tabId: string): void {
    const page = this.pages.get(tabId)
    if (!page) return
    void this.relay.request('tabs.close', { tabId: page.tabId }).catch(() => {})
    this.forget(tabId)
  }

  snapshotState(_win: BrowserWindow, tabId: string): { tabId: string; currentUrl: string; title: string; loading: boolean } | null {
    const page = this.pages.get(tabId)
    if (!page) return null
    return { tabId, currentUrl: page.getURL(), title: page.getTitle(), loading: page.isLoading() }
  }

  setAutomationState(_win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    active: boolean
    release?: boolean
    sessionName?: string
    action?: string
  }): void {
    const page = this.pages.get(params.tabId)
    if (!page) return
    const threadId = params.threadId ?? ''
    this.renameSession(threadId, params.sessionName)
    const controller = this.tabThreads.get(params.tabId) ?? this.controllers.get(params.tabId)
    if (params.action === 'claim') {
      if (controller !== undefined && controller !== threadId) {
        throw new Error('InvalidArgument: Browser tab is controlled by another DotCraft session.')
      }
      if (controller === undefined) this.controllers.set(params.tabId, threadId)
    } else if (controller !== threadId) {
      return
    }
    if (params.release) {
      this.agentTabs.delete(params.tabId)
      this.tabThreads.delete(params.tabId)
      this.controllers.delete(params.tabId)
    }
    const next: OverlayState = { active: params.active, sessionName: params.sessionName }
    const current = this.overlays.get(params.tabId)
    if (current?.active === next.active && current.sessionName === next.sessionName) return
    if (!next.active && !current) return
    if (next.active) this.overlays.set(params.tabId, next)
    else this.overlays.delete(params.tabId)
    if (!next.active) this.cursorWaiters.get(params.tabId)?.finish()
    void this.relay.request('overlay.set', {
      tabId: page.tabId,
      active: next.active,
      ...(next.sessionName ? { sessionName: next.sessionName } : {})
    }).catch(() => {})
  }

  setViewport(): void {}

  setVisible(): void {}

  setCaptureSurface(_win: BrowserWindow, tabId: string, size: { width: number; height: number } | null): void {
    const page = this.pages.get(tabId)
    if (!size || !page || page.active) return
    void this.relay.request('tabs.update', { tabId: page.tabId, active: true }).catch(() => {})
  }

  getLayoutSize(_win: BrowserWindow, tabId: string): { width: number; height: number } {
    const snapshot = this.requirePage(tabId).snapshot
    return snapshot.width && snapshot.height ? { width: snapshot.width, height: snapshot.height } : { ...DEFAULT_LAYOUT }
  }

  isVisible(_win: BrowserWindow, tabId: string): boolean {
    return this.pages.get(tabId)?.active === true
  }

  async moveMouse(_win: BrowserWindow, params: { tabId: string; x: number; y: number; waitForArrival?: boolean }): Promise<void> {
    const page = this.pages.get(params.tabId)
    if (!page) return
    this.cursorWaiters.get(params.tabId)?.finish()
    const sequence = this.nextCursorSequence++
    const animate = params.waitForArrival !== false && page.active
    const arrived = animate ? this.waitForCursor(params.tabId, sequence) : Promise.resolve()
    try {
      await this.relay.request('overlay.moveCursor', { tabId: page.tabId, x: params.x, y: params.y, sequence, animate })
    } catch {
      this.cursorWaiters.get(params.tabId)?.finish()
    }
    await arrived
  }

  prepareNavigation(_page: Electron.WebContents, url: string): string {
    if (url === 'about:blank') return url
    const protocol = new URL(url).protocol
    if (!['http:', 'https:', 'file:'].includes(protocol)) {
      throw new Error(`NavigationFailed: Blocked browser scheme: ${protocol}`)
    }
    return url
  }

  private renameSession(threadId: string, sessionName: string | undefined): void {
    if (!sessionName || this.threadSessionNames.get(threadId) === sessionName) return
    this.threadSessionNames.set(threadId, sessionName)
    if (!this.threadsWithTabs.has(threadId)) return
    void this.relay.request('tabs.setGroupTitle', { groupKey: threadId, title: sessionName }).catch(() => {})
  }

  private waitForCursor(tabId: string, sequence: number): Promise<void> {
    return new Promise((resolve) => {
      const timer = setTimeout(() => finish(), CURSOR_ARRIVAL_TIMEOUT_MS)
      const finish = () => {
        clearTimeout(timer)
        if (this.cursorWaiters.get(tabId)?.sequence === sequence) this.cursorWaiters.delete(tabId)
        resolve()
      }
      this.cursorWaiters.set(tabId, { sequence, finish })
    })
  }

  private register(hostId: string, snapshot: ChromeTabSnapshot): ChromeTabPage {
    const page = new ChromeTabPage(this.relay, snapshot)
    this.pages.set(hostId, page)
    this.hostIds.set(snapshot.tabId, hostId)
    page.once('destroyed', () => {
      if (this.pages.get(hostId) === page) this.forget(hostId)
    })
    return page
  }

  private forget(hostId: string): void {
    const page = this.pages.get(hostId)
    this.pages.delete(hostId)
    if (page && this.hostIds.get(page.tabId) === hostId) this.hostIds.delete(page.tabId)
    this.agentTabs.delete(hostId)
    this.tabThreads.delete(hostId)
    this.controllers.delete(hostId)
    this.overlays.delete(hostId)
    this.cursorWaiters.get(hostId)?.finish()
  }

  private requirePage(tabId: string): ChromeTabPage {
    const page = this.pages.get(tabId)
    if (!page || page.isDestroyed()) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return page
  }

  private pageForChromeTab(value: unknown): ChromeTabPage | undefined {
    const hostId = this.hostIds.get(Number(value))
    return hostId ? this.pages.get(hostId) : undefined
  }

  private handleEvent(name: string, params: Record<string, unknown>): void {
    switch (name) {
      case 'debugger.event':
        this.pageForChromeTab(params.tabId)?.handleCdpEvent(
          String(params.method), params.params as Record<string, unknown>, params.sessionId as string | undefined)
        return
      case 'debugger.detached':
        this.pageForChromeTab(params.tabId)?.handleDetached(String(params.reason))
        return
      case 'tabs.updated':
        this.pageForChromeTab(params.tabId)?.handleTabUpdate(params as unknown as ChromeTabSnapshot)
        return
      case 'tabs.removed':
        this.pageForChromeTab(params.tabId)?.handleRemoved()
        return
      case 'overlay.cursorArrived': {
        const hostId = this.hostIds.get(Number(params.tabId))
        const waiter = hostId ? this.cursorWaiters.get(hostId) : undefined
        if (waiter && Number(params.sequence) >= waiter.sequence) waiter.finish()
        return
      }
    }
  }
}
