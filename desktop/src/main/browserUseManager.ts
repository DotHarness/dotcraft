import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { mkdtemp, readFile, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, extname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { BrowserWindow } from 'electron'
import {
  BrowserUseBackendError,
  BrowserUseBackendServer,
  type BrowserUseBackendCommandContext,
  type BrowserUseBackendRequestHandler
} from './browserUseBackendServer'
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
const BROWSER_USE_MAX_RESULT_BYTES = 1024 * 1024
const BROWSER_USE_DISPLAY_TRUNCATE_MAX_CHARS = 100_000
const READONLY_EVALUATE_ERROR = 'ReadonlyEvaluateViolation'
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
  },
  {
    id: 'webmcp',
    description: 'List and invoke WebMCP tools explicitly exposed by the current page through navigator.modelContext.',
    docs: 'docs/capabilities/tab/webmcp.md'
  }
]
const BROWSER_USE_PAGE_ASSET_BUNDLE_KINDS = new Set(['font', 'image', 'stylesheet', 'video'])

type BrowserUseLoadState = 'commit' | 'domcontentloaded' | 'load' | 'networkidle'

const READONLY_EVALUATE_DENY_PATTERNS: Array<{ pattern: RegExp; label: string }> = [
  { pattern: /\b(?:window\.)?(?:scrollTo|scrollBy|open|close|stop|print)\s*\(/, label: 'window mutation or viewport side effect' },
  { pattern: /\b(?:window\.)?location(?:\s*=|\.[A-Za-z_$][\w$]*\s*=|\.(?:assign|replace|reload)\s*\()/, label: 'navigation mutation' },
  { pattern: /\bhistory\.(?:pushState|replaceState|go|back|forward)\s*\(/, label: 'history mutation' },
  { pattern: /\bdocument\.(?:write|writeln|open|close)\s*\(/, label: 'document mutation' },
  { pattern: /\bdocument\.cookie\s*=/, label: 'cookie write' },
  { pattern: /\b(?:localStorage|sessionStorage)\.(?:setItem|removeItem|clear)\s*\(/, label: 'storage mutation' },
  { pattern: /\bnavigator\.sendBeacon\s*\(/, label: 'network side effect' },
  { pattern: /\b(?:fetch|XMLHttpRequest)\s*\(/, label: 'network request' },
  { pattern: /\bnew\s+XMLHttpRequest\s*\(/, label: 'network request' },
  { pattern: /\.(?:appendChild|removeChild|replaceChild|insertBefore|insertAdjacentHTML|insertAdjacentElement|setAttribute|removeAttribute|toggleAttribute|click|focus|blur|submit|requestSubmit|dispatchEvent)\s*\(/, label: 'DOM or interaction mutation' },
  { pattern: /\.classList\.(?:add|remove|toggle|replace)\s*\(/, label: 'classList mutation' },
  { pattern: /\.(?:innerHTML|outerHTML|textContent|innerText|value|checked|disabled|selected|selectedIndex|style)\s*=/, label: 'DOM property mutation' },
  { pattern: /\.style\.[A-Za-z_$][\w$]*\s*=/, label: 'style mutation' },
  { pattern: /\b(?:eval|Function)\s*\(/, label: 'dynamic code execution' },
  { pattern: /\bnew\s+Function\s*\(/, label: 'dynamic code execution' }
]

function maskJavaScriptLiteralsAndComments(source: string): string {
  let output = ''
  let index = 0
  let state: 'code' | 'single' | 'double' | 'template' | 'lineComment' | 'blockComment' = 'code'
  while (index < source.length) {
    const char = source[index] ?? ''
    const next = source[index + 1] ?? ''
    if (state === 'code') {
      if (char === "'" || char === '"' || char === '`') {
        state = char === "'" ? 'single' : char === '"' ? 'double' : 'template'
        output += ' '
        index += 1
        continue
      }
      if (char === '/' && next === '/') {
        state = 'lineComment'
        output += '  '
        index += 2
        continue
      }
      if (char === '/' && next === '*') {
        state = 'blockComment'
        output += '  '
        index += 2
        continue
      }
      output += char
      index += 1
      continue
    }
    if (state === 'lineComment') {
      output += char === '\n' ? '\n' : ' '
      if (char === '\n') state = 'code'
      index += 1
      continue
    }
    if (state === 'blockComment') {
      output += char === '\n' ? '\n' : ' '
      if (char === '*' && next === '/') {
        output += ' '
        index += 2
        state = 'code'
      } else {
        index += 1
      }
      continue
    }
    output += char === '\n' ? '\n' : ' '
    if (char === '\\') {
      output += next === '\n' ? '\n' : ' '
      index += 2
      continue
    }
    if ((state === 'single' && char === "'") || (state === 'double' && char === '"') || (state === 'template' && char === '`')) {
      state = 'code'
    }
    index += 1
  }
  return output
}

function readonlyEvaluateViolation(label: string): Error {
  return new Error(`${READONLY_EVALUATE_ERROR}: ${label} is not allowed in tab.playwright.evaluate. Use locators, CUA, DOM-CUA, navigation helpers, or wait helpers for page side effects.`)
}

function assertReadOnlyEvaluateSource(source: string): void {
  const stripped = maskJavaScriptLiteralsAndComments(source)
  for (const { pattern, label } of READONLY_EVALUATE_DENY_PATTERNS) {
    if (pattern.test(stripped)) throw readonlyEvaluateViolation(label)
  }
}

function readOnlyEvaluateSource(source: string): string {
  assertReadOnlyEvaluateSource(source)
  return `(() => {
    const __dotcraftSource = ${JSON.stringify(source)};
    const __dotcraftRestore = [];
    const __dotcraftThrow = (name) => {
      throw new Error(${JSON.stringify(READONLY_EVALUATE_ERROR)} + ': ' + name + ' is not allowed in tab.playwright.evaluate. Use locators, CUA, DOM-CUA, navigation helpers, or wait helpers for page side effects.');
    };
    const __dotcraftPatch = (target, key, label) => {
      try {
        if (!target) return;
        const original = target[key];
        if (typeof original !== 'function') return;
        target[key] = function readonlyEvaluateBlocked() { __dotcraftThrow(label); };
        __dotcraftRestore.push(() => {
          try { target[key] = original; } catch (_) {}
        });
      } catch (_) {}
    };
    __dotcraftPatch(globalThis, 'fetch', 'fetch');
    __dotcraftPatch(globalThis, 'XMLHttpRequest', 'XMLHttpRequest');
    __dotcraftPatch(globalThis, 'open', 'window.open');
    __dotcraftPatch(globalThis, 'scrollTo', 'window.scrollTo');
    __dotcraftPatch(globalThis, 'scrollBy', 'window.scrollBy');
    __dotcraftPatch(globalThis.navigator, 'sendBeacon', 'navigator.sendBeacon');
    __dotcraftPatch(globalThis.history, 'pushState', 'history.pushState');
    __dotcraftPatch(globalThis.history, 'replaceState', 'history.replaceState');
    __dotcraftPatch(globalThis.history, 'go', 'history.go');
    __dotcraftPatch(globalThis.history, 'back', 'history.back');
    __dotcraftPatch(globalThis.history, 'forward', 'history.forward');
    __dotcraftPatch(globalThis.location, 'assign', 'location.assign');
    __dotcraftPatch(globalThis.location, 'replace', 'location.replace');
    __dotcraftPatch(globalThis.location, 'reload', 'location.reload');
    __dotcraftPatch(globalThis.localStorage, 'setItem', 'localStorage.setItem');
    __dotcraftPatch(globalThis.localStorage, 'removeItem', 'localStorage.removeItem');
    __dotcraftPatch(globalThis.localStorage, 'clear', 'localStorage.clear');
    __dotcraftPatch(globalThis.sessionStorage, 'setItem', 'sessionStorage.setItem');
    __dotcraftPatch(globalThis.sessionStorage, 'removeItem', 'sessionStorage.removeItem');
    __dotcraftPatch(globalThis.sessionStorage, 'clear', 'sessionStorage.clear');
    __dotcraftPatch(globalThis.Document?.prototype, 'write', 'document.write');
    __dotcraftPatch(globalThis.Document?.prototype, 'writeln', 'document.writeln');
    __dotcraftPatch(globalThis.Node?.prototype, 'appendChild', 'Node.appendChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'removeChild', 'Node.removeChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'replaceChild', 'Node.replaceChild');
    __dotcraftPatch(globalThis.Node?.prototype, 'insertBefore', 'Node.insertBefore');
    __dotcraftPatch(globalThis.Element?.prototype, 'setAttribute', 'Element.setAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'removeAttribute', 'Element.removeAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'toggleAttribute', 'Element.toggleAttribute');
    __dotcraftPatch(globalThis.Element?.prototype, 'insertAdjacentHTML', 'Element.insertAdjacentHTML');
    __dotcraftPatch(globalThis.Element?.prototype, 'insertAdjacentElement', 'Element.insertAdjacentElement');
    __dotcraftPatch(globalThis.EventTarget?.prototype, 'dispatchEvent', 'EventTarget.dispatchEvent');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'click', 'HTMLElement.click');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'focus', 'HTMLElement.focus');
    __dotcraftPatch(globalThis.HTMLElement?.prototype, 'blur', 'HTMLElement.blur');
    __dotcraftPatch(globalThis.HTMLFormElement?.prototype, 'submit', 'HTMLFormElement.submit');
    __dotcraftPatch(globalThis.HTMLFormElement?.prototype, 'requestSubmit', 'HTMLFormElement.requestSubmit');
    return (async () => {
      try {
        return await (0, eval)(__dotcraftSource);
      } finally {
        for (let i = __dotcraftRestore.length - 1; i >= 0; i -= 1) __dotcraftRestore[i]();
      }
    })();
  })()`
}

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
  clipboardItems: BrowserUseClipboardItem[]
  adopted?: boolean
  userOwned?: boolean
  keptStatus?: BrowserFinalizeKeepStatus
  closed?: boolean
  cdpAttached?: boolean
  targetSessions: Map<string, string>
  backendQueue?: Promise<void>
  debuggerMessageHandler?: (...args: unknown[]) => void
  debuggerDetachHandler?: (...args: unknown[]) => void
  webContentsFailLoadHandler?: (...args: unknown[]) => void
  lastNavigationFailure?: BrowserUseNavigationFailure
  snapshotRefs: Map<string, BrowserUseElementMatch>
  domCuaNodes: Map<string, BrowserUseElementMatch>
  pageAssetInventories: Map<string, BrowserUsePageAssetInventory>
  snapshotGeneration: number
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

interface BrowserUseBackendPendingCommand {
  abortController: AbortController
  evaluationId?: string
  operation: string
  tabId?: string
}

interface BrowserUseThreadRuntime {
  threadId: string
  owner: BrowserWindow
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
  backendTabIds: Map<string, number>
  backendTabs: Map<number, BrowserUseTabRuntime>
  recentUserBackendTabIds: Set<number>
  recentOpenTabIds: Set<string>
  pendingBackendCommands: Map<unknown, BrowserUseBackendPendingCommand>
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

type BrowserUseLocatorKind = 'css' | 'text' | 'role' | 'label' | 'placeholder' | 'testId' | 'ref' | 'and' | 'or'

interface BrowserUseLocatorTextMatcher {
  value?: string
  pattern?: string
  flags?: string
  exact?: boolean
}

interface BrowserUseLocatorFilter {
  kind: 'hasText' | 'hasNotText' | 'visible' | 'has' | 'hasNot'
  value?: boolean
  matcher?: BrowserUseLocatorTextMatcher
  descriptor?: BrowserUseLocatorDescriptor
}

interface BrowserUseLocatorDescriptor {
  kind: BrowserUseLocatorKind
  value: string
  exact?: boolean
  name?: string
  index?: number
  filters?: BrowserUseLocatorFilter[]
  left?: BrowserUseLocatorDescriptor
  right?: BrowserUseLocatorDescriptor
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

function isChromiumErrorPageUrl(url: string): boolean {
  return /^chrome-error:\/\//i.test(url)
}

export class BrowserUseManager implements BrowserUseBackendRequestHandler {
  private readonly runtimes = new Map<string, BrowserUseThreadRuntime>()
  private readonly runtimesBySessionId = new Map<string, BrowserUseThreadRuntime>()
  private readonly pendingApprovals = new Map<string, {
    resolve: (action: BrowserUseApprovalResponseAction) => void
    timer: ReturnType<typeof setTimeout>
    onClosed: () => void
    owner: BrowserWindow
  }>()
  private readonly closedTabIdsByOwner = new WeakMap<BrowserWindow, Set<string>>()
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
    agent: Record<string, unknown>
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

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime) return { ok: false }
    for (const tab of [...runtime.tabs.values()]) {
      this.detachDebugger(tab)
      if (tab.adopted || tab.userOwned) {
        this.setAutomationState(runtime, tab, false)
        tab.adopted = false
        tab.userOwned = false
      } else {
        this.viewerHost.destroyTab(tab.owner, tab.id)
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
      recentOpenTabIds: new Set<string>(),
      pendingBackendCommands: new Map<unknown, BrowserUseBackendPendingCommand>(),
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

  async handleBrowserUseBackendRequest(
    method: string,
    params: Record<string, unknown>,
    context?: BrowserUseBackendCommandContext
  ): Promise<unknown> {
    if (method === 'ping') return 'pong'
    const runtime = this.runtimeForBackendParams(params)
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
            `Browser backend command ${operation} timed out after ${timeoutMs}ms.`
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

  private isBackendResultCapExempt(operation: string, params: Record<string, unknown>): boolean {
    if (operation === 'executeCdp' && this.stringParam(params, 'method') === 'Page.captureScreenshot') return true
    return operation === 'executeUnhandledCommand' &&
      (this.stringParam(params, 'type') === 'playwright_element_screenshot' ||
        this.stringParam(params, 'type') === 'tab_screenshot')
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
    return [...runtime.tabs.values()].map((tab) => this.backendTabSnapshot(runtime, tab))
  }

  private backendUserTabList(runtime: BrowserUseThreadRuntime): Record<string, unknown>[] {
    const candidate = this.viewerHost.getAutomationTargetTab?.(runtime.owner, runtime.threadId)
    if (candidate && !runtime.tabs.has(candidate.tabId)) {
      this.registerTab(runtime.owner, runtime, candidate.tabId, false, true)
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
    const keep = this.parseBackendFinalizeKeep(params.keep)
    const kept: number[] = []
    const closed: number[] = []
    const released: number[] = []
    for (const tab of [...runtime.tabs.values()]) {
      const backendTabId = this.backendTabIdFor(runtime, tab)
      const keptStatus = keep.get(backendTabId)
      if (keptStatus) {
        tab.keptStatus = keptStatus
        kept.push(backendTabId)
        this.setAutomationState(runtime, tab, true, keptStatus)
        continue
      }
      if (tab.adopted || tab.userOwned) {
        this.setAutomationState(runtime, tab, false)
        tab.adopted = false
        tab.userOwned = false
        released.push(backendTabId)
        continue
      }
      this.closeTab(tab)
      closed.push(backendTabId)
    }
    return { ok: true, kept, closed, released }
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
    return await this.queueBackendTabCommand(tab, async () => {
      try {
        if (method === 'Page.navigate') {
          return await this.backendCdpNavigate(runtime, tab, commandParams, sessionId)
        }
        if (method === 'Page.reload') {
          this.markAutomation(tab, 'reload')
          this.clearNavigationFailure(tab)
          this.invalidatePageScopedCaches(tab)
        }
        if (method === 'Page.close' || method === 'Target.closeTarget') {
          this.closeTab(tab)
          return {}
        }
        return await this.cdpCommand(tab, method, commandParams, sessionId)
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
    this.markAutomation(tab, 'navigate')
    this.clearNavigationFailure(tab)
    this.invalidatePageScopedCaches(tab)
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
      case 'runtime_config':
        return {
          display_truncate_max_chars: BROWSER_USE_DISPLAY_TRUNCATE_MAX_CHARS,
          max_browser_result_bytes: BROWSER_USE_MAX_RESULT_BYTES
        }
      case 'browser_visibility_get':
        return { visible: runtime.browserVisible }
      case 'browser_visibility_set':
        return this.backendBrowserVisibilitySet(runtime, params)
      case 'browser_viewport_set':
        return this.backendBrowserViewportSet(runtime, params)
      case 'browser_viewport_reset':
        return this.backendBrowserViewportReset(runtime)
      case 'browser_user_open_tabs':
        return { tabs: this.backendUserTabList(runtime).map((tab) => this.stringifyBackendTabId(tab)) }
      case 'browser_user_claim_tab':
        return this.stringifyBackendTabId(this.backendClaimUserTab(runtime, params))
      case 'browser_user_history':
        throw BrowserUseBackendError.unsupportedApi('browser.user.history is not supported by Desktop IAB')
      case 'name_session':
        return this.backendNameSession(runtime, params)
      case 'tabs_content':
        return await this.backendTabsContent(runtime, params)
      case 'tab_dev_logs':
        return this.backendTabDevLogs(runtime, params)
      case 'tab_screenshot':
        return await this.backendTabScreenshot(runtime, params)
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
      case 'playwright_element_info':
        return await this.backendPlaywrightElementInfo(runtime, params, signal)
      case 'playwright_element_screenshot':
        return await this.backendPlaywrightElementScreenshot(runtime, params, signal)
      case 'playwright_evaluate':
        return await this.backendPlaywrightEvaluate(runtime, params)
      case 'playwright_dom_snapshot':
        return await this.backendPlaywrightDomSnapshot(runtime, params)
      case 'playwright_wait_for_timeout':
        return await this.backendPlaywrightWaitForTimeout(params)
      case 'playwright_wait_for_url':
        return await this.backendPlaywrightWaitForUrl(runtime, params)
      case 'playwright_wait_for_load_state':
        return await this.backendPlaywrightWaitForLoadState(runtime, params)
      case 'playwright_locator_click':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'click')
      case 'playwright_locator_dblclick':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'dblclick')
      case 'playwright_locator_fill':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'fill')
      case 'playwright_locator_press':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'press')
      case 'playwright_locator_wait_for':
        return await this.backendPlaywrightLocatorWaitFor(runtime, params)
      case 'playwright_locator_count':
        return await this.backendPlaywrightLocatorCount(runtime, params)
      case 'playwright_locator_select_option':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'selectOption')
      case 'playwright_locator_set_checked':
        return await this.backendPlaywrightLocatorAction(runtime, params, signal, 'setChecked')
      case 'playwright_locator_is_visible':
        return await this.backendPlaywrightLocatorIsVisible(runtime, params)
      case 'playwright_locator_is_enabled':
        return await this.backendPlaywrightLocatorIsEnabled(runtime, params)
      case 'playwright_locator_all_text_contents':
        return await this.backendPlaywrightLocatorAllTextContents(runtime, params)
      case 'playwright_locator_text_content':
        return await this.backendPlaywrightLocatorTextContent(runtime, params)
      case 'playwright_locator_inner_text':
        return await this.backendPlaywrightLocatorInnerText(runtime, params)
      case 'playwright_locator_get_attribute':
        return await this.backendPlaywrightLocatorGetAttribute(runtime, params)
      case 'playwright_locator_read_all':
        return await this.backendPlaywrightLocatorReadAll(runtime, params)
      case 'cua_move':
        return await this.backendCuaAction(runtime, params, signal, 'move')
      case 'cua_click':
        return await this.backendCuaAction(runtime, params, signal, 'click')
      case 'cua_double_click':
        return await this.backendCuaAction(runtime, params, signal, 'double_click')
      case 'cua_drag':
        return await this.backendCuaAction(runtime, params, signal, 'drag')
      case 'cua_keypress':
        return await this.backendCuaAction(runtime, params, signal, 'keypress')
      case 'cua_scroll':
        return await this.backendCuaAction(runtime, params, signal, 'scroll')
      case 'cua_type':
        return await this.backendCuaAction(runtime, params, signal, 'type')
      case 'dom_cua_get_visible_dom':
        return await this.domCuaVisibleDom(this.backendTabForCommand(runtime, params))
      case 'dom_cua_click':
        return await this.backendDomCuaAction(runtime, params, signal, 'click')
      case 'dom_cua_double_click':
        return await this.backendDomCuaAction(runtime, params, signal, 'double_click')
      case 'dom_cua_keypress':
        return await this.backendDomCuaAction(runtime, params, signal, 'keypress')
      case 'dom_cua_scroll':
        return await this.backendDomCuaAction(runtime, params, signal, 'scroll')
      case 'dom_cua_type':
        return await this.backendDomCuaAction(runtime, params, signal, 'type')
      case 'tab_page_assets_list':
        return await this.listPageAssets(this.backendTabForCommand(runtime, params))
      case 'tab_page_assets_bundle':
        return await this.backendPageAssetsBundle(runtime, params)
      case 'webmcp_list_tools':
        return await this.backendWebMcpListTools(runtime, params)
      case 'webmcp_invoke_tool':
        return await this.backendWebMcpInvokeTool(runtime, params)
      case 'tab_content_export':
        throw BrowserUseBackendError.unsupportedApi('tab_content_export')
      case 'tab_content_export_gsuite':
        throw BrowserUseBackendError.unsupportedApi('tab_content_export_gsuite')
      case 'playwright_wait_for_download':
      case 'playwright_download_path':
      case 'playwright_locator_download_media':
      case 'cua_download_media':
      case 'dom_cua_download_media':
        throw BrowserUseBackendError.unsupportedApi('ordinary downloads are not supported by Desktop IAB')
      case 'playwright_wait_for_file_chooser':
      case 'playwright_file_chooser_set_files':
        throw BrowserUseBackendError.unsupportedApi('ordinary file upload is not supported by Desktop IAB')
      case 'selected_tab': {
        const tab = await this.getOrAdoptSelectedTab(runtime.owner, runtime)
        return this.stringifyBackendTabId(this.backendTabSnapshot(runtime, tab))
      }
      case 'list_tabs':
        return { tabs: this.backendTabList(runtime).map((tab) => this.stringifyBackendTabId(tab)) }
      case 'create_tab': {
        const tab = await this.createTab(runtime.owner, runtime)
        runtime.selectedTabId = tab.id
        return { id: String(this.backendTabIdFor(runtime, tab)) }
      }
      case 'close_tab': {
        const tab = this.backendTabForCommand(runtime, params)
        this.closeTab(tab)
        return {}
      }
      case 'navigate_tab_url': {
        const tab = this.backendTabForCommand(runtime, params)
        const url = this.stringParam(params, 'url')
        if (!url) throw BrowserUseBackendError.invalidArgument('navigate_tab_url requires a url.')
        await this.navigate(tab, url)
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
      case 'tab_id': {
        const tab = this.backendTabForCommand(runtime, params)
        return { id: String(this.backendTabIdFor(runtime, tab)) }
      }
      default:
        throw BrowserUseBackendError.unsupportedApi(`executeUnhandledCommand(${type})`)
    }
  }

  private async backendTabScreenshot(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const clip = this.backendScreenshotClip(params)
    const image = await this.screenshot(tab, {
      fullPage: params.fullPage === true,
      ...(clip ? { clip } : {})
    })
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

  private async backendPlaywrightEvaluate(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const script = this.stringParam(params, 'script')
    if (!script) throw BrowserUseBackendError.invalidArgument('playwright_evaluate requires script.')
    const options = this.objectParam(params, 'options')
    const hasArg = Object.prototype.hasOwnProperty.call(params, 'arg')
    const source = hasArg
      ? `((fn, arg) => fn(arg))(${script}, ${this.evaluateArgSource(params.arg)})`
      : script
    return {
      value: await this.evaluateSourceInPage(tab, source, {
        timeoutMs: this.numberParam(options, 'timeoutMs') ??
          this.numberParam(options, 'timeout') ??
          this.numberParam(params, 'timeout_ms') ??
          this.numberParam(params, 'timeoutMs')
      })
    }
  }

  private async backendPlaywrightDomSnapshot(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { dom_snapshot: await this.domSnapshot(tab) }
  }

  private async backendPlaywrightWaitForTimeout(params: Record<string, unknown>): Promise<Record<string, unknown>> {
    const timeoutMs = Math.max(0, Math.min(Math.floor(Number(params.timeout_ms ?? params.timeoutMs ?? 0) || 0), 120_000))
    await new Promise((resolve) => setTimeout(resolve, timeoutMs))
    return {}
  }

  private async backendPlaywrightWaitForUrl(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const url = this.stringParam(params, 'url')
    if (!url) throw BrowserUseBackendError.invalidArgument('playwright_wait_for_url requires url.')
    await this.waitForUrl(tab, url, this.backendCommandTimeoutMs(params))
    return { url: this.operationUrl(tab) }
  }

  private async backendPlaywrightWaitForLoadState(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    await this.waitForLoad(tab, this.stringParam(params, 'state') ?? 'load', this.backendCommandTimeoutMs(params))
    return {}
  }

  private backendLocatorDescriptor(params: Record<string, unknown>): BrowserUseLocatorDescriptor {
    const selector = this.stringParam(params, 'selector')
    if (!selector) throw BrowserUseBackendError.invalidArgument('Playwright locator command requires selector.')
    return { kind: 'css', value: selector }
  }

  private async backendPlaywrightLocatorAction(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal: AbortSignal | undefined,
    action: 'click' | 'dblclick' | 'fill' | 'press' | 'selectOption' | 'setChecked'
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const descriptor = this.backendLocatorDescriptor(params)
    return await this.queueBackendTabCommand(tab, async () => {
      if (action === 'click') await this.locatorClick(tab, descriptor)
      else if (action === 'dblclick') await this.locatorDoubleClick(tab, descriptor)
      else if (action === 'fill') {
        const value = String(params.value ?? '')
        if (params.replace === false) await this.locatorType(tab, descriptor, value)
        else await this.locatorFill(tab, descriptor, value)
      } else if (action === 'press') {
        await this.locatorPress(tab, descriptor, String(params.value ?? ''))
      } else if (action === 'selectOption') {
        await this.locatorSelectOption(tab, descriptor, Array.isArray(params.selections) ? params.selections : [])
      } else {
        await this.locatorSetChecked(tab, descriptor, params.checked === true)
      }
      return {}
    }, signal)
  }

  private async backendPlaywrightLocatorWaitFor(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    await this.locatorWaitFor(tab, this.backendLocatorDescriptor(params), {
      state: this.stringParam(params, 'state') ?? 'visible',
      timeoutMs: this.backendCommandTimeoutMs(params)
    })
    return {}
  }

  private async backendPlaywrightLocatorCount(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { count: (await this.resolveLocator(tab, this.backendLocatorDescriptor(params))).length }
  }

  private async backendPlaywrightLocatorIsVisible(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { value: (await this.resolveLocator(tab, this.backendLocatorDescriptor(params))).some((match) => match.visible) }
  }

  private async backendPlaywrightLocatorIsEnabled(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { value: Boolean(await this.locatorEvaluate(tab, this.backendLocatorDescriptor(params), 'isEnabled')) }
  }

  private async backendPlaywrightLocatorAllTextContents(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const values = (await this.resolveLocator(tab, this.backendLocatorDescriptor(params)))
      .map((match) => match.text || match.visibleText || '')
    return { values }
  }

  private async backendPlaywrightLocatorTextContent(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { value: await this.locatorEvaluate(tab, this.backendLocatorDescriptor(params), 'textContent') }
  }

  private async backendPlaywrightLocatorInnerText(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return { value: (await this.strictLocator(tab, this.backendLocatorDescriptor(params))).visibleText }
  }

  private async backendPlaywrightLocatorGetAttribute(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const name = this.stringParam(params, 'name')
    if (!name) throw BrowserUseBackendError.invalidArgument('playwright_locator_get_attribute requires name.')
    return { value: await this.locatorEvaluate(tab, this.backendLocatorDescriptor(params), 'getAttribute', name) }
  }

  private async backendPlaywrightLocatorReadAll(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    let descriptor = this.backendLocatorDescriptor(params)
    const relativeSelector = this.stringParam(params, 'relative_selector')
    if (relativeSelector) {
      descriptor = this.scopedLocatorDescriptor(tab, descriptor, { kind: 'css', value: relativeSelector })
    }
    const values = (await this.resolveLocator(tab, descriptor)).map((match) => ({
      attributes: {},
      inner_text: match.visibleText || match.text || '',
      text_content: match.text || match.visibleText || null
    }))
    return { values }
  }

  private async backendCuaAction(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal: AbortSignal | undefined,
    action: 'move' | 'click' | 'double_click' | 'drag' | 'keypress' | 'scroll' | 'type'
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return await this.queueBackendTabCommand(tab, async () => {
      if (action === 'move') {
        await this.cuaMove(tab, {
          x: this.finiteNumberParam(params, 'x'),
          y: this.finiteNumberParam(params, 'y'),
          waitForArrival: params.waitForArrival !== false
        })
      } else if (action === 'click') {
        await this.cuaClick(tab, {
          x: this.finiteNumberParam(params, 'x'),
          y: this.finiteNumberParam(params, 'y'),
          button: params.button as number | string | undefined
        })
      } else if (action === 'double_click') {
        await this.cuaDoubleClick(tab, {
          x: this.finiteNumberParam(params, 'x'),
          y: this.finiteNumberParam(params, 'y'),
          button: params.button as number | string | undefined
        })
      } else if (action === 'drag') {
        await this.cuaDrag(tab, { path: this.normalizePointPath(params.path) })
      } else if (action === 'keypress') {
        await this.cuaKeypress(tab, { keys: this.stringArrayFromUnknown(params.keys) })
      } else if (action === 'scroll') {
        await this.cuaScroll(tab, {
          x: this.finiteNumberParam(params, 'x'),
          y: this.finiteNumberParam(params, 'y'),
          scrollX: this.finiteNumberFromUnknown(params.scroll_x ?? params.scrollX ?? params.delta_x ?? params.deltaX ?? 0, 'scroll_x'),
          scrollY: this.finiteNumberFromUnknown(params.scroll_y ?? params.scrollY ?? params.delta_y ?? params.deltaY ?? 0, 'scroll_y')
        })
      } else {
        await this.cuaType(tab, { text: String(params.text ?? '') })
      }
      return {}
    }, signal)
  }

  private async backendDomCuaAction(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal: AbortSignal | undefined,
    action: 'click' | 'double_click' | 'keypress' | 'scroll' | 'type'
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return await this.queueBackendTabCommand(tab, async () => {
      if (action === 'click') await this.domCuaClick(tab, { node_id: this.stringParam(params, 'node_id') }, false)
      else if (action === 'double_click') await this.domCuaClick(tab, { node_id: this.stringParam(params, 'node_id') }, true)
      else if (action === 'keypress') await this.domCuaKeypress(tab, { keys: this.stringArrayFromUnknown(params.keys) })
      else if (action === 'scroll') {
        await this.domCuaScroll(tab, {
          node_id: this.stringParam(params, 'node_id'),
          x: this.finiteOptionalNumber(params.x, 'x'),
          y: this.finiteOptionalNumber(params.y, 'y'),
          scrollX: this.finiteOptionalNumber(params.scroll_x ?? params.scrollX, 'scroll_x'),
          scrollY: this.finiteOptionalNumber(params.scroll_y ?? params.scrollY, 'scroll_y'),
          deltaX: this.finiteOptionalNumber(params.delta_x ?? params.deltaX, 'delta_x'),
          deltaY: this.finiteOptionalNumber(params.delta_y ?? params.deltaY, 'delta_y')
        })
      } else {
        await this.domCuaType(tab, { text: String(params.text ?? '') })
      }
      return {}
    }, signal)
  }

  private async backendPageAssetsBundle(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    return await this.bundlePageAssets(tab, {
      inventoryId: this.stringParam(params, 'inventoryId') ?? this.stringParam(params, 'inventory_id'),
      assetIds: this.stringArrayFromUnknown(params.assetIds ?? params.asset_ids),
      kinds: this.stringArrayFromUnknown(params.kinds)
    })
  }

  private async backendWebMcpListTools(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const tools = (await this.listWebMcpTools(tab)).map((tool) => {
      const { invoke: _invoke, ...serializable } = tool
      return {
        ...serializable,
        input_schema: serializable.input_schema ?? serializable.inputSchema ?? {}
      }
    })
    return { tools }
  }

  private async backendWebMcpInvokeTool(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const toolName = this.stringParam(params, 'tool_name') ?? this.stringParam(params, 'toolName')
    return {
      result: await this.invokeWebMcpTool(tab, {
        toolName,
        input: params.input,
        timeoutMs: Number(params.timeout_ms ?? params.timeoutMs)
      })
    }
  }

  private finiteNumberParam(params: Record<string, unknown>, key: string): number {
    return this.finiteNumberFromUnknown(params[key], key)
  }

  private finiteNumberOrDefault(value: unknown, name: string, fallback: number): number {
    return value == null ? fallback : this.finiteNumberFromUnknown(value, name)
  }

  private finiteOptionalNumber(value: unknown, name: string): number | undefined {
    return value == null ? undefined : this.finiteNumberFromUnknown(value, name)
  }

  private finiteNumberFromUnknown(value: unknown, name: string): number {
    const numeric = Number(value)
    if (!Number.isFinite(numeric)) {
      throw BrowserUseBackendError.invalidArgument(`${name} must be a finite number.`)
    }
    return numeric
  }

  private normalizePointPath(value: unknown): Array<{ x: number; y: number }> {
    if (!Array.isArray(value)) throw BrowserUseBackendError.invalidArgument('cua_drag requires path.')
    return value.map((point, index) => {
      const raw = point && typeof point === 'object' && !Array.isArray(point)
        ? point as Record<string, unknown>
        : {}
      return {
        x: this.finiteNumberFromUnknown(raw.x, `path[${index}].x`),
        y: this.finiteNumberFromUnknown(raw.y, `path[${index}].y`)
      }
    })
  }

  private stringArrayFromUnknown(value: unknown): string[] {
    return Array.isArray(value)
      ? value.map((item) => String(item))
      : []
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
        tab = await this.createTab(runtime.owner, runtime, url)
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

  private backendBrowserViewportSet(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>
  ): Record<string, unknown> {
    runtime.viewportWidth = this.normalizeViewportDimension(params.width, 'width')
    runtime.viewportHeight = this.normalizeViewportDimension(params.height, 'height')
    this.applyViewport(runtime)
    return {}
  }

  private backendBrowserViewportReset(runtime: BrowserUseThreadRuntime): Record<string, unknown> {
    runtime.viewportWidth = BROWSER_USE_DEFAULT_VIEWPORT_WIDTH
    runtime.viewportHeight = BROWSER_USE_DEFAULT_VIEWPORT_HEIGHT
    this.applyViewport(runtime)
    return {}
  }

  private async evaluatePageContent(tab: BrowserUseTabRuntime, contentType: 'html' | 'text'): Promise<string> {
    const expression = contentType === 'html'
      ? 'document.documentElement ? document.documentElement.outerHTML : ""'
      : 'document.body ? document.body.innerText : (document.documentElement ? document.documentElement.innerText : "")'
    return await this.executeJavaScript<string>(tab, expression, `tabs_content.${contentType}`, false)
  }

  private async backendPlaywrightElementInfo(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<unknown> {
    const tab = this.backendTabForCommand(runtime, params)
    const x = Number(params.x)
    const y = Number(params.y)
    if (!Number.isFinite(x) || !Number.isFinite(y)) {
      throw BrowserUseBackendError.invalidArgument('playwright_element_info requires numeric x and y.')
    }
    return await this.queueBackendTabCommand(tab, async () => await this.evaluateCdpExpression(tab, `(() => {
      const x = ${JSON.stringify(x)};
      const y = ${JSON.stringify(y)};
      const element = document.elementFromPoint(x, y);
      if (!element) return [];
      const rect = element.getBoundingClientRect();
      const tagName = element.tagName.toLowerCase();
      const text = (element.innerText || element.textContent || "").trim().slice(0, 500);
      const id = element.id || "";
      const testId = element.getAttribute("data-testid") || element.getAttribute("data-test-id") || "";
      const role = element.getAttribute("role") || "";
      const ariaName = element.getAttribute("aria-label") || element.getAttribute("title") || text;
      const selectors = [];
      if (id) selectors.push("#" + CSS.escape(id));
      if (testId) selectors.push("[data-testid=\\"" + CSS.escape(testId) + "\\"]");
      selectors.push(tagName);
      return [{
        nodeId: null,
        tagName,
        role: role || null,
        visibleText: text || null,
        ariaName: ariaName || null,
        testId: testId || null,
        selector: {
          primary: selectors[0] || null,
          candidates: selectors
        },
        boundingBox: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        preview: "<" + tagName + ">" + (text ? " " + text : "")
      }];
    })()`), signal)
  }

  private async backendPlaywrightElementScreenshot(
    runtime: BrowserUseThreadRuntime,
    params: Record<string, unknown>,
    signal?: AbortSignal
  ): Promise<Record<string, unknown>> {
    const tab = this.backendTabForCommand(runtime, params)
    const info = await this.backendPlaywrightElementInfo(runtime, params, signal) as
      Array<{ boundingBox?: { x?: number; y?: number; width?: number; height?: number } }>
    const box = info[0]?.boundingBox
    if (!box) throw BrowserUseBackendError.invalidArgument('playwright_element_screenshot did not hit an element.')
    const clip = {
      x: Math.max(0, Number(box.x) || 0),
      y: Math.max(0, Number(box.y) || 0),
      width: Math.max(1, Number(box.width) || 1),
      height: Math.max(1, Number(box.height) || 1),
      scale: 1
    }
    return await this.queueBackendTabCommand(tab, async () => await this.cdpCommand<Record<string, unknown>>(
      tab,
      'Page.captureScreenshot',
      {
        format: 'png',
        fromSurface: true,
        captureBeyondViewport: true,
        clip
      }), signal)
  }

  private async evaluateCdpExpression<T = unknown>(
    tab: BrowserUseTabRuntime,
    expression: string
  ): Promise<T> {
    const result = await this.cdpCommand<{
      result?: { value?: T; unserializableValue?: string }
      exceptionDetails?: { text?: string; exception?: { description?: string; value?: unknown } }
    }>(tab, 'Runtime.evaluate', {
      expression,
      awaitPromise: true,
      returnByValue: true
    })
    if (result.exceptionDetails) {
      throw new Error(
        result.exceptionDetails.exception?.description ??
        result.exceptionDetails.text ??
        'Runtime.evaluate failed.')
    }
    return (result.result?.value ?? result.result?.unserializableValue) as T
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

  private stringifyBackendTabId(tab: Record<string, unknown>): Record<string, unknown> {
    return {
      ...tab,
      id: String(tab.id),
      tabId: String(tab.tabId)
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

  private numberParam(params: Record<string, unknown> | null, key: string): number | undefined {
    const value = params?.[key]
    const numeric = Number(value)
    return Number.isFinite(numeric) ? numeric : undefined
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
        'tabs.content({ urls, contentType })',
        'tabs.finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })',
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
      content: async (options?: { urls?: unknown; contentType?: unknown; content_type?: unknown; timeoutMs?: unknown }) => {
        const result = await this.backendTabsContent(runtime, {
          urls: Array.isArray(options?.urls) ? options.urls : [],
          content_type: options?.content_type ?? options?.contentType ?? 'text',
          timeoutMs: options?.timeoutMs
        })
        return Array.isArray(result.results) ? result.results : []
      },
      finalize: async (options?: { keep?: unknown[] }) => this.finalizeTabs(runtime, options),
      describeApi: () => ['list()', 'new(url?)', 'selected()', 'get(id)', 'content({ urls, contentType })', 'finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })']
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
      if (tab.adopted || tab.userOwned) {
        this.setAutomationState(runtime, tab, false)
        tab.adopted = false
        tab.userOwned = false
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
      throw new Error('browser.tabs.finalize requires keep to be an array of { tab, status: "deliverable"|"handoff" } entries.')
    }
    const result = new Map<string, BrowserFinalizeKeepStatus>()
    for (const item of keep) {
      if (!item || typeof item !== 'object' || Array.isArray(item)) {
        throw new Error('browser.tabs.finalize keep entries must be objects shaped like { tab, status: "deliverable"|"handoff" }.')
      }
      const entry = item as Record<string, unknown>
      const status = entry.status
      if (status !== 'handoff' && status !== 'deliverable') {
        throw new Error('browser.tabs.finalize keep entries must include status "handoff" or "deliverable"; use { tab, status: "deliverable"|"handoff" }.')
      }
      const id = this.tabIdFromReference(entry.tab)
      if (!id) {
        throw new Error('browser.tabs.finalize keep entries must include a tab reference; use { tab, status: "deliverable"|"handoff" }.')
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
    const wc = this.webContentsFor(tab.owner, tab.id)
    const debuggerApi = wc.debugger as Electron.Debugger & {
      on?(event: 'message' | 'detach', listener: (...args: unknown[]) => void): void
      off?(event: 'message' | 'detach', listener: (...args: unknown[]) => void): void
    }
    if (!debuggerApi) {
      throw new Error(`Browser tab ${tab.id} does not expose Electron debugger/CDP.`)
    }
    if (!tab.cdpAttached || !debuggerApi.isAttached()) {
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
  }

  private detachDebugger(tab: BrowserUseTabRuntime): void {
    try {
      const wc = this.webContentsFor(tab.owner, tab.id)
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
    const method = typeof args[eventOffset] === 'string' ? args[eventOffset] : ''
    if (!method) return
    const rawParams = args[eventOffset + 1]
    const params = rawParams && typeof rawParams === 'object' && !Array.isArray(rawParams)
      ? rawParams as Record<string, unknown>
      : {}
    const sessionId = typeof args[eventOffset + 2] === 'string' ? args[eventOffset + 2] : undefined
    if (method === 'Target.detachedFromTarget') {
      const targetId = typeof params.targetId === 'string' ? params.targetId : undefined
      if (targetId) tab.targetSessions.delete(targetId)
    }
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
    sessionId?: string
  ): Promise<T> {
    await this.ensureDebuggerAttached(tab)
    const debuggerApi = this.webContentsFor(tab.owner, tab.id).debugger as Electron.Debugger & {
      sendCommand(method: string, params?: Record<string, unknown>, sessionId?: string): Promise<unknown>
    }
    return await (sessionId
      ? debuggerApi.sendCommand(method, params, sessionId)
      : debuggerApi.sendCommand(method, params)) as T
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
    userGesture = true,
    timeoutMs?: number
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
      },
      timeoutMs)
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
      const adopted = this.registerTab(owner, runtime, candidate.tabId, true, true)
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
    adopted: boolean,
    userOwned = adopted
  ): BrowserUseTabRuntime {
    const existing = runtime.tabs.get(id)
    if (existing) {
      if (adopted) existing.adopted = true
      if (userOwned) existing.userOwned = true
      return existing
    }
    const wc = this.webContentsFor(owner, id)
    this.closedTabIdsByOwner.get(owner)?.delete(id)
    const tab: BrowserUseTabRuntime = {
      id,
      owner,
      logs: [],
      clipboardItems: [],
      adopted,
      userOwned,
      targetSessions: new Map(),
      snapshotRefs: new Map(),
      domCuaNodes: new Map(),
      pageAssetInventories: new Map(),
      snapshotGeneration: 0
    }
    runtime.tabs.set(id, tab)

    wc.on('console-message', (_event, level, message) => {
      const levelNames = ['debug', 'info', 'warn', 'error'] as const
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
      this.forgetBackendTab(runtime, tab)
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
      evaluate: async (
        expressionOrFunction: string | ((arg?: unknown) => unknown),
        arg?: unknown,
        options?: { timeoutMs?: number; timeout?: number }
      ) => this.evaluateInPage(tab, expressionOrFunction, arg, options),
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
        read: async () => this.readVirtualClipboard(tab),
        readText: async () => this.readVirtualClipboardText(tab),
        write: async (items: unknown) => this.writeVirtualClipboard(tab, items),
        writeText: async (text: string) => this.writeVirtualClipboardText(tab, text),
        describeApi: () => ['readText()', 'writeText(text)', 'read()', 'write(items)']
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
        'evaluate(expressionOrFunction, arg?, options?) read-only',
        'playwright.*',
        'cua.*',
        'dom_cua.*',
        'capabilities.list()',
        'capabilities.get("pageAssets")',
        'capabilities.get("webmcp")'
      ]
    }
  }

  private createTabCapabilitiesApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    const available = BROWSER_USE_TAB_CAPABILITIES.map((capability) => ({ ...capability }))
    return {
      list: async () => available,
      get: async (id: string) => {
        if (id === 'pageAssets') return this.createPageAssetsCapability(tab)
        if (id === 'webmcp') return this.createWebMcpCapability(tab)
        throw new Error(`Tab capability not found: ${id}. Available capabilities: pageAssets, webmcp.`)
      },
      describeApi: () => ['list()', 'get("pageAssets")', 'get("webmcp")']
    }
  }

  private createPageAssetsCapability(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      list: async () => this.listPageAssets(tab),
      bundle: async (options: { assetIds?: string[]; inventoryId?: string; kinds?: string[] }) => this.bundlePageAssets(tab, options),
      describeApi: () => ['list()', 'bundle({ inventoryId, kinds?, assetIds? })']
    }
  }

  private createWebMcpCapability(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      listTools: async () => this.listWebMcpTools(tab),
      invokeTool: async (options: { toolName?: string; input?: unknown; timeoutMs?: number }) => this.invokeWebMcpTool(tab, options),
      describeApi: () => ['listTools()', 'invokeTool({ toolName, input?, timeoutMs? })']
    }
  }

  private async listWebMcpTools(tab: BrowserUseTabRuntime): Promise<Array<Record<string, unknown>>> {
    this.markAutomation(tab, 'webmcp.listTools')
    await this.waitForPageReady(tab, {
      operation: 'webmcp.ready',
      requireContent: false,
      timeoutMs: this.operationTimeoutMs()
    })
    const tools = await this.executeJavaScript<Array<Record<string, unknown>>>(tab, `(() => {
      const modelContext = navigator.modelContext;
      if (!modelContext || typeof modelContext.getTools !== "function") {
        throw new Error("WebMCP modelContext is unavailable in the current page.");
      }
      return Promise.resolve(modelContext.getTools()).then((tools) => tools.map((tool) => ({
        name: String(tool.name || ""),
        title: tool.title,
        description: tool.description,
        inputSchema: tool.inputSchema == null ? null : (
          typeof tool.inputSchema === "string" ? JSON.parse(tool.inputSchema) : tool.inputSchema
        ),
        annotations: tool.annotations,
        origin: tool.origin,
        pageUrl: tool.pageUrl
      })));
    })()`, 'webmcp.listTools')
    if (!Array.isArray(tools)) throw new Error('WebMCP listTools failed: no result returned.')
    return tools.map((tool) => ({
      ...tool,
      invoke: async (input: unknown, options?: { timeoutMs?: number }) => this.invokeWebMcpTool(tab, {
        toolName: typeof tool.name === 'string' ? tool.name : '',
        input,
        timeoutMs: options?.timeoutMs
      })
    }))
  }

  private async invokeWebMcpTool(
    tab: BrowserUseTabRuntime,
    options: { toolName?: string; input?: unknown; timeoutMs?: number }
  ): Promise<unknown> {
    const toolName = String(options?.toolName ?? '').trim()
    if (!toolName) throw new Error('tab.capabilities.webmcp.invokeTool requires a toolName')
    const timeoutMs = this.normalizeWebMcpTimeout(options?.timeoutMs)
    this.markAutomation(tab, 'webmcp.invokeTool')
    await this.waitForPageReady(tab, {
      operation: 'webmcp.ready',
      requireContent: false,
      timeoutMs
    })
    return await this.executeJavaScript(tab, `(() => {
      const modelContext = navigator.modelContext;
      if (!modelContext || typeof modelContext.getTools !== "function" || typeof modelContext.executeTool !== "function") {
        throw new Error("WebMCP modelContext is unavailable in the current page.");
      }
      return Promise.resolve(modelContext.getTools()).then((tools) => {
        const tool = tools.find((candidate) => candidate.name === ${JSON.stringify(toolName)});
        if (!tool) throw new Error(${JSON.stringify(`WebMCP tool not found: ${toolName}`)});
        return modelContext.executeTool(tool, ${JSON.stringify(JSON.stringify(options?.input ?? null))});
      }).then((result) => {
        if (result == null) return null;
        try {
          return JSON.parse(result);
        } catch {
          return result;
        }
      });
    })()`, 'webmcp.invokeTool')
  }

  private normalizeWebMcpTimeout(timeoutMs?: number): number {
    const numeric = Number(timeoutMs)
    const requested = Number.isFinite(numeric) && numeric > 0 ? numeric : this.operationTimeoutMs()
    return Math.max(1, Math.min(Math.floor(requested), 120_000))
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
      describeApi: () => ['get_visible_dom()', 'click({ node_id })', 'double_click({ node_id })', 'type({ node_id?, text })', 'keypress({ node_id?, key|keys })', 'scroll({ node_id?, x?, y?, scrollX?, scrollY?, deltaX?, deltaY? })', 'download_media() unsupported']
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
      x: this.finiteNumberOrDefault(options.x, 'x', 0),
      y: this.finiteNumberOrDefault(options.y, 'y', 0),
      scrollX: this.finiteNumberOrDefault(options.scrollX ?? options.deltaX, 'scrollX', 0),
      scrollY: this.finiteNumberOrDefault(options.scrollY ?? options.deltaY, 'scrollY', 0)
    }
  }

  private normalizeDomCuaScrollDistance(options: {
    x?: number
    y?: number
    scrollX?: number
    scrollY?: number
    deltaX?: number
    deltaY?: number
  } = {}): { scrollX: number; scrollY: number } {
    return {
      scrollX: this.finiteNumberOrDefault(options.scrollX ?? options.deltaX ?? options.x, 'scrollX', 0),
      scrollY: this.finiteNumberOrDefault(options.scrollY ?? options.deltaY ?? options.y, 'scrollY', 0)
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
    if (tab.closed) throw BrowserUseBackendError.pageClosed(tab.id)
    if (!nodeId) throw BrowserUseBackendError.invalidArgument('DOM CUA action requires node_id from get_visible_dom().')
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
    throw BrowserUseBackendError.nodeStale(nodeId)
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
    const scroll = this.normalizeDomCuaScrollDistance(options)
    if (options.node_id) {
      const target = this.domCuaTarget(tab, options)
      const point = this.actionPoint(target)
      await this.cuaScroll(tab, { ...scroll, ...point })
      return
    }
    await this.cuaScroll(tab, { ...this.viewportCenter(tab), ...scroll })
  }

  private viewportCenter(tab: BrowserUseTabRuntime): { x: number; y: number } {
    const runtime = this.getRuntimeForTab(tab)
    return {
      x: Math.max(0, Math.round(runtime.viewportWidth / 2)),
      y: Math.max(0, Math.round(runtime.viewportHeight / 2))
    }
  }

  private createPlaywrightApi(tab: BrowserUseTabRuntime): Record<string, unknown> {
    return {
      domSnapshot: async () => this.domSnapshot(tab),
      screenshot: async (options?: { fullPage?: boolean; clip?: Electron.Rectangle }) => this.screenshot(tab, options),
      evaluate: async (
        expressionOrFunction: string | ((arg?: unknown) => unknown),
        arg?: unknown,
        options?: { timeoutMs?: number; timeout?: number }
      ) => this.evaluateInPage(tab, expressionOrFunction, arg, options),
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
      locator: (selector: string, options?: Record<string, unknown>) => this.createLocatorApi(tab, this.withLocatorOptions({ kind: 'css', value: String(selector) }, options)),
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
      frameLocator: (selector: string) => this.createFrameLocatorApi(tab, String(selector)),
      describeApi: () => ['evaluate(fnOrExpression, arg?, options?) read-only', 'domSnapshot()', 'screenshot(options?)', 'waitForLoadState(stateOrOptions?, timeoutMs?)', 'waitForURL(url, options?)', 'waitForTimeout(ms)', 'expectNavigation(action, options?)', 'locator(selector, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'waitForEvent(event) unsupported', 'frameLocator(selector)']
    }
  }

  private createFrameLocatorApi(tab: BrowserUseTabRuntime, frameSelector: string): Record<string, unknown> {
    const frame = String(frameSelector ?? '').trim()
    if (!frame) throw new Error('playwright.frameLocator requires a selector.')
    const inFrame = (selector: string) => `${frame} >> internal:control=enter-frame >> ${selector}`
    return {
      locator: (selector: string, options?: Record<string, unknown>) => this.createLocatorApi(tab, this.withLocatorOptions({ kind: 'css', value: inFrame(String(selector)) }, options)),
      getByTestId: (testId: string) => this.createLocatorApi(tab, { kind: 'css', value: inFrame(this.playwrightSelectorFor({ kind: 'testId', value: String(testId) })) }),
      getByText: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'css',
        value: inFrame(this.playwrightSelectorFor({
          kind: 'text',
          value: String(text),
          exact: options?.exact === true
        }))
      }),
      getByLabel: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'css',
        value: inFrame(this.playwrightSelectorFor({
          kind: 'label',
          value: String(text),
          exact: options?.exact === true
        }))
      }),
      getByPlaceholder: (text: string, options?: { exact?: boolean }) => this.createLocatorApi(tab, {
        kind: 'css',
        value: inFrame(this.playwrightSelectorFor({
          kind: 'placeholder',
          value: String(text),
          exact: options?.exact === true
        }))
      }),
      getByRole: (role: string, options?: { exact?: boolean; name?: string }) => this.createLocatorApi(tab, {
        kind: 'css',
        value: inFrame(this.playwrightSelectorFor({
          kind: 'role',
          value: String(role),
          exact: options?.exact === true,
          name: options?.name == null ? undefined : String(options.name)
        }))
      }),
      frameLocator: (selector: string) => this.createFrameLocatorApi(tab, inFrame(String(selector))),
      describeApi: () => ['locator(selector, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'frameLocator(selector)']
    }
  }

  private createLocatorApi(tab: BrowserUseTabRuntime, descriptor: BrowserUseLocatorDescriptor): Record<string, unknown> {
    return {
      __dotcraftLocatorDescriptor: descriptor,
      count: async () => (await this.resolveLocator(tab, descriptor)).length,
      all: async () => {
        const matches = await this.resolveLocator(tab, descriptor)
        return matches.map((_match, index) => this.createLocatorApi(tab, { ...descriptor, index }))
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
      locator: (selector: string, options?: Record<string, unknown>) => this.createLocatorApi(tab, this.withLocatorOptions(
        this.scopedLocatorDescriptor(tab, descriptor, { kind: 'css', value: String(selector) }),
        options
      )),
      filter: (options?: Record<string, unknown>) => this.createLocatorApi(tab, this.withLocatorOptions(descriptor, options)),
      and: (other: unknown) => this.createLocatorApi(tab, {
        kind: 'and',
        value: '',
        left: descriptor,
        right: this.locatorDescriptorFromApi(other, 'locator.and')
      }),
      or: (other: unknown) => this.createLocatorApi(tab, {
        kind: 'or',
        value: '',
        left: descriptor,
        right: this.locatorDescriptorFromApi(other, 'locator.or')
      }),
      first: () => this.createLocatorApi(tab, { ...descriptor, index: 0 }),
      last: () => this.createLocatorApi(tab, { ...descriptor, index: -1 }),
      nth: (index: number) => this.createLocatorApi(tab, { ...descriptor, index: Math.trunc(Number(index)) }),
      describeApi: () => ['count()', 'all()', 'filter(options)', 'and(locator)', 'or(locator)', 'click(options?)', 'dblclick(options?)', 'fill(value, options?)', 'type(value, options?)', 'press(key, options?)', 'innerText(options?)', 'textContent(options?)', 'getAttribute(name, options?)', 'isVisible()', 'isEnabled()', 'waitFor({ state, timeoutMs })', 'allTextContents(options?)', 'check(options?)', 'uncheck(options?)', 'setChecked(checked, options?)', 'selectOption(value, options?)', 'getByRole(role, options?)', 'getByText(text, options?)', 'getByLabel(text, options?)', 'getByPlaceholder(text, options?)', 'getByTestId(testId)', 'locator(selector, options?)', 'first()', 'last()', 'nth(index)']
    }
  }

  private withLocatorOptions(
    descriptor: BrowserUseLocatorDescriptor,
    options?: Record<string, unknown>
  ): BrowserUseLocatorDescriptor {
    const filters = this.locatorFiltersFromOptions(options)
    if (filters.length === 0) return descriptor
    return {
      ...descriptor,
      filters: [...(descriptor.filters ?? []), ...filters]
    }
  }

  private locatorFiltersFromOptions(options?: Record<string, unknown>): BrowserUseLocatorFilter[] {
    if (!options || typeof options !== 'object' || Array.isArray(options)) return []
    const filters: BrowserUseLocatorFilter[] = []
    if ('hasText' in options && options.hasText != null) {
      filters.push({ kind: 'hasText', matcher: this.locatorTextMatcher(options.hasText, options) })
    }
    if ('hasNotText' in options && options.hasNotText != null) {
      filters.push({ kind: 'hasNotText', matcher: this.locatorTextMatcher(options.hasNotText, options) })
    }
    if ('visible' in options && typeof options.visible === 'boolean') {
      filters.push({ kind: 'visible', value: options.visible })
    }
    if (options.has != null) {
      filters.push({ kind: 'has', descriptor: this.locatorDescriptorFromApi(options.has, 'locator.filter({ has })') })
    }
    if (options.hasNot != null) {
      filters.push({ kind: 'hasNot', descriptor: this.locatorDescriptorFromApi(options.hasNot, 'locator.filter({ hasNot })') })
    }
    return filters
  }

  private locatorTextMatcher(value: unknown, options?: Record<string, unknown>): BrowserUseLocatorTextMatcher {
    if (value instanceof RegExp) return { pattern: value.source, flags: value.flags }
    return {
      value: String(value ?? ''),
      exact: options?.exact === true
    }
  }

  private locatorDescriptorFromApi(value: unknown, operation: string): BrowserUseLocatorDescriptor {
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      const descriptor = (value as Record<string, unknown>).__dotcraftLocatorDescriptor
      if (descriptor && typeof descriptor === 'object' && !Array.isArray(descriptor)) {
        return descriptor as BrowserUseLocatorDescriptor
      }
    }
    throw new Error(`InvalidArgument: ${operation} requires another DotCraft locator.`)
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
    this.clearNavigationFailure(tab)
    this.invalidatePageScopedCaches(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoBack()) wc.navigationHistory.goBack()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
    return this.tabSnapshot(tab)
  }

  private async goForward(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'forward')
    this.clearNavigationFailure(tab)
    this.invalidatePageScopedCaches(tab)
    const wc = this.webContentsFor(tab.owner, tab.id)
    if (wc.navigationHistory.canGoForward()) wc.navigationHistory.goForward()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
    return this.tabSnapshot(tab)
  }

  private async reload(tab: BrowserUseTabRuntime): Promise<Record<string, unknown>> {
    this.markAutomation(tab, 'reload')
    this.clearNavigationFailure(tab)
    this.invalidatePageScopedCaches(tab)
    this.webContentsFor(tab.owner, tab.id).reload()
    await this.waitForLoad(tab, 'load', 30_000).catch((error) => {
      if (error instanceof BrowserUseBackendError && error.message.startsWith('NavigationFailed:')) throw error
    })
    this.throwIfNavigationFailed(tab)
    return this.tabSnapshot(tab)
  }

  private closeTab(tab: BrowserUseTabRuntime): void {
    if (tab.closed) return
    this.markAutomation(tab, 'close')
    const runtime = this.getRuntimeForTab(tab)
    this.detachDebugger(tab)
    this.invalidatePageScopedCaches(tab)
    this.forgetBackendTab(runtime, tab)
    this.viewerHost.destroyTab(tab.owner, tab.id)
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

  private async navigate(
    tab: BrowserUseTabRuntime,
    url: string,
    options: { skipPolicyCheck?: boolean } = {}
  ): Promise<Record<string, unknown>> {
    const normalized = normalizeBrowserUseUrl(url)
    if (!normalized) throw new Error(`Invalid browser URL: ${url}`)
    this.markAutomation(tab, 'navigate')
    this.clearNavigationFailure(tab)
    this.invalidatePageScopedCaches(tab)
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

  private invalidatePageScopedCaches(tab: BrowserUseTabRuntime): void {
    this.invalidateSnapshotRefs(tab)
    tab.domCuaNodes.clear()
    tab.pageAssetInventories.clear()
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
      title: snapshot.title,
      url: snapshot.url,
      bodyText: snapshot.bodyText,
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

  private async evaluateInPage(
    tab: BrowserUseTabRuntime,
    expressionOrFunction: string | ((arg?: unknown) => unknown),
    arg?: unknown,
    options?: { timeoutMs?: number; timeout?: number }
  ): Promise<unknown> {
    const source = typeof expressionOrFunction === 'function'
      ? `((fn, arg) => fn(arg))(${expressionOrFunction.toString()}, ${this.evaluateArgSource(arg)})`
      : String(expressionOrFunction)
    return this.evaluateSourceInPage(tab, source, options)
  }

  private async evaluateSourceInPage(
    tab: BrowserUseTabRuntime,
    source: string,
    options?: { timeoutMs?: number; timeout?: number }
  ): Promise<unknown> {
    const timeoutMs = this.normalizeEvaluateTimeout(options)
    await this.waitForPageReady(tab, {
      operation: 'evaluate.ready',
      requireContent: false,
      timeoutMs
    })
    return this.executeJavaScript(tab, readOnlyEvaluateSource(source), 'evaluate', false, timeoutMs)
  }

  private normalizeEvaluateTimeout(options?: { timeoutMs?: number; timeout?: number }): number {
    const raw = options?.timeoutMs ?? options?.timeout
    const timeoutMs = typeof raw === 'number' && Number.isFinite(raw)
      ? raw
      : this.operationTimeoutMs()
    return Math.max(1, Math.min(Math.floor(timeoutMs), 120_000))
  }

  private evaluateArgSource(arg: unknown): string {
    if (arg === undefined) return 'undefined'
    try {
      const serialized = JSON.stringify(arg)
      if (serialized === undefined) return 'undefined'
      return serialized
    } catch {
      throw new Error('playwright.evaluate arg must be JSON-serializable.')
    }
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
    const x = Number(options.x)
    const y = Number(options.y)
    const scrollX = Number(options.scrollX ?? 0)
    const scrollY = Number(options.scrollY ?? 0)
    if (scrollX === 0 && scrollY === 0) {
      throw BrowserUseBackendError.invalidArgument('Scroll requires a non-zero distance. For CUA use scrollX/scrollY or deltaX/deltaY; for DOM-CUA page scroll use { y: 700 }.')
    }
    await this.withBrowserOperation(tab, 'cua.scroll', async () => {
      await this.viewerHost.moveMouse(tab.owner, { tabId: tab.id, x, y })
      try {
        await this.cdpCommand(tab, 'Input.synthesizeScrollGesture', {
          x,
          y,
          xDistance: -scrollX,
          yDistance: -scrollY,
          gestureSourceType: 'mouse',
          preventFling: true,
          speed: 8000
        })
      } catch (error) {
        if (!this.isCdpUnavailableError(error)) throw error
        await this.viewerHost.scrollMouse(tab.owner, {
          tabId: tab.id,
          x,
          y,
          scrollX,
          scrollY
        })
      }
    })
  }

  private isCdpUnavailableError(error: unknown): boolean {
    const message = error instanceof Error ? error.message : String(error)
    if (message.includes('does not expose Electron debugger/CDP')) return true
    return message.includes('Input.synthesizeScrollGesture') &&
      /unknown method|not found|not available|unsupported/i.test(message)
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
    if (descriptor.kind === 'and' || descriptor.kind === 'or') {
      return await this.resolveCompositeLocator(tab, descriptor)
    }
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
    return this.applyLocatorIndex(await this.applyLocatorFilters(tab, filtered, descriptor), descriptor)
  }

  private async resolveCompositeLocator(
    tab: BrowserUseTabRuntime,
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch[]> {
    if (!descriptor.left || !descriptor.right) {
      throw new Error(`Unsupported browser locator: ${this.describeLocator(descriptor)}`)
    }
    const left = await this.resolveLocator(tab, descriptor.left)
    const right = await this.resolveLocator(tab, descriptor.right)
    const rightKeys = new Set(right.map((match) => this.locatorMatchKey(match)))
    const combined = descriptor.kind === 'and'
      ? left.filter((match) => rightKeys.has(this.locatorMatchKey(match)))
      : this.uniqueLocatorMatches([...left, ...right])
    return this.applyLocatorIndex(await this.applyLocatorFilters(tab, combined, descriptor), descriptor)
  }

  private uniqueLocatorMatches(matches: BrowserUseElementMatch[]): BrowserUseElementMatch[] {
    const seen = new Set<string>()
    const result: BrowserUseElementMatch[] = []
    for (const match of matches) {
      const key = this.locatorMatchKey(match)
      if (seen.has(key)) continue
      seen.add(key)
      result.push(match)
    }
    return result
  }

  private locatorMatchKey(match: BrowserUseElementMatch): string {
    return [
      match.ref ?? '',
      match.selector,
      match.href ?? '',
      match.testId ?? '',
      match.role,
      match.name || match.text || match.visibleText,
      match.index
    ].join('\u0000')
  }

  private async applyLocatorFilters(
    tab: BrowserUseTabRuntime,
    matches: BrowserUseElementMatch[],
    descriptor: BrowserUseLocatorDescriptor
  ): Promise<BrowserUseElementMatch[]> {
    const filters = descriptor.filters ?? []
    if (filters.length === 0) return matches
    const result: BrowserUseElementMatch[] = []
    for (const match of matches) {
      let include = true
      for (const filter of filters) {
        if (!await this.locatorFilterMatches(tab, match, filter)) {
          include = false
          break
        }
      }
      if (include) result.push(match)
    }
    return result
  }

  private async locatorFilterMatches(
    tab: BrowserUseTabRuntime,
    match: BrowserUseElementMatch,
    filter: BrowserUseLocatorFilter
  ): Promise<boolean> {
    if (filter.kind === 'hasText') return this.matchesLocatorText(match.visibleText || match.text || match.name, filter.matcher)
    if (filter.kind === 'hasNotText') return !this.matchesLocatorText(match.visibleText || match.text || match.name, filter.matcher)
    if (filter.kind === 'visible') return match.visible === (filter.value !== false)
    if (filter.kind === 'has' || filter.kind === 'hasNot') {
      if (!filter.descriptor || !match.selector) return filter.kind === 'hasNot'
      const childDescriptor = this.scopedLocatorDescriptor(tab, { kind: 'css', value: match.selector }, filter.descriptor)
      const count = (await this.resolveLocator(tab, childDescriptor)).length
      return filter.kind === 'has' ? count > 0 : count === 0
    }
    return true
  }

  private matchesLocatorText(value: string, matcher?: BrowserUseLocatorTextMatcher): boolean {
    const actual = this.normalizeLocatorText(value)
    if (!matcher) return actual.length > 0
    if (matcher.pattern != null) {
      try {
        return new RegExp(matcher.pattern, matcher.flags ?? '').test(actual)
      } catch (error) {
        throw new Error(`InvalidArgument: invalid locator text matcher RegExp: ${error instanceof Error ? error.message : String(error)}`)
      }
    }
    const expected = this.normalizeLocatorText(matcher.value ?? '')
    return matcher.exact === true
      ? actual === expected
      : actual.toLowerCase().includes(expected.toLowerCase())
  }

  private normalizeLocatorText(value: string): string {
    return String(value ?? '').replace(/\s+/g, ' ').trim()
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
    this.throwIfNavigationFailed(tab)
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
        try {
          this.throwIfNavigationFailed(tab)
        } catch (error) {
          cleanup()
          reject(error)
          return
        }
        if (!matches(wc.getURL())) return
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
        wc.off('did-fail-load', onFailLoad)
        signal?.removeEventListener('abort', onAbort)
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      wc.on('did-navigate', done)
      wc.on('did-navigate-in-page', done)
      wc.on('did-stop-loading', done)
      wc.on('did-fail-load', onFailLoad)
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
    if (descriptor.kind === 'and' || descriptor.kind === 'or') {
      return `${descriptor.kind}(${descriptor.left ? this.describeLocator(descriptor.left) : '?'}, ${descriptor.right ? this.describeLocator(descriptor.right) : '?'})${index}`
    }
    const filters = descriptor.filters?.length ? `.filter(${descriptor.filters.map((filter) => filter.kind).join(',')})` : ''
    return `${descriptor.kind}=${descriptor.name ?? descriptor.value}${filters}${index}`
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
export { BROWSER_USE_OPEN_CHANNEL }
