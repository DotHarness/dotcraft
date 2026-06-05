import { BrowserWindow, WebContentsView, nativeImage, session, shell } from 'electron'
import { createHash } from 'crypto'
import { fileURLToPath } from 'url'
import type { BrowserEventPayload } from '../shared/viewer/types'
import { installViewerProtocolHandlerForSession, viewerUrlToPath } from './viewerFileProtocol'

const BROWSER_EVENT_CHANNEL = 'viewer:browser:event'
const START_URL = 'about:blank'
const VIEWER_SCHEME = 'dotcraft-viewer:'
const ALLOWED_SCHEMES = new Set(['http:', 'https:', VIEWER_SCHEME])
const EXTERNAL_HANDOFF_SCHEMES = new Set(['mailto:', 'tel:'])
const DEFAULT_START_TITLE = 'DotCraft Browser'
const VIRTUAL_MOUSE_SCRIPT_TIMEOUT_MS = 750
const VIRTUAL_MOUSE_MOVE_DURATION_MS = 160
const VIRTUAL_MOUSE_MIN_PATH_STEPS = 5
const VIRTUAL_MOUSE_MAX_PATH_STEPS = 16

interface BrowserTabRuntime {
  tabId: string
  threadId?: string
  workspacePath: string
  view: WebContentsView
  desiredVisible: boolean
  visible: boolean
  boundsInitialized: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  allowFileScheme?: boolean
  automationEnabled?: boolean
  automationSessionName?: string
  automationActive?: boolean
  virtualMouseX?: number
  virtualMouseY?: number
  virtualMouseMoved?: boolean
  viewportWidth?: number
  viewportHeight?: number
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

export type BrowserAutomationMouseButton = 'left' | 'right' | 'middle'

export interface BrowserAutomationPoint {
  tabId: string
  x: number
  y: number
}

export interface BrowserAutomationMoveParams extends BrowserAutomationPoint {
  waitForArrival?: boolean
}

export interface BrowserAutomationClickParams extends BrowserAutomationPoint {
  button?: BrowserAutomationMouseButton
}

export interface BrowserAutomationDragParams {
  tabId: string
  path: Array<{ x: number; y: number }>
}

export interface BrowserAutomationScrollParams extends BrowserAutomationPoint {
  scrollX: number
  scrollY: number
}

export interface BrowserAutomationKeypressParams {
  tabId: string
  keys: string[]
}

export interface BrowserAutomationTypeParams {
  tabId: string
  text: string
}

export interface BrowserAutomationStateParams {
  tabId: string
  active: boolean
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

function clampViewportCoordinate(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.max(0, Math.round(value))
}

function mouseButton(button?: BrowserAutomationMouseButton): BrowserAutomationMouseButton {
  return button === 'right' || button === 'middle' ? button : 'left'
}

function viewportCenter(tab: BrowserTabRuntime): { x: number; y: number } {
  return {
    x: Math.max(0, Math.round((tab.viewportWidth ?? 1280) / 2)),
    y: Math.max(0, Math.round((tab.viewportHeight ?? 720) / 2))
  }
}

function easeInOutCubic(value: number): number {
  return value < 0.5
    ? 4 * value * value * value
    : 1 - Math.pow(-2 * value + 2, 3) / 2
}

function mouseMovePath(
  from: { x: number; y: number },
  to: { x: number; y: number },
  animated: boolean
): Array<{ x: number; y: number }> {
  const distance = Math.hypot(to.x - from.x, to.y - from.y)
  if (!animated || distance < 2) return [to]

  const steps = Math.min(
    VIRTUAL_MOUSE_MAX_PATH_STEPS,
    Math.max(VIRTUAL_MOUSE_MIN_PATH_STEPS, Math.ceil(distance / 80))
  )
  const points: Array<{ x: number; y: number }> = []
  for (let step = 1; step <= steps; step++) {
    const progress = easeInOutCubic(step / steps)
    const point = {
      x: Math.round(from.x + (to.x - from.x) * progress),
      y: Math.round(from.y + (to.y - from.y) * progress)
    }
    const previous = points.at(-1)
    if (!previous || previous.x !== point.x || previous.y !== point.y) {
      points.push(point)
    }
  }
  const last = points.at(-1)
  if (!last || last.x !== to.x || last.y !== to.y) {
    points.push(to)
  }
  return points
}

function normalizeKeyboardKey(key: string): string {
  if (key === 'ControlOrMeta') return process.platform === 'darwin' ? 'Meta' : 'Control'
  return key
}

function electronModifiers(keys: string[]): Array<'shift' | 'control' | 'alt' | 'meta'> {
  const modifiers = new Set<'shift' | 'control' | 'alt' | 'meta'>()
  for (const raw of keys.map(normalizeKeyboardKey)) {
    const key = raw.toLowerCase()
    if (key === 'shift') modifiers.add('shift')
    if (key === 'control' || key === 'ctrl') modifiers.add('control')
    if (key === 'alt' || key === 'option') modifiers.add('alt')
    if (key === 'meta' || key === 'cmd' || key === 'command') modifiers.add('meta')
  }
  return [...modifiers]
}

const VIRTUAL_MOUSE_BOOTSTRAP = `
(() => {
  const id = '__dotcraft_virtual_mouse';
  let cursor = document.getElementById(id);
  if (!cursor) {
    cursor = document.createElement('div');
    cursor.id = id;
    cursor.setAttribute('aria-hidden', 'true');
    Object.assign(cursor.style, {
      position: 'fixed',
      left: '0px',
      top: '0px',
      width: '28px',
      height: '28px',
      pointerEvents: 'none',
      zIndex: '2147483647',
      transform: 'translate3d(var(--dotcraft-cursor-x, 0px), var(--dotcraft-cursor-y, 0px), 0)',
      transition: 'transform var(--dotcraft-cursor-duration, 120ms) cubic-bezier(.2,.8,.2,1)',
      filter: 'drop-shadow(0 3px 7px rgba(0,0,0,.42))'
    });
    cursor.innerHTML = '<svg width="28" height="28" viewBox="0 0 28 28" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M4.7 3.9 21.8 14l-8.2 1.7L9.5 23.3 4.7 3.9Z" fill="#2f8af5" stroke="white" stroke-width="2" stroke-linejoin="round"/></svg>';
    document.documentElement.appendChild(cursor);
  }
  window.__dotcraftVirtualMouseMove = (x, y, duration) => new Promise((resolve) => {
    cursor.style.setProperty('--dotcraft-cursor-duration', Math.max(0, duration || 0) + 'ms');
    cursor.style.setProperty('--dotcraft-cursor-x', Math.max(0, x) + 'px');
    cursor.style.setProperty('--dotcraft-cursor-y', Math.max(0, y) + 'px');
    window.setTimeout(resolve, Math.max(0, duration || 0) + 20);
  });
  window.__dotcraftVirtualMouseClick = (x, y) => {
    const ripple = document.createElement('div');
    Object.assign(ripple.style, {
      position: 'fixed',
      left: Math.max(0, x - 20) + 'px',
      top: Math.max(0, y - 20) + 'px',
      width: '40px',
      height: '40px',
      borderRadius: '999px',
      pointerEvents: 'none',
      zIndex: '2147483646',
      border: '2px solid rgba(47,138,245,.7)',
      opacity: '0.8',
      transform: 'scale(.35)',
      transition: 'transform 220ms ease, opacity 220ms ease'
    });
    document.documentElement.appendChild(ripple);
    requestAnimationFrame(() => {
      ripple.style.transform = 'scale(1.35)';
      ripple.style.opacity = '0';
    });
    window.setTimeout(() => ripple.remove(), 260);
  };
})();
`

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
  private readonly byWindowId = new Map<number, WindowRuntime>()
  private readonly configuredPartitions = new Set<string>()
  private startPageHint = 'Enter a URL in the address bar to begin browsing.'

