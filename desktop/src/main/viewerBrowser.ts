import { BrowserGuestRegistry } from './browserGuestRegistry'
import { app, BrowserWindow, nativeImage, session, shell } from 'electron'
import { join } from 'node:path'
import { BrowserDownloads } from './browserDownloads'
import { createHash } from 'crypto'
import { fileURLToPath } from 'url'
import type { BrowserEventPayload } from '../shared/viewer/types'
import { installViewerProtocolHandlerForSession, viewerUrlToPath } from './viewerFileProtocol'
import { configureEmbeddedBrowserIdentity } from './browserIdentity'
import { applyEmbeddedBrowserSecurity } from './browserSecurity'

const BROWSER_EVENT_CHANNEL = 'viewer:browser:event'
const START_URL = 'about:blank'
const VIEWER_SCHEME = 'dotcraft-viewer:'
const ALLOWED_SCHEMES = new Set(['http:', 'https:', VIEWER_SCHEME])
const EXTERNAL_HANDOFF_SCHEMES = new Set(['mailto:', 'tel:'])
const DEFAULT_START_TITLE = 'DotCraft Browser'

interface BrowserTabRuntime {
  tabId: string
  threadId?: string
  workspacePath: string
  page: Electron.WebContents
  desiredVisible: boolean
  visible: boolean
  boundsInitialized: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  allowFileScheme?: boolean
  automationSessionName?: string
  automationActive?: boolean
  authPopups: Set<BrowserWindow>
}

interface WindowRuntime {
  tabs: Map<string, BrowserTabRuntime>
  activeTabId: string | null
}

export interface BrowserSnapshot {
  tabId: string
  threadId?: string
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

function historyOf(webContents: Electron.WebContents): Electron.NavigationHistory {
  return webContents.navigationHistory
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

function extractScheme(raw: string): string | null {
  try {
    return new URL(raw).protocol.toLowerCase()
  } catch {
    return null
  }
}

function requestsControlledPopup(details: Electron.HandlerDetails): boolean {
  const frameName = details.frameName.trim().toLowerCase()
  const isNamedBrowsingContext = frameName !== ''
    && !['_blank', '_self', '_parent', '_top'].includes(frameName)
  return details.disposition === 'new-window' || details.features.trim() !== '' || isNamedBrowsingContext
}

export type BrowserNavigationDecision = 'allow' | 'external-handoff' | 'blocked'

export function classifyBrowserUrl(url: string): BrowserNavigationDecision {
  const scheme = extractScheme(url)
  if (!scheme) return 'blocked'
  if (ALLOWED_SCHEMES.has(scheme)) return 'allow'
  if (EXTERNAL_HANDOFF_SCHEMES.has(scheme)) return 'external-handoff'
  return 'blocked'
}

export function partitionForWorkspace(workspacePath: string): string {
  const normalized = workspacePath.trim().toLowerCase()
  const hash = createHash('sha1').update(normalized).digest('hex').slice(0, 12)
  return `persist:dotcraft-viewer:${hash}`
}

export function normalizeBrowserUrl(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed) return null
  if (/[\u0000-\u001f]/.test(trimmed)) return null

  const looksLikeLocalHost =
    /^(localhost|127\.0\.0\.1|\[?::1\]?)(:\d+)?(\/|$)/i.test(trimmed)
  const withScheme = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? (looksLikeLocalHost ? `http://${trimmed}` : trimmed)
    : looksLikeLocalHost
      ? `http://${trimmed}`
      : `https://${trimmed}`
  try {
    const parsed = new URL(withScheme)
    return parsed.toString()
  } catch {
    return null
  }
}

function isNavigationAbortError(error: unknown): boolean {
  if (typeof error === 'string') return error.includes('ERR_ABORTED') || error.includes('(-3)')
  if (!error || typeof error !== 'object') return false
  const details = error as { code?: unknown; errno?: unknown; message?: unknown }
  if (details.code === -3 || details.errno === -3 || details.code === 'ERR_ABORTED') return true
  if (typeof details.message !== 'string') return false
  return details.message.includes('ERR_ABORTED') || details.message.includes('(-3)')
}

export async function loadOrReport(params: {
  tabId: string
  threadId?: string
  url: string
  load: () => Promise<unknown>
  emit: (payload: BrowserEventPayload) => void
  throwOnFailure?: boolean
}): Promise<void> {
  try {
    await params.load()
  } catch (error: unknown) {
    if (isNavigationAbortError(error)) return
    const message = error instanceof Error ? error.message : String(error)
    const details = error && typeof error === 'object' ? error as { code?: unknown; errno?: unknown } : {}
    const numericCode = typeof details.code === 'number'
      ? details.code
      : typeof details.errno === 'number'
        ? details.errno
        : undefined
    params.emit({
      tabId: params.tabId,
      threadId: params.threadId,
      type: 'did-fail-load',
      url: params.url,
      message,
      errorCode: numericCode,
      errorDescription: message,
      validatedURL: params.url,
      finalURL: params.url,
      isMainFrame: true
    })
    params.emit({
      tabId: params.tabId,
      threadId: params.threadId,
      type: 'did-stop-loading',
      url: params.url
    })
    if (params.throwOnFailure) {
      throw new Error(`NavigationFailed: ${message}`)
    }
  }
}

export class ViewerBrowserManager {
  readonly hosts = new BrowserGuestRegistry()
  private downloads: BrowserDownloads | null = null
  private readonly byWindowId = new Map<number, WindowRuntime>()
  private readonly configuredPartitions = new Set<string>()
  private startPageHint = 'Enter a URL in the address bar to begin browsing.'

