import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, extname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { BrowserWindow } from 'electron'
import { viewerBrowserManager } from './viewerBrowser'
import type { AppSettings } from './settings'
import {
  isBrowserUseUrlAllowed as isBrowserUseUrlAllowedByPolicy,
  normalizeBrowserUseDomainList,
  resolveBrowserUseNavigationDecision
} from './browserUsePolicy'

const require = createRequire(import.meta.url)
const playwrightCoreRoot = dirname(require.resolve('playwright-core/package.json'))
const { source: playwrightInjectedScriptSource } = require(join(playwrightCoreRoot, 'lib/generated/injectedScriptSource.js')) as { source: string }
const { parseSelector: parsePlaywrightSelector } = require(join(playwrightCoreRoot, 'lib/utils/isomorphic/selectorParser.js')) as {
  parseSelector: (selector: string) => unknown
}
const {
  getByLabelSelector,
  getByPlaceholderSelector,
  getByRoleSelector,
  getByTestIdSelector,
  getByTextSelector
} = require(join(playwrightCoreRoot, 'lib/utils/isomorphic/locatorUtils.js')) as {
  getByLabelSelector: (text: string, options?: { exact?: boolean }) => string
  getByPlaceholderSelector: (text: string, options?: { exact?: boolean }) => string
  getByRoleSelector: (role: string, options?: { exact?: boolean; name?: string }) => string
  getByTestIdSelector: (testIdAttributeName: string, testId: string) => string
  getByTextSelector: (text: string, options?: { exact?: boolean }) => string
}

const BROWSER_USE_OPEN_CHANNEL = 'viewer:browser:open'
const BROWSER_USE_APPROVAL_REQUEST_CHANNEL = 'viewer:browser:approval-request'
const BROWSER_USE_APPROVAL_TIMEOUT_MS = 120_000
const BROWSER_USE_OPERATION_TIMEOUT_MS = 10_000
const BROWSER_USE_NAVIGATION_TIMEOUT_MS = 30_000
const BROWSER_USE_BLANK_TAB_READY_TIMEOUT_MS = 5_000
const BROWSER_USE_NETWORK_IDLE_QUIET_MS = 500
const BROWSER_USE_DEFAULT_VIEWPORT_WIDTH = 1280
const BROWSER_USE_DEFAULT_VIEWPORT_HEIGHT = 720
const BROWSER_USE_BROWSER_CAPABILITIES = [
  {
    id: 'viewport',
    description: 'Set or reset the embedded browser viewport size.',
    docs: 'docs/capabilities/browser/viewport.md'
  },
  {
    id: 'visibility',
    description: 'Show or hide the embedded browser surface.',
    docs: 'docs/capabilities/browser/visibility.md'
  }
]
const BROWSER_USE_TAB_CAPABILITIES = [
  {
    id: 'pageAssets',
    description: 'Inventory and bundle file assets observed in the current rendered page state.',
    docs: 'docs/capabilities/tab/pageAssets.md'
  }
]
const BROWSER_USE_PAGE_ASSET_BUNDLE_KINDS = new Set(['font', 'image', 'stylesheet', 'video'])

type BrowserUseLoadState = 'commit' | 'domcontentloaded' | 'load' | 'networkidle'

export interface BrowserUseImageResult {
  mediaType: string
  dataBase64: string
}

export interface BrowserUseOpenPayload {
  threadId: string
  tabId: string
  initialUrl: string
  title?: string
  focusMode: 'first-open' | 'none'
}

export type BrowserUseApprovalResponseAction = 'allowOnce' | 'allowDomain' | 'blockDomain' | 'deny'

export interface BrowserUseApprovalRequestPayload {
  requestId: string
  threadId: string
  tabId: string
  url: string
  domain: string
  sessionName?: string
}

export interface BrowserUseApprovalResponsePayload {
  requestId: string
  action: BrowserUseApprovalResponseAction
}

interface BrowserUseViewerHost {
  createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    width?: number
    height?: number
    allowFileScheme?: boolean
  }): unknown
  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null
  getAutomationTargetTab?(win: BrowserWindow, threadId: string): {
    tabId: string
    currentUrl: string
    title: string
    loading: boolean
  } | null
  loadAutomationUrl(win: BrowserWindow, params: { tabId: string; url: string }): Promise<void>
  destroyTab(win: BrowserWindow, tabId: string): void
  snapshotState(win: BrowserWindow, tabId: string): {
    tabId: string
    currentUrl: string
    title: string
    loading: boolean
  } | null
  setAutomationState(win: BrowserWindow, params: {
    tabId: string
    active: boolean
    sessionName?: string
    action?: string
  }): void
  setBounds?(win: BrowserWindow, params: { tabId: string; x: number; y: number; width: number; height: number }): void
  setVisible?(win: BrowserWindow, params: { tabId: string; visible: boolean }): void
  moveMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; waitForArrival?: boolean }): Promise<void>
  clickMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; button?: 'left' | 'right' | 'middle' }): Promise<void>
  doubleClickMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; button?: 'left' | 'right' | 'middle' }): Promise<void>
  dragMouse(win: BrowserWindow, params: { tabId: string; path: Array<{ x: number; y: number }> }): Promise<void>
  scrollMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; scrollX: number; scrollY: number }): Promise<void>
  typeText(win: BrowserWindow, params: { tabId: string; text: string }): Promise<void>
  keypress(win: BrowserWindow, params: { tabId: string; keys: string[] }): void
}

interface BrowserUsePolicyHost {
  getSettings(): AppSettings
  updateSettings(partial: Partial<AppSettings>): Promise<void>
}

interface BrowserUseTabRuntime {
  id: string
  owner: BrowserWindow
  logs: BrowserUseLogEntry[]
  adopted?: boolean
  keptStatus?: BrowserFinalizeKeepStatus
  cdpAttached?: boolean
  snapshotRefs: Map<string, BrowserUseElementMatch>
  domCuaNodes: Map<string, BrowserUseElementMatch>
  pageAssetInventories: Map<string, BrowserUsePageAssetInventory>
  snapshotGeneration: number
}

type BrowserUsePageAssetKind = 'script' | 'font' | 'image' | 'stylesheet' | 'video' | 'other'

interface BrowserUsePageAssetSource {
  kind: 'attribute' | 'computedStyle' | 'resource'
  nodeId?: number
  property?: string
}

interface BrowserUsePageAsset {
  id: string
  kind: BrowserUsePageAssetKind
  name: string
  sources: BrowserUsePageAssetSource[]
  url: string
}

interface BrowserUseInlineSvg {
  id: string
  markup: string
  name: string
}

interface BrowserUsePageAssetInventory {
  id: string
  assets: BrowserUsePageAsset[]
  inlineSvgs: BrowserUseInlineSvg[]
  pageUrl: string | null
  summary: {
    byKind: Partial<Record<BrowserUsePageAssetKind, number>>
    inlineSvgCount: number
    totalCount: number
  }
}

type BrowserFinalizeKeepStatus = 'handoff' | 'deliverable'

interface BrowserSessionMetadata {
  protocolVersion?: number
  sessionId?: string
  threadId?: string
  turnId?: string
  evaluationId?: string
  backendId?: string
}

interface BrowserUseOperationTrace {
  operation: string
  tabId: string
  startedAt: number
  elapsedMs?: number
  timeoutMs: number
  url: string
  status: 'active' | 'completed' | 'failed' | 'timeout' | 'cancelled' | 'stale'
  error?: string
}

interface BrowserUseLogEntry {
  level: string
  message: string
  timestamp: string
  url?: string
}

interface BrowserUseThreadRuntime {
  threadId: string
  workspacePath: string
  sessionName?: string
  agent?: Record<string, unknown>
  display?: (imageLike: unknown) => Promise<void>
  tabs: Map<string, BrowserUseTabRuntime>
  selectedTabId: string | null
  logs: string[]
  images: BrowserUseImageResult[]
  hasFocusedFirstTab: boolean
  activeEvaluationId?: string
  activeAbortSignal?: AbortSignal
  browserSession?: BrowserSessionMetadata
  recentOpenTabIds: Set<string>
  activeOperation?: BrowserUseOperationTrace
  operationHistory: BrowserUseOperationTrace[]
  viewportWidth: number
  viewportHeight: number
  browserVisible: boolean
}

interface BrowserUseOperationTimeouts {
  operationMs?: number
  navigationMs?: number
  blankTabReadyMs?: number
}

type BrowserUseLocatorKind = 'css' | 'text' | 'role' | 'label' | 'placeholder' | 'testId' | 'ref'

interface BrowserUseLocatorDescriptor {
  kind: BrowserUseLocatorKind
  value: string
  exact?: boolean
  name?: string
  index?: number
}

interface BrowserUseElementMatch {
  ref?: string
  index: number
  tagName: string
  tag?: string
  role: string
  name: string
  text: string
  href?: string
  testId?: string
  selector: string
  visible: boolean
  enabled: boolean
  visibleText: string
  ariaName: string
  boundingBox: {
    x: number
    y: number
    width: number
    height: number
  } | null
}

interface BrowserUseSnapshotRefFilter {
  ref?: string
  href?: string
  testId?: string
  role?: string
  expectedName?: string
  tagName?: string
}

export function normalizeBrowserUseUrl(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null
  if (trimmed === 'about:blank') return trimmed
  const looksLikeLocalHost =
    /^(localhost|127\.0\.0\.1|\[?::1\]?)(:\d+)?(\/|$)/i.test(trimmed)
  const withScheme = looksLikeLocalHost
    ? `http://${trimmed}`
    : /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
      ? trimmed
      : `https://${trimmed}`
  try {
    return new URL(withScheme).toString()
  } catch {
    return null
  }
}

export function isBrowserUseUrlAllowed(url: string): boolean {
  return isBrowserUseUrlAllowedByPolicy(url)
}

function imageFromDataUrl(dataUrl: string): BrowserUseImageResult | null {
  const match = /^data:([^;,]+);base64,(.*)$/i.exec(dataUrl)
  if (!match) return null
  return { mediaType: match[1], dataBase64: match[2] }
}

function sanitizeThreadId(threadId: string): string {
  return threadId.replace(/[^a-zA-Z0-9_-]/g, '_')
}

export class BrowserUseManager {
  private readonly runtimes = new Map<string, BrowserUseThreadRuntime>()
  private readonly pendingApprovals = new Map<string, {
    resolve: (action: BrowserUseApprovalResponseAction) => void
    timer: ReturnType<typeof setTimeout>
    onClosed: () => void
    owner: BrowserWindow
  }>()
  private nextTabId = 1
  private nextApprovalId = 1
  private policyHost: BrowserUsePolicyHost | null = null

  constructor(
    private readonly viewerHost: BrowserUseViewerHost = viewerBrowserManager,
    private readonly timeouts: BrowserUseOperationTimeouts = {}
  ) {}

  private operationTimeoutMs(): number {
    return this.timeouts.operationMs ?? BROWSER_USE_OPERATION_TIMEOUT_MS
  }

  private navigationTimeoutMs(): number {
    return this.timeouts.navigationMs ?? BROWSER_USE_NAVIGATION_TIMEOUT_MS
  }

  private blankTabReadyTimeoutMs(): number {
    return this.timeouts.blankTabReadyMs ?? BROWSER_USE_BLANK_TAB_READY_TIMEOUT_MS
  }

  setPolicyHost(host: BrowserUsePolicyHost): void {
    this.policyHost = host
  }

  handleApprovalResponse(payload: BrowserUseApprovalResponsePayload): boolean {
    const pending = this.pendingApprovals.get(payload.requestId)
    if (!pending) return false
    this.pendingApprovals.delete(payload.requestId)
    clearTimeout(pending.timer)
    pending.owner.off('closed', pending.onClosed)
    pending.resolve(payload.action)
    return true
  }

  prepareNodeRepl(owner: BrowserWindow, params: {
    threadId: string
    workspacePath?: string
    evaluationId?: string
    signal?: AbortSignal
    browserSession?: BrowserSessionMetadata
  }): {
    agent: Record<string, unknown>
    display: (imageLike: unknown) => Promise<void>
    collect: () => { images: BrowserUseImageResult[]; logs: string[] }
  } {
    const runtime = this.getOrCreateRuntime(owner, params.threadId, params.workspacePath)
    runtime.logs = []
    runtime.images = []
    runtime.operationHistory = []
    runtime.activeOperation = undefined
    runtime.activeEvaluationId = params.evaluationId
    runtime.activeAbortSignal = params.signal
    runtime.browserSession = {
      ...(params.browserSession ?? {}),
      backendId: 'iab'
    }
    return {
      agent: runtime.agent!,
      display: runtime.display!,
      collect: () => ({
        images: [...runtime.images],
        logs: [...runtime.logs]
      })
    }
  }

  abortEvaluation(threadId: string, evaluationId?: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    if (evaluationId && runtime.activeEvaluationId && runtime.activeEvaluationId !== evaluationId) {
      return { ok: false }
    }
    runtime.activeEvaluationId = undefined
    runtime.activeAbortSignal = undefined
    this.recordActiveOperation(runtime, 'cancelled')
    runtime.activeOperation = undefined
    this.appendOperationDiagnostics(runtime, 'Browser evaluation aborted.')
    for (const tab of runtime.tabs.values()) {
      try {
        this.webContentsFor(tab.owner, tab.id).stop()
      } catch {
        // Best effort: stopping a destroyed or unavailable tab should not block cancellation.
      }
      this.setAutomationState(runtime, tab, false)
    }
    return { ok: true }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    for (const tab of [...runtime.tabs.values()]) {
      this.detachDebugger(tab)
      if (tab.adopted) {
        this.setAutomationState(runtime, tab, false)
      } else {
        this.viewerHost.destroyTab(tab.owner, tab.id)
      }
    }
    this.runtimes.delete(threadId)
    return { ok: true }
  }

  private getOrCreateRuntime(
    owner: BrowserWindow,
    threadId: string,
    workspacePath?: string
  ): BrowserUseThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const resolvedWorkspace = workspacePath || ''
    const runtime: BrowserUseThreadRuntime = {
      threadId,
      workspacePath: resolvedWorkspace,
      tabs: new Map<string, BrowserUseTabRuntime>(),
      selectedTabId: null,
      logs: [],
      images: [],
      hasFocusedFirstTab: false,
      recentOpenTabIds: new Set<string>(),
      operationHistory: [],
      viewportWidth: BROWSER_USE_DEFAULT_VIEWPORT_WIDTH,
      viewportHeight: BROWSER_USE_DEFAULT_VIEWPORT_HEIGHT,
      browserVisible: false
    }

    const display = async (imageLike: unknown): Promise<void> => {
      if (typeof imageLike === 'string') {
        const image = imageFromDataUrl(imageLike)
        if (image) runtime.images.push(image)
        return
      }
      if (imageLike && typeof imageLike === 'object') {
        const obj = imageLike as Partial<BrowserUseImageResult> & { mimeType?: string }
        const dataBase64 = typeof obj.dataBase64 === 'string' ? obj.dataBase64 : ''
        if (dataBase64) {
          runtime.images.push({
            mediaType: obj.mediaType ?? obj.mimeType ?? 'image/png',
            dataBase64
          })
        }
      }
    }

