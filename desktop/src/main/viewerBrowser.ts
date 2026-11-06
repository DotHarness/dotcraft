import { BrowserGuestRegistry } from './browserGuestRegistry'
import { BrowserPageLifecycle, type BrowserPageEntry, type BrowserPageParams, type BrowserRestoreValidator } from './browserPageLifecycle'
import { BrowserPageSnapshotStore } from './browserPageSnapshotStore'
import { app, BrowserWindow, shell } from 'electron'
import { bindBrowserPageEvents, type BrowserTabRuntime } from './browserPageEvents'
import { join } from 'node:path'
import { BrowserDownloads } from './browserDownloads'
import { fileURLToPath } from 'url'
import type { BrowserEventPayload, BrowserRestoreResult } from '../shared/viewer/types'
import { viewerUrlToPath } from './viewerFileProtocol'
import { BrowserSessionPolicy, bindBrowserPageSession, browserLoadUrl, assertBrowserPageNavigation } from './browserSessionPolicy'
import { BrowserGuestNavigation, classifyTabNavigation, extractScheme, normalizeBrowserUrl } from './browserGuestNavigation'
export { classifyBrowserUrl, normalizeBrowserUrl } from './browserGuestNavigation'
export { partitionForWorkspace } from './browserSessionPolicy'
import { loadOrReport } from './browserPageLoad'
export { loadOrReport } from './browserPageLoad'

const BROWSER_EVENT_CHANNEL = 'viewer:browser:event'
const START_URL = 'about:blank'
const VIEWER_SCHEME = 'dotcraft-viewer:'
const DEFAULT_START_TITLE = 'DotCraft Browser'

export interface BrowserSnapshot {
  tabId: string
  threadId?: string
  projectKey?: string
  browserStorageId?: string
  suspended?: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  canGoBack: boolean
  canGoForward: boolean
  loading: boolean
}

export interface BrowserAutomationMoveParams {
  tabId: string
  x: number
  y: number
  waitForArrival?: boolean
}

export interface BrowserAutomationStateParams {
  tabId: string
  active: boolean
  release?: boolean
  sessionName?: string
  action?: string
}

function emitBrowserEvent(win: BrowserWindow, payload: BrowserEventPayload): void {
  if (win.isDestroyed() || win.webContents.isDestroyed()) return
  win.webContents.send(BROWSER_EVENT_CHANNEL, payload)
}

function ensureDataUrl(html: string): string {
  return `data:text/html;charset=utf-8,${encodeURIComponent(html)}`
}

function buildStartPageHtml(message: string): string {
  return `<!doctype html><html><head><meta charset="utf-8"><title>${DEFAULT_START_TITLE}</title><style>
  body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:#1f1f1f;color:#d8d8d8;display:flex;align-items:center;justify-content:center;height:100vh}
  .wrap{max-width:520px;padding:24px 28px;border:1px solid rgba(255,255,255,0.12);border-radius:10px;background:rgba(255,255,255,0.03)}
  h1{margin:0 0 8px;font-size:18px}p{margin:0;font-size:13px;line-height:1.5;color:#b8b8b8}
  </style></head><body><div class="wrap"><h1>${DEFAULT_START_TITLE}</h1><p>${escapeHtml(message)}</p></div></body></html>`
}