  setStartPageHint(hint: string): void {
    this.startPageHint = hint.trim() || this.startPageHint
  }

  async createTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    allowFileScheme?: boolean
    skipStartPageLoad?: boolean
    automation?: boolean
  }): Promise<BrowserSnapshot> {
    const runtime = this.ensureWindowRuntime(win)
    const existing = runtime.tabs.get(params.tabId)
    if (existing) {
      if (params.threadId && !existing.threadId) existing.threadId = params.threadId
      return this.snapshotFromRuntime(existing)
    }

    const partition = partitionForWorkspace(params.workspacePath)
    const partitionSession = session.fromPartition(partition)
    this.configurePartitionSession(partition, partitionSession)

    const page = await this.hosts.request(win, params.tabId, partition, params.automation)
    if (page.isDestroyed() || !this.hosts.list(win).some(host => host.tabId === params.tabId)) throw new Error('Browser page closed.')
    const attached = runtime.tabs.get(params.tabId)
    if (attached) return this.snapshotFromRuntime(attached)
    const tabRuntime: BrowserTabRuntime = {
      tabId: params.tabId,
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      page,
      desiredVisible: false,
      visible: false,
      boundsInitialized: false,
      currentUrl: START_URL,
      title: DEFAULT_START_TITLE,
      allowFileScheme: params.allowFileScheme === true,
      authPopups: new Set()
    }
    runtime.tabs.set(params.tabId, tabRuntime)
    this.bindWebContentsEvents(win, tabRuntime)

    const desired = normalizeBrowserUrl(params.initialUrl ?? '') ?? START_URL
    if (desired === START_URL && params.skipStartPageLoad !== true) {
      const startPageUrl = ensureDataUrl(buildStartPageHtml(this.startPageHint))
      void loadOrReport({
        tabId: params.tabId,
        threadId: params.threadId,
        url: START_URL,
        load: () => page.loadURL(startPageUrl),
        emit: (payload) => emitBrowserEvent(win, payload)
      })
    } else {
      void this.navigate(win, { tabId: params.tabId, url: desired })
    }

    emitBrowserEvent(win, {
      tabId: params.tabId,
      threadId: params.threadId,
      type: 'page-title-updated',
      title: DEFAULT_START_TITLE
    })
    return this.snapshotFromRuntime(tabRuntime)
  }

  destroyTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) {
      this.hosts.remove(win, tabId)
      return
    }
    const tab = runtime.tabs.get(tabId)
    if (!tab) {
      this.hosts.remove(win, tabId)
      return
    }
    for (const popup of tab.authPopups) {
      if (!popup.isDestroyed()) popup.destroy()
    }
    tab.authPopups.clear()
    this.hosts.remove(win, tabId)
    runtime.tabs.delete(tabId)
    if (runtime.activeTabId === tabId) runtime.activeTabId = null
  }

  destroyAllTabs(win: BrowserWindow): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    for (const tabId of [...runtime.tabs.keys()]) {
      this.destroyTab(win, tabId)
    }
    this.hosts.clear(win)
    this.byWindowId.delete(win.id)
  }

  async createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    allowFileScheme?: boolean
  }): Promise<BrowserSnapshot> {
    return await this.createTab(win, {
      tabId: params.tabId,
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      initialUrl: params.initialUrl,
      allowFileScheme: params.allowFileScheme,
      skipStartPageLoad: true,
      automation: true
    })
  }

  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.page.isDestroyed()) return null
    return tab.page
  }

  listAutomationTargetTabs(win: BrowserWindow, threadId: string): BrowserSnapshot[] {
    const runtime = this.byWindowId.get(win.id)
    return runtime ? [...runtime.tabs.values()]
      .filter(tab => tab.threadId === threadId && !tab.page.isDestroyed())
      .map(tab => this.snapshotFromRuntime(tab)) : []
  }

  rebindThread(win: BrowserWindow, fromThreadId: string, toThreadId: string): void {
    for (const tab of this.byWindowId.get(win.id)?.tabs.values() ?? []) {
      if (tab.threadId === fromThreadId) tab.threadId = toThreadId
    }
  }

  getAutomationTargetTab(win: BrowserWindow, threadId: string): BrowserSnapshot | null {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return null
    const active = runtime.activeTabId ? runtime.tabs.get(runtime.activeTabId) : null
    if (active?.threadId === threadId && !active.page.isDestroyed()) {
      return this.snapshotFromRuntime(active)
    }
    const recent = [...runtime.tabs.values()].reverse().find((tab) => (
      tab.threadId === threadId && !tab.page.isDestroyed()
    ))
    return recent ? this.snapshotFromRuntime(recent) : null
  }

  async loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    await loadOrReport({
      tabId: params.tabId,
      threadId: tab.threadId,
      url: params.url,
      load: () => tab.page.loadURL(params.url),
      emit: (payload) => emitBrowserEvent(win, payload),
      throwOnFailure: true
    })
    tab.currentUrl = tab.page.getURL() || tab.currentUrl
  }

  async navigate(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const normalized = normalizeBrowserUrl(params.url)
    if (!normalized) return

    const navigationDecision = this.classifyUrlForTab(tab, normalized)
    if (navigationDecision === 'external-handoff') {
      await shell.openExternal(normalized)
      emitBrowserEvent(win, { tabId: params.tabId, threadId: tab.threadId, type: 'external-handoff', url: normalized })
      return
    }
    if (navigationDecision !== 'allow') {
      emitBrowserEvent(win, {
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
      load: () => tab.page.loadURL(normalized),
      emit: (payload) => emitBrowserEvent(win, payload)
    })
    tab.currentUrl = tab.page.getURL() || tab.currentUrl
  }

  goBack(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.page)
    if (history.canGoBack()) history.goBack()
  }

  goForward(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.page)
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
    tab.visible = params.visible && tab.boundsInitialized
    this.hosts.update(win, tab.tabId, { visible: tab.visible })
  }

  setActiveTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    runtime.activeTabId = tabId
    for (const tab of runtime.tabs.values()) {
      const visible = tab.tabId === tabId
      this.setVisible(win, { tabId: tab.tabId, visible })
    }
    const active = runtime.tabs.get(tabId)
    if (!active) return
    this.emitHistoryFlags(win, active)
  }

  async openInOsBrowser(win: BrowserWindow, tabId: string): Promise<void> {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const current = tab.currentUrl || tab.page.getURL()
    const scheme = extractScheme(current)
    if (!scheme || this.classifyUrlForTab(tab, current) === 'blocked') return
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
    this.hosts.setCaptureSurface(win, tabId, size)
  }

  setAutomationState(win: BrowserWindow, params: BrowserAutomationStateParams): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const wasActive = tab.automationActive === true
    tab.automationActive = params.active
    this.hosts.update(win, tab.tabId, { automation: params.active })
    if (params.active) this.hosts.cursors.activate(win, tab.tabId)
    else if (params.release) this.hosts.cursors.release(win, tab.tabId)
    else this.hosts.cursors.deactivate(win, tab.tabId)
    if (params.sessionName !== undefined) {
      tab.automationSessionName = params.sessionName
    }
    emitBrowserEvent(win, {
      tabId: params.tabId,
      threadId: tab.threadId,
      type: params.active ? (wasActive ? 'automation-updated' : 'automation-started') : 'automation-stopped',
      automationActive: params.active,
      sessionName: tab.automationSessionName,
      action: params.action
    })
  }

  async moveMouse(win: BrowserWindow, params: BrowserAutomationMoveParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    await this.hosts.cursors.move(win, tab.tabId, { x: params.x, y: params.y }, { waitForArrival: params.waitForArrival })
  }

  snapshotState(win: BrowserWindow, tabId: string): BrowserSnapshot | null {
    const tab = this.getTab(win, tabId)
    if (!tab) return null
    return this.snapshotFromRuntime(tab)
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
            if (win && !win.isDestroyed()) win.webContents.send('viewer:browser:feedback', { type: 'downloads', records })
          }
        }, (path) => shell.openPath(path)
      )
    }
    return this.downloads
  }

  private snapshotFromRuntime(tab: BrowserTabRuntime): BrowserSnapshot {
    const history = historyOf(tab.page)
    return {
      tabId: tab.tabId,
      threadId: tab.threadId,
      currentUrl: tab.currentUrl,
      title: tab.title,
      faviconDataUrl: tab.faviconDataUrl,
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward(),
      loading: tab.page.isLoading()
    }
  }

  private ensureWindowRuntime(win: BrowserWindow): WindowRuntime {
    const existing = this.byWindowId.get(win.id)
    if (existing) return existing
    const created: WindowRuntime = {
      tabs: new Map(),
      activeTabId: null
    }
    this.byWindowId.set(win.id, created)
    return created
  }

  private getTab(win: BrowserWindow, tabId: string): BrowserTabRuntime | null {
    return this.byWindowId.get(win.id)?.tabs.get(tabId) ?? null
  }

  private requireTab(win: BrowserWindow, tabId: string): BrowserTabRuntime {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.page.isDestroyed()) {
      throw new Error(`Browser tab is no longer available: ${tabId}`)
    }
    return tab
  }

  private emitHistoryFlags(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const history = historyOf(tab.page)
    emitBrowserEvent(win, {
      tabId: tab.tabId,
      threadId: tab.threadId,
      type: 'update-history-flags',
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward()
    })
  }

  private bindWebContentsEvents(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const wc = tab.page

    wc.on('did-start-loading', () => {
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'did-start-loading' })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-stop-loading', () => {
      tab.currentUrl = wc.getURL() || tab.currentUrl
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'did-stop-loading', url: tab.currentUrl })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-navigate', (_event, url) => {
      tab.currentUrl = url
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'did-navigate', url })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
      if (!isMainFrame || errorCode === -3) return
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        threadId: tab.threadId,
        type: 'did-fail-load',
        url: validatedURL,
        message: errorDescription,
        errorCode,
        errorDescription,
        validatedURL,
        finalURL: tab.page.getURL(),
        isMainFrame
      })
    })
    wc.on('page-title-updated', (event, title) => {
      event.preventDefault()
      tab.title = title || DEFAULT_START_TITLE
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'page-title-updated', title: tab.title })
    })
    wc.on('page-favicon-updated', async (_event, favicons) => {
      const first = favicons[0]
      if (!first) return
      try {
        const res = await fetch(first)
        if (!res.ok) return
        const data = Buffer.from(await res.arrayBuffer())
        const image = nativeImage.createFromBuffer(data)
        if (image.isEmpty()) return
        tab.faviconDataUrl = image.toDataURL()
        emitBrowserEvent(win, {
          tabId: tab.tabId,
          threadId: tab.threadId,
          type: 'page-favicon-updated',
          faviconDataUrl: tab.faviconDataUrl
        })
      } catch {
        // Best-effort favicon loading.
      }
    })
    wc.on('render-process-gone', () => {
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'crashed' })
    })

    wc.on('will-navigate', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) {
        event.preventDefault()
      }
    })
    wc.on('will-redirect', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) {
        event.preventDefault()
      }
    })

    this.bindWindowOpenEvents(win, tab, wc)
  }

  private bindWindowOpenEvents(win: BrowserWindow, tab: BrowserTabRuntime, wc: Electron.WebContents): void {
    wc.setWindowOpenHandler((details) => this.handleWindowOpen(win, tab, details))
    wc.on('did-create-window', (popup) => this.bindAuthPopup(win, tab, popup))
  }

  private handleWindowOpen(
    win: BrowserWindow,
    tab: BrowserTabRuntime,
    details: Electron.HandlerDetails
  ): Electron.WindowOpenHandlerResponse {
    const normalized = normalizeBrowserUrl(details.url)
    const opensControlledPopup = requestsControlledPopup(details)
    const isBlankPopup = opensControlledPopup && details.url === 'about:blank'
    const isAllowedPopup = normalized && this.classifyUrlForTab(tab, normalized) === 'allow'
    if (opensControlledPopup && (isBlankPopup || isAllowedPopup)) {
      return {
        action: 'allow',
        outlivesOpener: false,
        overrideBrowserWindowOptions: {
          parent: win,
          autoHideMenuBar: true,
          webPreferences: applyEmbeddedBrowserSecurity({}, tab.page.session)
        }
      }
    }
    if (!normalized) return { action: 'deny' }
    const navigationDecision = this.classifyUrlForTab(tab, normalized)
    if (navigationDecision === 'allow') {
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        threadId: tab.threadId,
        type: 'request-new-tab',
        url: normalized
      })
      return { action: 'deny' }
    }
    if (navigationDecision === 'external-handoff') {
      void shell.openExternal(normalized)
    } else {
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        threadId: tab.threadId,
        type: 'blocked-navigation',
        message: `Blocked scheme: ${extractScheme(normalized) ?? 'unknown'}`
      })
    }
    return { action: 'deny' }
  }

  private bindAuthPopup(win: BrowserWindow, tab: BrowserTabRuntime, popup: BrowserWindow): void {
    tab.authPopups.add(popup)
    popup.setMenuBarVisibility(false)
    popup.webContents.setUserAgent(tab.page.getUserAgent())
    popup.once('closed', () => tab.authPopups.delete(popup))
    popup.webContents.on('will-navigate', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) event.preventDefault()
    })
    popup.webContents.on('will-redirect', (event, url) => {
      if (this.handleSchemeBoundary(win, tab, url)) event.preventDefault()
    })
    this.bindWindowOpenEvents(win, tab, popup.webContents)
  }

  private classifyUrlForTab(tab: BrowserTabRuntime, url: string): BrowserNavigationDecision {
    const scheme = extractScheme(url)
    if (scheme === 'file:' && tab.allowFileScheme === true) return 'allow'
    return classifyBrowserUrl(url)
  }

  private handleSchemeBoundary(win: BrowserWindow, tab: BrowserTabRuntime, url: string): boolean {
    const scheme = extractScheme(url)
    const navigationDecision = this.classifyUrlForTab(tab, url)
    if (navigationDecision === 'allow') return false
    if (navigationDecision === 'external-handoff') {
      void shell.openExternal(url)
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'external-handoff', url })
      return true
    }
    if (navigationDecision === 'blocked') {
      emitBrowserEvent(win, {
        tabId: tab.tabId,
        threadId: tab.threadId,
        type: 'blocked-navigation',
        message: `Blocked scheme: ${scheme ?? 'unknown'}`
      })
      return true
    }
    return true
  }

  configurePartitionSession(partitionName: string, partitionSession: Electron.Session): void {
    if (this.configuredPartitions.has(partitionName)) return
    installViewerProtocolHandlerForSession(partitionSession)
    configureEmbeddedBrowserIdentity(partitionSession, {
      appName: app.getName(),
      preferredLanguages: app.getPreferredSystemLanguages(),
      fallbackLocale: app.getLocale()
    })
    this.configuredPartitions.add(partitionName)

    partitionSession.on('will-download', (event, item, webContents) => {
      for (const runtime of this.byWindowId.values()) {
        for (const tab of runtime.tabs.values()) {
          if (tab.page.id === webContents.id) {
            this.userDownloads().start(item, { tabId: tab.tabId, threadId: tab.threadId })
            return
          }
        }
      }
      event.preventDefault()
    })

    partitionSession.setPermissionCheckHandler((_wc, permission) => {
      return permission === 'clipboard-sanitized-write'
    })
    partitionSession.setPermissionRequestHandler((_wc, permission, callback) => {
      callback(permission === 'clipboard-sanitized-write')
    })
  }

}

export const viewerBrowserManager = new ViewerBrowserManager()
export { BROWSER_EVENT_CHANNEL, START_URL }
