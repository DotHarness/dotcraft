import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { BrowserWindow } from 'electron'
import {
  BrowserUseBackendError,
  BrowserUseBackendServer,
  type BrowserUseBackendCommandContext,
  type BrowserUseBackendRequestHandler
} from './browserUseBackendServer'
import { viewerBrowserManager } from './viewerBrowser'
import { BrowserScreenshot, type BrowserScreenshotContext } from './browserScreenshot'
import { BrowserTabLifecycle, readBrowserTurnNotification } from './browserTabLifecycle'
import { browserObservationSource } from './browserUseObservation'
import { BrowserUseViewports, normalizeViewportSize, type ViewportSize } from './browserUseViewport'
import type { AppSettings } from './settings'
import {
  isBrowserUseUrlAllowed as isBrowserUseUrlAllowedByPolicy,
  normalizeBrowserUseDomainList,
  resolveBrowserUseNavigationDecision
} from './browserUsePolicy'
import type {
  BrowserUseApprovalRequestPayload,
  BrowserUseApprovalResponseAction,
  BrowserUseApprovalResponsePayload,
  BrowserUseClosePayload,
  BrowserUseOpenPayload
} from '../shared/viewer/types'

const require = createRequire(import.meta.url)
const playwrightCoreRoot = dirname(require.resolve('playwright-core/package.json'))
const { source: playwrightInjectedScriptSource } = require(join(playwrightCoreRoot, 'lib/generated/injectedScriptSource.js')) as { source: string }

const BROWSER_USE_OPEN_CHANNEL = 'viewer:browser:open'
const BROWSER_USE_CLOSE_CHANNEL = 'viewer:browser:close'
const BROWSER_USE_APPROVAL_REQUEST_CHANNEL = 'viewer:browser:approval-request'
const BROWSER_USE_APPROVAL_TIMEOUT_MS = 120_000
const BROWSER_USE_OPERATION_TIMEOUT_MS = 10_000
const BROWSER_USE_NAVIGATION_TIMEOUT_MS = 30_000
const BROWSER_USE_BLANK_TAB_READY_TIMEOUT_MS = 5_000
const BROWSER_USE_NETWORK_IDLE_QUIET_MS = 500
const BROWSER_USE_INPUT_METHODS = new Set(['Input.dispatchMouseEvent', 'Input.dispatchKeyEvent', 'Input.insertText'])
const BROWSER_USE_MAX_RESULT_BYTES = 1024 * 1024
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

type BrowserUseLoadState = 'commit' | 'domcontentloaded' | 'load' | 'networkidle'

export interface BrowserUseImageResult {
  mediaType: string
  dataBase64: string
}

interface BrowserUseViewerHost {
  setCaptureSurface(win: BrowserWindow, tabId: string, size: { width: number; height: number } | null): void
  createAutomationTab(win: BrowserWindow, params: {
    tabId: string
    threadId?: string
    workspacePath: string
    initialUrl?: string
    allowFileScheme?: boolean
  }): unknown
  getTabWebContents(win: BrowserWindow, tabId: string): Electron.WebContents | null
  listAutomationTargetTabs?(win: BrowserWindow, threadId: string): Array<{ tabId: string; currentUrl: string; title: string; loading: boolean }>
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
    release?: boolean
    sessionName?: string
    action?: string
  }): void
  setViewport(win: BrowserWindow, params: { tabId: string; viewport: ViewportSize | undefined }): void
  getLayoutSize(win: BrowserWindow, tabId: string): ViewportSize
  isVisible(win: BrowserWindow, tabId: string): boolean
  setVisible?(win: BrowserWindow, params: { tabId: string; visible: boolean }): void
  moveMouse(win: BrowserWindow, params: { tabId: string; x: number; y: number; waitForArrival?: boolean }): Promise<void>
}

interface BrowserUsePolicyHost {
  getSettings(): AppSettings
  updateSettings(partial: Partial<AppSettings>): Promise<void>
}

interface BrowserUseTabRuntime {
  id: string
  owner: BrowserWindow
  logs: BrowserUseLogEntry[]
  clipboardItems: BrowserUseClipboardItem[]
  adopted?: boolean
  userOwned?: boolean
  disposeListeners?: () => void
  keptStatus?: BrowserFinalizeKeepStatus
  closed?: boolean
  exposedToRenderer?: boolean
  cdpAttached?: boolean
  targetSessions: Map<string, string>
  backendQueue?: Promise<void>
  debuggerMessageHandler?: (...args: unknown[]) => void
  debuggerDetachHandler?: (...args: unknown[]) => void
  webContentsFailLoadHandler?: (...args: unknown[]) => void
  lastNavigationFailure?: BrowserUseNavigationFailure
}

interface BrowserUseCreateTabOptions {
  exposeToRenderer?: boolean
  visible?: boolean
  purpose?: 'normal' | 'temporary-content'
}

interface BrowserUseClipboardEntry {
  mime_type: string
  text?: string
  base64?: string
}

interface BrowserUseClipboardItem {
  entries: BrowserUseClipboardEntry[]
  presentation_style?: 'unspecified' | 'inline' | 'attachment'
}

interface BrowserUseNavigationFailure {
  errorCode?: number
  errorDescription: string
  validatedURL: string
  finalURL: string
  isMainFrame: boolean
  timestamp: number
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

interface BrowserUseBackendPendingCommand {
  abortController: AbortController
  evaluationId?: string
  operation: string
  tabId?: string
}

interface BrowserUseThreadRuntime {
  threadId: string
  lifecycle: BrowserTabLifecycle
  owner: BrowserWindow
  workspacePath: string
  sessionName?: string
  display?: (imageLike: unknown) => Promise<void>
  tabs: Map<string, BrowserUseTabRuntime>
  selectedTabId: string | null
  logs: string[]
  images: BrowserUseImageResult[]
  hasFocusedFirstTab: boolean
  activeEvaluationId?: string
  activeAbortSignal?: AbortSignal
  browserSession?: BrowserSessionMetadata
  backendTabIds: Map<string, number>
  backendTabs: Map<number, BrowserUseTabRuntime>
  recentUserBackendTabIds: Set<number>
  pendingBackendCommands: Map<unknown, BrowserUseBackendPendingCommand>
  activeOperation?: BrowserUseOperationTrace
  operationHistory: BrowserUseOperationTrace[]
  pendingViewport?: ViewportSize
  browserVisible: boolean
}

interface BrowserUseOperationTimeouts {
  operationMs?: number
  navigationMs?: number
  blankTabReadyMs?: number
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

function isChromiumErrorPageUrl(url: string): boolean {
  return /^chrome-error:\/\//i.test(url)
}

export class BrowserUseManager implements BrowserUseBackendRequestHandler {
  private readonly screenshots = new BrowserScreenshot()
  private readonly runtimes = new Map<string, BrowserUseThreadRuntime>()
  private readonly runtimesBySessionId = new Map<string, BrowserUseThreadRuntime>()
  private readonly pendingApprovals = new Map<string, {
    resolve: (action: BrowserUseApprovalResponseAction) => void
    timer: ReturnType<typeof setTimeout>
    onClosed: () => void
    owner: BrowserWindow
  }>()
  private readonly closedTabIdsByOwner = new WeakMap<BrowserWindow, Set<string>>()
  private readonly viewports = new BrowserUseViewports<BrowserUseTabRuntime>({
    send: async (tab, method, params) => {
      this.attachDebugger(tab)
      return await this.webContentsFor(tab.owner, tab.id).debugger.sendCommand(method, params)
    },
    present: (tab, viewport) => this.viewerHost.setViewport(tab.owner, { tabId: tab.id, viewport })
  })
  private readonly backendServer = new BrowserUseBackendServer({
    handleBrowserUseBackendRequest: (method, params, context) =>
      this.handleBrowserUseBackendRequest(method, params, context)
  })
  private nextTabId = 1
  private nextBackendTabId = 1
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