function escapeHtml(input: string): string {
  return input
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

export class ViewerBrowserManager {
  readonly hosts = new BrowserGuestRegistry()
  private downloads: BrowserDownloads | null = null
  private readonly byWindowId = new Map<number, Map<string, BrowserTabRuntime>>()
  private readonly creating = new Map<BrowserPageEntry, Promise<BrowserSnapshot>>()
  private snapshotStore?: BrowserPageSnapshotStore
  private readonly pages: BrowserPageLifecycle

  constructor(options: { snapshotStore?: BrowserPageSnapshotStore; now?: () => number; budget?: number } = {}) {
    this.snapshotStore = options.snapshotStore
    this.pages = new BrowserPageLifecycle(
      () => this.snapshotStore ??= new BrowserPageSnapshotStore(join(app.getPath('userData'), 'browser-page-states.json')),
      entry => this.releasePage(entry, true),
      tabId => this.downloads?.hasActiveDownload(tabId) === true,
      options.now, options.budget
    )
  }

  registerPersistenceOwner(win: BrowserWindow): void { this.pages.registerPersistenceOwner(win) }
  setRestoreValidator(validator: BrowserRestoreValidator): void { this.pages.setRestoreValidator(validator) }
  setForegroundScope(win: BrowserWindow, scope: { projectKey: string; threadId?: string }): void { this.pages.setForeground(win, scope) }
  restoreState(win: BrowserWindow, params: Omit<BrowserPageParams, 'tabId'> & { tabs: Array<{ tabId: string; browserStorageId: string; initialUrl?: string }> }): BrowserRestoreResult[] {
    return this.pages.restoreState(win, params)
  }

  async ensureTabWebContents(win: BrowserWindow, tabId: string): Promise<Electron.WebContents> {
    const entry = this.pages.get(win, tabId)
    if (!entry) throw new Error(`Browser tab is no longer available: ${tabId}`)
    const previousAutomation = entry.automation
    entry.automation = true
    try { await this.createTab(win, entry.params) } catch (error) {
      entry.automation = previousAutomation
      throw error
    }
    const page = this.getTabWebContents(win, tabId)
    if (!page) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return page
  }

  suspendTab(win: BrowserWindow, tabId: string): boolean {
    return this.pages.trySuspend(win, tabId)
  }

  private releasePage(entry: BrowserPageEntry, suspended: boolean): void {
    const { win, owner } = entry
    const runtime = this.byWindowId.get(win.id)
    const tab = runtime?.get(owner.tabId)
    runtime?.delete(owner.tabId)
    if (tab) {
      for (const popup of tab.authPopups) if (!popup.isDestroyed()) popup.destroy()
      tab.authPopups.clear()
    }
    this.hosts.remove(win, owner.tabId)
    if (suspended) this.emit(win, {
      tabId: owner.tabId, threadId: owner.threadId, projectKey: owner.projectKey,
      browserStorageId: entry.browserStorageId, type: 'suspended', suspended: true
    })
  }
  private readonly navigation = new BrowserGuestNavigation((win, payload) => this.emit(win, payload))
  private readonly sessions = new BrowserSessionPolicy((event, item, page) => {
    for (const runtime of this.byWindowId.values()) {
      for (const tab of runtime.values()) {
        if (tab.page.id === page.id) {
          this.userDownloads().start(item, { tabId: tab.tabId, threadId: tab.threadId })
          return
        }
      }
    }
    event.preventDefault()
  })
  private startPageHint = 'Enter a URL in the address bar to begin browsing.'

  setStartPageHint(hint: string): void {
    this.startPageHint = hint.trim() || this.startPageHint
  }

  async createTab(win: BrowserWindow, params: BrowserPageParams): Promise<BrowserSnapshot> {
    const entry = this.pages.prepare(win, params)
    const pending = this.creating.get(entry)
    if (pending) return pending
    const existing = this.getTab(win, params.tabId)
    if (existing && !existing.page.isDestroyed()) return this.snapshotFromRuntime(existing)
    const creation = this.createPage(entry).finally(() => { this.creating.delete(entry) })
    this.creating.set(entry, creation)
    return creation
  }

  private async createPage(entry: BrowserPageEntry): Promise<BrowserSnapshot> {
    const { win, params } = entry
    const runtime = this.ensureWindowRuntime(win)
    const initialUrl = entry.snapshot?.url ?? params.initialUrl ?? START_URL
    const { partition, kind } = this.sessions.select(win, params.workspacePath, params.sessionUrl ?? initialUrl)
    entry.restoring = true
    try {
      const page = await this.hosts.request(win, params.tabId, partition, entry.automation)
      if (!this.pages.current(entry) || page.isDestroyed() || !this.hosts.list(win).some(host => host.tabId === params.tabId)) throw new Error('Browser page closed.')
      const tab: BrowserTabRuntime = {
        tabId: params.tabId, threadId: params.threadId,
        projectKey: entry.owner.projectKey, browserStorageId: entry.browserStorageId, page,
        desiredVisible: false, boundsInitialized: false,
        currentUrl: initialUrl, title: entry.snapshot?.title ?? DEFAULT_START_TITLE,
        faviconDataUrl: entry.snapshot?.faviconDataUrl, automationActive: entry.automation, authPopups: new Set()
      }
      runtime.set(params.tabId, tab)
      entry.page = page
      bindBrowserPageSession(page, kind, params.allowFileScheme === true)
      this.bindWebContentsEvents(win, tab)
      let restored = false
      if (entry.snapshot) restored = await this.pages.restore(entry, page)
      if (!restored) {
        const desired = normalizeBrowserUrl(entry.snapshot ? initialUrl : params.initialUrl ?? initialUrl) ?? START_URL
        if (entry.snapshot) {
          await this.pages.validateNavigation(entry, desired, kind)
          if (!this.pages.current(entry) || page.isDestroyed()) throw new Error('Browser page closed.')
          await loadOrReport({ tabId: params.tabId, threadId: params.threadId, url: desired,
            load: () => page.loadURL(browserLoadUrl(desired)), throwOnFailure: true,
            emit: payload => this.emit(win, payload) })
          if (!this.pages.current(entry) || page.isDestroyed()) throw new Error('Browser page closed.')
          tab.currentUrl = page.getURL() || desired
        } else if (desired === START_URL && params.skipStartPageLoad !== true) {
          void loadOrReport({ tabId: params.tabId, threadId: params.threadId, url: START_URL,
            load: () => page.loadURL(ensureDataUrl(buildStartPageHtml(this.startPageHint))),
            emit: payload => this.emit(win, payload) })
        } else void this.navigate(win, { tabId: params.tabId, url: desired })
      }
      this.emit(win, {
        tabId: tab.tabId, threadId: tab.threadId, projectKey: tab.projectKey, browserStorageId: tab.browserStorageId,
        type: 'restored', suspended: false, url: tab.currentUrl, title: tab.title
      })
      return this.snapshotFromRuntime(tab)
    } catch (error) {
      if (this.pages.current(entry)) {
        this.releasePage(entry, false)
        entry.page = undefined
      }
      throw error
    } finally {
      entry.restoring = false
      this.pages.enforce(win)
    }
  }

  destroyTab(win: BrowserWindow, tabId: string): void {
    const entry = this.pages.get(win, tabId)
    this.pages.close(win, tabId, true)
    if (entry) this.releasePage(entry, false)
    else this.hosts.remove(win, tabId)
    this.pages.enforce(win)
  }

  destroyThreadTabs(win: BrowserWindow, scope: { projectKey: string; threadId: string }): void {
    for (const entry of this.pages.entries(win)) {
      if (entry.owner.projectKey === scope.projectKey && entry.owner.threadId === scope.threadId) this.destroyTab(win, entry.owner.tabId)
    }
    this.pages.forgetThread(win, scope.projectKey, scope.threadId)
  }

  destroyAllTabs(win: BrowserWindow): void {
    const entries = this.pages.entries(win)
    this.pages.disposeWindow(win)
    for (const entry of entries) this.releasePage(entry, false)
    this.hosts.clear(win)
    this.byWindowId.delete(win.id)
  }

  async createAutomationTab(win: BrowserWindow, params: BrowserPageParams): Promise<BrowserSnapshot> {
    return await this.createTab(win, {
      ...params,
      skipStartPageLoad: true,
      automation: true
    })
  }

  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.page.isDestroyed()) return null
    return tab.page
  }

  listAutomationTargetTabs(win: BrowserWindow, threadId: string, projectKey?: string): BrowserSnapshot[] {
    return this.pages.entries(win).filter(entry => entry.owner.threadId === threadId && (projectKey === undefined || entry.owner.projectKey === projectKey))
      .map(entry => this.snapshotState(win, entry.owner.tabId)!)
  }

  rebindThread(win: BrowserWindow, fromThreadId: string, toThreadId: string): void {
    this.pages.rebindThread(win, fromThreadId, toThreadId)
    for (const tab of this.byWindowId.get(win.id)?.values() ?? []) {
      if (tab.threadId === fromThreadId) tab.threadId = toThreadId
    }
  }

  async loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const target = assertBrowserPageNavigation(tab.page, params.url)
    await loadOrReport({
      tabId: params.tabId,
      threadId: tab.threadId,
      url: target,
      load: () => tab.page.loadURL(target),
      emit: (payload) => this.emit(win, payload),
      throwOnFailure: true
    })
    if (this.getTab(win, params.tabId) !== tab || tab.page.isDestroyed()) return
    tab.currentUrl = tab.page.getURL() || tab.currentUrl
  }

  async navigate(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const normalized = normalizeBrowserUrl(params.url)
    if (!normalized) return

    const navigationDecision = classifyTabNavigation(tab, normalized)
    if (navigationDecision === 'external-handoff') {
      await shell.openExternal(normalized)
      this.emit(win, { tabId: params.tabId, threadId: tab.threadId, type: 'external-handoff', url: normalized })
      return
    }
    if (navigationDecision === 'new-tab') {
      this.navigation.boundary(win, tab, normalized)
      return
    }
    if (navigationDecision !== 'allow') {
      this.emit(win, {
        tabId: params.tabId,
        threadId: tab.threadId,
        type: 'blocked-navigation',
        message: `Blocked scheme: ${extractScheme(normalized) ?? 'unknown'}`
      })
      return
    }

    await loadOrReport({
      tabId: params.tabId,
      threadId: tab.threadId,
      url: normalized,
      load: () => tab.page.loadURL(browserLoadUrl(normalized)),
      emit: (payload) => this.emit(win, payload)
    })
    if (this.getTab(win, params.tabId) !== tab || tab.page.isDestroyed()) return
    tab.currentUrl = tab.page.getURL() || tab.currentUrl
  }

  goBack(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = tab.page.navigationHistory
    if (history.canGoBack()) history.goBack()
  }

  goForward(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = tab.page.navigationHistory
    if (history.canGoForward()) history.goForward()
  }

  reload(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.page.reload()
  }

  stop(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.page.stop()
  }

  setBounds(win: BrowserWindow, params: { tabId: string; x: number; y: number; width: number; height: number }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const { x, y, width, height } = params
    if (![x, y, width, height].every(Number.isFinite) || width <= 1 || height <= 1) return
    tab.boundsInitialized = true
    this.hosts.update(win, tab.tabId, { bounds: { x, y, width, height }, visible: tab.desiredVisible })
  }

  setViewport(win: BrowserWindow, params: { tabId: string; viewport: { width: number; height: number } | undefined }): void {
    const tab = this.getTab(win, params.tabId)
    if (tab) this.hosts.update(win, tab.tabId, { viewport: params.viewport })
  }

  getLayoutSize(win: BrowserWindow, tabId: string): { width: number; height: number } {
    const size = this.hosts.layoutSize(win, tabId)
    if (!size) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return size
  }

  isVisible(win: BrowserWindow, tabId: string): boolean {
    return this.hosts.isVisible(win, tabId)
  }

  setVisible(win: BrowserWindow, params: { tabId: string; visible: boolean }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.desiredVisible = params.visible
    const entry = this.pages.get(win, params.tabId)
    if (entry) entry.visible = params.visible
    this.hosts.update(win, tab.tabId, { visible: params.visible && tab.boundsInitialized })
    this.pages.enforce(win)
  }

  setActiveTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    this.pages.select(win, tabId)
    for (const tab of runtime.values()) {
      const visible = tab.tabId === tabId
      this.setVisible(win, { tabId: tab.tabId, visible })
    }
    const active = runtime.get(tabId)
    if (!active) return
    this.emitHistoryFlags(win, active)
  }

  async openInOsBrowser(win: BrowserWindow, tabId: string): Promise<void> {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const current = tab.currentUrl || tab.page.getURL()
    const scheme = extractScheme(current)
    if (!scheme || classifyTabNavigation(tab, current) === 'blocked') return
    if (scheme === VIEWER_SCHEME) {
      await shell.openPath(viewerUrlToPath(current))
      return
    }
    if (scheme === 'file:') {
      await shell.openPath(fileURLToPath(current))
      return
    }
    await shell.openExternal(current)
  }

  setCaptureSurface(win: BrowserWindow, tabId: string, size: { width: number; height: number } | null): void {
    const entry = this.pages.get(win, tabId)
    if (entry) entry.capture = size !== null
    this.hosts.setCaptureSurface(win, tabId, size)
    this.pages.enforce(win)
  }

  setAutomationState(win: BrowserWindow, params: BrowserAutomationStateParams): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const wasActive = tab.automationActive === true
    tab.automationActive = params.active
    const entry = this.pages.get(win, params.tabId)
    if (entry) entry.automation = params.active
    this.hosts.update(win, tab.tabId, { automation: params.active })
    if (params.active) this.hosts.cursors.activate(win, tab.tabId)
    else if (params.release) this.hosts.cursors.release(win, tab.tabId)
    else this.hosts.cursors.deactivate(win, tab.tabId)
    if (params.sessionName !== undefined) {
      tab.automationSessionName = params.sessionName
    }
    this.emit(win, {
      tabId: params.tabId,
      threadId: tab.threadId,
      type: params.active ? (wasActive ? 'automation-updated' : 'automation-started') : 'automation-stopped',
      automationActive: params.active,
      sessionName: tab.automationSessionName,
      action: params.action
    })
    this.pages.enforce(win)
  }

  async moveMouse(win: BrowserWindow, params: BrowserAutomationMoveParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    await this.hosts.cursors.move(win, tab.tabId, { x: params.x, y: params.y }, { waitForArrival: params.waitForArrival })
  }

  snapshotState(win: BrowserWindow, tabId: string): BrowserSnapshot | null {
    const tab = this.getTab(win, tabId)
    if (tab && !tab.page.isDestroyed()) return this.snapshotFromRuntime(tab)
    const entry = this.pages.get(win, tabId)
    if (!entry) return null
    const snapshot = entry.snapshot
    return {
      tabId, threadId: entry.owner.threadId, projectKey: entry.owner.projectKey, browserStorageId: entry.browserStorageId,
      suspended: true, currentUrl: snapshot?.url ?? entry.params.initialUrl ?? START_URL,
      title: snapshot?.title ?? DEFAULT_START_TITLE, faviconDataUrl: snapshot?.faviconDataUrl,
      canGoBack: (snapshot?.navigation.index ?? 0) > 0,
      canGoForward: snapshot ? snapshot.navigation.index < snapshot.navigation.entries.length - 1 : false,
      loading: false
    }
  }

  feedbackTarget(win: BrowserWindow, tabId: string): { page: Electron.WebContents; tabId: string; threadId?: string } {
    const tab = this.requireTab(win, tabId)
    return { page: tab.page, tabId: tab.tabId, threadId: tab.threadId }
  }

  userDownloads(): BrowserDownloads {
    if (!this.downloads) {
      this.downloads = new BrowserDownloads(
        app.getPath('downloads'), join(app.getPath('userData'), 'browser-downloads.json'),
        (records) => {
          for (const id of this.byWindowId.keys()) {
            const win = BrowserWindow.fromId(id)
            if (win && !win.isDestroyed()) {
              win.webContents.send('viewer:browser:feedback', { type: 'downloads', records })
              this.pages.enforce(win)
            }
          }
        }, (path) => shell.openPath(path)
      )
    }
    return this.downloads
  }

  private emit(win: BrowserWindow, payload: BrowserEventPayload): void {
    const entry = this.pages.get(win, payload.tabId)
    emitBrowserEvent(win, { ...payload,
      ...(entry ? { projectKey: entry.owner.projectKey, browserStorageId: entry.browserStorageId } : {}) })
  }

  private checkpoint(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const entry = this.pages.get(win, tab.tabId)
    if (!entry || entry.page !== tab.page) return
    entry.faviconDataUrl = tab.faviconDataUrl
    this.pages.checkpoint(entry)
    this.pages.enforce(win)
  }

  private snapshotFromRuntime(tab: BrowserTabRuntime): BrowserSnapshot {
    const history = tab.page.navigationHistory
    return {
      tabId: tab.tabId,
      threadId: tab.threadId,
      projectKey: tab.projectKey,
      browserStorageId: tab.browserStorageId,
      suspended: false,
      currentUrl: tab.currentUrl,
      title: tab.title,
      faviconDataUrl: tab.faviconDataUrl,
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward(),
      loading: tab.page.isLoading()
    }
  }

  private ensureWindowRuntime(win: BrowserWindow): Map<string, BrowserTabRuntime> {
    const existing = this.byWindowId.get(win.id)
    if (existing) return existing
    const created = new Map<string, BrowserTabRuntime>()
    this.byWindowId.set(win.id, created)
    return created
  }

  private getTab(win: BrowserWindow, tabId: string): BrowserTabRuntime | null {
    return this.byWindowId.get(win.id)?.get(tabId) ?? null
  }

  private requireTab(win: BrowserWindow, tabId: string): BrowserTabRuntime {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.page.isDestroyed()) {
      throw new Error(`Browser tab is no longer available: ${tabId}`)
    }
    return tab
  }

  private emitHistoryFlags(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const history = tab.page.navigationHistory
    this.emit(win, {
      tabId: tab.tabId,
      threadId: tab.threadId,
      type: 'update-history-flags',
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward()
    })
  }

  private bindWebContentsEvents(win: BrowserWindow, tab: BrowserTabRuntime): void {
    bindBrowserPageEvents(tab, {
      current: () => this.getTab(win, tab.tabId) === tab,
      emit: payload => this.emit(win, payload),
      history: () => this.emitHistoryFlags(win, tab),
      checkpoint: () => this.checkpoint(win, tab),
      destroyed: () => {
        this.byWindowId.get(win.id)?.delete(tab.tabId)
        const entry = this.pages.get(win, tab.tabId)
        if (entry?.page === tab.page) entry.page = undefined
      }
    })
    this.navigation.bind(win, tab)
  }
}

export const viewerBrowserManager = new ViewerBrowserManager()
export { BROWSER_EVENT_CHANNEL, START_URL }
