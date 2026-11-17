import { EventEmitter } from 'node:events'
import type { ChromeRelay } from './chromeRelayClient'

export interface ChromeTabSnapshot {
  tabId: number
  windowId?: number
  url: string
  title: string
  loading: boolean
  active: boolean
  width?: number
  height?: number
}

interface NavigationEntry { id: number }

const STOPPED_BY_USER = 'CommandCancelled: Browser control was stopped in Chrome.'
const CONSOLE_LEVELS: Record<string, number> = { debug: 0, log: 1, info: 1, warning: 2, error: 3, assert: 3 }

function stringField(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

function consoleText(args: unknown): string {
  if (!Array.isArray(args)) return ''
  return args.map((arg) => {
    const item = arg as { value?: unknown; description?: unknown; unserializableValue?: unknown }
    if (item.value !== undefined) return typeof item.value === 'string' ? item.value : JSON.stringify(item.value)
    return String(item.unserializableValue ?? item.description ?? '')
  }).join(' ')
}

class ChromeTabDebugger extends EventEmitter {
  private attached = false
  private ready: Promise<void> | null = null

  constructor(private readonly page: ChromeTabPage) {
    super()
  }

  isAttached(): boolean {
    return this.attached
  }

  attach(_protocolVersion?: string): void {
    if (this.page.isDestroyed()) throw new Error(`PageClosed: Browser page is closed: ${this.page.tabId}`)
    if (this.page.stoppedByUser) throw new Error(STOPPED_BY_USER)
    if (this.attached) return
    this.attached = true
    const ready = this.page.relay.request('debugger.attach', { tabId: this.page.tabId })
      .then(() => this.page.initializeAttachment())
    this.ready = ready
    ready.catch(() => {
      if (this.ready !== ready) return
      this.attached = false
      this.ready = null
    })
  }

  detach(): void {
    if (!this.attached) return
    this.attached = false
    this.ready = null
    void this.page.relay.request('debugger.detach', { tabId: this.page.tabId }).catch(() => {})
  }

  async sendCommand(method: string, params: Record<string, unknown> = {}, sessionId?: string): Promise<unknown> {
    const ready = this.ready
    if (!this.attached || !ready) throw new Error(`Debugger is not attached to Chrome tab ${this.page.tabId}.`)
    await ready
    return await this.page.relay.request('debugger.send', {
      tabId: this.page.tabId,
      method,
      params,
      ...(sessionId ? { sessionId } : {})
    })
  }

  sendInternal(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    return this.page.relay.request('debugger.send', { tabId: this.page.tabId, method, params })
  }

  handleDetached(reason: string): void {
    if (!this.attached) return
    this.attached = false
    this.ready = null
    this.emit('detach', {}, reason)
  }
}

export class ChromeTabPage extends EventEmitter {
  readonly debugger: ChromeTabDebugger
  readonly navigationHistory = {
    canGoBack: (): boolean => this.historyIndex > 0,
    canGoForward: (): boolean => this.historyIndex >= 0 && this.historyIndex < this.historyEntries.length - 1,
    goBack: (): void => this.goToHistoryOffset(-1),
    goForward: (): void => this.goToHistoryOffset(1)
  }
  stoppedByUser = false
  private destroyed = false
  private url: string
  private title: string
  private loading: boolean
  private snapshotState: ChromeTabSnapshot
  private mainFrameId: string | null = null
  private historyIndex = -1
  private historyEntries: NavigationEntry[] = []

  constructor(readonly relay: ChromeRelay, snapshot: ChromeTabSnapshot) {
    super()
    this.debugger = new ChromeTabDebugger(this)
    this.snapshotState = snapshot
    this.url = snapshot.url
    this.title = snapshot.title
    this.loading = snapshot.loading
  }

  get tabId(): number {
    return this.snapshotState.tabId
  }

  get id(): number {
    return -this.tabId
  }

  get active(): boolean {
    return this.snapshotState.active
  }

  get snapshot(): ChromeTabSnapshot {
    return { ...this.snapshotState, url: this.url, title: this.title, loading: this.loading }
  }

  getURL(): string { return this.url }
  getTitle(): string { return this.title }
  isLoading(): boolean { return this.loading }
  isDestroyed(): boolean { return this.destroyed }
  getBackgroundThrottling(): boolean { return false }

  stop(): void {
    if (this.debugger.isAttached()) void this.debugger.sendCommand('Page.stopLoading').catch(() => {})
  }

  reload(): void {
    this.debugger.attach()
    this.setLoading(true)
    void this.debugger.sendCommand('Page.reload').catch(() => this.setLoading(false))
  }

  async loadURL(url: string): Promise<void> {
    this.debugger.attach()
    const result = await this.debugger.sendCommand('Page.navigate', { url }) as { errorText?: string; loaderId?: string }
    if (result?.errorText) throw new Error(result.errorText)
    if (!result?.loaderId || !this.loading) return
    await new Promise<void>((resolve, reject) => {
      const cleanup = () => {
        this.off('did-stop-loading', done)
        this.off('did-fail-load', failed)
        this.off('destroyed', closed)
      }
      const done = () => { cleanup(); resolve() }
      const failed = (_event: unknown, _code: number, description: string) => { cleanup(); reject(new Error(description)) }
      const closed = () => { cleanup(); reject(new Error(`PageClosed: Browser page is closed: ${this.tabId}`)) }
      this.on('did-stop-loading', done)
      this.on('did-fail-load', failed)
      this.once('destroyed', closed)
    })
  }

  async initializeAttachment(): Promise<void> {
    await this.debugger.sendInternal('Page.enable')
    await this.debugger.sendInternal('Runtime.enable')
    const tree = await this.debugger.sendInternal('Page.getFrameTree') as { frameTree?: { frame?: { id?: string; url?: string } } }
    const frame = tree?.frameTree?.frame
    if (frame?.id) this.mainFrameId = frame.id
    if (frame?.url) this.url = frame.url
    await this.refreshHistory()
  }

  handleCdpEvent(method: string, params: Record<string, unknown>, sessionId?: string): void {
    this.debugger.emit('message', {}, method, params, sessionId)
    if (sessionId) return
    switch (method) {
      case 'Page.frameNavigated': {
        const frame = params.frame as { id?: string; parentId?: string; url?: string; unreachableUrl?: string } | undefined
        if (!frame || frame.parentId) return
        if (frame.id) this.mainFrameId = frame.id
        this.url = frame.url ?? this.url
        if (frame.unreachableUrl) {
          this.emit('did-fail-load', {}, -2, 'Chromium error page after navigation.', frame.unreachableUrl, true)
        }
        this.emit('did-navigate', {}, this.url)
        void this.refreshHistory()
        return
      }
      case 'Page.navigatedWithinDocument':
        if (this.isMainFrame(params.frameId)) {
          this.url = stringField(params.url) ?? this.url
          void this.refreshHistory()
        }
        return
      case 'Page.frameStartedLoading':
        if (this.isMainFrame(params.frameId)) this.setLoading(true)
        return
      case 'Page.frameStoppedLoading':
        if (this.isMainFrame(params.frameId)) this.setLoading(false)
        return
      case 'Page.domContentEventFired':
        this.emit('dom-ready', {})
        return
      case 'Page.loadEventFired':
        this.emit('did-finish-load', {})
        return
      case 'Runtime.consoleAPICalled':
        this.emit('console-message', {}, CONSOLE_LEVELS[stringField(params.type) ?? 'log'] ?? 1, consoleText(params.args))
        return
    }
  }

  handleTabUpdate(snapshot: ChromeTabSnapshot): void {
    this.snapshotState = snapshot
    this.title = snapshot.title
    if (this.debugger.isAttached()) return
    const navigated = snapshot.url !== this.url
    this.url = snapshot.url
    if (navigated) this.emit('did-navigate', {}, this.url)
    if (snapshot.loading !== this.loading) {
      this.setLoading(snapshot.loading)
      if (!snapshot.loading) {
        this.emit('dom-ready', {})
        this.emit('did-finish-load', {})
      }
    }
  }

  handleDetached(reason: string): void {
    if (reason === 'canceled_by_user') this.stoppedByUser = true
    this.debugger.handleDetached(reason)
  }

  handleRemoved(): void {
    if (this.destroyed) return
    this.destroyed = true
    this.debugger.handleDetached('target_closed')
    this.emit('destroyed')
  }

  private isMainFrame(frameId: unknown): boolean {
    return this.mainFrameId === null || frameId === this.mainFrameId
  }

  private setLoading(loading: boolean): void {
    if (this.loading === loading) return
    this.loading = loading
    this.emit(loading ? 'did-start-loading' : 'did-stop-loading', {})
  }

  private async refreshHistory(): Promise<void> {
    if (!this.debugger.isAttached()) return
    try {
      const history = await this.debugger.sendInternal('Page.getNavigationHistory') as { currentIndex?: number; entries?: NavigationEntry[] }
      this.historyIndex = typeof history?.currentIndex === 'number' ? history.currentIndex : -1
      this.historyEntries = Array.isArray(history?.entries) ? history.entries : []
    } catch {}
  }

  private goToHistoryOffset(offset: number): void {
    const entry = this.historyEntries[this.historyIndex + offset]
    if (!entry || !this.debugger.isAttached()) return
    this.setLoading(true)
    void this.debugger.sendCommand('Page.navigateToHistoryEntry', { entryId: entry.id }).catch(() => this.setLoading(false))
  }
}