  async prepareNodeRepl(owner: BrowserWindow, params: {
    threadId: string
    workspacePath?: string
    evaluationId?: string
    signal?: AbortSignal
    browserSession?: BrowserSessionMetadata
  }): Promise<{
    display: (imageLike: unknown) => Promise<void>
    collect: () => { images: BrowserUseImageResult[]; logs: string[] }
  }> {
    await this.backendServer.ensureStarted()
    const runtime = this.getOrCreateRuntime(owner, params.threadId, params.workspacePath)
    runtime.owner = owner
    runtime.logs = []
    runtime.images = []
    runtime.operationHistory = []
    runtime.activeOperation = undefined
    runtime.activeEvaluationId = params.evaluationId
    runtime.activeAbortSignal = params.signal
    runtime.browserSession = {
      ...(params.browserSession ?? {}),
      sessionId: params.browserSession?.sessionId ?? params.threadId,
      turnId: params.browserSession?.turnId ?? params.evaluationId,
      evaluationId: params.browserSession?.evaluationId ?? params.evaluationId,
      backendId: 'iab'
    }
    const sessionId = runtime.browserSession.sessionId
    if (sessionId) this.runtimesBySessionId.set(sessionId, runtime)
    return {
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
    for (const [key, pending] of runtime.pendingBackendCommands) {
      if (!evaluationId || !pending.evaluationId || pending.evaluationId === evaluationId) {
        pending.abortController.abort()
        runtime.pendingBackendCommands.delete(key)
      }
    }
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

  handleTurnNotification(method: string, params: unknown, workspacePath?: string): ReturnType<typeof readBrowserTurnNotification> {
    const turn = readBrowserTurnNotification(method, params)
    if (!turn) return
    const runtime = this.runtimes.get(turn.threadId)
    if (!runtime) return turn
    if (workspacePath && runtime.workspacePath && workspacePath !== runtime.workspacePath) return
    if (!turn.terminal) {
      runtime.lifecycle.beginTurn(turn.turnId)
    } else {
      runtime.pendingViewport = undefined
      runtime.lifecycle.finishTurn(turn.turnId, runtime.tabs.values(), this.lifecycleEffects(runtime))
    }
    return turn
  }

  private lifecycleEffects(runtime: BrowserUseThreadRuntime) {
    return {
      release: (tab: BrowserUseTabRuntime, status?: BrowserFinalizeKeepStatus) => this.releaseTab(runtime, tab, status),
      retain: (tab: BrowserUseTabRuntime, status: BrowserFinalizeKeepStatus) => {
        this.detachDebugger(tab)
        this.setAutomationState(runtime, tab, false, status)
      },
      close: (tab: BrowserUseTabRuntime) => this.closeTab(tab)
    }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    for (const tab of [...runtime.tabs.values()]) {
      this.detachDebugger(tab)
      if (tab.adopted || tab.userOwned) {
        this.releaseTab(runtime, tab)
      } else {
        this.closeTab(tab)
      }
    }
    runtime.backendTabIds.clear()
    runtime.backendTabs.clear()
    runtime.recentUserBackendTabIds.clear()
    runtime.pendingBackendCommands.clear()
    this.runtimes.delete(threadId)
    if (runtime.browserSession?.sessionId) {
      this.runtimesBySessionId.delete(runtime.browserSession.sessionId)
    }
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
      lifecycle: new BrowserTabLifecycle(),
      owner,
      workspacePath: resolvedWorkspace,
      tabs: new Map<string, BrowserUseTabRuntime>(),
      selectedTabId: null,
      logs: [],
      images: [],
      hasFocusedFirstTab: false,
      backendTabIds: new Map<string, number>(),
      backendTabs: new Map<number, BrowserUseTabRuntime>(),
      recentUserBackendTabIds: new Set<number>(),
      pendingBackendCommands: new Map<unknown, BrowserUseBackendPendingCommand>(),
      operationHistory: [],
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

    runtime.display = display

    this.runtimes.set(threadId, runtime)
    return runtime
  }

  async handleBrowserUseBackendRequest(
    method: string,
    params: Record<string, unknown>,
    context?: BrowserUseBackendCommandContext
  ): Promise<unknown> {
    if (method === 'ping') return 'pong'
    const runtime = this.runtimeForBackendParams(params)
    const turnId = typeof params.turn_id === 'string' ? params.turn_id : runtime.browserSession?.turnId
    if (turnId) runtime.lifecycle.recordUse(turnId)
    return await this.withBackendCommand(runtime, method, params, context ?? this.standaloneBackendContext(method), async (signal) => {
      switch (method) {
        case 'getInfo':
          return this.backendInfo(runtime)
        case 'getTabs':
          return this.backendTabList(runtime)
        case 'getUserTabs':
          return this.backendUserTabList(runtime)
        case 'getUserHistory':
          throw BrowserUseBackendError.unsupportedApi('browser.user.history is not supported by Desktop IAB')
        case 'claimUserTab':
          return this.backendClaimUserTab(runtime, params)
        case 'createTab':
          return await this.backendCreateTab(runtime, params)
        case 'finalizeTabs':
          return await this.backendFinalizeTabs(runtime, params)
        case 'nameSession':
          return this.backendNameSession(runtime, params)
        case 'attach':
          return await this.backendAttach(runtime, params)
        case 'detach':
          return this.backendDetach(runtime, params)
        case 'executeCdp':
          return await this.backendExecuteCdp(runtime, params, signal)
        case 'moveMouse':
          return await this.backendMoveMouse(runtime, params)
        case 'attachTarget':
          return await this.backendAttachTarget(runtime, params, signal)
        case 'detachTarget':
          return await this.backendDetachTarget(runtime, params, signal)
        case 'executeUnhandledCommand':
          return await this.backendExecuteUnhandledCommand(runtime, params, signal)
        default:
          throw BrowserUseBackendError.methodNotFound(method)
      }
    })
  }

  async closeBackendForTests(): Promise<void> {
    await this.backendServer.close()
  }

  private standaloneBackendContext(method: string): BrowserUseBackendCommandContext {
    const abortController = new AbortController()
    return {
      requestId: Symbol(method),
      hasResponse: true,
      signal: abortController.signal,
      cancel: () => abortController.abort()
    }
  }

  async handleBrowserUseElicitation(threadId: string, request: unknown): Promise<Record<string, unknown>> {
    const payload = request && typeof request === 'object' && !Array.isArray(request)
      ? request as Record<string, unknown>
      : {}
    const meta = this.elicitationMeta(payload)
    const fileTransfer = typeof meta.file_transfer === 'string' ? meta.file_transfer : undefined
    if (fileTransfer === 'download') {
      return {
        action: 'accept',
        meta: {
          persist: 'session',
          threadId
        }
      }
    }
    if (fileTransfer === 'upload') {
      return {
        action: 'decline',
        meta: { reason: 'UnsupportedApi: ordinary file upload is not supported by Desktop IAB' }
      }
    }
    if (meta.sensitive_data === 'browsing_history') {
      return {
        action: 'decline',
        meta: { reason: 'UnsupportedApi: browser.user.history is not supported by Desktop IAB' }
      }
    }
    return {
      action: 'decline',
      meta: { reason: 'UnsupportedApi: unsupported Browser Use elicitation in Desktop IAB' }
    }
  }

  private elicitationMeta(payload: Record<string, unknown>): Record<string, unknown> {
    for (const key of ['meta', '_meta', 'content']) {
      const value = payload[key]
      if (value && typeof value === 'object' && !Array.isArray(value)) {
        return value as Record<string, unknown>
      }
    }
    return {}
  }

  private async withBackendCommand<T>(
    runtime: BrowserUseThreadRuntime,
    operation: string,
    params: Record<string, unknown>,
    context: BrowserUseBackendCommandContext,
    run: (signal: AbortSignal) => Promise<T> | T
  ): Promise<T> {
    const requestKey = context.requestId ?? Symbol(operation)
    const abortController = new AbortController()
    const timeoutMs = this.backendCommandTimeoutMs(params)
    const evaluationId = runtime.browserSession?.evaluationId ?? runtime.activeEvaluationId
    const activeSignal = runtime.activeAbortSignal
    runtime.pendingBackendCommands.set(requestKey, {
      abortController,
      evaluationId,
      operation
    })

    if (activeSignal?.aborted) {
      runtime.pendingBackendCommands.delete(requestKey)
      throw BrowserUseBackendError.commandCancelled(`Browser backend command ${operation} was cancelled before it started.`)
    }

    let commandPromise: Promise<T>
    try {
      commandPromise = Promise.resolve(run(abortController.signal))
    } catch (error) {
      runtime.pendingBackendCommands.delete(requestKey)
      throw error
    }
    commandPromise.catch(() => {})

    return await new Promise<T>((resolve, reject) => {
      let settled = false
      const cleanup = () => {
        clearTimeout(timeout)
        activeSignal?.removeEventListener('abort', onAbort)
        abortController.signal.removeEventListener('abort', onAbort)
        runtime.pendingBackendCommands.delete(requestKey)
      }
      const finish = (callback: () => void) => {
        if (settled) return
        settled = true
        cleanup()
        callback()
      }
      const onAbort = () => {
        if (!abortController.signal.aborted) abortController.abort()
        finish(() => reject(BrowserUseBackendError.commandCancelled(
          `Browser backend command ${operation} was cancelled.`
        )))
      }
      const timeout = setTimeout(() => {
        finish(() => {
          abortController.abort()
          reject(BrowserUseBackendError.commandTimeout(
            `Browser backend command ${operation} timed out after ${timeoutMs}ms.`,
            this.backendCommandTimeoutData(runtime, operation, params)
          ))
        })
      }, timeoutMs)

      activeSignal?.addEventListener('abort', onAbort, { once: true })
      abortController.signal.addEventListener('abort', onAbort, { once: true })
      commandPromise.then(
        (value) => finish(() => {
          try {
            if (!this.isBackendResultCapExempt(operation, params)) {
              this.assertBackendResultWithinLimit(operation, value)
            }
            resolve(value)
          } catch (error) {
            reject(error)
          }
        }),
        (error) => finish(() => reject(this.normalizeBackendError(error, operation)))
      )
    })
  }

  private backendCommandTimeoutMs(params: Record<string, unknown>): number {
    const raw = params.timeoutMs ?? params.timeout_ms ?? params.timeout
    const numeric = Number(raw)
    const requested = Number.isFinite(numeric) && numeric > 0 ? numeric : this.operationTimeoutMs()
    return Math.max(1, Math.min(Math.floor(requested), 120_000))
  }

  private backendCommandTimeoutData(
    runtime: BrowserUseThreadRuntime,
    operation: string,
    params: Record<string, unknown>
  ): Record<string, unknown> {
    const data: Record<string, unknown> = { operation }
    const commandType = this.stringParam(params, 'type')
    const cdpMethod = this.stringParam(params, 'method')
    if (commandType) data.commandType = commandType
    if (cdpMethod) data.cdpMethod = cdpMethod
    const target = this.objectParam(params, 'target')
    const tabId = this.positiveIntegerParam(params, 'tab_id') ??
      this.positiveIntegerParam(params, 'tabId') ??
      this.positiveIntegerParam(target ?? {}, 'tab_id') ??
      this.positiveIntegerParam(target ?? {}, 'tabId')
    if (tabId) {
      data.tabId = String(tabId)
      const tab = runtime.backendTabs.get(tabId)
      if (tab) data.url = this.operationUrl(tab)
    }
    return data
  }

  private isBackendResultCapExempt(operation: string, params: Record<string, unknown>): boolean {
    if (operation === 'executeCdp' && this.stringParam(params, 'method') === 'Page.captureScreenshot') return true
    return operation === 'executeUnhandledCommand' && this.stringParam(params, 'type') === 'tab_screenshot'
  }

  private assertBackendResultWithinLimit(operation: string, value: unknown): void {
    let byteLength = 0
    try {
      byteLength = Buffer.byteLength(JSON.stringify(value) ?? '', 'utf8')
    } catch {
      byteLength = Buffer.byteLength(String(value), 'utf8')
    }
    if (byteLength > BROWSER_USE_MAX_RESULT_BYTES) {
      throw BrowserUseBackendError.resultTooLarge(
        `${operation} result exceeded ${BROWSER_USE_MAX_RESULT_BYTES} bytes.`,
        { byteLength, maxBytes: BROWSER_USE_MAX_RESULT_BYTES }
      )
    }
  }

  private normalizeBackendError(error: unknown, operation: string): unknown {
    if (error instanceof BrowserUseBackendError) return error
    if (error instanceof Error) {
      if (/Browser tab is no longer available|Browser backend tab not found/i.test(error.message)) {
        return BrowserUseBackendError.pageClosed(operation)
      }
      return error
    }
    return new BrowserUseBackendError(String(error))
  }

  private async queueBackendTabCommand<T>(
    tab: BrowserUseTabRuntime,
    run: () => Promise<T> | T,
    signal?: AbortSignal
  ): Promise<T> {
    const previous = tab.backendQueue ?? Promise.resolve()
    let release!: () => void
    const current = new Promise<void>((resolveCurrent) => {
      release = resolveCurrent
    })
    tab.backendQueue = previous.catch(() => {}).then(() => current)
    await previous.catch(() => {})
    let released = false
    const releaseQueue = () => {
      if (released) return
      released = true
      release()
    }
    signal?.addEventListener('abort', releaseQueue, { once: true })
    try {
      return await run()
    } finally {
      signal?.removeEventListener('abort', releaseQueue)
      releaseQueue()
    }
  }

  private runtimeForBackendParams(params: Record<string, unknown>): BrowserUseThreadRuntime {
    const sessionId = this.stringParam(params, 'session_id') ?? this.stringParam(params, 'sessionId')
    const turnId = this.stringParam(params, 'turn_id') ?? this.stringParam(params, 'turnId')
    if (!sessionId) {
      throw new BrowserUseBackendError('SessionMetadataMissing: missing session_id.', -32002)
    }
    if (!turnId) {
      throw new BrowserUseBackendError('SessionMetadataMissing: missing turn_id.', -32002)
    }
    const runtime = this.runtimesBySessionId.get(sessionId)
    if (!runtime) {
      throw new BrowserUseBackendError('SessionMetadataMissing: no active Desktop IAB runtime for session.', -32002)
    }
    return runtime
  }

  private backendInfo(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    return {
      id: 'iab',
      name: 'DotCraft In-App Browser',
      type: 'iab',
      protocolVersion: 2,
      supportsCommandCancel: true,
      supportsTypedFinalize: true,
      maxBrowserResultBytes: BROWSER_USE_MAX_RESULT_BYTES,
      capabilities: {
        browser: BROWSER_USE_BROWSER_CAPABILITIES.map((capability) => ({ ...capability })),
        tab: BROWSER_USE_TAB_CAPABILITIES.map((capability) => ({ ...capability })),
        docs: {
          supported: [
            'tabs',
            'browserCapabilities',
            'basicNavigation',
            'cdp',
            'playwrightCommonSubset',
            'domCua',
            'pageAssets',
            'webmcp'
          ],
          unsupported: [
            'browser.user.history',
            'ordinaryDownloads',
            'fileUpload',
            'fileChooser',
            'tab_content_export'
          ],
          notes: [
            'pageAssets.bundle is supported through Desktop file-transfer approval and temp output.',
            'Text and JSON browser results are capped at 1MB; screenshots are exempt.'
          ]
        }
      },
      metadata: {
        dotcraftSessionId: runtime.browserSession?.sessionId ?? runtime.threadId
      },
      tabCount: runtime.tabs.size
    }
  }

  private backendTabList(runtime: BrowserUseThreadRuntime): Record<string, unknown>[] {
    return this.modelFacingTabs(runtime).map((tab) => this.backendTabSnapshot(runtime, tab))
  }

  private modelFacingTabs(runtime: BrowserUseThreadRuntime): BrowserUseTabRuntime[] {
    return [...runtime.tabs.values()].filter((tab) => tab.exposedToRenderer !== false)
  }

  private backendUserTabList(runtime: BrowserUseThreadRuntime): Record<string, unknown>[] {
    const candidate = this.viewerHost.getAutomationTargetTab?.(runtime.owner, runtime.threadId)
    const candidates = this.viewerHost.listAutomationTargetTabs?.(runtime.owner, runtime.threadId) ?? (candidate ? [candidate] : [])
    for (const page of candidates) {
      if (!runtime.tabs.has(page.tabId)) this.registerTab(runtime.owner, runtime, page.tabId, true)
    }
    const tabs = this.backendTabList(runtime)
    runtime.recentUserBackendTabIds = new Set(tabs.map((tab) => Number(tab.id)).filter((id) => Number.isInteger(id)))
    return tabs
  }

  private backendClaimUserTab(runtime: BrowserUseThreadRuntime, params: Record<string, unknown>): Record<string, unknown> {
    const tab = this.backendTabForParams(runtime, params)
    const backendTabId = this.backendTabIdFor(runtime, tab)
    if (runtime.recentUserBackendTabIds.size > 0 && !runtime.recentUserBackendTabIds.has(backendTabId)) {
      throw new Error('Cannot claim browser tab: pass a tab id from the current session latest getUserTabs result.')
    }
    tab.adopted = true
    runtime.selectedTabId = tab.id
    this.setAutomationState(runtime, tab, true, 'claim')
    this.applyPendingViewport(runtime, tab)
    return this.backendTabSnapshot(runtime, tab)
  }

  private async backendCreateTab(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const initialUrl = this.stringParam(params, 'url')
    const tab = await this.createTab(runtime.owner, runtime, initialUrl)
    runtime.selectedTabId = tab.id
    return this.backendTabSnapshot(runtime, tab)
  }

  private async backendFinalizeTabs(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const backendKeep = this.parseBackendFinalizeKeep(params.keep)
    const ids = new Map([...runtime.tabs.values()].map((tab) => [tab.id, this.backendTabIdFor(runtime, tab)]))
    const keep = new Map<string, BrowserFinalizeKeepStatus>()
    for (const [id, backendId] of ids) {
      const status = backendKeep.get(backendId)
      if (status) keep.set(id, status)
    }
    const result = runtime.lifecycle.finalize(runtime.tabs.values(), keep, this.lifecycleEffects(runtime))
    return {
      ok: true,
      kept: result.kept.map((id) => ids.get(id)!),
      closed: result.closed.map((id) => ids.get(id)!),
      released: result.released.map((id) => ids.get(id)!)
    }
  }

  private backendNameSession(runtime: BrowserUseThreadRuntime, params: Record<string, unknown>): Record<string, unknown> {
    runtime.sessionName = String(params.name ?? '').trim()
    for (const tab of runtime.tabs.values()) {
      this.setAutomationState(runtime, tab, true, 'session')
    }
    return { ok: true, name: runtime.sessionName }
  }

  private async backendAttach(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForParams(runtime, params)
    await this.ensureDebuggerAttached(tab)
    return { ok: true, tabId: this.backendTabIdFor(runtime, tab) }
  }

  private backendDetach(runtime: BrowserUseThreadRuntime, params: Record<string, unknown>): Record<string, unknown> {
    const tab = this.backendTabForParams(runtime, params)
    this.detachDebugger(tab)
    return { ok: true, tabId: this.backendTabIdFor(runtime, tab) }
  }

  private async backendAttachTarget(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForParams(runtime, params)
    const targetId = this.stringParam(params, 'targetId') ?? this.stringParam(params, 'target_id')
    if (!targetId) throw BrowserUseBackendError.unsupportedApi('attachTarget without targetId')
    return await this.queueBackendTabCommand(tab, async () => {
      try {
        const result = await this.cdpCommand<{ sessionId?: string }>(tab, 'Target.attachToTarget', {
          targetId,
          flatten: true
        })
        if (!result.sessionId) throw BrowserUseBackendError.unsupportedApi(`attachTarget(${targetId})`)
        tab.targetSessions.set(targetId, result.sessionId)
        return { ok: true, sessionId: result.sessionId }
      } catch (error) {
        if (error instanceof BrowserUseBackendError) throw error
        throw BrowserUseBackendError.unsupportedApi(`attachTarget(${targetId})`)
      }
    }, signal)
  }

  private async backendDetachTarget(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForParams(runtime, params)
    const targetId = this.stringParam(params, 'targetId') ?? this.stringParam(params, 'target_id')
    if (!targetId) return { ok: true }
    const sessionId = tab.targetSessions.get(targetId)
    if (!sessionId) return { ok: true }
    return await this.queueBackendTabCommand(tab, async () => {
      await this.cdpCommand(tab, 'Target.detachFromTarget', { sessionId })
      tab.targetSessions.delete(targetId)
      return { ok: true }
    }, signal)
  }

  private async backendExecuteCdp(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<unknown> {
    const method = this.stringParam(params, 'method')
    if (!method) throw BrowserUseBackendError.invalidArgument('executeCdp requires a method.')
    const target = this.objectParam(params, 'target')
    const tab = this.backendTabForTarget(runtime, target ?? params)
    const commandParams = this.objectParam(params, 'commandParams') ?? this.objectParam(params, 'params') ?? {}
    const sessionId = this.backendTargetSessionId(tab, target)
    if (method.startsWith('Input.') && !BROWSER_USE_INPUT_METHODS.has(method)) {
      throw BrowserUseBackendError.unsupportedApi(method)
    }
    this.markAutomation(tab, method)
    return await this.queueBackendTabCommand(tab, async () => {
      try {
        if (method.startsWith('Input.')) {
          await this.cdpCommand(tab, 'Emulation.setFocusEmulationEnabled', { enabled: true }, sessionId, signal)
        }
        if (method === 'Page.navigate') {
          return await this.backendCdpNavigate(runtime, tab, commandParams, sessionId)
        }
        if (method === 'Page.reload') {
          this.clearNavigationFailure(tab)
        }
        if (method === 'Page.close' || method === 'Target.closeTarget') {
          this.closeTab(tab)
          return {}
        }
        return await this.cdpCommand(tab, method, commandParams, sessionId, signal)
      } catch (error) {
        if (this.isCdpNodeStaleError(method, commandParams, error)) {
          throw BrowserUseBackendError.nodeStale(
            commandParams.backendNodeId ?? commandParams.nodeId ?? commandParams.objectId ?? method
          )
        }
        throw error
      }
    }, signal)
  }

  private isCdpNodeStaleError(
    method: string,
    commandParams: Record<string, unknown>,
    error: unknown
  ): boolean {
    if (!method.startsWith('DOM.')) return false
    if (
      commandParams.backendNodeId == null &&
      commandParams.nodeId == null &&
      commandParams.objectId == null
    ) {
      return false
    }
    const message = error instanceof Error ? error.message : String(error)
    return /no node with given id|could not find node|node.*not found|cannot find context with specified id/i.test(message)
  }

  private async backendCdpNavigate(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    commandParams: Record<string, unknown>,
    sessionId?: string
  ): Promise<unknown> {
    const url = typeof commandParams.url === 'string' ? commandParams.url : ''
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw BrowserUseBackendError.invalidArgument(`Invalid browser URL: ${url}`)
    this.clearNavigationFailure(tab)
    await this.ensureNavigationAllowed(tab.owner, runtime, tab.id, normalized)
    const result = await this.cdpCommand<{ errorText?: string }>(tab, 'Page.navigate', {
      ...commandParams,
      url: normalized
    }, sessionId)
    if (result.errorText) {
      const failure: BrowserUseNavigationFailure = {
        errorDescription: result.errorText,
        validatedURL: normalized,
        finalURL: this.operationUrl(tab),
        isMainFrame: true,
        timestamp: Date.now()
      }
      this.recordNavigationFailure(tab, failure)
      throw this.navigationFailureError(failure)
    }
    const chromiumFailure = this.chromiumErrorPageFailure(tab, normalized)
    if (chromiumFailure) {
      this.recordNavigationFailure(tab, chromiumFailure)
      throw this.navigationFailureError(chromiumFailure)
    }
    return result
  }

  private async backendMoveMouse(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForParams(runtime, params)
    const point = this.objectParam(params, 'point')
    const x = Number(point?.x ?? params.x)
    const y = Number(point?.y ?? params.y)
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
      throw BrowserUseBackendError.invalidArgument('moveMouse requires finite x and y coordinates.')
    }
    this.setAutomationState(runtime, tab, true, 'move')
    await this.viewerHost.moveMouse(tab.owner, {
      tabId: tab.id,
      x,
      y,
      waitForArrival: params.waitForArrival !== false
    })
    return { ok: true }
  }

  private async backendExecuteUnhandledCommand(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<unknown> {
    const type = this.stringParam(params, 'type')
    if (!type) throw BrowserUseBackendError.invalidArgument('executeUnhandledCommand requires a type.')
    switch (type) {
      case 'tab_mark': {
        const tab = this.backendTabForParams(runtime, params)
        const status = params.status
        if (status !== 'handoff' && status !== 'deliverable') {
          throw BrowserUseBackendError.invalidArgument('tab_mark requires status handoff or deliverable.')
        }
        runtime.lifecycle.mark(tab.id, status)
        tab.keptStatus = status
        this.setAutomationState(runtime, tab, true, status)
        return { ok: true }
      }
      case 'browser_visibility_get':
        return { visible: runtime.browserVisible }
      case 'browser_visibility_set':
        return this.backendBrowserVisibilitySet(runtime, params)
      case 'browser_viewport_set':
        return await this.backendBrowserViewportSet(runtime, params)
      case 'browser_viewport_reset':
        return await this.backendBrowserViewportReset(runtime)
      case 'tabs_content':
        return await this.backendTabsContent(runtime, params)
      case 'tab_dev_logs':
        return this.backendTabDevLogs(runtime, params)
      case 'tab_screenshot':
        return await this.backendTabScreenshot(runtime, params, signal)
      case 'tab_clipboard_read_text':
        return await this.backendClipboardReadText(runtime, params)
      case 'tab_clipboard_write_text':
        await this.backendClipboardWriteText(runtime, params)
        return {}
      case 'tab_clipboard_read':
        return await this.backendClipboardRead(runtime, params)
      case 'tab_clipboard_write':
        await this.backendClipboardWrite(runtime, params)
        return {}
      case 'playwright_locator_operation': {
        const tab = this.backendTabForCommand(runtime, params)
        await this.ensurePlaywrightInjected(tab)
        const value = await this.executeJavaScript<unknown>(tab,
          `window.__dotcraftBrowserUseLocator(${JSON.stringify(params.descriptor)}, ${JSON.stringify(params.operation)}, ${JSON.stringify(params.payload ?? {})})`, 'locator.operation')
        return { value }
      }
      case 'dom_cua_node_info': {
        const tab = this.backendTabForCommand(runtime, params)
        await this.ensurePlaywrightInjected(tab)
        return await this.executeJavaScript(tab, `window.__dotcraftBrowserUseNode(${JSON.stringify(params.node_id)})`, 'domCua.node')
      }
      case 'playwright_dom_snapshot':
        return await this.backendPlaywrightDomSnapshot(runtime, params)
      case 'playwright_wait_for_load_state':
        return await this.backendPlaywrightWaitForLoadState(runtime, params)
      case 'tab_content_export':
        throw BrowserUseBackendError.unsupportedApi('tab_content_export')
      case 'close_tab': {
        const tab = this.backendTabForCommand(runtime, params)
        this.closeTab(tab)
        return {}
      }
      case 'navigate_tab_back':
        await this.goBack(this.backendTabForCommand(runtime, params))
        return {}
      case 'navigate_tab_forward':
        await this.goForward(this.backendTabForCommand(runtime, params))
        return {}
      case 'navigate_tab_reload':
        await this.reload(this.backendTabForCommand(runtime, params))
        return {}
      default:
        throw BrowserUseBackendError.unsupportedApi(`executeUnhandledCommand(${type})`)
    }
  }

  private async backendTabScreenshot(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const clip = this.backendScreenshotClip(params)
    const image = await this.screenshot(tab, {
      fullPage: params.fullPage === true,
      ...(clip ? { clip } : {})
    }, signal)
    return { data: image.dataBase64 }
  }

  private backendScreenshotClip(params: Record<string, unknown>): Electron.Rectangle | undefined {
    const cropX = Number(params.cropX)
    const cropY = Number(params.cropY)
    const cropWidth = Number(params.cropWidth)
    const cropHeight = Number(params.cropHeight)
    if (![cropX, cropY, cropWidth, cropHeight].some(Number.isFinite)) return undefined
    if (![cropX, cropY, cropWidth, cropHeight].every(Number.isFinite)) {
      throw BrowserUseBackendError.invalidArgument('tab_screenshot crop fields must all be finite numbers.')
    }
    return {
      x: Math.max(0, cropX),
      y: Math.max(0, cropY),
      width: Math.max(1, cropWidth),
      height: Math.max(1, cropHeight)
    }
  }

  private async backendPlaywrightDomSnapshot(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { dom_snapshot: await this.domSnapshot(tab) }
  }

  private async backendPlaywrightWaitForLoadState(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    await this.waitForLoad(tab, this.stringParam(params, 'state') ?? 'load', this.backendCommandTimeoutMs(params))
    return {}
  }

  private async backendTabsContent(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const rawUrls = params.urls
    if (!Array.isArray(rawUrls)) throw BrowserUseBackendError.invalidArgument('tabs_content requires urls.')
    const contentType = this.stringParam(params, 'content_type') ?? this.stringParam(params, 'contentType') ?? 'text'
    if (contentType !== 'html' && contentType !== 'text' && contentType !== 'domSnapshot') {
      throw BrowserUseBackendError.invalidArgument(`Unsupported tabs_content content_type: ${contentType}`)
    }
    const results: Array<{ url: string; title: string | null; content: string | null }> = []
    for (const rawUrl of rawUrls) {
      const url = typeof rawUrl === 'string' ? rawUrl : ''
      if (!url) {
        results.push({ url: '', title: null, content: null })
        continue
      }
      let tab: BrowserUseTabRuntime | null = null
      try {
        tab = await this.createTab(runtime.owner, runtime, url, {
          exposeToRenderer: false,
          visible: false,
          purpose: 'temporary-content'
        })
        const content = contentType === 'domSnapshot'
          ? await this.domSnapshot(tab)
          : await this.evaluatePageContent(tab, contentType)
        results.push({
          url: this.operationUrl(tab),
          title: this.safeTabTitle(tab),
          content
        })
      } catch {
        results.push({ url, title: null, content: null })
      } finally {
        if (tab) this.closeTab(tab)
      }
    }
    return { results }
  }

  private backendTabDevLogs(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Record<string, unknown> {
    const tab = this.backendTabForCommand(runtime, params)
    const levels = Array.isArray(params.levels)
      ? params.levels.filter((level): level is string => typeof level === 'string')
      : undefined
    const limit = typeof params.limit === 'number' && Number.isFinite(params.limit)
      ? Math.max(1, Math.floor(params.limit))
      : undefined
    return {
      logs: this.devLogs(tab, {
        filter: this.stringParam(params, 'filter'),
        levels,
        limit
      })
    }
  }

  private async backendClipboardReadText(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { text: await this.readVirtualClipboardText(tab) }
  }

  private async backendClipboardWriteText(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<void> {
    const tab = this.backendTabForCommand(runtime, params)
    if (typeof params.text !== 'string') {
      throw BrowserUseBackendError.invalidArgument('tab_clipboard_write_text requires text.')
    }
    await this.writeVirtualClipboardText(tab, params.text)
  }

  private async backendClipboardRead(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { items: await this.readVirtualClipboard(tab) }
  }

  private async backendClipboardWrite(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<void> {
    const tab = this.backendTabForCommand(runtime, params)
    await this.writeVirtualClipboard(tab, params.items)
  }

  private async readVirtualClipboardText(tab: BrowserUseTabRuntime): Promise<string> {
    const text = this.virtualClipboardPlainText(tab)
    if (text != null) return text
    return await this.executeJavaScript<string>(tab, `(() => {
      if (navigator.clipboard?.readText == null) return "";
      return navigator.clipboard.readText();
    })()`, 'clipboard.readText').then(
      (value) => typeof value === 'string' ? value : String(value ?? ''),
      () => ''
    )
  }

  private async writeVirtualClipboardText(tab: BrowserUseTabRuntime, text: string): Promise<void> {
    const value = String(text ?? '')
    tab.clipboardItems = [{
      entries: [{ mime_type: 'text/plain', text: value }],
      presentation_style: 'unspecified'
    }]
    await this.executeJavaScript(tab, `(() => {
      if (navigator.clipboard?.writeText == null) return false;
      return navigator.clipboard.writeText(${JSON.stringify(value)}).then(() => true, () => false);
    })()`, 'clipboard.writeText').catch(() => false)
  }

  private async readVirtualClipboard(tab: BrowserUseTabRuntime): Promise<BrowserUseClipboardItem[]> {
    if (tab.clipboardItems.length > 0) return tab.clipboardItems.map((item) => ({
      entries: item.entries.map((entry) => ({ ...entry })),
      presentation_style: item.presentation_style
    }))
    const text = await this.readVirtualClipboardText(tab)
    return text
      ? [{
          entries: [{ mime_type: 'text/plain', text }],
          presentation_style: 'unspecified'
        }]
      : []
  }

  private async writeVirtualClipboard(tab: BrowserUseTabRuntime, items: unknown): Promise<void> {
    tab.clipboardItems = this.normalizeClipboardItems(items)
    const text = this.virtualClipboardPlainText(tab)
    if (text != null) {
      await this.executeJavaScript(tab, `(() => {
        if (navigator.clipboard?.writeText == null) return false;
        return navigator.clipboard.writeText(${JSON.stringify(text)}).then(() => true, () => false);
      })()`, 'clipboard.write').catch(() => false)
    }
  }

  private virtualClipboardPlainText(tab: BrowserUseTabRuntime): string | null {
    for (const item of tab.clipboardItems) {
      for (const entry of item.entries) {
        if (entry.mime_type === 'text/plain' && typeof entry.text === 'string') return entry.text
      }
    }
    return null
  }

  private normalizeClipboardItems(items: unknown): BrowserUseClipboardItem[] {
    if (!Array.isArray(items)) throw BrowserUseBackendError.invalidArgument('tab_clipboard_write requires items.')
    return items.map((item) => {
      const rawItem = item && typeof item === 'object' && !Array.isArray(item)
        ? item as Record<string, unknown>
        : {}
      const entries = Array.isArray(rawItem.entries)
        ? rawItem.entries.map((entry) => this.normalizeClipboardEntry(entry))
        : []
      if (entries.length === 0) {
        throw BrowserUseBackendError.invalidArgument('tab_clipboard_write items require at least one entry.')
      }
      const rawStyle = rawItem.presentation_style ?? rawItem.presentationStyle
      const presentationStyle = rawStyle === 'inline' || rawStyle === 'attachment' || rawStyle === 'unspecified'
        ? rawStyle
        : 'unspecified'
      return {
        entries,
        presentation_style: presentationStyle
      }
    })
  }

  private normalizeClipboardEntry(entry: unknown): BrowserUseClipboardEntry {
    const rawEntry = entry && typeof entry === 'object' && !Array.isArray(entry)
      ? entry as Record<string, unknown>
      : {}
    const mimeType = this.stringValue(rawEntry.mime_type ?? rawEntry.mimeType).trim()
    if (!mimeType) throw BrowserUseBackendError.invalidArgument('clipboard entry requires mime_type.')
    const text = typeof rawEntry.text === 'string' ? rawEntry.text : undefined
    const base64 = typeof rawEntry.base64 === 'string' ? rawEntry.base64 : undefined
    if ((text == null && base64 == null) || (text != null && base64 != null)) {
      throw BrowserUseBackendError.invalidArgument('clipboard entry must include exactly one of text or base64.')
    }
    return {
      mime_type: mimeType,
      ...(text == null ? {} : { text }),
      ...(base64 == null ? {} : { base64 })
    }
  }

  private backendBrowserVisibilitySet(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Record<string, unknown> {
    if (typeof params.visible !== 'boolean') {
      throw BrowserUseBackendError.invalidArgument('browser_visibility_set requires visible.')
    }
    runtime.browserVisible = params.visible
    for (const tab of runtime.tabs.values()) {
      this.viewerHost.setVisible?.(tab.owner, { tabId: tab.id, visible: runtime.browserVisible })
    }
    if (runtime.browserVisible) this.presentVisibleTabs(runtime)
    return {}
  }

  private async backendBrowserViewportSet(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    await this.setViewport(runtime, normalizeViewportSize(params.width, params.height))
    return {}
  }

  private async backendBrowserViewportReset(runtime: BrowserUseThreadRuntime): Promise<Record<string, unknown>> {
    await this.setViewport(runtime, undefined)
    return {}
  }

  private async setViewport(runtime: BrowserUseThreadRuntime, size: ViewportSize | undefined): Promise<void> {
    const controlled = this.modelFacingTabs(runtime).filter((tab) => !tab.userOwned || tab.adopted)
    const tab = controlled.find((item) => item.id === runtime.selectedTabId) ?? controlled[0]
    if (!tab) {
      runtime.pendingViewport = size
      return
    }
    await (size ? this.viewports.set(tab, size) : this.viewports.reset(tab))
  }

  private applyPendingViewport(runtime: BrowserUseThreadRuntime, tab: BrowserUseTabRuntime): void {
    const size = runtime.pendingViewport
    if (!size) return
    runtime.pendingViewport = undefined
    this.viewports.set(tab, size).catch(() => {})
  }

  private async evaluatePageContent(tab: BrowserUseTabRuntime, contentType: 'html' | 'text'): Promise<string> {
    const expression = contentType === 'html'
      ? 'document.documentElement ? document.documentElement.outerHTML : ""'
      : 'document.body ? document.body.innerText : (document.documentElement ? document.documentElement.innerText : "")'
    return await this.executeJavaScript<string>(tab, expression, `tabs_content.${contentType}`, false)
  }

  private backendTabIdFor(runtime: BrowserUseThreadRuntime, tab: BrowserUseTabRuntime): number {
    const existing = runtime.backendTabIds.get(tab.id)
    if (existing) return existing
    const id = this.nextBackendTabId++
    runtime.backendTabIds.set(tab.id, id)
    runtime.backendTabs.set(id, tab)
    return id
  }

  private forgetBackendTab(runtime: BrowserUseThreadRuntime, tab: BrowserUseTabRuntime): void {
    const backendId = runtime.backendTabIds.get(tab.id)
    if (backendId) runtime.backendTabs.delete(backendId)
    runtime.backendTabIds.delete(tab.id)
    runtime.recentUserBackendTabIds.delete(backendId ?? -1)
  }

  private backendTabSnapshot(runtime: BrowserUseThreadRuntime, tab: BrowserUseTabRuntime): Record<string, unknown> {
    const id = this.backendTabIdFor(runtime, tab)
    const snapshot = this.tabSnapshot(tab)
    return {
      id,
      tabId: id,
      url: snapshot.url,
      title: snapshot.title,
      loading: snapshot.loading,
      active: runtime.selectedTabId === tab.id
    }
  }

  private backendTabForParams(runtime: BrowserUseThreadRuntime, params: Record<string, unknown>): BrowserUseTabRuntime {
    const id = this.positiveIntegerParam(params, 'tabId') ?? this.positiveIntegerParam(params, 'tab_id')
    if (!id) throw BrowserUseBackendError.invalidArgument('Browser backend command requires a positive integer tabId.')
    return this.backendTabForId(runtime, id)
  }

  private backendTabForCommand(runtime: BrowserUseThreadRuntime, params: Record<string, unknown>): BrowserUseTabRuntime {
    const id = this.positiveIntegerParam(params, 'tab_id') ?? this.positiveIntegerParam(params, 'tabId')
    if (!id) throw BrowserUseBackendError.invalidArgument('Browser command requires a positive integer tab_id.')
    return this.backendTabForId(runtime, id)
  }

  private backendTabForTarget(
    runtime: BrowserUseThreadRuntime,
    targetOrParams: Record<string, unknown> | null
  ): BrowserUseTabRuntime {
    const source = targetOrParams ?? {}
    const id = this.positiveIntegerParam(source, 'tabId') ??
      this.positiveIntegerParam(source, 'tab_id')
    if (!id) throw BrowserUseBackendError.invalidArgument('CDP target requires a positive integer tabId.')
    return this.backendTabForId(runtime, id)
  }

  private backendTabForId(runtime: BrowserUseThreadRuntime, id: number): BrowserUseTabRuntime {
    const tab = runtime.backendTabs.get(id)
    if (!tab) throw BrowserUseBackendError.tabStale(id)
    return tab
  }

  private backendTargetSessionId(
    tab: BrowserUseTabRuntime,
    target: Record<string, unknown> | null
  ): string | undefined {
    const explicit = typeof target?.sessionId === 'string' ? target.sessionId : undefined
    if (explicit) return explicit
    const targetId = typeof target?.targetId === 'string' ? target.targetId : undefined
    if (!targetId) return undefined
    const sessionId = tab.targetSessions.get(targetId)
    if (!sessionId) throw BrowserUseBackendError.unsupportedApi(`target session ${targetId}`)
    return sessionId
  }

  private parseBackendFinalizeKeep(value: unknown): Map<number, BrowserFinalizeKeepStatus> {
    if (value == null) return new Map()
    if (!Array.isArray(value)) throw new Error('finalizeTabs keep must be an array of { tabId, status: "deliverable"|"handoff" } entries.')
    const keep = new Map<number, BrowserFinalizeKeepStatus>()
    for (const item of value) {
      if (!item || typeof item !== 'object' || Array.isArray(item)) {
        throw new Error('finalizeTabs keep entries must be objects shaped like { tabId, status: "deliverable"|"handoff" }.')
      }
      const entry = item as Record<string, unknown>
      const id = this.positiveIntegerFromUnknown(entry.tabId ?? entry.tab_id ?? entry.id)
      const status = entry.status
      if (!id) throw new Error('finalizeTabs keep entries require a positive integer tabId; use { tabId, status: "deliverable"|"handoff" }.')
      if (status !== 'handoff' && status !== 'deliverable') {
        throw new Error('finalizeTabs keep entries must include status "handoff" or "deliverable"; use { tabId, status: "deliverable"|"handoff" }.')
      }
      keep.set(id, status)
    }
    return keep
  }

  private stringParam(params: Record<string, unknown>, key: string): string | undefined {
    const value = params[key]
    return typeof value === 'string' && value.trim() ? value.trim() : undefined
  }

  private objectParam(params: Record<string, unknown>, key: string): Record<string, unknown> | null {
    const value = params[key]
    return value && typeof value === 'object' && !Array.isArray(value)
      ? value as Record<string, unknown>
      : null
  }

  private positiveIntegerParam(params: Record<string, unknown>, key: string): number | null {
    return this.positiveIntegerFromUnknown(params[key])
  }

  private positiveIntegerFromUnknown(value: unknown): number | null {
    const numeric = Number(value)
    return Number.isInteger(numeric) && numeric > 0 ? numeric : null
  }

  private emitCdpEvent(
    tab: BrowserUseTabRuntime,
    method: string,
    params: Record<string, unknown> = {},
    sessionId?: string
  ): void {
    let backendTabId: number
    try {
      backendTabId = this.backendTabIdFor(this.getRuntimeForTab(tab), tab)
    } catch {
      return
    }
    const source = sessionId ? { tabId: backendTabId, sessionId } : { tabId: backendTabId }
    this.backendServer.sendNotification('onCDPEvent', {
      source,
      method,
      params
    })
  }

  private async createTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    initialUrl?: string,
    options: BrowserUseCreateTabOptions = {}
  ): Promise<BrowserUseTabRuntime> {
    const exposeToRenderer = options.exposeToRenderer !== false
    const visible = options.visible ?? runtime.browserVisible
    const normalizedInitial = initialUrl ? normalizeBrowserUseUrl(initialUrl) : null
    if (initialUrl && !normalizedInitial) throw new Error(`Invalid browser URL: ${initialUrl}`)
    const id = `browser-${sanitizeThreadId(runtime.threadId)}-${this.nextTabId++}`
    if (normalizedInitial) {
      await this.ensureNavigationAllowed(owner, runtime, id, normalizedInitial)
    }

    await this.viewerHost.createAutomationTab(owner, {
      tabId: id,
      threadId: runtime.threadId,
      workspacePath: runtime.workspacePath || owner.getTitle(),
      initialUrl: 'about:blank',
      allowFileScheme: true
    })

    const tab = this.registerTab(owner, runtime, id, false)
    tab.exposedToRenderer = exposeToRenderer
    if (!visible) {
      this.viewerHost.setVisible?.(owner, { tabId: tab.id, visible: false })
    }

    if (exposeToRenderer) {
      const focusMode = visible && !runtime.hasFocusedFirstTab ? 'first-open' : 'none'
      if (focusMode === 'first-open') runtime.hasFocusedFirstTab = true
      this.emitOpen(owner, {
        threadId: runtime.threadId,
        tabId: id,
        initialUrl: normalizedInitial ?? 'about:blank',
        title: runtime.sessionName?.trim() || 'Browser',
        focusMode
      })
    }

    if (exposeToRenderer) this.applyPendingViewport(runtime, tab)

    if (normalizedInitial) {
      this.markAutomation(tab, 'navigate')
      this.clearNavigationFailure(tab)
      await this.loadAutomationUrl(tab, normalizedInitial)
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

  private emitClose(owner: BrowserWindow, payload: BrowserUseClosePayload): void {
    if (owner.isDestroyed() || owner.webContents.isDestroyed()) return
    owner.webContents.send(BROWSER_USE_CLOSE_CHANNEL, payload)
  }

  private presentVisibleTabs(runtime: BrowserUseThreadRuntime): void {
    let focusNext = !runtime.hasFocusedFirstTab
    for (const tab of runtime.tabs.values()) {
      if (tab.exposedToRenderer !== true) continue
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

  private tabForId(owner: BrowserWindow, tabId: string): BrowserUseTabRuntime | null {
    for (const runtime of this.runtimes.values()) {
      if (runtime.owner !== owner) continue
      const tab = runtime.tabs.get(tabId)
      if (tab) return tab
    }
    return null
  }

  private webContentsFor(owner: BrowserWindow, tabId: string): Electron.WebContents {
    if (this.closedTabIdsByOwner.get(owner)?.has(tabId)) {
      throw BrowserUseBackendError.pageClosed(tabId)
    }
    const tab = this.tabForId(owner, tabId)
    if (tab?.closed) throw BrowserUseBackendError.pageClosed(tabId)
    const wc = this.viewerHost.getTabWebContents(owner, tabId)
    if (!wc || wc.isDestroyed()) throw BrowserUseBackendError.pageClosed(tabId)
    return wc
  }

  private async ensureDebuggerAttached(tab: BrowserUseTabRuntime): Promise<void> {
    if (this.attachDebugger(tab)) this.viewports.attached(tab)
  }

  private attachDebugger(tab: BrowserUseTabRuntime): boolean {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const debuggerApi = wc.debugger as Electron.Debugger & {
      on?(event: 'message' | 'detach', listener: (...args: unknown[]) => void): void
      off?(event: 'message' | 'detach', listener: (...args: unknown[]) => void): void
    }
    if (!debuggerApi) {
      throw new Error(`Browser tab ${tab.id} does not expose Electron debugger/CDP.`)
    }
    const fresh = !tab.cdpAttached || !debuggerApi.isAttached()
    if (fresh) {
      debuggerApi.attach('1.3')
      tab.cdpAttached = true
    }
    if (!tab.debuggerMessageHandler && typeof debuggerApi.on === 'function') {
      tab.debuggerMessageHandler = (...args: unknown[]) => this.handleDebuggerMessage(tab, args)
      debuggerApi.on('message', tab.debuggerMessageHandler)
    }
    if (!tab.debuggerDetachHandler && typeof debuggerApi.on === 'function') {
      tab.debuggerDetachHandler = (...args: unknown[]) => {
        tab.cdpAttached = false
        this.screenshots.reset(wc)
        tab.targetSessions.clear()
        this.emitCdpEvent(tab, 'Inspector.detached', {
          reason: this.stringFromDebuggerArgs(args) ?? 'detached'
        })
      }
      debuggerApi.on('detach', tab.debuggerDetachHandler)
    }
    if (!tab.webContentsFailLoadHandler && typeof wc.on === 'function') {
      tab.webContentsFailLoadHandler = (...args: unknown[]) => {
        const failure = this.navigationFailureFromWebContentsArgs(tab, args)
        if (failure) this.recordNavigationFailure(tab, failure)
      }
      wc.on('did-fail-load', tab.webContentsFailLoadHandler)
    }
    return fresh
  }

  private detachDebugger(tab: BrowserUseTabRuntime): void {
    try {
      const wc = this.webContentsFor(tab.owner, tab.id)
      this.screenshots.reset(wc)
      const debuggerApi = wc.debugger as Electron.Debugger & {
        off?(event: 'message' | 'detach', listener: (...args: unknown[]) => void): void
      }
      if (tab.debuggerMessageHandler && typeof debuggerApi?.off === 'function') {
        debuggerApi.off('message', tab.debuggerMessageHandler)
      }
      if (tab.debuggerDetachHandler && typeof debuggerApi?.off === 'function') {
        debuggerApi.off('detach', tab.debuggerDetachHandler)
      }
      if (tab.webContentsFailLoadHandler && typeof wc.off === 'function') {
        wc.off('did-fail-load', tab.webContentsFailLoadHandler)
      }
      if (debuggerApi?.isAttached()) debuggerApi.detach()
    } catch {
      // Best effort only. Browser tab teardown should not be blocked by debugger cleanup.
    } finally {
      tab.cdpAttached = false
      tab.debuggerMessageHandler = undefined
      tab.debuggerDetachHandler = undefined
      tab.webContentsFailLoadHandler = undefined
      tab.targetSessions.clear()
    }
  }

  private handleDebuggerMessage(tab: BrowserUseTabRuntime, args: unknown[]): void {
    const eventOffset = typeof args[1] === 'string' ? 1 : 0
    const rawMethod = args[eventOffset]
    const method = typeof rawMethod === 'string' ? rawMethod : ''
    if (!method) return
    const rawParams = args[eventOffset + 1]
    const params = rawParams && typeof rawParams === 'object' && !Array.isArray(rawParams)
      ? rawParams as Record<string, unknown>
      : {}
    const rawSessionId = args[eventOffset + 2]
    const sessionId = typeof rawSessionId === 'string' ? rawSessionId : undefined
    if (method === 'Target.detachedFromTarget') {
      const targetId = typeof params.targetId === 'string' ? params.targetId : undefined
      if (targetId) tab.targetSessions.delete(targetId)
    }
    if (this.screenshots.consumesEvent(this.webContentsFor(tab.owner, tab.id), method, params, sessionId)) return
    this.emitCdpEvent(tab, method, params, sessionId)
  }

  private stringFromDebuggerArgs(args: unknown[]): string | undefined {
    for (const value of args) {
      if (typeof value === 'string' && value.trim()) return value
    }
    return undefined
  }

  private async cdpCommand<T = unknown>(
    tab: BrowserUseTabRuntime,
    method: string,
    params?: Record<string, unknown>,
    sessionId?: string,
    signal?: AbortSignal
  ): Promise<T> {
    await this.ensureDebuggerAttached(tab)
    await this.viewports.settled(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    const send = async () => await (sessionId
      ? wc.debugger.sendCommand(method, params, sessionId)
      : wc.debugger.sendCommand(method, params)) as T
    if (!sessionId && method === 'Page.captureScreenshot') {
      return await this.screenshots.captureCdp(this.screenshotContext(tab, signal), params ?? {}) as T
    }
    if (!sessionId && (method === 'Page.startScreencast' || method === 'Page.stopScreencast')) {
      return await this.screenshots.rawScreencast(this.screenshotContext(tab, signal), method, send)
    }
    return await send()
  }

  private operationUrl(tab: BrowserUseTabRuntime): string {
    try {
      return this.webContentsFor(tab.owner, tab.id).getURL() || 'about:blank'
    } catch {
      return 'unknown'
    }
  }

  private clearNavigationFailure(tab: BrowserUseTabRuntime): void {
    tab.lastNavigationFailure = undefined
  }

  private navigationFailureData(failure: BrowserUseNavigationFailure): Record<string, unknown> {
    return {
      errorCode: failure.errorCode,
      errorDescription: failure.errorDescription,
      validatedURL: failure.validatedURL,
      finalURL: failure.finalURL,
      isMainFrame: failure.isMainFrame
    }
  }

  private navigationFailureError(failure: BrowserUseNavigationFailure): BrowserUseBackendError {
    return BrowserUseBackendError.navigationFailed(
      failure.errorDescription || `Navigation failed${failure.errorCode == null ? '' : ` (${failure.errorCode})`}`,
      this.navigationFailureData(failure)
    )
  }

  private recordNavigationFailure(tab: BrowserUseTabRuntime, failure: BrowserUseNavigationFailure): void {
    const previous = tab.lastNavigationFailure
    const duplicate = previous &&
      previous.errorCode === failure.errorCode &&
      previous.errorDescription === failure.errorDescription &&
      previous.validatedURL === failure.validatedURL &&
      previous.finalURL === failure.finalURL &&
      Date.now() - previous.timestamp < 250
    tab.lastNavigationFailure = failure
    if (duplicate) return
    this.emitCdpEvent(tab, 'Page.navigationBlocked', this.navigationFailureData(failure))
  }

  private navigationFailureFromWebContentsArgs(
    tab: BrowserUseTabRuntime,
    args: unknown[]
  ): BrowserUseNavigationFailure | null {
    const rawCode = Number(args[1])
    const errorCode = Number.isFinite(rawCode) ? rawCode : undefined
    const errorDescription = typeof args[2] === 'string' && args[2].trim()
      ? args[2].trim()
      : 'Navigation failed'
    const validatedURL = typeof args[3] === 'string' && args[3].trim()
      ? args[3].trim()
      : this.operationUrl(tab)
    const isMainFrame = args[4] !== false
    if (!isMainFrame || errorCode === -3) return null
    return {
      errorCode,
      errorDescription,
      validatedURL,
      finalURL: this.operationUrl(tab),
      isMainFrame,
      timestamp: Date.now()
    }
  }

  private chromiumErrorPageFailure(
    tab: BrowserUseTabRuntime,
    validatedURL?: string
  ): BrowserUseNavigationFailure | null {
    const finalURL = this.operationUrl(tab)
    if (!isChromiumErrorPageUrl(finalURL)) return null
    return {
      errorDescription: 'Chromium error page after navigation.',
      validatedURL: validatedURL || finalURL,
      finalURL,
      isMainFrame: true,
      timestamp: Date.now()
    }
  }

  private throwIfNavigationFailed(tab: BrowserUseTabRuntime): void {
    if (tab.lastNavigationFailure) {
      throw this.navigationFailureError(tab.lastNavigationFailure)
    }
    const chromiumFailure = this.chromiumErrorPageFailure(tab)
    if (chromiumFailure) {
      this.recordNavigationFailure(tab, chromiumFailure)
      throw this.navigationFailureError(chromiumFailure)
    }
  }

  private safeTabTitle(tab: BrowserUseTabRuntime): string {
    try {
      return this.webContentsFor(tab.owner, tab.id).getTitle() || ''
    } catch {
      return ''
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
          reject(BrowserUseBackendError.commandTimeout(message, {
            operation,
            tabId: tab.id,
            url: currentUrl()
          }))
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
    try {
      await this.withBrowserOperation(
        tab,
        operation,
        () => this.viewerHost.loadAutomationUrl(tab.owner, { tabId: tab.id, url }),
        timeoutMs)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      if (message.startsWith('NavigationFailed:')) {
        const failure = tab.lastNavigationFailure ?? {
          errorDescription: message.replace(/^NavigationFailed:\s*/, '') || 'Navigation failed',
          validatedURL: url,
          finalURL: this.operationUrl(tab),
          isMainFrame: true,
          timestamp: Date.now()
        }
        this.recordNavigationFailure(tab, failure)
        throw this.navigationFailureError(failure)
      }
      throw error
    }
    const chromiumFailure = this.chromiumErrorPageFailure(tab, url)
    if (chromiumFailure) {
      this.recordNavigationFailure(tab, chromiumFailure)
      throw this.navigationFailureError(chromiumFailure)
    }
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
    this.throwIfNavigationFailed(tab)
    await this.waitForScriptReady(tab, Math.min(options.timeoutMs, this.blankTabReadyTimeoutMs()))
    const deadline = Date.now() + Math.max(1, Math.min(options.timeoutMs, 120_000))
    for (;;) {
      this.throwIfNavigationFailed(tab)
      const signal = this.getRuntimeForTab(tab).activeAbortSignal
      if (signal?.aborted) throw new Error(`Browser operation '${options.operation}' was cancelled for tab ${tab.id}.`)
      const rawState = await this.executeJavaScript<unknown>(tab, `
        (() => {
          const bodyText = (document.body?.innerText || '').trim();
          const interactive = document.querySelectorAll('a,button,input,textarea,select,summary,[role="button"],[role="link"]').length;
          const appRoot = document.querySelector('#app, #root, [data-v-app], main, nav, header');
          return {
            url: location.href,
            title: document.title,
            readyState: document.readyState,
            hasBody: Boolean(document.body),
            bodyTextLength: bodyText.length,
            interactiveCount: interactive,
            appRootTextLength: (appRoot?.textContent || '').trim().length
          };
        })()
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
      const hasRequiredContent = hasUsefulContent || (
        state.hasBody &&
        (options.operation === 'domSnapshot.ready' || options.operation === 'waitForLoadState.domcontentloaded')
      )
      if (documentReady && (blank || !options.requireContent || hasRequiredContent)) return
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
        hasBody: boolean
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
      hasBody: state.hasBody === true,
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

  private registerTab(
    owner: BrowserWindow,
    runtime: BrowserUseThreadRuntime,
    id: string,
    userOwned: boolean
  ): BrowserUseTabRuntime {
    const existing = runtime.tabs.get(id)
    if (existing) {
      if (userOwned) existing.userOwned = true
      if (userOwned) existing.exposedToRenderer = true
      return existing
    }
    const wc = this.webContentsFor(owner, id)
    this.closedTabIdsByOwner.get(owner)?.delete(id)
    const tab: BrowserUseTabRuntime = {
      id,
      owner,
      logs: [],
      clipboardItems: [],
      userOwned,
      exposedToRenderer: userOwned,
      targetSessions: new Map()
    }
    runtime.tabs.set(id, tab)

    const onConsole = (_event: unknown, level: number, message: string) => {
      const levelNames = ['debug', 'info', 'warn', 'error'] as const
      tab.logs.push({
        level: levelNames[level as number] ?? String(level ?? 'log'),
        message,
        timestamp: new Date().toISOString(),
        url: wc.getURL()
      })
    }
    const onDestroyed = () => {
      tab.disposeListeners?.()
      this.detachDebugger(tab)
      runtime.tabs.delete(id)
      this.forgetBackendTab(runtime, tab)
      if (runtime.selectedTabId === id) runtime.selectedTabId = null
    }
    wc.on('console-message', onConsole)
    wc.once('destroyed', onDestroyed)
    tab.disposeListeners = () => {
      wc.off('console-message', onConsole)
      wc.off('destroyed', onDestroyed)
    }
    return tab
  }

  private tabSnapshot(tab: BrowserUseTabRuntime): { url: string; title: string; loading: boolean } {
    const snapshot = this.viewerHost.snapshotState(tab.owner, tab.id)
    if (snapshot) return { url: snapshot.currentUrl, title: snapshot.title, loading: snapshot.loading }
    const wc = this.webContentsFor(tab.owner, tab.id)
    return { url: wc.getURL(), title: wc.getTitle(), loading: wc.isLoading() }
  }

  private setAutomationState(
    runtime: BrowserUseThreadRuntime,
    tab: BrowserUseTabRuntime,
    active: boolean,
    action?: string,
    release?: boolean
  ): void {
    this.viewerHost.setAutomationState(tab.owner, {
      tabId: tab.id,
      active,
      release,
      sessionName: runtime.sessionName,
      action
    })
  }

  private markAutomation(tab: BrowserUseTabRuntime, action: string): void {
    const runtime = this.getRuntimeForTab(tab)
    this.setAutomationState(runtime, tab, true, action)
  }

  private async goBack(tab: BrowserUseTabRuntime): Promise<void> {
    this.markAutomation(tab, 'back')
    this.clearNavigationFailure(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoBack()) wc.navigationHistory.goBack()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
  }

  private async goForward(tab: BrowserUseTabRuntime): Promise<void> {
    this.markAutomation(tab, 'forward')
    this.clearNavigationFailure(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoForward()) wc.navigationHistory.goForward()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
  }

  private async reload(tab: BrowserUseTabRuntime): Promise<void> {
    this.markAutomation(tab, 'reload')
    this.clearNavigationFailure(tab)
    this.webContentsFor(tab.owner, tab.id).reload()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
  }

  private releaseTab(runtime: BrowserUseThreadRuntime, tab: BrowserUseTabRuntime, status?: BrowserFinalizeKeepStatus): void {
    this.setAutomationState(runtime, tab, false, status, true)
    this.viewports.release(tab)
    this.detachDebugger(tab)
    tab.disposeListeners?.()
    this.forgetBackendTab(runtime, tab)
    runtime.tabs.delete(tab.id)
    if (runtime.selectedTabId === tab.id) runtime.selectedTabId = null
  }

  private closeTab(tab: BrowserUseTabRuntime): void {
    if (tab.closed) return
    this.markAutomation(tab, 'close')
    const runtime = this.getRuntimeForTab(tab)
    const shouldNotifyRenderer = tab.exposedToRenderer === true
    this.detachDebugger(tab)
    this.forgetBackendTab(runtime, tab)
    this.viewerHost.destroyTab(tab.owner, tab.id)
    if (shouldNotifyRenderer) {
      this.emitClose(tab.owner, {
        threadId: runtime.threadId,
        tabId: tab.id
      })
    }
    runtime.tabs.delete(tab.id)
    tab.closed = true
    let closedTabIds = this.closedTabIdsByOwner.get(tab.owner)
    if (!closedTabIds) {
      closedTabIds = new Set()
      this.closedTabIdsByOwner.set(tab.owner, closedTabIds)
    }
    closedTabIds.add(tab.id)
    if (runtime.selectedTabId === tab.id) runtime.selectedTabId = null
  }

  private getRuntimeForTab(tab: BrowserUseTabRuntime): BrowserUseThreadRuntime {
    for (const runtime of this.runtimes.values()) {
      if (runtime.tabs.get(tab.id) === tab) return runtime
    }
    throw BrowserUseBackendError.pageClosed(tab.id)
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

  private screenshotContext(tab: BrowserUseTabRuntime, signal?: AbortSignal): BrowserScreenshotContext {
    const runtime = this.getRuntimeForTab(tab)
    const page = this.webContentsFor(tab.owner, tab.id)
    return {
      tabId: tab.id, page,
      layoutSize: this.viewerHost.getLayoutSize(tab.owner, tab.id),
      visible: this.viewerHost.isVisible(tab.owner, tab.id),
      timeoutMs: this.operationTimeoutMs(),
      signal: signal ?? runtime.activeAbortSignal,
      send: async (method, params) => await page.debugger.sendCommand(method, params),
      setSurface: size => this.viewerHost.setCaptureSurface(tab.owner, tab.id, size),
      diagnostic: message => runtime.logs.push(`${message} tab=${tab.id}`)
    }
  }

  private async screenshot(
    tab: BrowserUseTabRuntime,
    options?: { fullPage?: boolean; clip?: Electron.Rectangle },
    signal?: AbortSignal
  ): Promise<BrowserUseImageResult> {
    this.markAutomation(tab, 'screenshot')
    await this.ensureDebuggerAttached(tab)
    await this.viewports.settled(tab)
    const dataBase64 = await this.withBrowserOperation(tab, 'screenshot', () =>
      this.screenshots.screenshot(this.screenshotContext(tab, signal), options))
    return { mediaType: 'image/jpeg', dataBase64 }
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
    const accessibilitySnapshot = snapshot.accessibilitySnapshot || this.formatAccessibilitySnapshot(snapshot.elements)
    return JSON.stringify({
      title: snapshot.title,
      url: snapshot.url,
      bodyText: snapshot.bodyText,
      accessibilitySnapshot,
      elements: snapshot.elements
    }, null, 2)
  }

  private normalizeSnapshotPayload(rawSnapshot: unknown): {
    title: string
    url: string
    bodyText: string
    accessibilitySnapshot: string
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
      accessibilitySnapshot: typeof obj.accessibilitySnapshot === 'string' ? obj.accessibilitySnapshot : '',
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
      'Boolean(window.__dotcraftBrowserUseSnapshot && window.__dotcraftBrowserUseLocator && window.__dotcraftBrowserUseNode)',
      'playwright.inject.check').catch(() => false)
    if (installed === true) return

    await this.executeJavaScript(tab, browserObservationSource(playwrightInjectedScriptSource), 'playwright.inject')
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
    this.throwIfNavigationFailed(tab)
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
        try {
          this.throwIfNavigationFailed(tab)
        } catch (error) {
          cleanup()
          reject(error)
          return
        }
        cleanup()
        resolve()
      }
      const onFailLoad = (...args: unknown[]) => {
        const failure = this.navigationFailureFromWebContentsArgs(tab, args)
        if (!failure) return
        this.recordNavigationFailure(tab, failure)
        cleanup()
        reject(this.navigationFailureError(failure))
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState.commit' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-start-loading', done)
        wc.off('did-navigate', done)
        wc.off('did-fail-load', onFailLoad)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-start-loading', done)
      wc.once('did-navigate', done)
      wc.once('did-fail-load', onFailLoad)
    })
  }

  private waitForLoadEvent(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    this.throwIfNavigationFailed(tab)
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
        try {
          this.throwIfNavigationFailed(tab)
        } catch (error) {
          cleanup()
          reject(error)
          return
        }
        cleanup()
        resolve()
      }
      const onFailLoad = (...args: unknown[]) => {
        const failure = this.navigationFailureFromWebContentsArgs(tab, args)
        if (!failure) return
        this.recordNavigationFailure(tab, failure)
        cleanup()
        reject(this.navigationFailureError(failure))
      }
      const onAbort = () => {
        cleanup()
        reject(new Error(`Browser operation 'waitForLoadState' was cancelled for tab ${tab.id}.`))
      }
      const cleanup = () => {
        clearTimeout(timeout)
        wc.off('did-finish-load', done)
        wc.off('did-stop-loading', done)
        wc.off('did-fail-load', onFailLoad)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.once('did-finish-load', done)
      wc.once('did-stop-loading', done)
      wc.once('did-fail-load', onFailLoad)
    })
  }

  private async waitForNetworkIdle(tab: BrowserUseTabRuntime, timeoutMs: number): Promise<void> {
    const wc = this.webContentsFor(tab.owner, tab.id)
    const deadline = Date.now() + timeoutMs
    for (;;) {
      this.throwIfNavigationFailed(tab)
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
          try {
            this.throwIfNavigationFailed(tab)
          } catch (error) {
            cleanup()
            reject(error)
            return
          }
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
        const onFailLoad = (...args: unknown[]) => {
          const failure = this.navigationFailureFromWebContentsArgs(tab, args)
          if (!failure) return
          this.recordNavigationFailure(tab, failure)
          cleanup()
          reject(this.navigationFailureError(failure))
        }
        const cleanup = () => {
          clearTimeout(quietTimer)
          clearTimeout(hardTimer)
          wc.off('did-start-loading', restart)
          wc.off('did-stop-loading', restart)
          wc.off('did-fail-load', onFailLoad)
          signal?.removeEventListener('abort', onAbort)
        }
        signal?.addEventListener('abort', onAbort, { once: true })
        wc.on('did-start-loading', restart)
        wc.on('did-stop-loading', restart)
        wc.on('did-fail-load', onFailLoad)
        restart()
      })
      if (!wc.isLoading()) return
    }
  }

}

export const browserUseManager = new BrowserUseManager()
export { BROWSER_USE_OPEN_CHANNEL, BROWSER_USE_CLOSE_CHANNEL }