    const browser = this.createBrowserApi(owner, runtime)
    const browsers = {
      list: async () => [this.browserInfo(runtime)],
      get: async (id: string) => {
        const normalized = String(id ?? '').toLowerCase()
        if (normalized === 'iab' || normalized === 'browser') return browser
        throw new Error(`Browser not found: ${id}. Available browser id: iab.`)
      },
      describeApi: () => ['list()', 'get("iab")', 'get("browser")']
    }
    const agent = { browser, browsers }

    runtime.agent = agent
    runtime.display = display

    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private browserInfo(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    return {
      id: 'iab',
      name: 'DotCraft Browser',
      type: 'iab',
      capabilities: {
        browser: BROWSER_USE_BROWSER_CAPABILITIES.map((capability) => ({ ...capability })),
        tab: BROWSER_USE_TAB_CAPABILITIES.map((capability) => ({ ...capability }))
      },
      tabCount: runtime.tabs.size
    }
  }

  private createBrowserApi(owner: BrowserWindow, runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    const tabs = this.createTabsApi(owner, runtime)
    return {
      browserId: 'iab',
      nameSession: async (name: string) => {
        runtime.sessionName = String(name ?? '').trim()
        for (const tab of runtime.tabs.values()) {
          this.setAutomationState(runtime, tab, true, 'session')
        }
        return { ok: true, name: runtime.sessionName }
      },
      goto: async (url: string) => {
        const tab = await this.getOrAdoptSelectedTab(owner, runtime)
        await this.navigate(tab, url)
        return this.createTabApi(tab)
      },
      tabs,
      user: {
        openTabs: async () => {
          const tabs = [...runtime.tabs.values()].map((tab) => this.tabSnapshot(tab))
          runtime.recentOpenTabIds = new Set(tabs.map((tab) => String(tab.id)))
          return tabs
        },
        claimTab: async (tab: unknown) => this.claimTab(runtime, tab),
        describeApi: () => ['openTabs()', 'claimTab(tabOrId)']
      },
      capabilities: this.createBrowserCapabilitiesApi(runtime),
      describeApi: () => [
        'nameSession(name)',
        'goto(url)',
        'tabs.list()',
        'tabs.new(url?)',
        'tabs.selected()',
        'tabs.get(id)',
        'tabs.finalize({ keep })',
        'user.openTabs()',
        'user.claimTab(tabOrId)',
        'capabilities.list()',
        'capabilities.get(id)'
      ]
    }
  }

  private createTabsApi(owner: BrowserWindow, runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    return {
      list: async () => [...runtime.tabs.values()].map((tab) => this.tabSnapshot(tab)),
      new: async (url?: string) => {
        const tab = await this.createTab(owner, runtime, url)
        runtime.selectedTabId = tab.id
        return this.createTabApi(tab)
      },
      selected: async () => {
        const tab = await this.getOrAdoptSelectedTab(owner, runtime)
        return this.createTabApi(tab)
      },
      get: async (id: string) => {
        const tab = runtime.tabs.get(id)
        if (!tab) throw new Error(`Browser tab not found: ${id}`)
        return this.createTabApi(tab)
      },
      finalize: async (options?: { keep?: unknown[] }) => this.finalizeTabs(runtime, options),
      describeApi: () => ['list()', 'new(url?)', 'selected()', 'get(id)', 'finalize({ keep })']
    }
  }

  private createBrowserCapabilitiesApi(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    const available = BROWSER_USE_BROWSER_CAPABILITIES.map((capability) => ({ ...capability }))
    return {
      list: async () => available,
      get: async (id: string) => {
        if (id === 'viewport') return this.createViewportCapability(runtime)
        if (id === 'visibility') return this.createVisibilityCapability(runtime)
        throw new Error(`Browser capability not found: ${id}. Available capabilities: viewport, visibility.`)
      },
      describeApi: () => ['list()', 'get("viewport")', 'get("visibility")']
    }
  }

  private createViewportCapability(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    return {
      set: async (options: { width?: number; height?: number }) => {
        const width = this.normalizeViewportDimension(options?.width, 'width')
        const height = this.normalizeViewportDimension(options?.height, 'height')
        runtime.viewportWidth = width
        runtime.viewportHeight = height
        this.applyViewport(runtime)
        return { ok: true, width, height }
      },
      reset: async () => {
        runtime.viewportWidth = BROWSER_USE_DEFAULT_VIEWPORT_WIDTH
        runtime.viewportHeight = BROWSER_USE_DEFAULT_VIEWPORT_HEIGHT
        this.applyViewport(runtime)
        return { ok: true, width: runtime.viewportWidth, height: runtime.viewportHeight }
      },
      describeApi: () => ['set({ width, height })', 'reset()']
    }
  }

  private createVisibilityCapability(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    return {
      get: async () => runtime.browserVisible,
      set: async (visible: boolean) => {
        runtime.browserVisible = visible === true
        for (const tab of runtime.tabs.values()) {
          this.viewerHost.setVisible?.(tab.owner, { tabId: tab.id, visible: runtime.browserVisible })
        }
        if (runtime.browserVisible) this.presentVisibleTabs(runtime)
        return { ok: true, visible: runtime.browserVisible }
      },
      describeApi: () => ['get()', 'set(visible)']
    }
  }

  private normalizeViewportDimension(value: unknown, name: string): number {
    const numeric = Number(value)
    if (!Number.isFinite(numeric) || numeric < 200 || numeric > 10_000) {
      throw new Error(`Invalid browser viewport ${name}: ${value}. Expected a number between 200 and 10000.`)
    }
    return Math.round(numeric)
  }

  private async unsupported(api: string): Promise<never> {
    throw new Error(`DotCraft embedded browser does not support ${api}.`)
  }

  private applyViewport(runtime: BrowserUseThreadRuntime): void {
    for (const tab of runtime.tabs.values()) {
      this.viewerHost.setBounds?.(tab.owner, {
        tabId: tab.id,
        x: 0,
        y: 0,
        width: runtime.viewportWidth,
        height: runtime.viewportHeight
      })
    }
  }

  private claimTab(runtime: BrowserUseThreadRuntime, item: unknown): Record<string, unknown> {
    const id = this.tabIdFromReference(item)
    if (!id || !runtime.recentOpenTabIds.has(id)) {
      throw new Error('Cannot claim browser tab: pass a tab object or id from the current session latest user.openTabs() result.')
    }
    const tab = runtime.tabs.get(id)
    if (!tab) throw new Error(`Browser tab not found: ${id}`)
    tab.adopted = true
    this.setAutomationState(runtime, tab, true, 'claim')
    return this.createTabApi(tab)
  }

  private async finalizeTabs(
    runtime: BrowserUseThreadRuntime,
    options?: { keep?: unknown[] }
  ): Promise<Record<string, unknown>> {
    const keep = this.parseFinalizeKeep(options)
    const kept: string[] = []
    const closed: string[] = []
    const released: string[] = []
    for (const tab of [...runtime.tabs.values()]) {
      const keptStatus = keep.get(tab.id)
      if (keptStatus) {
        tab.keptStatus = keptStatus
        kept.push(tab.id)
        this.setAutomationState(runtime, tab, true, keptStatus)
        continue
      }
      if (tab.adopted) {
        this.setAutomationState(runtime, tab, false)
        tab.adopted = false
        released.push(tab.id)
        continue
      }
      this.closeTab(tab)
      closed.push(tab.id)
    }
    runtime.logs.push(
      `Browser finalize summary sessionId=${runtime.browserSession?.sessionId ?? runtime.threadId} ` +
      `turnId=${runtime.browserSession?.turnId ?? ''} evaluationId=${runtime.browserSession?.evaluationId ?? runtime.activeEvaluationId ?? ''} ` +
      `backendId=iab created=${closed.length + kept.length} claimed=${released.length + kept.length} kept=${kept.length} closed=${closed.length} released=${released.length}`
    )
    return { ok: true, kept, closed, released }
  }

  private parseFinalizeKeep(options?: { keep?: unknown[] }): Map<string, BrowserFinalizeKeepStatus> {
    const keep = options?.keep ?? []
    if (!Array.isArray(keep)) {
      throw new Error('browser.tabs.finalize requires keep to be an array of { tab, status } entries.')
    }
    const result = new Map<string, BrowserFinalizeKeepStatus>()
    for (const item of keep) {
      if (!item || typeof item !== 'object' || Array.isArray(item)) {
        throw new Error('browser.tabs.finalize keep entries must be objects shaped like { tab, status }.')
      }
      const entry = item as Record<string, unknown>
      const status = entry.status
      if (status !== 'handoff' && status !== 'deliverable') {
        throw new Error('browser.tabs.finalize keep status must be "handoff" or "deliverable".')
      }
      const id = this.tabIdFromReference(entry.tab)
      if (!id) {
        throw new Error('browser.tabs.finalize keep entries must include a tab reference.')
      }
      result.set(id, status)
    }
    return result
  }

  private tabIdFromReference(item: unknown): string {
    if (typeof item === 'string') return item
    if (typeof item === 'number' && Number.isFinite(item)) return String(Math.trunc(item))
    if (item && typeof item === 'object') {
      const obj = item as Record<string, unknown>
      if (typeof obj.id === 'string') return obj.id
      if (typeof obj.tabId === 'string') return obj.tabId
      if (obj.info && typeof obj.info === 'object') return this.tabIdFromReference(obj.info)
    }
    return ''
  }