  setStartPageHint(hint: string): void {
    this.startPageHint = hint.trim() || this.startPageHint
  }

  createTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    allowFileScheme?: boolean
    skipStartPageLoad?: boolean
  }): BrowserSnapshot {
    const runtime = this.ensureWindowRuntime(win)
    const existing = runtime.tabs.get(params.tabId)
    if (existing) {
      if (params.threadId && !existing.threadId) existing.threadId = params.threadId
      return this.snapshotFromRuntime(existing)
    }

    const partition = partitionForWorkspace(params.workspacePath)
    const partitionSession = session.fromPartition(partition)
    this.configurePartitionSession(partition, partitionSession)

    const view = new WebContentsView({
      webPreferences: {
        session: partitionSession,
        contextIsolation: true,
        sandbox: true,
        nodeIntegration: false
      }
    })
    const tabRuntime: BrowserTabRuntime = {
      tabId: params.tabId,
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      view,
      desiredVisible: false,
      visible: false,
      boundsInitialized: false,
      currentUrl: START_URL,
      title: DEFAULT_START_TITLE,
      allowFileScheme: params.allowFileScheme === true,
      viewportWidth: 1280,
      viewportHeight: 720
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
        load: () => view.webContents.loadURL(startPageUrl),
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
    if (!runtime) return
    const tab = runtime.tabs.get(tabId)
    if (!tab) return

    this.detachView(win, tab)
    runtime.tabs.delete(tabId)
    if (!tab.view.webContents.isDestroyed()) {
      tab.view.webContents.close({ waitForBeforeUnload: false })
    }
    if (runtime.activeTabId === tabId) runtime.activeTabId = null
  }

  destroyAllTabs(win: BrowserWindow): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    for (const tabId of [...runtime.tabs.keys()]) {
      this.destroyTab(win, tabId)
    }
    this.byWindowId.delete(win.id)
  }

  createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    width?: number
    height?: number
    allowFileScheme?: boolean
  }): BrowserSnapshot {
    const snapshot = this.createTab(win, {
      tabId: params.tabId,
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      initialUrl: params.initialUrl,
      allowFileScheme: params.allowFileScheme,
      skipStartPageLoad: true
    })
    const tab = this.getTab(win, params.tabId)
    if (tab) {
      tab.automationEnabled = true
      const width = Math.max(1, Math.round(params.width ?? 1280))
      const height = Math.max(1, Math.round(params.height ?? 720))
      tab.viewportWidth = width
      tab.viewportHeight = height
      this.centerVirtualMouse(tab)
      // Keep automation pages at a useful capture size before the renderer has
      // measured the actual detail-panel slot. This must not count as initialized
      // UI bounds, otherwise addChildView can briefly cover the whole window.
      tab.view.setBounds({
        x: -10000,
        y: -10000,
        width,
        height
      })
      this.emitVirtualCursor(win, tab, tab.virtualMouseX!, tab.virtualMouseY!)
    }
    return snapshot
  }

  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null {
    const tab = this.getTab(win, tabId)
    if (!tab || tab.view.webContents.isDestroyed()) return null
    return tab.view.webContents
  }

  getAutomationTargetTab(win: BrowserWindow, threadId: string): BrowserSnapshot | null {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return null
    const active = runtime.activeTabId ? runtime.tabs.get(runtime.activeTabId) : null
    if (active?.threadId === threadId && !active.view.webContents.isDestroyed()) {
      return this.snapshotFromRuntime(active)
    }
    const recent = [...runtime.tabs.values()].reverse().find((tab) => (
      tab.threadId === threadId && !tab.view.webContents.isDestroyed()
    ))
    return recent ? this.snapshotFromRuntime(recent) : null
  }

  async loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void> {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.currentUrl = params.url
    await loadOrReport({
      tabId: params.tabId,
      threadId: tab.threadId,
      url: params.url,
      load: () => tab.view.webContents.loadURL(params.url),
      emit: (payload) => emitBrowserEvent(win, payload),
      throwOnFailure: true
    })
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

    tab.currentUrl = normalized
    await loadOrReport({
      tabId: params.tabId,
      threadId: tab.threadId,
      url: normalized,
      load: () => tab.view.webContents.loadURL(normalized),
      emit: (payload) => emitBrowserEvent(win, payload)
    })
  }

  goBack(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.view.webContents)
    if (history.canGoBack()) history.goBack()
  }

  goForward(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    const history = historyOf(tab.view.webContents)
    if (history.canGoForward()) history.goForward()
  }

  reload(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.view.webContents.reload()
  }

  stop(win: BrowserWindow, tabId: string): void {
    const tab = this.getTab(win, tabId)
    if (!tab) return
    tab.view.webContents.stop()
  }

  setBounds(win: BrowserWindow, params: { tabId: string; x: number; y: number; width: number; height: number }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    const width = Math.max(1, Math.round(params.width))
    const height = Math.max(1, Math.round(params.height))
    tab.viewportWidth = width
    tab.viewportHeight = height
    tab.view.setBounds({
      x: Math.round(params.x),
      y: Math.round(params.y),
      width,
      height
    })
    tab.boundsInitialized = true
    if (tab.desiredVisible && !tab.visible) {
      this.attachView(win, tab)
    }
    if (tab.automationEnabled && !tab.virtualMouseMoved) {
      this.centerVirtualMouse(tab)
      this.emitVirtualCursor(win, tab, tab.virtualMouseX!, tab.virtualMouseY!)
      if (tab.automationActive) void this.injectVirtualMouse(tab)
    }
  }

  setVisible(win: BrowserWindow, params: { tabId: string; visible: boolean }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.desiredVisible = params.visible
    if (params.visible) {
      if (tab.boundsInitialized) this.attachView(win, tab)
    } else {
      this.detachView(win, tab)
    }
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
    const current = tab.currentUrl || tab.view.webContents.getURL()
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

  setAutomationState(win: BrowserWindow, params: BrowserAutomationStateParams): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab) return
    tab.automationEnabled = true
    const wasActive = tab.automationActive === true
    tab.automationActive = params.active
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
    if (params.active) {
      if (!tab.virtualMouseMoved) {
        this.centerVirtualMouse(tab)
        this.emitVirtualCursor(win, tab, tab.virtualMouseX!, tab.virtualMouseY!)
      }
      void this.injectVirtualMouse(tab)
    }
  }

  async moveMouse(win: BrowserWindow, params: BrowserAutomationMoveParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    const x = clampViewportCoordinate(params.x)
    const y = clampViewportCoordinate(params.y)
    await this.animateMouseTo(win, tab, x, y, {
      waitForArrival: params.waitForArrival !== false
    })
  }

  async clickMouse(win: BrowserWindow, params: BrowserAutomationClickParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    const x = clampViewportCoordinate(params.x)
    const y = clampViewportCoordinate(params.y)
    const button = mouseButton(params.button)
    this.focusTabWebContents(tab)
    await this.animateMouseTo(win, tab, x, y)
    tab.view.webContents.sendInputEvent({ type: 'mouseDown', x, y, button, clickCount: 1 } as Electron.MouseInputEvent)
    tab.view.webContents.sendInputEvent({ type: 'mouseUp', x, y, button, clickCount: 1 } as Electron.MouseInputEvent)
    void this.showVirtualClick(tab, x, y)
  }

  async doubleClickMouse(win: BrowserWindow, params: BrowserAutomationClickParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    const x = clampViewportCoordinate(params.x)
    const y = clampViewportCoordinate(params.y)
    const button = mouseButton(params.button)
    this.focusTabWebContents(tab)
    await this.animateMouseTo(win, tab, x, y)
    tab.view.webContents.sendInputEvent({ type: 'mouseDown', x, y, button, clickCount: 1 } as Electron.MouseInputEvent)
    tab.view.webContents.sendInputEvent({ type: 'mouseUp', x, y, button, clickCount: 1 } as Electron.MouseInputEvent)
    tab.view.webContents.sendInputEvent({ type: 'mouseDown', x, y, button, clickCount: 2 } as Electron.MouseInputEvent)
    tab.view.webContents.sendInputEvent({ type: 'mouseUp', x, y, button, clickCount: 2 } as Electron.MouseInputEvent)
    void this.showVirtualClick(tab, x, y)
  }

  async dragMouse(win: BrowserWindow, params: BrowserAutomationDragParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    if (params.path.length < 2) throw new Error('Browser drag requires at least two path points.')
    const points = params.path.map((point) => ({
      x: clampViewportCoordinate(point.x),
      y: clampViewportCoordinate(point.y)
    }))
    const first = points[0]!
    this.focusTabWebContents(tab)
    await this.animateMouseTo(win, tab, first.x, first.y)
    tab.view.webContents.sendInputEvent({ type: 'mouseDown', x: first.x, y: first.y, button: 'left', clickCount: 1 } as Electron.MouseInputEvent)
    for (const point of points.slice(1)) {
      await this.animateMouseTo(win, tab, point.x, point.y, { button: 'left' })
    }
    const last = points[points.length - 1]!
    tab.view.webContents.sendInputEvent({ type: 'mouseUp', x: last.x, y: last.y, button: 'left', clickCount: 1 } as Electron.MouseInputEvent)
  }

  async scrollMouse(win: BrowserWindow, params: BrowserAutomationScrollParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    const x = clampViewportCoordinate(params.x)
    const y = clampViewportCoordinate(params.y)
    await this.animateMouseTo(win, tab, x, y)
    tab.view.webContents.sendInputEvent({
      type: 'mouseWheel',
      x,
      y,
      deltaX: Math.round(params.scrollX || 0),
      deltaY: Math.round(params.scrollY || 0)
    } as Electron.MouseWheelInputEvent)
  }

  async typeText(win: BrowserWindow, params: BrowserAutomationTypeParams): Promise<void> {
    const tab = this.requireTab(win, params.tabId)
    tab.view.webContents.insertText(String(params.text ?? ''))
  }

  keypress(win: BrowserWindow, params: BrowserAutomationKeypressParams): void {
    const tab = this.requireTab(win, params.tabId)
    const normalized = params.keys.map(normalizeKeyboardKey).filter(Boolean)
    if (normalized.length === 0) return
    const keyCode = normalized[normalized.length - 1]!
    const modifiers = electronModifiers(normalized.slice(0, -1))
    tab.view.webContents.sendInputEvent({ type: 'keyDown', keyCode, modifiers } as Electron.KeyboardInputEvent)
    tab.view.webContents.sendInputEvent({ type: 'keyUp', keyCode, modifiers } as Electron.KeyboardInputEvent)
  }

  snapshotState(win: BrowserWindow, tabId: string): BrowserSnapshot | null {
    const tab = this.getTab(win, tabId)
    if (!tab) return null
    return this.snapshotFromRuntime(tab)
  }

  private snapshotFromRuntime(tab: BrowserTabRuntime): BrowserSnapshot {
    const history = historyOf(tab.view.webContents)
    return {
      tabId: tab.tabId,
      threadId: tab.threadId,
      currentUrl: tab.currentUrl,
      title: tab.title,
      faviconDataUrl: tab.faviconDataUrl,
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward(),
      loading: tab.view.webContents.isLoading()
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
    if (!tab || tab.view.webContents.isDestroyed()) {
      throw new Error(`Browser tab is no longer available: ${tabId}`)
    }
    tab.automationEnabled = true
    return tab
  }

  private centerVirtualMouse(tab: BrowserTabRuntime): void {
    const center = viewportCenter(tab)
    tab.virtualMouseX = center.x
    tab.virtualMouseY = center.y
  }

  private ensureVirtualMousePoint(tab: BrowserTabRuntime): { x: number; y: number } {
    if (tab.virtualMouseX === undefined || tab.virtualMouseY === undefined) {
      this.centerVirtualMouse(tab)
    }
    return {
      x: tab.virtualMouseX ?? 0,
      y: tab.virtualMouseY ?? 0
    }
  }

  private async animationDelay(timeoutMs: number): Promise<void> {
    if (timeoutMs <= 0) return
    await new Promise((resolve) => setTimeout(resolve, timeoutMs))
  }

  private async animateMouseTo(
    win: BrowserWindow,
    tab: BrowserTabRuntime,
    x: number,
    y: number,
    options: { button?: BrowserAutomationMouseButton; waitForArrival?: boolean } = {}
  ): Promise<void> {
    const waitForArrival = options.waitForArrival !== false
    const start = this.ensureVirtualMousePoint(tab)
    const path = mouseMovePath(start, { x, y }, waitForArrival)
    const stepDelay = waitForArrival && path.length > 1
      ? Math.max(1, Math.floor(VIRTUAL_MOUSE_MOVE_DURATION_MS / (path.length - 1)))
      : 0

    tab.virtualMouseMoved = true
    void this.moveVirtualMouse(tab, x, y, waitForArrival)

    let previous = start
    for (let idx = 0; idx < path.length; idx++) {
      const point = path[idx]!
      tab.virtualMouseX = point.x
      tab.virtualMouseY = point.y
      tab.view.webContents.sendInputEvent({
        type: 'mouseMove',
        x: point.x,
        y: point.y,
        ...(options.button ? { button: options.button } : {}),
        movementX: point.x - previous.x,
        movementY: point.y - previous.y
      } as Electron.MouseInputEvent)
      this.emitVirtualCursor(win, tab, point.x, point.y)
      previous = point
      if (stepDelay > 0 && idx < path.length - 1) {
        await this.animationDelay(stepDelay)
      }
    }
  }

  private focusTabWebContents(tab: BrowserTabRuntime): void {
    try {
      ;(tab.view.webContents as Electron.WebContents & { focus?: () => void }).focus?.()
    } catch {
      // Best effort focus before native input.
    }
  }

  private executeOverlayScript(tab: BrowserTabRuntime, script: string): Promise<unknown> {
    const execution = tab.view.webContents.executeJavaScript(script, true)
    execution.catch(() => {})
    return new Promise((resolve, reject) => {
      let settled = false
      const timeout = setTimeout(() => {
        if (settled) return
        settled = true
        reject(new Error(`Virtual mouse overlay script timed out after ${VIRTUAL_MOUSE_SCRIPT_TIMEOUT_MS}ms.`))
      }, VIRTUAL_MOUSE_SCRIPT_TIMEOUT_MS)
      execution.then(
        (value) => {
          if (settled) return
          settled = true
          clearTimeout(timeout)
          resolve(value)
        },
        (error) => {
          if (settled) return
          settled = true
          clearTimeout(timeout)
          reject(error)
        }
      )
    })
  }

  private async injectVirtualMouse(tab: BrowserTabRuntime, position?: { x: number; y: number }): Promise<void> {
    if (!tab.automationEnabled || tab.view.webContents.isDestroyed()) return
    try {
      if (!tab.virtualMouseMoved && (tab.virtualMouseX === undefined || tab.virtualMouseY === undefined)) {
        this.centerVirtualMouse(tab)
      }
      await this.executeOverlayScript(tab, VIRTUAL_MOUSE_BOOTSTRAP)
      const x = position?.x ?? tab.virtualMouseX
      const y = position?.y ?? tab.virtualMouseY
      if (x !== undefined && y !== undefined) {
        await this.executeOverlayScript(
          tab,
          `window.__dotcraftVirtualMouseMove?.(${x}, ${y}, 0)`,
        )
      }
    } catch {
      // Some pages cannot accept the overlay. Browser input should still work.
    }
  }

  private async moveVirtualMouse(
    tab: BrowserTabRuntime,
    x: number,
    y: number,
    waitForArrival: boolean
  ): Promise<void> {
    try {
      const start = this.ensureVirtualMousePoint(tab)
      const duration = waitForArrival ? VIRTUAL_MOUSE_MOVE_DURATION_MS : 0
      const script = `window.__dotcraftVirtualMouseMove?.(${x}, ${y}, ${duration})`
      const move = async () => {
        await this.injectVirtualMouse(tab, start)
        tab.virtualMouseX = x
        tab.virtualMouseY = y
        await this.executeOverlayScript(tab, script)
      }
      if (waitForArrival) await move()
      else void move().catch(() => {})
    } catch {
      // Best effort visual cursor.
    }
  }

  private async showVirtualClick(tab: BrowserTabRuntime, x: number, y: number): Promise<void> {
    try {
      await this.injectVirtualMouse(tab)
      await this.executeOverlayScript(tab, `window.__dotcraftVirtualMouseClick?.(${x}, ${y})`)
    } catch {
      // Best effort click ripple.
    }
  }

  private emitVirtualCursor(win: BrowserWindow, tab: BrowserTabRuntime, x: number, y: number): void {
    emitBrowserEvent(win, {
      tabId: tab.tabId,
      threadId: tab.threadId,
      type: 'virtual-cursor',
      x,
      y,
      automationActive: tab.automationActive ?? true,
      sessionName: tab.automationSessionName
    })
  }

  private emitHistoryFlags(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const history = historyOf(tab.view.webContents)
    emitBrowserEvent(win, {
      tabId: tab.tabId,
      threadId: tab.threadId,
      type: 'update-history-flags',
      canGoBack: history.canGoBack(),
      canGoForward: history.canGoForward()
    })
  }

  private bindWebContentsEvents(win: BrowserWindow, tab: BrowserTabRuntime): void {
    const wc = tab.view.webContents

    wc.on('did-start-loading', () => {
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'did-start-loading' })
      this.emitHistoryFlags(win, tab)
    })
    wc.on('did-stop-loading', () => {
      tab.currentUrl = wc.getURL() || tab.currentUrl
      emitBrowserEvent(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'did-stop-loading', url: tab.currentUrl })
      this.emitHistoryFlags(win, tab)
      if (tab.automationEnabled) {
        if (!tab.virtualMouseMoved) {
          this.centerVirtualMouse(tab)
          this.emitVirtualCursor(win, tab, tab.virtualMouseX!, tab.virtualMouseY!)
        }
        void this.injectVirtualMouse(tab)
      }
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
        finalURL: tab.view.webContents.getURL(),
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

    wc.setWindowOpenHandler((details) => {
      const normalized = normalizeBrowserUrl(details.url)
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
    })
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
    this.configuredPartitions.add(partitionName)

    partitionSession.on('will-download', (event, item, webContents) => {
      event.preventDefault()
      item.cancel()
      const win = BrowserWindow.fromWebContents(webContents)
      if (!win) return
      const runtime = this.byWindowId.get(win.id)
      if (!runtime) return
      for (const tab of runtime.tabs.values()) {
        if (tab.view.webContents.id === webContents.id) {
          emitBrowserEvent(win, {
            tabId: tab.tabId,
            threadId: tab.threadId,
            type: 'download-blocked',
            message: 'Downloads are disabled in embedded browser tabs.'
          })
          return
        }
      }
    })

    partitionSession.setPermissionCheckHandler((_wc, permission) => {
      return permission === 'clipboard-sanitized-write'
    })
    partitionSession.setPermissionRequestHandler((_wc, permission, callback) => {
      callback(permission === 'clipboard-sanitized-write')
    })
  }

  private detachView(win: BrowserWindow, tab: BrowserTabRuntime): void {
    if (!tab.visible) return
    if (tab.view.webContents.isDestroyed()) {
      tab.visible = false
      return
    }
    try {
      win.contentView.removeChildView(tab.view)
    } catch {
      // Ignore removal races when window is tearing down.
    }
    tab.visible = false
  }

  private attachView(win: BrowserWindow, tab: BrowserTabRuntime): void {
    if (tab.visible) return
    if (tab.view.webContents.isDestroyed()) return
    win.contentView.addChildView(tab.view)
    tab.visible = true
  }
}

export const viewerBrowserManager = new ViewerBrowserManager()
export { BROWSER_EVENT_CHANNEL, START_URL }