  private async createTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    initialUrl?: string
  ): Promise<BrowserUseTabRuntime> {
    const normalizedInitial = initialUrl ? normalizeBrowserUseUrl(initialUrl) : null
    if (initialUrl && !normalizedInitial) throw new Error(`Invalid browser URL: ${initialUrl}`)
    const id = `browser-${sanitizeThreadId(runtime.threadId)}-${this.nextTabId++}`
    if (normalizedInitial) {
      await this.ensureNavigationAllowed(owner, runtime, id, normalizedInitial)
    }

    this.viewerHost.createAutomationTab(owner, {
      tabId: id,
      threadId: runtime.threadId,
      workspacePath: runtime.workspacePath || owner.getTitle(),
      initialUrl: 'about:blank',
      width: runtime.viewportWidth,
      height: runtime.viewportHeight,
      allowFileScheme: true
    })

    const tab = this.registerTab(owner, runtime, id, false)
    if (!runtime.browserVisible) {
      this.viewerHost.setVisible?.(owner, { tabId: tab.id, visible: false })
    }

    const focusMode = runtime.browserVisible && !runtime.hasFocusedFirstTab ? 'first-open' : 'none'
    if (focusMode === 'first-open') runtime.hasFocusedFirstTab = true
    this.emitOpen(owner, {
      threadId: runtime.threadId,
      tabId: id,
      initialUrl: normalizedInitial ?? 'about:blank',
      title: runtime.sessionName?.trim() || 'Browser',
      focusMode
    })

    if (normalizedInitial) {
      await this.navigate(tab, normalizedInitial, { skipPolicyCheck: true })
    } else {
      await this.loadAutomationUrl(
        tab,
        'about:blank',
        this.blankTabReadyTimeoutMs(),
        'initial blank page')
      await this.waitForScriptReady(tab, this.blankTabReadyTimeoutMs())
    }
    return tab
  }

  private emitOpen(owner: BrowserWindow, payload: BrowserUseOpenPayload): void {
    if (owner.isDestroyed() || owner.webContents.isDestroyed()) return
    owner.webContents.send(BROWSER_USE_OPEN_CHANNEL, payload)
  }

  private presentVisibleTabs(runtime: BrowserUseThreadRuntime): void {
    let focusNext = !runtime.hasFocusedFirstTab
    for (const tab of runtime.tabs.values()) {
      this.emitOpen(tab.owner, {
        threadId: runtime.threadId,
        tabId: tab.id,
        initialUrl: this.operationUrl(tab),
        title: runtime.sessionName?.trim() || 'Browser',
        focusMode: focusNext ? 'first-open' : 'none'
      })
      focusNext = false
    }
    if (runtime.tabs.size > 0) runtime.hasFocusedFirstTab = true
  }

  private webContentsFor(owner: BrowserWindow, tabId: string): Electron.WebContents {
    const wc = this.viewerHost.getTabWebContents(owner, tabId)
    if (!wc || wc.isDestroyed()) throw new Error(`Browser tab is no longer available: ${tabId}`)
    return wc
  }

  private async ensureDebuggerAttached(tab: BrowserUseTabRuntime): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const debuggerApi = wc.debugger
    if (!debuggerApi) {
      throw new Error(`Browser tab ${tab.id} does not expose Electron debugger/CDP.`)
    }
    if (!tab.cdpAttached || !debuggerApi.isAttached()) {
      debuggerApi.attach('1.3')
      tab.cdpAttached = true
    }
  }

  private detachDebugger(tab: BrowserUseTabRuntime): void {
    try {
      const debuggerApi = this.webContentsFor(tab.owner, tab.id).debugger
      if (debuggerApi?.isAttached()) debuggerApi.detach()
    } catch {
      // Best effort only. Browser tab teardown should not be blocked by debugger cleanup.
    } finally {
      tab.cdpAttached = false
    }
  }

  private async cdpCommand<T = unknown>(
    tab: BrowserUseTabRuntime,
    method: string,
    params?: Record<string, unknown>
  ): Promise<T> {
    await this.ensureDebuggerAttached(tab)
    return await this.webContentsFor(tab.owner, tab.id).debugger.sendCommand(method, params) as T
  }

  private operationUrl(tab: BrowserUseTabRuntime): string {
    try {
      return this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'
    } catch {
      return 'unknown'
    }
  }

  private beginOperation(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    operation: string,
    timeoutMs: number
  ): BrowserUseOperationTrace {
    const trace: BrowserUseOperationTrace = {
      operation,
      tabId: tab.id,
      startedAt: Date.now(),
      timeoutMs,
      url: this.operationUrl(tab),
      status: 'active'
    }
    runtime.activeOperation = trace
    return trace
  }

  private finishOperation(
    runtime: BrowserUseThreadRuntime,
    trace: BrowserUseOperationTrace,
    status: BrowserUseOperationTrace['status'],
    error?: string
  ): void {
    if (runtime.activeOperation === trace) {
      runtime.activeOperation = undefined
    }
    trace.elapsedMs = Math.max(0, Date.now() - trace.startedAt)
    trace.status = status
    trace.error = error
    runtime.operationHistory.push({ ...trace })
    runtime.operationHistory = runtime.operationHistory.slice(-8)
  }

  private recordActiveOperation(
    runtime: BrowserUseThreadRuntime,
    status: BrowserUseOperationTrace['status'],
    error?: string
  ): void {
    if (!runtime.activeOperation) return
    this.finishOperation(runtime, runtime.activeOperation, status, error)
  }

  private appendOperationDiagnostics(runtime: BrowserUseThreadRuntime, prefix: string): void {
    const traces = [...runtime.operationHistory]
    if (runtime.activeOperation) traces.push({
      ...runtime.activeOperation,
      elapsedMs: Math.max(0, Date.now() - runtime.activeOperation.startedAt)
    })
    if (traces.length === 0) return
    const tail = traces.slice(-5).map((trace) => {
      const elapsed = trace.elapsedMs ?? Math.max(0, Date.now() - trace.startedAt)
      const error = trace.error ? ` error=${trace.error}` : ''
      return `${trace.operation} status=${trace.status} tab=${trace.tabId} url=${trace.url} elapsedMs=${elapsed} timeoutMs=${trace.timeoutMs}${error}`
    })
    runtime.logs.push(`${prefix}\nRecent browser operations:\n${tail.join('\n')}`)
  }

  private async withBrowserOperation<T>(
    tab: BrowserUseTabRuntime,
    operation: string,
    run: () => Promise<T> | T,
    timeoutMs?: number
  ): Promise<T> {
    const runtime = this.getRuntimeForTab(tab)
    const signal = runtime.activeAbortSignal
    const evaluationId = runtime.activeEvaluationId
    if (signal?.aborted) {
      throw new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`)
    }
    const effectiveTimeoutMs = Math.max(1, Math.min(timeoutMs ?? this.operationTimeoutMs(), 120_000))
    const trace = this.beginOperation(runtime, tab, operation, effectiveTimeoutMs)

    let operationPromise: Promise<T>
    try {
      operationPromise = Promise.resolve(run())
    } catch (error) {
      this.finishOperation(runtime, trace, 'failed', error instanceof Error ? error.message : String(error))
      throw error
    }
    operationPromise.catch(() => {})

    return new Promise<T>((resolve, reject) => {
      let settled = false
      const cleanup = () => {
        clearTimeout(timeout)
        signal?.removeEventListener('abort', onAbort)
      }
      const finish = (callback: () => void) => {
        if (settled) return
        settled = true
        cleanup()
        callback()
      }
      const ensureStillActive = () => {
        if (signal?.aborted) {
          this.finishOperation(runtime, trace, 'cancelled')
          return new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id} at ${currentUrl()}.`)
        }
        if (evaluationId && runtime.activeEvaluationId !== evaluationId) {
          this.finishOperation(runtime, trace, 'stale')
          return new Error(`Browser operation '${operation}' result arrived after evaluation ${evaluationId} was no longer active for tab ${tab.id} at ${currentUrl()}.`)
        }
        return null
      }
      const currentUrl = () => {
        try {
          return this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'
        } catch {
          return 'unknown'
        }
      }
      const onAbort = () => {
        finish(() => {
          this.finishOperation(runtime, trace, 'cancelled')
          reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id} at ${currentUrl()}.`))
        })
      }
      const timeout = setTimeout(() => {
        finish(() => {
          const message = `Browser operation '${operation}' timed out after ${effectiveTimeoutMs}ms for tab ${tab.id} at ${currentUrl()}.`
          this.finishOperation(runtime, trace, 'timeout', message)
          this.appendOperationDiagnostics(runtime, message)
          reject(new Error(message))
        })
      }, effectiveTimeoutMs)

      signal?.addEventListener('abort', onAbort, { once: true })
      operationPromise.then(
        (value) => finish(() => {
          const stale = ensureStillActive()
          if (stale) reject(stale)
          else {
            this.finishOperation(runtime, trace, 'completed')
            resolve(value)
          }
        }),
        (error) => finish(() => {
          this.finishOperation(runtime, trace, 'failed', error instanceof Error ? error.message : String(error))
          reject(error)
        })
      )
    })
  }

  private async loadAutomationUrl(
    tab: BrowserUseTabRuntime,
    url: string,
    timeoutMs = this.navigationTimeoutMs(),
    operation = 'navigate'
  ): Promise<void> {
    await this.withBrowserOperation(
      tab,
      operation,
      () => this.viewerHost.loadAutomationUrl(tab.owner, { tabId: tab.id, url }),
      timeoutMs)
  }

  private async waitForScriptReady(
    tab: BrowserUseTabRuntime,
    timeoutMs = this.blankTabReadyTimeoutMs()
  ): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (!wc.isLoading() && wc.getURL()) return
    await this.withBrowserOperation(tab, 'wait for script-ready document', () => new Promise<void>((resolve) => {
      const done = () => {
        cleanup()
        resolve()
      }
      const cleanup = () => {
        wc.off('dom-ready', done)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
      }
      wc.once('dom-ready', done)
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
    }), timeoutMs)
  }

  private executeJavaScript<T = unknown>(
    tab: BrowserUseTabRuntime,
    source: string,
    operation: string,
    userGesture = true
  ): Promise<T> {
    return this.withBrowserOperation(
      tab,
      operation,
      async () => {
        const result = await this.cdpCommand<{
          result?: { value?: T; unserializableValue?: string }
          exceptionDetails?: {
            text?: string
            exception?: { description?: string; value?: unknown }
          }
        }>(tab, 'Runtime.evaluate', {
          expression: source,
          awaitPromise: true,
          returnByValue: true,
          userGesture
        })
        if (result.exceptionDetails) {
          const details = result.exceptionDetails
          const message = details.exception?.description ||
            (details.exception?.value == null ? undefined : String(details.exception.value)) ||
            details.text ||
            `JavaScript evaluation failed during ${operation}`
          throw new Error(message)
        }
        if (result.result?.unserializableValue != null) {
          return result.result.unserializableValue as T
        }
        return result.result?.value as T
      })
  }

  private async waitForPageReady(
    tab: BrowserUseTabRuntime,
    options: { operation: string; requireContent: boolean; timeoutMs: number }
  ): Promise<void> {
    await this.waitForScriptReady(tab, Math.min(options.timeoutMs, this.blankTabReadyTimeoutMs()))
    const deadline = Date.now() + Math.max(1, Math.min(options.timeoutMs, 120_000))
    for (;;) {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) throw new Error(`Browser operation '${options.operation}' was cancelled for tab ${tab.id}.`)
      const rawState = await this.executeJavaScript<unknown>(tab, `
        new Promise((resolve) => {
          const sample = () => {
            const bodyText = (document.body?.innerText || '').trim();
            const interactive = document.querySelectorAll('a,button,input,textarea,select,summary,[role="button"],[role="link"]').length;
            const appRoot = document.querySelector('#app, #root, [data-v-app], main, nav, header');
            resolve({
              url: location.href,
              title: document.title,
              readyState: document.readyState,
              bodyTextLength: bodyText.length,
              interactiveCount: interactive,
              appRootTextLength: (appRoot?.textContent || '').trim().length
            });
          };
          requestAnimationFrame(() => requestAnimationFrame(sample));
        })
      `, options.operation)
      const state = this.normalizeReadinessState(rawState)
      if (!state) {
        if (Date.now() >= deadline) {
          throw new Error(`Browser operation '${options.operation}' timed out after ${Math.max(1, Math.min(options.timeoutMs, 120_000))}ms for tab ${tab.id} at ${this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'}.`)
        }
        await this.delay(tab, 100, options.operation)
        continue
      }
      const documentReady = state.readyState === 'interactive' || state.readyState === 'complete'
      const blank = state.url === 'about:blank'
      const hasUsefulContent =
        state.bodyTextLength > 0 ||
        state.interactiveCount > 0 ||
        state.appRootTextLength > 0 ||
        state.title.trim().length > 0
      if (documentReady && (blank || !options.requireContent || hasUsefulContent)) return
      if (Date.now() >= deadline) {
        throw new Error(
          `Browser operation '${options.operation}' timed out after ${Math.max(1, Math.min(options.timeoutMs, 120_000))}ms for tab ${tab.id} at ${state.url || this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'}.`)
      }
      await this.delay(tab, 100, options.operation)
    }
  }

  private normalizeReadinessState(rawState: unknown): {
        url: string
        title: string
        readyState: string
        bodyTextLength: number
        interactiveCount: number
        appRootTextLength: number
      } | null {
    let parsed = rawState
    if (typeof parsed === 'string') {
      try {
        parsed = JSON.parse(parsed)
      } catch {
        return null
      }
    }
    if (!parsed || typeof parsed !== 'object') return null
    const state = parsed as Record<string, unknown>
    return {
      url: typeof state.url === 'string' ? state.url : '',
      title: typeof state.title === 'string' ? state.title : '',
      readyState: typeof state.readyState === 'string' ? state.readyState : '',
      bodyTextLength: typeof state.bodyTextLength === 'number' ? state.bodyTextLength : 0,
      interactiveCount: typeof state.interactiveCount === 'number' ? state.interactiveCount : 0,
      appRootTextLength: typeof state.appRootTextLength === 'number' ? state.appRootTextLength : 0
    }
  }

  private delay(tab: BrowserUseTabRuntime, timeoutMs: number, operation: string): Promise<void> {
    const signal = this.getRuntimeForTab(tab).activeAbortSignal
    if (signal?.aborted) return Promise.reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`))
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        signal?.removeEventListener('abort', onAbort)
        resolve()
      }, timeoutMs)
      const onAbort = () => {
        clearTimeout(timeout)
        reject(new Error(`Browser operation '${operation}' was cancelled for tab ${tab.id}.`))
      }
      signal?.addEventListener('abort', onAbort, { once: true })
    })
  }

  private getSelectedTab(runtime: BrowserUseThreadRuntime): BrowserUseTabRuntime | null {
    if (runtime.selectedTabId) {
      const existing = runtime.tabs.get(runtime.selectedTabId)
      if (existing) return existing
    }
    const first = runtime.tabs.values().next().value as BrowserUseTabRuntime | undefined
    return first ?? null
  }

  private async getOrAdoptSelectedTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime
  ): Promise<BrowserUseTabRuntime> {
    if (runtime.selectedTabId) {
      const existing = runtime.tabs.get(runtime.selectedTabId)
      if (existing) return existing
    }

    const candidate = this.viewerHost.getAutomationTargetTab?.(owner, runtime.threadId)
    if (candidate) {
      const adopted = this.registerTab(owner, runtime, candidate.tabId, true)
      runtime.selectedTabId = adopted.id
      return adopted
    }

    const selected = this.getSelectedTab(runtime)
    if (selected) return selected

    const created = await this.createTab(owner, runtime)
    runtime.selectedTabId = created.id
    return created
  }

  private registerTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    id: string,
    adopted: boolean
  ): BrowserUseTabRuntime {
    const existing = runtime.tabs.get(id)
    if (existing) return existing
    const wc = this.webContentsFor(owner, id)
    const tab: BrowserUseTabRuntime = {
      id,
      owner,
      logs: [],
      adopted,
      snapshotRefs: new Map(),
      domCuaNodes: new Map(),
      pageAssetInventories: new Map(),
      snapshotGeneration: 0
    }
    runtime.tabs.set(id, tab)

    wc.on('console-message', (_event, level, message) => {
      const levelNames = ['verbose', 'info', 'warning', 'error'] as const
      tab.logs.push({
        level: levelNames[level as number] ?? String(level ?? 'log'),
        message,
        timestamp: new Date().toISOString(),
        url: wc.getURL()
      })
    })
    wc.once('destroyed', () => {
      this.detachDebugger(tab)
      runtime.tabs.delete(id)
      if (runtime.selectedTabId === id) runtime.selectedTabId = null
    })
    return tab
  }

  private createTabApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      id: tab.id,
      navigate: async (url: string) => this.navigate(tab, url),
      goto: async (url: string) => this.navigate(tab, url),
      back: async () => this.goBack(tab),
      forward: async () => this.goForward(tab),
      reload: async () => this.reload(tab),
      close: async () => this.closeTab(tab),
      url: async () => this.webContentsFor(tab.owner, tab.id).getURL(),
      title: async () => this.webContentsFor(tab.owner, tab.id).getTitle(),
      screenshot: async (options?: { fullPage?: boolean; clip?: Electron.Rectangle }) => this.screenshot(tab, options),
      domSnapshot: async () => this.domSnapshot(tab),
      evaluate: async (expressionOrFunction: string | (() => unknown)) => this.evaluateInPage(tab, expressionOrFunction),
      click: async (selector: string) => this.click(tab, selector),
      clickRef: async (ref: string) => this.locatorClick(tab, { kind: 'ref', value: String(ref) }),
      fillRef: async (ref: string, value: string) => this.locatorFill(tab, { kind: 'ref', value: String(ref) }, value),
      pressRef: async (ref: string, key: string) => this.locatorPress(tab, { kind: 'ref', value: String(ref) }, key),
      type: async (selector: string, text: string) => this.type(tab, selector, text),
      press: async (selector: string, key: string) => this.press(tab, selector, key),
      waitForLoadState: async (state = 'load', timeoutMs = 30_000) => this.waitForLoad(tab, state, timeoutMs),
      consoleLogs: async () => tab.logs.map((entry) => entry.message),
      playwright: this.createPlaywrightApi(tab),
      cua: this.createCuaApi(tab),
      dom_cua: this.createDomCuaApi(tab),
      capabilities: this.createTabCapabilitiesApi(tab),
      dev: {
        logs: async (options?: { filter?: string; levels?: string[]; limit?: number }) => this.devLogs(tab, options),
        describeApi: () => ['logs({ filter?, levels?, limit? })']
      },
      clipboard: {
        read: async () => this.unsupported('tab.clipboard.read() rich clipboard items'),
        readText: async () => this.executeJavaScript(tab, 'navigator.clipboard.readText()', 'clipboard.readText'),
        write: async () => this.unsupported('tab.clipboard.write() rich clipboard items'),
        writeText: async (text: string) => this.executeJavaScript(
          tab,
          `navigator.clipboard.writeText(${JSON.stringify(String(text ?? ''))})`,
          'clipboard.writeText'),
        describeApi: () => ['readText()', 'writeText(text)', 'read() unsupported', 'write(items) unsupported']
      },
      describeApi: () => [
        'goto(url)',
        'reload()',
        'back()',
        'forward()',
        'close()',
        'url()',
        'title()',
        'domSnapshot()',
        'screenshot(options?)',
        'evaluate(expressionOrFunction)',
        'playwright.*',
        'cua.*',
        'dom_cua.*',
        'capabilities.list()',
        'capabilities.get("pageAssets")'
      ]
    }
  }

  private createTabCapabilitiesApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    const available = BROWSER_USE_TAB_CAPABILITIES.map((capability) => ({ ...capability }))
    return {
      list: async () => available,
      get: async (id: string) => {
        if (id === 'pageAssets') return this.createPageAssetsCapability(tab)
        throw new Error(`Tab capability not found: ${id}. Available capabilities: pageAssets.`)
      },
      describeApi: () => ['list()', 'get("pageAssets")']
    }
  }

  private createPageAssetsCapability(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      list: async () => this.listPageAssets(tab),
      bundle: async (options: { assetIds?: string[]; inventoryId?: string; kinds?: string[] }) => this.bundlePageAssets(tab, options),
      describeApi: () => ['list()', 'bundle({ inventoryId, kinds?, assetIds? })']
    }
  }

  private async listPageAssets(tab: BrowserUseTabRuntime): Promise<BrowserUsePageAssetInventory> {
    this.markAutomation(tab, 'pageAssets.list')
    await this.waitForPageReady(tab, {
      operation: 'pageAssets.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    const raw = await this.executeJavaScript<unknown>(tab, `
      (() => {
        const __dotcraftBrowserUsePageAssets = true;
        const assets = new Map();
        const inlineSvgs = [];
        const absoluteUrl = (value) => {
          try {
            const text = String(value || '').trim();
            if (!text || text.startsWith('#')) return '';
            return new URL(text, document.baseURI).href;
          } catch {
            return '';
          }
        };
        const nameFromUrl = (url, fallback) => {
          try {
            const path = new URL(url).pathname.split('/').filter(Boolean).pop();
            return decodeURIComponent(path || fallback || 'asset');
          } catch {
            return fallback || 'asset';
          }
        };
        const add = (url, kind, source, fallbackName) => {
          const href = absoluteUrl(url);
          if (!href) return;
          const key = kind + '\\n' + href;
          const existing = assets.get(key);
          if (existing) {
            existing.sources.push(source);
            return;
          }
          assets.set(key, {
            kind,
            name: nameFromUrl(href, fallbackName),
            sources: [source],
            url: href
          });
        };
        const bySelector = (selector, kind, attribute, fallbackName) => {
          Array.from(document.querySelectorAll(selector)).forEach((node, index) => {
            add(node.getAttribute(attribute), kind, { kind: 'attribute', nodeId: index + 1, property: attribute }, fallbackName);
          });
        };
        bySelector('img[src], input[type="image"][src]', 'image', 'src', 'image');
        bySelector('source[src], video[src]', 'video', 'src', 'video');
        bySelector('link[rel~="stylesheet"][href]', 'stylesheet', 'href', 'stylesheet');
        bySelector('script[src]', 'script', 'src', 'script');
        Array.from(document.querySelectorAll('link[href]')).forEach((node, index) => {
          const rel = String(node.getAttribute('rel') || '').toLowerCase();
          const as = String(node.getAttribute('as') || '').toLowerCase();
          const href = node.getAttribute('href');
          if (rel.includes('preload') || rel.includes('prefetch')) {
            if (as === 'font') add(href, 'font', { kind: 'attribute', nodeId: index + 1, property: 'href' }, 'font');
            else if (as === 'image') add(href, 'image', { kind: 'attribute', nodeId: index + 1, property: 'href' }, 'image');
            else if (as === 'style') add(href, 'stylesheet', { kind: 'attribute', nodeId: index + 1, property: 'href' }, 'stylesheet');
            else if (as === 'video') add(href, 'video', { kind: 'attribute', nodeId: index + 1, property: 'href' }, 'video');
          }
        });
        Array.from(performance.getEntriesByType('resource') || []).forEach((entry) => {
          const initiator = String(entry.initiatorType || '').toLowerCase();
          const kind =
            initiator === 'script' ? 'script' :
            initiator === 'css' || initiator === 'link' ? 'stylesheet' :
            initiator === 'img' || initiator === 'image' ? 'image' :
            initiator === 'video' ? 'video' :
            initiator === 'font' ? 'font' :
            'other';
          add(entry.name, kind, { kind: 'resource', property: initiator || 'resource' }, kind);
        });
        const urlPattern = /url\\((?:"([^"]+)"|'([^']+)'|([^)]*))\\)/g;
        Array.from(document.querySelectorAll('*')).forEach((node, index) => {
          const style = getComputedStyle(node);
          for (const property of ['backgroundImage', 'borderImageSource', 'listStyleImage', 'cursor']) {
            const value = String(style[property] || '');
            let match;
            while ((match = urlPattern.exec(value))) {
              add(match[1] || match[2] || match[3], 'image', { kind: 'computedStyle', nodeId: index + 1, property }, 'image');
            }
          }
        });
        Array.from(document.querySelectorAll('svg')).slice(0, 100).forEach((node, index) => {
          inlineSvgs.push({
            id: 'inline-svg-' + (index + 1),
            markup: node.outerHTML,
            name: node.getAttribute('aria-label') || node.getAttribute('id') || node.getAttribute('class') || 'inline-svg-' + (index + 1)
          });
        });
        return { assets: Array.from(assets.values()), inlineSvgs, pageUrl: location.href };
      })()
    `, 'pageAssets.list')
    const inventory = this.normalizePageAssetInventory(tab, raw)
    tab.pageAssetInventories.set(inventory.id, inventory)
    return inventory
  }

  private normalizePageAssetInventory(tab: BrowserUseTabRuntime, raw: unknown): BrowserUsePageAssetInventory {
    const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
    const rawAssets = Array.isArray(obj.assets) ? obj.assets : []
    const usedIds = new Map<string, number>()
    const assets: BrowserUsePageAsset[] = []
    for (const rawAsset of rawAssets) {
      if (!rawAsset || typeof rawAsset !== 'object') continue
      const asset = rawAsset as Record<string, unknown>
      const url = this.stringValue(asset.url).trim()
      if (!url) continue
      const kind = this.normalizePageAssetKind(asset.kind)
      const baseId = `${kind}-${createHash('sha1').update(url).digest('hex').slice(0, 10)}`
      const nextIndex = usedIds.get(baseId) ?? 0
      usedIds.set(baseId, nextIndex + 1)
      const id = nextIndex === 0 ? baseId : `${baseId}-${nextIndex + 1}`
      assets.push({
        id,
        kind,
        name: this.stringValue(asset.name).trim() || this.pageAssetNameFromUrl(url, kind),
        sources: this.normalizePageAssetSources(asset.sources),
        url
      })
    }
    const inlineSvgs = (Array.isArray(obj.inlineSvgs) ? obj.inlineSvgs : []).map((value, index) => {
      const svg = value && typeof value === 'object' ? value as Record<string, unknown> : {}
      return {
        id: this.stringValue(svg.id).trim() || `inline-svg-${index + 1}`,
        markup: this.stringValue(svg.markup),
        name: this.stringValue(svg.name).trim() || `inline-svg-${index + 1}`
      }
    }).filter((svg) => svg.markup.trim().length > 0)
    const byKind: Partial<Record<BrowserUsePageAssetKind, number>> = {}
    for (const asset of assets) {
      byKind[asset.kind] = (byKind[asset.kind] ?? 0) + 1
    }
    const inventory: BrowserUsePageAssetInventory = {
      id: `page-assets-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`,
      assets,
      inlineSvgs,
      pageUrl: typeof obj.pageUrl === 'string' ? obj.pageUrl : this.operationUrl(tab),
      summary: {
        byKind,
        inlineSvgCount: inlineSvgs.length,
        totalCount: assets.length
      }
    }
    return inventory
  }

  private normalizePageAssetKind(value: unknown): BrowserUsePageAssetKind {
    const kind = this.stringValue(value).trim()
    return kind === 'script' || kind === 'font' || kind === 'image' || kind === 'stylesheet' || kind === 'video'
      ? kind
      : 'other'
  }

  private normalizePageAssetSources(value: unknown): BrowserUsePageAssetSource[] {
    const items = Array.isArray(value) ? value : []
    const sources = items.map((item) => {
      const source = item && typeof item === 'object' ? item as Record<string, unknown> : {}
      const kind: BrowserUsePageAssetSource['kind'] =
        source.kind === 'computedStyle' || source.kind === 'resource' ? source.kind : 'attribute'
      const nodeId = Number(source.nodeId)
      return {
        kind,
        ...(Number.isFinite(nodeId) && nodeId > 0 ? { nodeId: Math.trunc(nodeId) } : {}),
        ...(typeof source.property === 'string' && source.property.trim() ? { property: source.property.trim() } : {})
      }
    })
    return sources.length > 0 ? sources : [{ kind: 'resource' }]
  }

  private pageAssetNameFromUrl(url: string, fallback: string): string {
    try {
      const parsed = new URL(url)
      if (parsed.protocol === 'data:') return fallback
      const name = basename(decodeURIComponent(parsed.pathname || '')).trim()
      return name || fallback
    } catch {
      return fallback
    }
  }

  private async bundlePageAssets(
    tab: BrowserUseTabRuntime,
    options: { assetIds?: string[]; inventoryId?: string; kinds?: string[] } = {}
  ): Promise<Record<string, unknown>> {
    const inventoryId = this.stringValue(options.inventoryId).trim()
    if (!inventoryId) throw new Error('pageAssets.bundle requires inventoryId from a prior pageAssets.list() result.')
    const inventory = tab.pageAssetInventories.get(inventoryId)
    if (!inventory) throw new Error(`Page asset inventory not found: ${inventoryId}. Call pageAssets.list() again before bundling.`)
    const kindFilter = new Set((Array.isArray(options.kinds) ? options.kinds : [...BROWSER_USE_PAGE_ASSET_BUNDLE_KINDS])
      .map((kind) => this.normalizePageAssetKind(kind))
      .filter((kind) => BROWSER_USE_PAGE_ASSET_BUNDLE_KINDS.has(kind)))
    const assetIdFilter = new Set((Array.isArray(options.assetIds) ? options.assetIds : [])
      .map((id) => this.stringValue(id).trim())
      .filter(Boolean))
    const requested = inventory.assets.filter((asset) => {
      if (!BROWSER_USE_PAGE_ASSET_BUNDLE_KINDS.has(asset.kind)) return false
      if (kindFilter.size > 0 && !kindFilter.has(asset.kind)) return false
      if (assetIdFilter.size > 0 && !assetIdFilter.has(asset.id)) return false
      return true
    })
    const startedAt = Date.now()
    const directoryPath = await mkdtemp(join(tmpdir(), 'dotcraft-page-assets-'))
    const assets: Array<Record<string, unknown>> = []
    const failures: Array<Record<string, unknown>> = []
    for (const asset of requested) {
      try {
        const downloaded = await this.writeBundledPageAsset(asset, directoryPath)
        assets.push(downloaded)
      } catch (error) {
        failures.push({
          contentType: null,
          id: asset.id,
          name: asset.name,
          reason: error instanceof Error ? error.message : String(error),
          url: asset.url
        })
      }
    }
    const manifestPath = join(directoryPath, 'manifest.json')
    const summary = {
      requestedCount: requested.length,
      downloadedCount: assets.length,
      failedCount: failures.length,
      elapsedMs: Date.now() - startedAt
    }
    await writeFile(manifestPath, JSON.stringify({
      inventoryId,
      pageUrl: inventory.pageUrl,
      assets,
      failures,
      summary
    }, null, 2), 'utf8')
    return { assets, directoryPath, failures, manifestPath, summary }
  }

  private async writeBundledPageAsset(asset: BrowserUsePageAsset, directoryPath: string): Promise<Record<string, unknown>> {
    const data = await this.readPageAssetData(asset.url)
    const fileName = this.pageAssetFileName(asset, data.contentType)
    const path = join(directoryPath, fileName)
    await writeFile(path, data.buffer)
    return {
      contentType: data.contentType,
      id: asset.id,
      kind: asset.kind,
      name: fileName,
      path,
      url: asset.url
    }
  }

  private async readPageAssetData(url: string): Promise<{ buffer: Buffer; contentType: string | null }> {
    const parsed = new URL(url)
    if (parsed.protocol === 'data:') return this.readDataUrlAsset(url)
    if (parsed.protocol === 'file:') {
      return {
        buffer: await readFile(fileURLToPath(parsed)),
        contentType: null
      }
    }
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      throw new Error(`Unsupported asset URL scheme: ${parsed.protocol}`)
    }
    const response = await fetch(url)
    if (!response.ok) throw new Error(`HTTP ${response.status} while downloading asset.`)
    return {
      buffer: Buffer.from(await response.arrayBuffer()),
      contentType: response.headers.get('content-type')
    }
  }

  private readDataUrlAsset(url: string): { buffer: Buffer; contentType: string | null } {
    const match = /^data:([^;,]+)?(;base64)?,([\s\S]*)$/i.exec(url)
    if (!match) throw new Error('Invalid data URL asset.')
    const contentType = match[1] || null
    const body = match[3] ?? ''
    const buffer = match[2]
      ? Buffer.from(body, 'base64')
      : Buffer.from(decodeURIComponent(body), 'utf8')
    return { buffer, contentType }
  }

  private pageAssetFileName(asset: BrowserUsePageAsset, contentType: string | null): string {
    const base = this.sanitizeAssetFileBase(asset.name || asset.id)
    const extension = extname(base) ? '' : this.extensionForAsset(asset.kind, contentType, asset.url)
    return `${asset.id}-${base}${extension}`
  }

  private sanitizeAssetFileBase(value: string): string {
    const cleaned = value
      .replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-')
      .replace(/\s+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 80)
    return cleaned || 'asset'
  }

  private extensionForAsset(kind: BrowserUsePageAssetKind, contentType: string | null, url: string): string {
    try {
      const extension = extname(new URL(url).pathname)
      if (extension) return extension
    } catch {
      // Fall through to content-type or kind-based defaults.
    }
    const type = (contentType ?? '').split(';')[0]?.trim().toLowerCase()
    if (type === 'text/css') return '.css'
    if (type === 'image/png') return '.png'
    if (type === 'image/jpeg') return '.jpg'
    if (type === 'image/svg+xml') return '.svg'
    if (type === 'font/woff') return '.woff'
    if (type === 'font/woff2') return '.woff2'
    if (kind === 'stylesheet') return '.css'
    if (kind === 'font') return '.woff2'
    if (kind === 'video') return '.mp4'
    return ''
  }

  private createCuaApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      move: async (options: { x: number; y: number; waitForArrival?: boolean }) => this.cuaMove(tab, options),
      click: async (options: { x: number; y: number; button?: number | string }) => this.cuaClick(tab, options),
      double_click: async (options: { x: number; y: number; button?: number | string }) => this.cuaDoubleClick(tab, options),
      drag: async (options: { path: Array<{ x: number; y: number }> }) => this.cuaDrag(tab, options),
      scroll: async (options: { x: number; y: number; scrollX?: number; scrollY?: number; deltaX?: number; deltaY?: number }) => this.cuaScroll(tab, this.normalizeScrollOptions(options)),
      type: async (options: { text: string } | string) => this.cuaType(tab, this.normalizeTypeOptions(options)),
      keypress: async (options: { keys: string[] } | string | string[]) => this.cuaKeypress(tab, this.normalizeKeypressOptions(options)),
      get_visible_screenshot: async () => this.screenshot(tab),
      download_media: async () => this.unsupported('tab.cua.download_media()'),
      describeApi: () => ['move({ x, y })', 'click({ x, y })', 'double_click({ x, y })', 'drag({ path })', 'scroll({ x, y, scrollX?, scrollY?, deltaX?, deltaY? })', 'type(textOrOptions)', 'keypress(keyOrOptions)', 'get_visible_screenshot()', 'download_media() unsupported']
    }
  }

  private createDomCuaApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      get_visible_dom: async () => this.domCuaVisibleDom(tab),
      click: async (options: { node_id?: string }) => this.domCuaClick(tab, options, false),
      double_click: async (options: { node_id?: string }) => this.domCuaClick(tab, options, true),
      type: async (options: { node_id?: string; text?: string } | string) => this.domCuaType(tab, options),
      keypress: async (options: { node_id?: string; key?: string; keys?: string[] } | string | string[]) => this.domCuaKeypress(tab, options),
      scroll: async (options: { node_id?: string; x?: number; y?: number; scrollX?: number; scrollY?: number; deltaX?: number; deltaY?: number }) => this.domCuaScroll(tab, options),
      download_media: async () => this.unsupported('tab.dom_cua.download_media()'),
      describeApi: () => ['get_visible_dom()', 'click({ node_id })', 'double_click({ node_id })', 'type({ node_id?, text })', 'keypress({ node_id?, key|keys })', 'scroll({ node_id?, deltaX?, deltaY? })', 'download_media() unsupported']
    }
  }

  private normalizeScrollOptions(options: {
    x?: number
    y?: number
    scrollX?: number
    scrollY?: number
    deltaX?: number
    deltaY?: number
  } = {}): { x: number; y: number; scrollX: number; scrollY: number } {
    return {
      x: Number(options.x ?? 0),
      y: Number(options.y ?? 0),
      scrollX: Number(options.scrollX ?? options.deltaX ?? 0),
      scrollY: Number(options.scrollY ?? options.deltaY ?? 0)
    }
  }

  private normalizeTypeOptions(options: { text?: string } | string): { text: string } {
    return typeof options === 'string'
      ? { text: options }
      : { text: String(options?.text ?? '') }
  }

  private normalizeKeypressOptions(options: { key?: string; keys?: string[] } | string | string[]): { keys: string[] } {
    if (typeof options === 'string') return { keys: [options] }
    if (Array.isArray(options)) return { keys: options.map(String) }
    const keys = Array.isArray(options?.keys)
      ? options.keys.map(String)
      : options?.key == null ? [] : [String(options.key)]
    return { keys }
  }

  private async domCuaVisibleDom(tab: BrowserUseTabRuntime): Promise<Array<Record<string, unknown>>> {
    const snapshot = JSON.parse(await this.domSnapshot(tab)) as { elements?: BrowserUseElementMatch[] }
    tab.domCuaNodes.clear()
    return (snapshot.elements ?? []).map((element, index) => {
      const nodeId = element.ref ?? `dom:${index}`
      tab.domCuaNodes.set(nodeId, element)
      return {
        node_id: nodeId,
        ref: element.ref,
        tagName: element.tagName || element.tag,
        role: element.role,
        name: element.name || element.ariaName,
        text: element.visibleText || element.text,
        selector: element.selector,
        href: element.href,
        testId: element.testId,
        visible: element.visible,
        enabled: element.enabled,
        boundingBox: element.boundingBox
      }
    })
  }

  private domCuaTarget(tab: BrowserUseTabRuntime, options: { node_id?: string } = {}): BrowserUseElementMatch {
    const nodeId = String(options.node_id ?? '')
    if (!nodeId) throw new Error('DOM CUA action requires node_id from get_visible_dom().')
    const current = tab.snapshotRefs.get(nodeId)
    if (current) return current
    const cached = tab.domCuaNodes.get(nodeId)
    if (cached) return cached
    const domIndex = /^dom:(\d+)$/.exec(nodeId)
    if (domIndex) {
      const index = Number(domIndex[1])
      const match = [...tab.snapshotRefs.values()].find((item) => item.index === index)
      if (match) return match
    }
    throw new Error(`DOM CUA node is no longer available: ${nodeId}. Take a fresh get_visible_dom() snapshot.`)
  }

  private async domCuaClick(
    tab: BrowserUseTabRuntime,
    options: { node_id?: string },
    doubleClick: boolean
  ): Promise<void> {
    const target = this.domCuaTarget(tab, options)
    const point = this.actionPoint(target)
    if (doubleClick) await this.cuaDoubleClick(tab, point)
    else await this.cuaClick(tab, { ...point, preserveRefs: true })
  }

  private async domCuaType(
    tab: BrowserUseTabRuntime,
    options: { node_id?: string; text?: string } | string
  ): Promise<void> {
    if (typeof options === 'string') {
      await this.cuaType(tab, { text: options })
      return
    }
    if (options.node_id) {
      const point = this.actionPoint(this.domCuaTarget(tab, options))
      await this.cuaClick(tab, { ...point, preserveRefs: true })
    }
    await this.cuaType(tab, { text: String(options.text ?? '') })
  }

  private async domCuaKeypress(
    tab: BrowserUseTabRuntime,
    options: { node_id?: string; key?: string; keys?: string[] } | string | string[]
  ): Promise<void> {
    if (options && typeof options === 'object' && !Array.isArray(options) && options.node_id) {
      const point = this.actionPoint(this.domCuaTarget(tab, options))
      await this.cuaClick(tab, { ...point, preserveRefs: true })
    }
    await this.cuaKeypress(tab, this.normalizeKeypressOptions(options))
  }

  private async domCuaScroll(
    tab: BrowserUseTabRuntime,
    options: { node_id?: string; x?: number; y?: number; scrollX?: number; scrollY?: number; deltaX?: number; deltaY?: number } = {}
  ): Promise<void> {
    const scroll = this.normalizeScrollOptions(options)
    if (options.node_id) {
      const target = this.domCuaTarget(tab, options)
      const point = this.actionPoint(target)
      await this.cuaScroll(tab, { ...scroll, ...point })
      return
    }
    await this.cuaScroll(tab, scroll)
  }

  private createPlaywrightApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      domSnapshot: async () => this.domSnapshot(tab),
      screenshot: async (options?: { fullPage?: boolean; clip?: Electron.Rectangle }) => this.screenshot(tab, options),
      waitForLoadState: async (stateOrOptions?: string | { state?: string; timeoutMs?: number }, timeoutMs?: number) => {
        const state = typeof stateOrOptions === 'string' ? stateOrOptions : stateOrOptions?.state
        const timeout = typeof stateOrOptions === 'object' ? stateOrOptions.timeoutMs : timeoutMs
        return this.waitForLoad(tab, state ?? 'load', timeout ?? 30_000)
      },
      waitForTimeout: async (timeoutMs: number) => new Promise((resolve) => {
        setTimeout(resolve, Math.max(0, Math.min(timeoutMs, 120_000)))
      }),
      waitForURL: async (url: unknown, options?: { timeoutMs?: number; timeout?: number }) => {
        const timeout = options?.timeoutMs ?? options?.timeout ?? 30_000
        return this.waitForUrl(tab, url, timeout)
      },
      waitForEvent: async (event: string) => this.unsupported(`tab.playwright.waitForEvent("${event}")`),
      expectNavigation: async <T>(action: () => Promise<T>, options?: { timeoutMs?: number; url?: string }) => {
        const result = await action()
        if (options?.url) {
          await this.waitForUrl(tab, options.url, options.timeoutMs ?? 30_000)
        } else {
          await this.waitForLoad(tab, 'load', options?.timeoutMs ?? 30_000)
        }
        return result
      },
      clickRef: async (ref: string) => this.locatorClick(tab, { kind: 'ref', value: String(ref) }),
      fillRef: async (ref: string, value: string) => this.locatorFill(tab, { kind: 'ref', value: String(ref) }, value),
      pressRef: async (ref: string, key: string) => this.locatorPress(tab, { kind: 'ref', value: String(ref) }, key),
      locator: (selector: string) => this.createLocatorApi(tab, { kind: 'css', value: String(selector) }),
      getByTestId: (testId: string) => this.createLocatorApi(tab, { kind: 'testId', value: String(testId) }),
      getByText: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'text',
        value: String(text),
        exact: options?.exact === true
      }),
      getByLabel: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'label',
        value: String(text),
        exact: options?.exact === true
      }),
      getByPlaceholder: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'placeholder',
        value: String(text),
        exact: options?.exact === true
      }),
      getByRole: (role: string, options?: { exact?: boolean; name?: string }) => this.createLocatorApi(tab, {
        kind: 'role',
        value: String(role),
        exact: options?.exact === true,
        name: options?.name == null ? undefined : String(options.name)
      }),
      frameLocator: () => {
        throw new Error('Browser frameLocator is not supported in this Desktop runtime yet.')
      },
      describeApi: () => ['domSnapshot()', 'screenshot(options?)', 'waitForLoadState(stateOrOptions?, timeoutMs?)', 'waitForURL(url, options?)', 'waitForTimeout(ms)', 'expectNavigation(action, options?)', 'locator(selector)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'waitForEvent(event) unsupported', 'frameLocator(selector) unsupported']
    }
  }

  private createLocatorApi(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Record<string, unknown> {
    return {
      count: async () => (await this.resolveLocator(tab, descriptor)).length,
      all: async () => {
        const matches = await this.resolveLocator(tab, descriptor)
        return matches.map((match) => this.createLocatorApi(tab, { ...descriptor, index: match.index }))
      },
      click: async (_options?: unknown) => this.locatorClick(tab, descriptor),
      dblclick: async (_options?: unknown) => this.locatorDoubleClick(tab, descriptor),
      fill: async (value: string, _options?: unknown) => this.locatorFill(tab, descriptor, value),
      type: async (value: string, _options?: unknown) => this.locatorType(tab, descriptor, value),
      press: async (value: string, _options?: unknown) => this.locatorPress(tab, descriptor, value),
      allTextContents: async (_options?: unknown) => (await this.resolveLocator(tab, descriptor)).map((match) => match.text || match.visibleText),
      check: async (_options?: unknown) => this.locatorSetChecked(tab, descriptor, true),
      uncheck: async (_options?: unknown) => this.locatorSetChecked(tab, descriptor, false),
      setChecked: async (checked: boolean, _options?: unknown) => this.locatorSetChecked(tab, descriptor, checked === true),
      selectOption: async (value: unknown, _options?: unknown) => this.locatorSelectOption(tab, descriptor, value),
      innerText: async (_options?: unknown) => (await this.strictLocator(tab, descriptor)).visibleText,
      textContent: async (_options?: unknown) => this.locatorEvaluate(tab, descriptor, 'textContent'),
      getAttribute: async (name: string, _options?: unknown) => this.locatorEvaluate(tab, descriptor, 'getAttribute', name),
      isVisible: async () => (await this.resolveLocator(tab, descriptor)).some((match) => match.visible),
      isEnabled: async () => this.locatorEvaluate(tab, descriptor, 'isEnabled'),
      waitFor: async (options?: { state?: string; timeoutMs?: number }) => this.locatorWaitFor(tab, descriptor, options),
      getByText: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        ...this.scopedLocatorDescriptor(tab, descriptor, {
          kind: 'text',
          value: String(text),
          exact: options?.exact === true
        })
      }),
      getByRole: (role: string, options?: { exact?: boolean; name?: string }) => this.createLocatorApi(tab, {
        ...this.scopedLocatorDescriptor(tab, descriptor, {
          kind: 'role',
          value: String(role),
          exact: options?.exact === true,
          name: options?.name == null ? undefined : String(options.name)
        })
      }),
      getByLabel: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, this.scopedLocatorDescriptor(tab, descriptor, {
        kind: 'label',
        value: String(text),
        exact: options?.exact === true
      })),
      getByPlaceholder: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, this.scopedLocatorDescriptor(tab, descriptor, {
        kind: 'placeholder',
        value: String(text),
        exact: options?.exact === true
      })),
      getByTestId: (testId: string) => this.createLocatorApi(tab, this.scopedLocatorDescriptor(tab, descriptor, { kind: 'testId', value: String(testId) })),
      locator: (selector: string) => this.createLocatorApi(tab, this.scopedLocatorDescriptor(tab, descriptor, { kind: 'css', value: String(selector) })),
      first: () => this.createLocatorApi(tab, { ...descriptor, index: 0 }),
      last: () => this.createLocatorApi(tab, { ...descriptor, index: -1 }),
      nth: (index: number) => this.createLocatorApi(tab, { ...descriptor, index: Math.trunc(Number(index)) }),
      describeApi: () => ['count()', 'all()', 'click(options?)', 'dblclick(options?)', 'fill(value, options?)', 'type(value, options?)', 'press(key, options?)', 'innerText(options?)', 'textContent(options?)', 'getAttribute(name, options?)', 'isVisible()', 'isEnabled()', 'waitFor({ state, timeoutMs })', 'allTextContents(options?)', 'check(options?)', 'uncheck(options?)', 'setChecked(checked, options?)', 'selectOption(value, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'locator(selector)', 'first()', 'last()', 'nth(index)']
    }
  }

  private scopedLocatorDescriptor(
    tab: BrowserUseTabRuntime,
    parent: BrowserUseLocatorDescriptor,
    child: BrowserUseLocatorDescriptor
  ): BrowserUseLocatorDescriptor {
    return {
      kind: 'css',
      value: `${this.selectorForLocatorDescriptor(tab, parent)} >> ${this.playwrightSelectorFor(child)}`
    }
  }

  private selectorForLocatorDescriptor(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): string {
    if (descriptor.kind !== 'ref') return this.playwrightSelectorFor(descriptor)
    const snapshotRef = this.snapshotRef(tab, descriptor.value)
    return snapshotRef.selector || this.fallbackSelectorForSnapshotRef(tab, snapshotRef)
  }

  private tabSnapshot(tab: BrowserUseTabRuntime): Record<string, unknown> {
    const snapshot = this.viewerHost.snapshotState(tab.owner, tab.id)
    if (snapshot) {
      return {
        id: tab.id,
        url: snapshot.currentUrl,
        title: snapshot.title,
        loading: snapshot.loading
      }
    }
    const wc = this.webContentsFor(tab.owner, tab.id)
    return {
      id: tab.id,
      url: wc.getURL(),
      title: wc.getTitle(),
      loading: wc.isLoading()
    }
  }

  private setAutomationState(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    active: boolean,
    action?: string
  ): void {
    this.viewerHost.setAutomationState(tab.owner, {
      tabId: tab.id,
      active,
      sessionName: runtime.sessionName,
      action
    })
  }

  private markAutomation(tab: BrowserUseTabRuntime, action: string): void {
    const runtime = this.getRuntimeForTab(tab)
    this.setAutomationState(runtime, tab, true, action)
  }

  private async goBack(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'back')
    this.invalidateSnapshotRefs(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoBack()) wc.navigationHistory.goBack()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async goForward(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'forward')
    this.invalidateSnapshotRefs(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoForward()) wc.navigationHistory.goForward()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private async reload(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'reload')
    this.invalidateSnapshotRefs(tab)
    this.webContentsFor(tab.owner, tab.id).reload()
    await this.waitForLoad(tab, 'load', 30_000).catch(() => {})
    return this.tabSnapshot(tab)
  }

  private closeTab(tab: BrowserUseTabRuntime): void {
    this.markAutomation(tab, 'close')
    const runtime = this.getRuntimeForTab(tab)
    this.detachDebugger(tab)
    this.viewerHost.destroyTab(tab.owner, tab.id)
    runtime.tabs.delete(tab.id)
    if (runtime.selectedTabId === tab.id) runtime.selectedTabId = null
  }

  private async navigate(
    tab: BrowserUseTabRuntime,
    url: string,
    options: { skipPolicyCheck?: boolean } = {}
  ): Promise<Record<string, unknown>> {
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw new Error(`Invalid browser URL: ${url}`)
    this.markAutomation(tab, 'navigate')
    this.invalidateSnapshotRefs(tab)
    if (options.skipPolicyCheck !== true) {
      const runtime = this.getRuntimeForTab(tab)
      await this.ensureNavigationAllowed(tab.owner, runtime, tab.id, normalized)
    }
    await this.loadAutomationUrl(tab, normalized)
    return this.tabSnapshot(tab)
  }

  private invalidateSnapshotRefs(tab: BrowserUseTabRuntime): void {
    tab.snapshotRefs.clear()
    tab.snapshotGeneration += 1
  }

  private getRuntimeForTab(tab: BrowserUseTabRuntime): BrowserUseThreadRuntime {
    for (const runtime of this.runtimes.values()) {
      if (runtime.tabs.get(tab.id) === tab) return runtime
    }
    throw new Error(`Browser tab is no longer attached to a runtime: ${tab.id}`)
  }

  private async ensureNavigationAllowed(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    tabId: string,
    url: string
  ): Promise<void> {
    const settings = this.policyHost?.getSettings().browserUse
    const decision = resolveBrowserUseNavigationDecision(url, settings)
    if (decision.kind === 'allow') return
    if (decision.kind === 'block') throw new Error(decision.reason)

    const action = await this.requestApproval(owner, {
      requestId: `browser-approval-${this.nextApprovalId++}`,
      threadId: runtime.threadId,
      tabId,
      url,
      domain: decision.domain,
      sessionName: runtime.sessionName
    })

    if (action === 'allowOnce') return
    if (action === 'allowDomain') {
      await this.addDomainToBrowserUseSettings(decision.domain, 'allowedDomains')
      return
    }
    if (action === 'blockDomain') {
      await this.addDomainToBrowserUseSettings(decision.domain, 'blockedDomains')
      throw new Error(`Blocked browser domain: ${decision.domain}`)
    }
    throw new Error(`Browser navigation denied for domain: ${decision.domain}`)
  }

  private requestApproval(
    owner: BrowserWindow,
    payload: BrowserUseApprovalRequestPayload
  ): Promise<BrowserUseApprovalResponseAction> {
    if (owner.isDestroyed() || owner.webContents.isDestroyed()) {
      return Promise.resolve('deny')
    }
    return new Promise((resolve) => {
      const onClosed = () => {
        this.pendingApprovals.delete(payload.requestId)
        clearTimeout(timer)
        resolve('deny')
      }
      const timer = setTimeout(() => {
        owner.off('closed', onClosed)
        this.pendingApprovals.delete(payload.requestId)
        resolve('deny')
      }, BROWSER_USE_APPROVAL_TIMEOUT_MS)
      this.pendingApprovals.set(payload.requestId, { resolve, timer, onClosed, owner })
      owner.once('closed', onClosed)
      owner.webContents.send(BROWSER_USE_APPROVAL_REQUEST_CHANNEL, payload)
    })
  }

  private async addDomainToBrowserUseSettings(
    domain: string,
    listName: 'allowedDomains' | 'blockedDomains'
  ): Promise<void> {
    if (!this.policyHost) return
    const current = this.policyHost.getSettings().browserUse ?? {}
    const allowedDomains = normalizeBrowserUseDomainList(current.allowedDomains)
    const blockedDomains = normalizeBrowserUseDomainList(current.blockedDomains)
    if (listName === 'allowedDomains') {
      await this.policyHost.updateSettings({
        browserUse: {
          ...current,
          allowedDomains: Array.from(new Set([...allowedDomains, domain])),
          blockedDomains: blockedDomains.filter((item) => item !== domain)
        }
      })
      return
    }
    await this.policyHost.updateSettings({
      browserUse: {
        ...current,
        blockedDomains: Array.from(new Set([...blockedDomains, domain])),
        allowedDomains: allowedDomains.filter((item) => item !== domain)
      }
    })
  }

  private async screenshot(
    tab: BrowserUseTabRuntime,
    options?: { fullPage?: boolean; clip?: Electron.Rectangle }
  ): Promise<BrowserUseImageResult> {
    this.markAutomation(tab, 'screenshot')
    await this.waitForPageReady(tab, {
      operation: 'screenshot.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    if (options?.fullPage) {
      const dataBase64 = await this.captureFullPageScreenshot(tab, options.clip)
      return {
        mediaType: 'image/png',
        dataBase64
      }
    }
    const image = await this.withBrowserOperation(
      tab,
      'screenshot',
      () => this.webContentsFor(tab.owner, tab.id).capturePage(options?.clip))
    return {
      mediaType: 'image/png',
      dataBase64: image.toPNG().toString('base64')
    }
  }

  private async captureFullPageScreenshot(tab: BrowserUseTabRuntime, clip?: Electron.Rectangle): Promise<string> {
    const runtime = this.getRuntimeForTab(tab)
    const metrics = await this.withBrowserOperation(
      tab,
      'screenshot.metrics',
      () => this.cdpCommand<{
        contentSize?: { x?: number; y?: number; width?: number; height?: number }
      }>(tab, 'Page.getLayoutMetrics'))
    const contentSize = metrics.contentSize ?? {}
    const sourceClip = clip ?? {
      x: contentSize.x ?? 0,
      y: contentSize.y ?? 0,
      width: contentSize.width ?? runtime.viewportWidth,
      height: contentSize.height ?? runtime.viewportHeight
    }
    const normalizedClip = {
      x: Math.max(0, Number(sourceClip.x) || 0),
      y: Math.max(0, Number(sourceClip.y) || 0),
      width: Math.max(1, Number(sourceClip.width) || runtime.viewportWidth),
      height: Math.max(1, Number(sourceClip.height) || runtime.viewportHeight),
      scale: 1
    }
    const result = await this.withBrowserOperation(
      tab,
      'screenshot.fullPage',
      () => this.cdpCommand<{ data?: string }>(tab, 'Page.captureScreenshot', {
        format: 'png',
        fromSurface: true,
        captureBeyondViewport: true,
        clip: normalizedClip
      }))
    if (!result.data) throw new Error(`Browser tab ${tab.id} did not return screenshot data.`)
    return result.data
  }

  private async domSnapshot(tab: BrowserUseTabRuntime): Promise<string> {
    await this.waitForPageReady(tab, {
      operation: 'domSnapshot.ready',
      requireContent: true,
      timeoutMs: this.operationTimeoutMs()
    })
    await this.ensurePlaywrightInjected(tab)
    const rawSnapshot = await this.executeJavaScript<unknown>(
      tab,
      'window.__dotcraftBrowserUseSnapshot()',
      'domSnapshot')
    const snapshot = this.normalizeSnapshotPayload(rawSnapshot)
    const elements = this.assignSnapshotRefs(tab, snapshot.elements)
    const accessibilitySnapshot = this.formatAccessibilitySnapshot(elements)
    return JSON.stringify({
      ...snapshot,
      accessibilitySnapshot,
      elements
    }, null, 2)
  }

  private normalizeSnapshotPayload(rawSnapshot: unknown): {
    title: string
    url: string
    bodyText: string
    elements: BrowserUseElementMatch[]
  } {
    const parsed = typeof rawSnapshot === 'string'
      ? this.tryParseJson(rawSnapshot) ?? {}
      : rawSnapshot
    const obj = parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : {}
    const elements = Array.isArray(obj.elements)
      ? obj.elements.map((item, index) => this.normalizeElementMatch(item, index))
      : []
    return {
      title: typeof obj.title === 'string' ? obj.title : '',
      url: typeof obj.url === 'string' ? obj.url : '',
      bodyText: typeof obj.bodyText === 'string' ? obj.bodyText : '',
      elements
    }
  }

  private tryParseJson(value: string): unknown | null {
    try {
      return JSON.parse(value)
    } catch {
      return null
    }
  }

  private normalizeElementMatch(value: unknown, index: number): BrowserUseElementMatch {
    if (!value || typeof value !== 'object') {
      const text = String(value ?? '')
      return {
        index,
        tagName: '',
        tag: '',
        role: '',
        name: text,
        text,
        selector: '',
        visible: true,
        enabled: true,
        visibleText: text,
        ariaName: text,
        boundingBox: null
      }
    }
    const obj = value as Record<string, unknown>
    const boundingBox = obj.boundingBox && typeof obj.boundingBox === 'object'
      ? obj.boundingBox as BrowserUseElementMatch['boundingBox']
      : null
    const tagName = this.stringValue(obj.tagName ?? obj.tag)
    const text = this.stringValue(obj.text ?? obj.visibleText)
    const name = this.stringValue(obj.name ?? obj.ariaName)
    return {
      ref: typeof obj.ref === 'string' ? obj.ref : undefined,
      index: typeof obj.index === 'number' ? obj.index : index,
      tagName,
      tag: this.stringValue(obj.tag ?? tagName),
      role: this.stringValue(obj.role),
      name,
      text,
      href: typeof obj.href === 'string' ? obj.href : undefined,
      testId: typeof obj.testId === 'string' ? obj.testId : undefined,
      selector: this.stringValue(obj.selector),
      visible: obj.visible !== false,
      enabled: obj.enabled !== false,
      visibleText: this.stringValue(obj.visibleText ?? text),
      ariaName: this.stringValue(obj.ariaName ?? name),
      boundingBox
    }
  }

  private stringValue(value: unknown): string {
    return typeof value === 'string' ? value : value == null ? '' : String(value)
  }

  private assignSnapshotRefs(
    tab: BrowserUseTabRuntime,
    elements: BrowserUseElementMatch[]
  ): BrowserUseElementMatch[] {
    tab.snapshotGeneration += 1
    tab.snapshotRefs.clear()
    return elements.map((element, index) => {
      const ref = `e${index + 1}`
      const withRef = {
        ...element,
        ref,
        index
      }
      tab.snapshotRefs.set(ref, withRef)
      return withRef
    })
  }

  private formatAccessibilitySnapshot(elements: BrowserUseElementMatch[]): string {
    return elements.map((element) => {
      const role = element.role || element.tagName || 'element'
      const label = this.escapeSnapshotText(element.name || element.text || element.visibleText || element.selector)
      const details = [
        `[ref=${element.ref ?? ''}]`,
        element.href ? `[href=${this.escapeSnapshotText(element.href)}]` : '',
        element.testId ? `[testId=${this.escapeSnapshotText(element.testId)}]` : '',
        element.selector ? `[selector=${this.escapeSnapshotText(element.selector)}]` : '',
        element.enabled ? '' : '[disabled]'
      ].filter(Boolean).join(' ')
      return `- ${role} "${label}" ${details}`.trim()
    }).join('\n')
  }

  private escapeSnapshotText(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').slice(0, 160)
  }

  private async ensurePlaywrightInjected(tab: BrowserUseTabRuntime): Promise<void> {
    const installed = await this.executeJavaScript<boolean>(
      tab,
      'Boolean(window.__dotcraftPlaywrightInjected && window.__dotcraftBrowserUseSnapshot && window.__dotcraftBrowserUseResolveSelector && window.__dotcraftBrowserUseElementInfo)',
      'playwright.inject.check').catch(() => false)
    if (installed === true) return

    await this.executeJavaScript(tab, `
      (() => {
        const module = { exports: {} };
        ${playwrightInjectedScriptSource}
        const injected = new (module.exports.InjectedScript())(globalThis, {
          isUnderTest: false,
          sdkLanguage: "javascript",
          testIdAttributeName: "data-testid",
          stableRafCount: 2,
          browserName: "chromium",
          isUtilityWorld: false,
          customEngines: []
        });
        const normalize = (value) => String(value ?? '').replace(/\\s+/g, ' ').trim();
        const cssEscape = (value) => window.CSS?.escape
          ? CSS.escape(String(value))
          : String(value).replace(/[^a-zA-Z0-9_-]/g, (ch) => '\\\\' + ch);
        const attrValue = (value) => String(value ?? '').replace(/\\\\/g, '\\\\\\\\').replace(/"/g, '\\\\"');
        const visible = (el) => {
          const style = window.getComputedStyle(el);
          const rect = el.getBoundingClientRect();
          return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
        };
        const enabled = (el) => !el.disabled && el.getAttribute('aria-disabled') !== 'true' && !el.closest('[aria-disabled="true"]');
        const roleOf = (el) => {
          const explicit = el.getAttribute('role');
          if (explicit) return normalize(explicit).split(' ')[0];
          const tag = el.tagName.toLowerCase();
          if (tag === 'a' && el.hasAttribute('href')) return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'input') {
            const type = (el.getAttribute('type') || 'text').toLowerCase();
            if (type === 'button' || type === 'submit' || type === 'reset') return 'button';
            if (type === 'checkbox') return 'checkbox';
            if (type === 'radio') return 'radio';
            if (type === 'search') return 'searchbox';
            return 'textbox';
          }
          if (tag === 'textarea') return 'textbox';
          if (tag === 'select') return 'combobox';
          if (tag === 'summary') return 'button';
          return '';
        };
        const textOf = (el) => normalize(
          el.innerText ||
          el.textContent ||
          el.getAttribute('aria-label') ||
          el.getAttribute('placeholder') ||
          el.getAttribute('value') ||
          ''
        );
        const nameOf = (el) => {
          return normalize(
            el.getAttribute('aria-label') ||
            el.getAttribute('aria-labelledby')?.split(/\\s+/).map((id) => document.getElementById(id)?.textContent || '').join(' ') ||
            el.getAttribute('title') ||
            el.getAttribute('alt') ||
            el.innerText ||
            el.textContent ||
            el.getAttribute('placeholder') ||
            el.getAttribute('value') ||
            ''
          );
        };
        const fallbackSelectorOf = (el) => {
          const tag = el.tagName.toLowerCase();
          if (el.id) return tag + '#' + cssEscape(el.id);
          const testId = el.getAttribute('data-testid');
          if (testId) return tag + '[data-testid="' + attrValue(testId) + '"]';
          const href = el.getAttribute('href');
          if (tag === 'a' && href) return 'a[href="' + attrValue(href) + '"]';
          const name = el.getAttribute('name');
          if (name) return tag + '[name="' + attrValue(name) + '"]';
          const aria = el.getAttribute('aria-label');
          if (aria) return tag + '[aria-label="' + attrValue(aria) + '"]';
          return tag;
        };
        const selectorOf = (el) => {
          try {
            return injected.generateSelectorSimple(el) || fallbackSelectorOf(el);
          } catch {
            return fallbackSelectorOf(el);
          }
        };
        const elementInfo = (el, index) => {
          const tagName = el.tagName.toLowerCase();
          const rect = el.getBoundingClientRect();
          const text = textOf(el);
          const name = nameOf(el);
          return {
            index,
            tagName,
            tag: tagName,
            role: roleOf(el),
            name,
            text,
            href: el.getAttribute('href') || undefined,
            testId: el.getAttribute('data-testid') || undefined,
            selector: selectorOf(el),
            visible: visible(el),
            enabled: enabled(el),
            visibleText: text,
            ariaName: name,
            boundingBox: rect ? { x: rect.left, y: rect.top, width: rect.width, height: rect.height } : null
          };
        };
        window.__dotcraftPlaywrightInjected = injected;
        window.__dotcraftBrowserUseElementInfo = elementInfo;
        window.__dotcraftBrowserUseResolveSelector = (parsed) => {
          const elements = injected.querySelectorAll(parsed, document);
          injected.checkDeprecatedSelectorUsage(parsed, elements);
          return elements.slice(0, 100).map(elementInfo);
        };
        window.__dotcraftBrowserUseSnapshot = () => {
          const interesting = [
            'a',
            'button',
            'input',
            'textarea',
            'select',
            'summary',
            '[role="button"]',
            '[role="link"]',
            '[role="menuitem"]',
            '[role="tab"]',
            '[contenteditable="true"]'
          ];
          const seen = new Set();
          const elements = Array.from(document.querySelectorAll(interesting.join(',')))
            .filter((el) => {
              if (!el || seen.has(el) || !visible(el)) return false;
              seen.add(el);
              return true;
            })
            .slice(0, 200)
            .map(elementInfo);
          const bodyText = (document.body?.innerText || '').trim().replace(/\\s+/g, ' ').slice(0, 4000);
          return { title: document.title, url: location.href, bodyText, elements };
        };
        return true;
      })()
    `, 'playwright.inject')
  }

  private async evaluateInPage(tab: BrowserUseTabRuntime, expressionOrFunction: string | (() => unknown)): Promise<unknown> {
    const source = typeof expressionOrFunction === 'function'
      ? `(${expressionOrFunction.toString()})()`
      : String(expressionOrFunction)
    await this.waitForPageReady(tab, {
      operation: 'evaluate.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    return this.executeJavaScript(tab, source, 'evaluate')
  }

  private async click(tab: BrowserUseTabRuntime, selector: string): Promise<void> {
    await this.locatorClick(tab, { kind: 'css', value: selector })
  }

  private async type(tab: BrowserUseTabRuntime, selector: string, text: string): Promise<void> {
    await this.locatorType(tab, { kind: 'css', value: selector }, text)
  }

  private async press(tab: BrowserUseTabRuntime, selector: string, key: string): Promise<void> {
    await this.locatorPress(tab, { kind: 'css', value: selector }, key)
  }

  private async cuaMove(tab: BrowserUseTabRuntime, options: { x: number; y: number; waitForArrival?: boolean }): Promise<void> {
    this.markAutomation(tab, 'move')
    await this.withBrowserOperation(tab, 'cua.move', () => this.viewerHost.moveMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      waitForArrival: options.waitForArrival
    }))
  }

  private async cuaClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string; preserveRefs?: boolean }): Promise<void> {
    this.markAutomation(tab, 'click')
    await this.withBrowserOperation(tab, 'cua.click', () => this.viewerHost.clickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    }))
    if (options.preserveRefs !== true) this.invalidateSnapshotRefs(tab)
  }

  private async cuaDoubleClick(tab: BrowserUseTabRuntime, options: { x: number; y: number; button?: number | string }): Promise<void> {
    this.markAutomation(tab, 'double click')
    await this.withBrowserOperation(tab, 'cua.double_click', () => this.viewerHost.doubleClickMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      button: this.normalizeMouseButton(options.button)
    }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaDrag(tab: BrowserUseTabRuntime, options: { path: Array<{ x: number; y: number }> }): Promise<void> {
    this.markAutomation(tab, 'drag')
    await this.withBrowserOperation(tab, 'cua.drag', () => this.viewerHost.dragMouse(tab.owner, {
      tabId: tab.id,
      path: Array.isArray(options.path) ? options.path : []
    }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaScroll(tab: BrowserUseTabRuntime, options: { x: number; y: number; scrollX: number; scrollY: number }): Promise<void> {
    this.markAutomation(tab, 'scroll')
    await this.withBrowserOperation(tab, 'cua.scroll', () => this.viewerHost.scrollMouse(tab.owner, {
      tabId: tab.id,
      x: Number(options.x),
      y: Number(options.y),
      scrollX: Number(options.scrollX ?? 0),
      scrollY: Number(options.scrollY ?? 0)
    }))
  }

  private async cuaType(tab: BrowserUseTabRuntime, options: { text: string }): Promise<void> {
    this.markAutomation(tab, 'type')
    await this.withBrowserOperation(
      tab,
      'cua.type',
      () => this.viewerHost.typeText(tab.owner, { tabId: tab.id, text: String(options.text ?? '') }))
    this.invalidateSnapshotRefs(tab)
  }

  private async cuaKeypress(tab: BrowserUseTabRuntime, options: { keys: string[] }): Promise<void> {
    this.markAutomation(tab, 'keypress')
    await this.withBrowserOperation(tab, 'cua.keypress', async () => {
      this.viewerHost.keypress(tab.owner, { tabId: tab.id, keys: Array.isArray(options.keys) ? options.keys.map(String) : [] })
    })
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorDoubleClick(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaDoubleClick(tab, { ...point })
  }

  private async locatorType(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.cuaType(tab, { text: String(value ?? '') })
  }

  private async locatorFill(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    if (descriptor.kind === 'ref') this.selectorForResolvedLocator(tab, descriptor, target)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.mutateStrictLocator(tab, descriptor, String(value ?? ''))
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorPress(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor, value: string): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const point = this.actionPoint(target)
    await this.cuaClick(tab, { ...point, preserveRefs: true })
    await this.cuaKeypress(tab, { keys: [String(value)] })
  }

  private async locatorSetChecked(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    checked: boolean
  ): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const locator = this.selectorForResolvedLocator(tab, descriptor, target)
    await this.ensurePlaywrightInjected(tab)
    const script = `
      ((parsed, snapshotRef, checked) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const matchesSnapshotRef = (info) => {
          if (!snapshotRef) return true;
          if (snapshotRef.href && info.href !== snapshotRef.href) return false;
          if (snapshotRef.testId && info.testId !== snapshotRef.testId) return false;
          if (snapshotRef.role && info.role !== snapshotRef.role) return false;
          if (snapshotRef.tagName && info.tagName !== snapshotRef.tagName && info.tag !== snapshotRef.tagName) return false;
          if (snapshotRef.expectedName) {
            const actualName = info.name || info.text || info.visibleText;
            if (actualName !== snapshotRef.expectedName) return false;
          }
          return true;
        };
        const candidates = injected.querySelectorAll(parsed, document).map((el, index) => ({
          el,
          info: window.__dotcraftBrowserUseElementInfo(el, index)
        })).filter((candidate) => matchesSnapshotRef(candidate.info));
        if (candidates.length !== 1) throw new Error('Locator resolved to ' + candidates.length + ' elements for checkbox state change.');
        const el = candidates[0].el;
        if (!('checked' in el)) throw new Error('Locator does not resolve to a checkable control.');
        if (el.checked !== checked) {
          el.focus();
          el.checked = checked;
          el.dispatchEvent(new InputEvent('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
        }
        return true;
      })(${JSON.stringify(locator.parsed)}, ${JSON.stringify(locator.snapshotRefFilter)}, ${JSON.stringify(checked)})
    `
    await this.executeJavaScript(tab, script, 'locator.setChecked')
    this.invalidateSnapshotRefs(tab)
  }

  private async locatorSelectOption(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    value: unknown
  ): Promise<void> {
    const target = await this.waitForActionableLocator(tab, descriptor)
    const locator = this.selectorForResolvedLocator(tab, descriptor, target)
    const values = (Array.isArray(value) ? value : [value]).map((item) => {
      if (item && typeof item === 'object') {
        const obj = item as Record<string, unknown>
        return {
          value: obj.value == null ? undefined : String(obj.value),
          label: obj.label == null ? undefined : String(obj.label),
          index: typeof obj.index === 'number' ? obj.index : undefined
        }
      }
      return { value: String(item ?? '') }
    })
    await this.ensurePlaywrightInjected(tab)
    const script = `
      ((parsed, snapshotRef, requested) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const matchesSnapshotRef = (info) => {
          if (!snapshotRef) return true;
          if (snapshotRef.href && info.href !== snapshotRef.href) return false;
          if (snapshotRef.testId && info.testId !== snapshotRef.testId) return false;
          if (snapshotRef.role && info.role !== snapshotRef.role) return false;
          if (snapshotRef.tagName && info.tagName !== snapshotRef.tagName && info.tag !== snapshotRef.tagName) return false;
          if (snapshotRef.expectedName) {
            const actualName = info.name || info.text || info.visibleText;
            if (actualName !== snapshotRef.expectedName) return false;
          }
          return true;
        };
        const candidates = injected.querySelectorAll(parsed, document).map((el, index) => ({
          el,
          info: window.__dotcraftBrowserUseElementInfo(el, index)
        })).filter((candidate) => matchesSnapshotRef(candidate.info));
        if (candidates.length !== 1) throw new Error('Locator resolved to ' + candidates.length + ' elements for selectOption.');
        const select = candidates[0].el;
        if (select.tagName?.toLowerCase() !== 'select') throw new Error('selectOption requires a native <select> element.');
        const options = Array.from(select.options);
        const selected = [];
        for (const item of requested) {
          const match = options.find((option, index) =>
            (item.index !== undefined && index === item.index) ||
            (item.value !== undefined && option.value === item.value) ||
            (item.label !== undefined && option.label === item.label)
          );
          if (!match) throw new Error('No matching <option> found for selectOption.');
          selected.push(match);
        }
        if (!select.multiple && selected.length > 1) throw new Error('Cannot select multiple options on a single-select element.');
        for (const option of options) option.selected = selected.includes(option);
        select.focus();
        select.dispatchEvent(new InputEvent('input', { bubbles: true }));
        select.dispatchEvent(new Event('change', { bubbles: true }));
        return true;
      })(${JSON.stringify(locator.parsed)}, ${JSON.stringify(locator.snapshotRefFilter)}, ${JSON.stringify(values)})
    `
    await this.executeJavaScript(tab, script, 'locator.selectOption')
    this.invalidateSnapshotRefs(tab)
  }

  private async waitForActionableLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch> {
    const deadline = Date.now() + this.operationTimeoutMs()
    let lastError: Error | null = null
    for (;;) {
      try {
        const target = await this.strictLocator(tab, descriptor)
        this.assertActionable(target, descriptor)
        return target
      } catch (error) {
        const current = error instanceof Error ? error : new Error(String(error))
        if (
          current.message.startsWith('Strict mode violation') ||
          current.message.startsWith('Unknown browser snapshot ref')
        ) {
          throw current
        }
        lastError = current
      }
      if (Date.now() >= deadline) {
        throw new Error(`Timed out waiting for locator ${this.describeLocator(descriptor)} to become visible and enabled. Last error: ${lastError?.message ?? 'unknown'}`)
      }
      await new Promise((resolve) => setTimeout(resolve, 100))
    }
  }

  private assertActionable(match: BrowserUseElementMatch, descriptor: BrowserUseLocatorDescriptor): void {
    if (!match.visible) {
      throw new Error(`Locator ${this.describeLocator(descriptor)} resolved to a hidden element: ${this.describeElementMatch(match)}`)
    }
    if (!match.enabled) {
      throw new Error(`Locator ${this.describeLocator(descriptor)} resolved to a disabled element: ${this.describeElementMatch(match)}`)
    }
  }

  private async strictLocator(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Promise<BrowserUseElementMatch> {
    const matches = await this.resolveLocator(tab, descriptor)
    if (matches.length === 0) {
      throw new Error(`No element found for locator: ${this.describeLocator(descriptor)}`)
    }
    if (matches.length > 1) {
      const examples = matches.slice(0, 5).map((match) => this.describeElementMatch(match)).join('; ')
      throw new Error(`Strict mode violation for locator ${this.describeLocator(descriptor)}: ${matches.length} elements matched. Matches: ${examples}`)
    }
    return matches[0]!
  }

  private describeElementMatch(match: BrowserUseElementMatch): string {
    const box = match.boundingBox
      ? `@${Math.round(match.boundingBox.x)},${Math.round(match.boundingBox.y)} ${Math.round(match.boundingBox.width)}x${Math.round(match.boundingBox.height)}`
      : '@no-box'
    const label = match.name || match.text || match.visibleText || match.ariaName || ''
    const href = match.href ? ` href=${match.href}` : ''
    const ref = match.ref ? ` ref=${match.ref}` : ''
    const state = `${match.visible ? 'visible' : 'hidden'}/${match.enabled ? 'enabled' : 'disabled'}`
    return `${match.tagName || match.tag || 'element'}[${match.role || 'generic'}] "${label}" ${match.selector}${href}${ref} ${state} ${box}`
  }

  private actionPoint(match: BrowserUseElementMatch): { x: number; y: number } {
    const box = match.boundingBox
    if (!box || box.width <= 0 || box.height <= 0) {
      throw new Error('Element does not have a clickable bounding box.')
    }
    return {
      x: Math.max(0, Math.round(box.x + box.width / 2)),
      y: Math.max(0, Math.round(box.y + box.height / 2))
    }
  }

  private async resolveLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch[]> {
    const snapshotRef = descriptor.kind === 'ref'
      ? this.snapshotRef(tab, descriptor.value)
      : null
    const selector = snapshotRef?.selector || (descriptor.kind !== 'ref' ? this.playwrightSelectorFor(descriptor) : '')
    if (!selector && snapshotRef) return [snapshotRef]
    await this.ensurePlaywrightInjected(tab)
    const parsed = parsePlaywrightSelector(selector)
    const matches = await this.executeJavaScript<BrowserUseElementMatch[]>(
      tab,
      `window.__dotcraftBrowserUseResolveSelector(${JSON.stringify(parsed)})`,
      'locator.resolve')
    const normalized = Array.isArray(matches)
      ? matches.map((match, index) => this.normalizeElementMatch(match, index))
      : []
    const filtered = snapshotRef
      ? normalized
      .filter((match) => this.matchesSnapshotRef(match, snapshotRef))
      .map((match) => ({ ...match, ref: snapshotRef.ref }))
      : normalized
    return this.applyLocatorIndex(filtered, descriptor)
  }

  private applyLocatorIndex(
    matches: BrowserUseElementMatch[],
    descriptor: BrowserUseLocatorDescriptor
  ): BrowserUseElementMatch[] {
    if (descriptor.index === undefined) return matches
    const index = descriptor.index < 0 ? matches.length + descriptor.index : descriptor.index
    const match = matches[index]
    return match ? [match] : []
  }

  private snapshotRef(tab: BrowserUseTabRuntime, ref: string): BrowserUseElementMatch {
    const snapshotRef = tab.snapshotRefs.get(ref)
    if (!snapshotRef) {
      throw new Error(`Unknown browser snapshot ref '${ref}' for tab ${tab.id}. Take a fresh domSnapshot() and use a current ref.`)
    }
    return snapshotRef
  }

  private matchesSnapshotRef(current: BrowserUseElementMatch, snapshotRef: BrowserUseElementMatch): boolean {
    if (snapshotRef.href && current.href !== snapshotRef.href) return false
    if (snapshotRef.testId && current.testId !== snapshotRef.testId) return false
    if (snapshotRef.role && current.role !== snapshotRef.role) return false
    const expectedName = snapshotRef.name || snapshotRef.text || snapshotRef.visibleText
    if (expectedName) {
      const actualName = current.name || current.text || current.visibleText
      if (actualName !== expectedName) return false
    }
    return true
  }

  private playwrightSelectorFor(descriptor: BrowserUseLocatorDescriptor): string {
    if (descriptor.kind === 'css') return descriptor.value
    if (descriptor.kind === 'text') return getByTextSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'label') return getByLabelSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'placeholder') return getByPlaceholderSelector(descriptor.value, { exact: descriptor.exact === true })
    if (descriptor.kind === 'testId') return getByTestIdSelector('data-testid', descriptor.value)
    if (descriptor.kind === 'role') {
      return getByRoleSelector(descriptor.value, {
        name: descriptor.name,
        exact: descriptor.exact === true
      })
    }
    throw new Error(`Unsupported browser locator: ${this.describeLocator(descriptor)}`)
  }

  private selectorForResolvedLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    target: BrowserUseElementMatch
  ): { parsed: unknown; snapshotRefFilter: BrowserUseSnapshotRefFilter | null } {
    const snapshotRef = descriptor.kind === 'ref' ? target : null
    const selector = target.selector || this.selectorForLocatorDescriptor(tab, snapshotRef ? { kind: 'ref', value: snapshotRef.ref ?? descriptor.value } : descriptor)
    return {
      parsed: parsePlaywrightSelector(selector),
      snapshotRefFilter: snapshotRef ? this.snapshotRefFilter(snapshotRef) : null
    }
  }

  private fallbackSelectorForSnapshotRef(tab: BrowserUseTabRuntime, snapshotRef: BrowserUseElementMatch): string {
    const tag = this.cssTagName(snapshotRef.tagName || snapshotRef.tag || '')
    if (snapshotRef.testId) {
      const prefix = tag || ''
      return `${prefix}[data-testid="${this.cssAttributeValue(snapshotRef.testId)}"]`
    }
    if (snapshotRef.href && (tag === 'a' || snapshotRef.role === 'link')) {
      return `${tag || 'a'}[href="${this.cssAttributeValue(snapshotRef.href)}"]`
    }
    const name = snapshotRef.name || snapshotRef.ariaName
    if (snapshotRef.role && name) {
      return getByRoleSelector(snapshotRef.role, { name, exact: true })
    }
    const text = snapshotRef.visibleText || snapshotRef.text || name
    if (text) {
      return getByTextSelector(text, { exact: true })
    }
    if (tag) return tag
    throw new Error(`Snapshot ref '${snapshotRef.ref ?? ''}' for tab ${tab.id} cannot be resolved to a live DOM selector. Take a fresh domSnapshot() and use a current ref or a stable selector.`)
  }

  private snapshotRefFilter(snapshotRef: BrowserUseElementMatch): BrowserUseSnapshotRefFilter {
    return {
      ref: snapshotRef.ref,
      href: snapshotRef.href,
      testId: snapshotRef.testId,
      role: snapshotRef.role || undefined,
      expectedName: snapshotRef.name || snapshotRef.text || snapshotRef.visibleText || undefined,
      tagName: snapshotRef.tagName || snapshotRef.tag || undefined
    }
  }

  private cssTagName(value: string): string {
    const tag = value.toLowerCase()
    return /^[a-z][a-z0-9-]*$/.test(tag) ? tag : ''
  }

  private cssAttributeValue(value: string): string {
    return String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"')
  }

  private async locatorEvaluate(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    operation: 'textContent' | 'getAttribute' | 'isEnabled',
    arg?: string
  ): Promise<unknown> {
    const target = await this.strictLocator(tab, descriptor)
    const locator = this.selectorForResolvedLocator(tab, descriptor, target)
    await this.ensurePlaywrightInjected(tab)
    const script = `
      ((parsed, snapshotRef, operation, arg) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const matchesSnapshotRef = (info) => {
          if (!snapshotRef) return true;
          if (snapshotRef.href && info.href !== snapshotRef.href) return false;
          if (snapshotRef.testId && info.testId !== snapshotRef.testId) return false;
          if (snapshotRef.role && info.role !== snapshotRef.role) return false;
          if (snapshotRef.tagName && info.tagName !== snapshotRef.tagName && info.tag !== snapshotRef.tagName) return false;
          if (snapshotRef.expectedName) {
            const actualName = info.name || info.text || info.visibleText;
            if (actualName !== snapshotRef.expectedName) return false;
          }
          return true;
        };
        let el = null;
        if (snapshotRef) {
          const candidates = injected.querySelectorAll(parsed, document);
          const matches = candidates.map((candidate, index) => ({
            el: candidate,
            info: window.__dotcraftBrowserUseElementInfo(candidate, index)
          })).filter((candidate) => matchesSnapshotRef(candidate.info));
          if (matches.length !== 1) {
            throw new Error('Snapshot ref ' + JSON.stringify(snapshotRef.ref || '') + ' resolved to ' + matches.length + ' live elements for locator evaluation. Take a fresh domSnapshot() or use a more stable selector. role=' + (snapshotRef.role || '') + ' name=' + (snapshotRef.expectedName || '') + ' href=' + (snapshotRef.href || '') + ' testId=' + (snapshotRef.testId || ''));
          }
          el = matches[0].el;
        } else {
          el = injected.querySelector(parsed, document, true);
        }
        if (!el) return null;
        if (operation === 'textContent') return el.textContent;
        if (operation === 'getAttribute') return el.getAttribute(arg);
        if (operation === 'isEnabled') return !el.disabled && el.getAttribute('aria-disabled') !== 'true' && !el.closest('[aria-disabled="true"]');
        return null;
      })(${JSON.stringify(locator.parsed)}, ${JSON.stringify(locator.snapshotRefFilter)}, ${JSON.stringify(operation)}, ${JSON.stringify(arg ?? '')})
    `
    return await this.executeJavaScript(tab, script, `locator.${operation}`)
  }

  private async mutateStrictLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    value: string
  ): Promise<void> {
    const target = await this.strictLocator(tab, descriptor)
    const locator = this.selectorForResolvedLocator(tab, descriptor, target)
    await this.ensurePlaywrightInjected(tab)
    const script = `
      ((parsed, snapshotRef, value) => {
        const injected = window.__dotcraftPlaywrightInjected;
        const matchesSnapshotRef = (info) => {
          if (!snapshotRef) return true;
          if (snapshotRef.href && info.href !== snapshotRef.href) return false;
          if (snapshotRef.testId && info.testId !== snapshotRef.testId) return false;
          if (snapshotRef.role && info.role !== snapshotRef.role) return false;
          if (snapshotRef.tagName && info.tagName !== snapshotRef.tagName && info.tag !== snapshotRef.tagName) return false;
          if (snapshotRef.expectedName) {
            const actualName = info.name || info.text || info.visibleText;
            if (actualName !== snapshotRef.expectedName) return false;
          }
          return true;
        };
        let el = null;
        if (snapshotRef) {
          const candidates = injected.querySelectorAll(parsed, document);
          const matches = candidates.map((candidate, index) => ({
            el: candidate,
            info: window.__dotcraftBrowserUseElementInfo(candidate, index)
          })).filter((candidate) => matchesSnapshotRef(candidate.info));
          if (matches.length !== 1) {
            throw new Error('Snapshot ref ' + JSON.stringify(snapshotRef.ref || '') + ' resolved to ' + matches.length + ' live elements for locator fill. Take a fresh domSnapshot() or use a more stable selector. role=' + (snapshotRef.role || '') + ' name=' + (snapshotRef.expectedName || '') + ' href=' + (snapshotRef.href || '') + ' testId=' + (snapshotRef.testId || ''));
          }
          el = matches[0].el;
        } else {
          el = injected.querySelector(parsed, document, true);
        }
        if (!el) throw new Error('Element is no longer available.');
        el.focus();
        if ('value' in el) {
          el.value = value;
          el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          return true;
        }
        el.textContent = value;
        el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: value }));
        return true;
      })(${JSON.stringify(locator.parsed)}, ${JSON.stringify(locator.snapshotRefFilter)}, ${JSON.stringify(value)})
    `
    await this.executeJavaScript(tab, script, 'locator.fill')
  }

  private async locatorWaitFor(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor,
    options?: { state?: string; timeoutMs?: number }
  ): Promise<void> {
    const expected = options?.state ?? 'visible'
    if (!['attached', 'visible', 'hidden', 'detached'].includes(expected)) {
      throw new Error(`Unsupported locator.waitFor state: ${expected}. Use attached, visible, hidden, or detached.`)
    }
    const deadline = Date.now() + Math.max(1_000, Math.min(options?.timeoutMs ?? 30_000, 120_000))
    let lastMatchCount = 0
    let lastVisibleCount = 0
    for (;;) {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) throw new Error(`Browser operation 'locator.waitFor' was cancelled for tab ${tab.id}.`)
      const matches = await this.resolveLocator(tab, descriptor)
      const visibleCount = matches.filter((m) => m.visible).length
      lastMatchCount = matches.length
      lastVisibleCount = visibleCount
      if (expected === 'attached' && matches.length > 0) return
      if (expected === 'visible' && visibleCount > 0) return
      if (expected === 'hidden' && visibleCount === 0) return
      if (expected === 'detached' && matches.length === 0) return
      if (Date.now() > deadline) {
        throw new Error(`Timed out waiting for locator ${this.describeLocator(descriptor)} to be ${expected}. matches=${lastMatchCount} visible=${lastVisibleCount}.`)
      }
      await new Promise((resolve) => setTimeout(resolve, 100))
    }
  }

  private waitForUrl(tab: BrowserUseTabRuntime, expectedUrl: unknown, timeoutMs: number): Promise<void> {
    return this.withBrowserOperation(
      tab,
      'waitForURL',
      () => this.waitForUrlInner(tab, expectedUrl, timeoutMs),
      timeoutMs)
  }

  private waitForUrlInner(tab: BrowserUseTabRuntime, expectedUrl: unknown, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const expectedDescription = this.describeExpectedUrl(expectedUrl)
    const matches = (url: string) => this.urlMatches(url, expectedUrl)
    if (matches(wc.getURL())) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForURL' was cancelled for tab ${tab.id}.`))
        return
      }
      const effectiveTimeoutMs = Math.max(1_000, Math.min(timeoutMs, 120_000))
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForURL' timed out after ${effectiveTimeoutMs}ms for tab ${tab.id}: ${expectedDescription}; current=${wc.getURL() || 'about:blank'}`))
      }, effectiveTimeoutMs)
      const done = () => {
        if (!matches(wc.getURL())) return
        cleanup()
        resolve()
      }
      const poll = setInterval(done, 100)
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForURL' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        clearInterval(poll)
        wc.off('did-navigate', done)
        wc.off('did-navigate-in-page', done)
        wc.off('did-stop-loading', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.on('did-navigate', done)
      wc.on('did-navigate-in-page', done)
      wc.on('did-stop-loading', done)
      done()
    })
  }

  private describeExpectedUrl(expectedUrl: unknown): string {
    if (expectedUrl instanceof RegExp) return expectedUrl.toString()
    if (typeof expectedUrl === 'string') return expectedUrl
    return String(expectedUrl)
  }

  private urlMatches(actualUrl: string, expectedUrl: unknown): boolean {
    if (expectedUrl instanceof RegExp) {
      expectedUrl.lastIndex = 0
      return expectedUrl.test(actualUrl)
    }
    if (typeof expectedUrl === 'string') return actualUrl === expectedUrl
    return actualUrl === String(expectedUrl)
  }

  private devLogs(tab: BrowserUseTabRuntime, options?: { filter?: string; levels?: string[]; limit?: number }): BrowserUseLogEntry[] {
    let entries = [...tab.logs]
    if (options?.filter) entries = entries.filter((entry) => entry.message.includes(options.filter!))
    if (options?.levels?.length) {
      const levels = new Set(options.levels.map((level) => level.toLowerCase()))
      entries = entries.filter((entry) => levels.has(entry.level.toLowerCase()))
    }
    const limit = Math.max(1, Math.min(options?.limit ?? entries.length, 500))
    return entries.slice(-limit)
  }

  private normalizeMouseButton(value: number | string | undefined): 'left' | 'right' | 'middle' {
    if (value === 2 || value === 'right') return 'right'
    if (value === 1 || value === 'middle') return 'middle'
    return 'left'
  }

  private describeLocator(descriptor: BrowserUseLocatorDescriptor): string {
    const index = descriptor.index === undefined ? '' : `[${descriptor.index}]`
    return `${descriptor.kind}=${descriptor.name ?? descriptor.value}${index}`
  }

  private normalizeLoadState(state: string): BrowserUseLoadState {
    const normalized = String(state ?? 'load').toLowerCase()
    if (normalized === 'commit' || normalized === 'domcontentloaded' || normalized === 'load' || normalized === 'networkidle') {
      return normalized
    }
    throw new Error(`Unsupported browser load state: ${state}`)
  }

  private async waitForLoad(
    tab: BrowserUseTabRuntime,
    state: string = 'load',
    timeoutMs: number = 30_000
  ): Promise<void> {
    const loadState = this.normalizeLoadState(state)
    const effectiveTimeoutMs = Math.max(1, Math.min(timeoutMs, 120_000))
    if (loadState === 'commit') {
      await this.waitForCommit(tab, effectiveTimeoutMs)
      return
    }
    if (loadState === 'domcontentloaded') {
      await this.waitForPageReady(tab, {
        operation: 'waitForLoadState.domcontentloaded',
        requireContent: false,
        timeoutMs: effectiveTimeoutMs
      })
      return
    }
    await this.waitForLoadEvent(tab, effectiveTimeoutMs)
    await this.waitForPageReady(tab, {
      operation: `waitForLoadState.${loadState}`,
      requireContent: loadState === 'networkidle',
      timeoutMs: effectiveTimeoutMs
    })
    if (loadState === 'networkidle') {
      await this.waitForNetworkIdle(tab, effectiveTimeoutMs)
    }
  }

  private waitForCommit(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.getURL()) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForLoadState.commit' was cancelled for tab ${tab.id}.`))
        return
      }
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState.commit' timed out after ${timeoutMs}ms for tab ${tab.id}.`))
      }, timeoutMs)
      const done = () => {
        cleanup()
        resolve()
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState.commit' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-start-loading', done)
        wc.off('did-navigate', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-start-loading', done)
      wc.once('did-navigate', done)
    })
  }

  private waitForLoadEvent(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (!wc.isLoading()) return Promise.resolve()
    return new Promise((resolve, reject) => {
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) {
        reject(new Error(`Browser operation 'waitForLoadState' was cancelled for tab ${tab.id}.`))
        return
      }
      const timeout = setTimeout(() => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState' timed out after ${Math.max(1_000, Math.min(timeoutMs, 120_000))}ms for tab ${tab.id}.`))
      }, Math.max(1_000, Math.min(timeoutMs, 120_000)))
      const done = () => {
        cleanup()
        resolve()
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
    })
  }

  private async waitForNetworkIdle(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const deadline = Date.now() + timeoutMs
    for (;;) {
      if (Date.now() >= deadline) {
        throw new Error(`Browser operation 'waitForLoadState.networkidle' timed out after ${timeoutMs}ms for tab ${tab.id} at ${wc.getURL() || 'about:blank'}.`)
      }
      await this.waitForLoadEvent(tab, Math.max(1, deadline - Date.now()))
      await new Promise<void>((resolve, reject) => {
        const signal = this.getRuntimeForTab(tab).activeAbortSignal
        if (signal?.aborted) {
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' was cancelled for tab ${tab.id}.`))
          return
        }
        let quietTimer: ReturnType<typeof setTimeout>
        const hardTimer = setTimeout(() => {
          cleanup()
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' timed out after ${timeoutMs}ms for tab ${tab.id} at ${wc.getURL() || 'about:blank'}.`))
        }, Math.max(1, deadline - Date.now()))
        const finish = () => {
          cleanup()
          resolve()
        }
        const restart = () => {
          clearTimeout(quietTimer)
          quietTimer = setTimeout(finish, BROWSER_USE_NETWORK_IDLE_QUIET_MS)
        }
        const onAbort = () => {
          cleanup()
          reject(new Error(`Browser operation 'waitForLoadState.networkidle' was cancelled for tab ${tab.id}.`))
        }
        const cleanup = () => {
          clearTimeout(quietTimer)
          clearTimeout(hardTimer)
          wc.off('did-start-loading', restart)
          wc.off('did-stop-loading', restart)
          signal?.removeEventListener('abort', onAbort)
        }
        signal?.addEventListener('abort', onAbort, { once: true })
        wc.on('did-start-loading', restart)
        wc.on('did-stop-loading', restart)
        restart()
      })
      if (!wc.isLoading()) return
    }
  }

}

export const browserUseManager = new BrowserUseManager()
export { BROWSER_USE_OPEN_CHANNEL }
