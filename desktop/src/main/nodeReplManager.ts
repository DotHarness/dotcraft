import { BrowserWindow, app } from 'electron'
import { existsSync } from 'fs'
import { createConnection, type Socket } from 'net'
import { tmpdir } from 'os'
import { join, resolve } from 'path'
import nodeProcess from 'node:process'
import { pathToFileURL, URL as NodeUrl } from 'url'
import { createContext, Script, type Context } from 'vm'
import { isAllowedBrowserUsePipePath } from './browserUseBackendServer'
import { browserUseManager, type BrowserUseImageResult, type BrowserUseManager } from './browserUseManager'
import { checkChromeSetup, resolveChromePluginRoot, runChromeSetupScript } from './chromeSetup'

export interface NodeReplEvaluateParams {
  threadId: string
  turnId?: string
  evaluationId?: string
  browserSession?: Record<string, unknown>
  code: string
  timeoutMs?: number
  workspacePath?: string
}

export interface BrowserSessionMetadata {
  protocolVersion: number
  sessionId: string
  threadId?: string
  turnId?: string
  evaluationId: string
  backendId?: string
}

export interface NodeReplEvaluateResult {
  text?: string
  resultText?: string
  images: BrowserUseImageResult[]
  logs: string[]
  error?: string
}

interface NodeReplThreadRuntime {
  context: Context
  globals: Record<string, unknown>
  logs: string[]
  responseMeta: Record<string, unknown>
  activeEvaluationId?: string
  activeAbortController?: AbortController
  chromeCancelEvaluation?: (evaluationId: string, reason: string) => Promise<void> | void
  phase?: string
}

interface BrowserRuntimeBindings {
  agent: Record<string, unknown>
  display: (imageLike: unknown) => Promise<void>
}

function describeResult(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value, null, 2)
  } catch {
    return String(value)
  }
}

function formatError(error: unknown, phase: string | undefined): string {
  const prefix = `phase=${phase ?? 'js-runtime'}`
  if (error instanceof Error) return `${prefix} ${error.name}: ${error.message}`
  return `${prefix} ${String(error)}`
}

class NodeReplEvaluationTimeoutError extends Error {
  constructor(timeoutMs: number, phase: string | undefined) {
    super(`NodeReplJs timed out after ${timeoutMs}ms (phase=${phase ?? 'unknown'}).`)
    this.name = 'NodeReplEvaluationTimeoutError'
  }
}

class NodeReplEvaluationCancelledError extends Error {
  constructor(phase: string | undefined) {
    super(`NodeReplJs cancelled (phase=${phase ?? 'unknown'}).`)
    this.name = 'NodeReplEvaluationCancelledError'
  }
}

function resolveBrowserClientPath(): string {
  const dev = join(app.getAppPath(), 'resources', 'browser', 'scripts', 'browser-client.mjs')
  if (existsSync(dev)) return pathToFileURL(dev).href
  const cwdDev = join(nodeProcess.cwd(), 'resources', 'browser', 'scripts', 'browser-client.mjs')
  if (existsSync(cwdDev)) return pathToFileURL(cwdDev).href

  const resourcesPath = nodeProcess.resourcesPath
  if (resourcesPath) {
    const packaged = join(resourcesPath, 'browser', 'scripts', 'browser-client.mjs')
    if (existsSync(packaged)) return pathToFileURL(packaged).href
  }

  return pathToFileURL(dev).href
}

function resolveChromeBrowserClientPath(): string {
  const dev = join(app.getAppPath(), 'resources', 'chrome', 'browser-client.mjs')
  if (existsSync(dev)) return pathToFileURL(dev).href
  const cwdDev = join(nodeProcess.cwd(), 'resources', 'chrome', 'browser-client.mjs')
  if (existsSync(cwdDev)) return pathToFileURL(cwdDev).href

  const resourcesPath = nodeProcess.resourcesPath
  if (resourcesPath) {
    const packaged = join(resourcesPath, 'chrome', 'browser-client.mjs')
    if (existsSync(packaged)) return pathToFileURL(packaged).href
  }

  return pathToFileURL(dev).href
}

async function createBrowserUseNativePipeConnection(path: unknown): Promise<Socket> {
  const pipePath = typeof path === 'string' ? path : ''
  if (!pipePath || !isAllowedBrowserUsePipePath(pipePath)) {
    throw new Error('Refusing to connect to a non-DotCraft browser-use native pipe.')
  }

  return await new Promise<Socket>((resolveConnection, rejectConnection) => {
    const socket = createConnection(pipePath)
    const onError = (error: Error) => {
      cleanup()
      rejectConnection(error)
    }
    const onConnect = () => {
      cleanup()
      resolveConnection(socket)
    }
    const cleanup = () => {
      socket.off('error', onError)
      socket.off('connect', onConnect)
    }
    socket.once('error', onError)
    socket.once('connect', onConnect)
  })
}

function createChromeSetupApi(workspacePath?: string): Record<string, unknown> {
  const pluginRoot = resolveChromePluginRoot(workspacePath)
  const scriptsPath = join(pluginRoot, 'scripts')

  return Object.freeze({
    async checkSetup() {
      return await checkChromeSetup(workspacePath)
    },
    async checkExtension() {
      return await runChromeSetupScript(workspacePath, 'check-extension-installed.js', ['--json'])
    },
    async checkNativeHost() {
      return await runChromeSetupScript(workspacePath, 'check-native-host-manifest.js', ['--json'])
    }
  })
}

function createReplRuntime(): NodeReplThreadRuntime {
  const globals: Record<string, unknown> = {}
  return {
    globals,
    context: createContext(globals, { codeGeneration: { strings: false, wasm: false } }),
    logs: [],
    responseMeta: {},
    phase: 'idle'
  }
}

function newEvaluationId(): string {
  return `node-repl-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

function stringField(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function normalizeBrowserSession(params: NodeReplEvaluateParams, evaluationId: string): BrowserSessionMetadata {
  const raw = params.browserSession ?? {}
  const sessionId = stringField(raw.sessionId) ?? params.threadId
  const turnId = params.turnId ?? stringField(raw.turnId) ?? evaluationId
  return {
    ...raw,
    protocolVersion: 1,
    sessionId,
    threadId: stringField(raw.threadId) ?? params.threadId,
    turnId,
    evaluationId,
    backendId: stringField(raw.backendId)
  }
}

function normalizeCellCode(code: string): string {
  return String(code ?? '').replace(/\bimport\s*\(/g, '__dotcraftDynamicImport(')
}

function compileCell(code: string): { script: Script; kind: 'expression' | 'statement' } {
  const normalized = normalizeCellCode(code)
  const trimmed = normalized.trim()
  if (!trimmed) {
    return {
      script: new Script('(async () => {})()', { filename: 'NodeReplJs' }),
      kind: 'statement'
    }
  }

  const expressionSource = `(async () => { return (${trimmed}\n); })()`
  try {
    return {
      script: new Script(expressionSource, { filename: 'NodeReplJs' }),
      kind: 'expression'
    }
  } catch {
    const statementSource = `(async () => {\n${normalized}\n})()`
    return {
      script: new Script(statementSource, { filename: 'NodeReplJs' }),
      kind: 'statement'
    }
  }
}

export class NodeReplManager {
  private readonly runtimes = new Map<string, NodeReplThreadRuntime>()

  constructor(private readonly browserManager: BrowserUseManager = browserUseManager) {}

  async evaluate(owner: BrowserWindow, params: NodeReplEvaluateParams): Promise<NodeReplEvaluateResult> {
    if (!params.threadId || typeof params.code !== 'string') {
      return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
    }

    const runtime = this.getOrCreateRuntime(params.threadId)
    if (runtime.activeEvaluationId) {
      return { error: `NodeReplJs is already running for this thread: ${runtime.activeEvaluationId}`, images: [], logs: [] }
    }

    const evaluationId = params.evaluationId?.trim() || newEvaluationId()
    const browserSession = normalizeBrowserSession(params, evaluationId)
    const abortController = new AbortController()
    runtime.activeEvaluationId = evaluationId
    runtime.activeAbortController = abortController
    runtime.logs = []
    runtime.phase = 'prepare'
    const browserRuntime = await this.browserManager.prepareNodeRepl(owner, {
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      evaluationId,
      signal: abortController.signal,
      browserSession
    })
    this.refreshContext(runtime, browserRuntime, params.threadId, evaluationId, abortController.signal, params.workspacePath, browserSession)

    const timeoutMs = Math.max(1_000, Math.min(params.timeoutMs ?? 30_000, 120_000))
    try {
      runtime.phase = 'js-compile'
      const cell = compileCell(params.code)
      runtime.phase = `js-runtime:${cell.kind}`
      const value = await this.withTimeout(
        Promise.resolve(cell.script.runInContext(runtime.context, {
          displayErrors: true,
          timeout: timeoutMs
        })),
        timeoutMs,
        abortController.signal,
        () => runtime.phase)
      const collected = browserRuntime.collect()
      return {
        resultText: describeResult(value),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } catch (error: unknown) {
      const isOuterControlError =
        error instanceof NodeReplEvaluationTimeoutError ||
        error instanceof NodeReplEvaluationCancelledError
      if (isOuterControlError) {
        await this.cancelChromeCommands(runtime, evaluationId, error instanceof Error ? error.message : 'outer-control')
        abortController.abort()
        this.browserManager.abortEvaluation(params.threadId, evaluationId)
        this.disposeReplRuntime(params.threadId, runtime)
      }
      const collected = browserRuntime.collect()
      return {
        error: error instanceof Error && isOuterControlError
          ? error.message
          : formatError(error, runtime.phase),
        images: collected.images,
        logs: [...runtime.logs, ...collected.logs]
      }
    } finally {
      ;(globalThis as Record<string, unknown>).process = nodeProcess
      if (runtime.activeEvaluationId === evaluationId) {
        runtime.activeEvaluationId = undefined
        runtime.activeAbortController = undefined
        runtime.phase = 'idle'
      }
    }
  }

  cancel(threadId: string, evaluationId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime || runtime.activeEvaluationId !== evaluationId) return { ok: false }
    void this.cancelChromeCommands(runtime, evaluationId, 'cancelled')
    runtime.activeAbortController?.abort(new NodeReplEvaluationCancelledError(runtime.phase))
    this.browserManager.abortEvaluation(threadId, evaluationId)
    this.disposeReplRuntime(threadId, runtime)
    return { ok: true }
  }

  reset(threadId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (runtime) {
      runtime.activeAbortController?.abort(new Error('NodeReplJs reset.'))
      if (runtime.activeEvaluationId) {
        void this.cancelChromeCommands(runtime, runtime.activeEvaluationId, 'reset')
        this.browserManager.abortEvaluation(threadId, runtime.activeEvaluationId)
      }
      this.disposeReplRuntime(threadId, runtime)
    }
    const browserReset = this.browserManager.reset(threadId)
    return { ok: Boolean(runtime) || browserReset.ok }
  }

  async disposeAllForTests(): Promise<void> {
    for (const [threadId, runtime] of [...this.runtimes]) {
      this.disposeReplRuntime(threadId, runtime)
    }
  }

  private getOrCreateRuntime(threadId: string): NodeReplThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const runtime = createReplRuntime()
    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private refreshContext(
    runtime: NodeReplThreadRuntime,
    browserRuntime: BrowserRuntimeBindings,
    threadId: string,
    evaluationId: string,
    signal: AbortSignal,
    workspacePath?: string,
    browserSession?: BrowserSessionMetadata
  ): void {
    const globals = runtime.globals
    const ensureActive = () => {
      if (signal.aborted || runtime.activeEvaluationId !== evaluationId) {
        throw new Error(`NodeReplJs evaluation is no longer active (phase=${runtime.phase ?? 'unknown'}).`)
      }
    }
    const consoleApi = {
      log: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      },
      warn: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      },
      error: (...args: unknown[]) => {
        if (runtime.activeEvaluationId === evaluationId) runtime.logs.push(args.map(describeResult).join(' '))
      }
    }
    const display = async (imageLike: unknown) => {
      ensureActive()
      await browserRuntime.display(imageLike)
    }
    const nodeReplEnv = Object.freeze({
      BROWSER_USE_AVAILABLE_BACKENDS: 'iab',
      BROWSER_USE_DISABLE_AMBIENT_NETWORK: '1',
      BROWSER_USE_SECURITY_MODE: 'disabled-for-local-testing'
    })
    const browserTurnMetadata = Object.freeze({
      session_id: browserSession?.sessionId,
      thread_id: browserSession?.threadId,
      turn_id: browserSession?.turnId,
      evaluation_id: browserSession?.evaluationId,
      backend_id: browserSession?.backendId ?? 'iab'
    })
    const requestMeta = Object.freeze({
      'x-dotcraft-turn-metadata': browserTurnMetadata
    })
    const setResponseMeta = (metaOrKey: unknown, value?: unknown) => {
      if (typeof metaOrKey === 'string') {
        runtime.responseMeta[metaOrKey] = value
        return
      }
      if (metaOrKey && typeof metaOrKey === 'object' && !Array.isArray(metaOrKey)) {
        Object.assign(runtime.responseMeta, metaOrKey)
      }
    }
    const nodeReplApi = Object.freeze({
      emitImage: display,
      setResponseMeta,
      createElicitation: async (request: unknown) => await this.browserManager.handleBrowserUseElicitation(threadId, request),
      env: nodeReplEnv,
      fetch: typeof fetch === 'function' ? fetch.bind(globalThis) : undefined,
      nativePipe: Object.freeze({
        createConnection: createBrowserUseNativePipeConnection
      }),
      requestMeta,
      tmpDir: tmpdir()
    })
    globals.agent = browserRuntime.agent
    globals.display = display
    globals.nodeRepl = nodeReplApi
    globals.console = consoleApi
    globals.setTimeout = setTimeout
    globals.clearTimeout = clearTimeout
    globals.setInterval = setInterval
    globals.clearInterval = clearInterval
    const chromePluginRoot = resolveChromePluginRoot(workspacePath)
    const dotcraftApi = Object.freeze({
      browserClientPath: resolveBrowserClientPath(),
      chromeBrowserClientPath: resolveChromeBrowserClientPath(),
      workspacePath: workspacePath ?? '',
      browserSession,
      chromePluginRoot,
      chromeScriptsPath: join(chromePluginRoot, 'scripts'),
      chrome: createChromeSetupApi(workspacePath)
    })
    globals.dotcraft = dotcraftApi
    ;(globalThis as Record<string, unknown>).process = nodeProcess
    ;(globalThis as Record<string, unknown>).nodeRepl = nodeReplApi
    ;(globalThis as Record<string, unknown>).dotcraft = dotcraftApi
    globals.URL = NodeUrl
    globals.__dotcraftDynamicImport = async (specifier: unknown) => import(String(specifier))
    globals.__dotcraftSetChromeCancelHook = (hook: unknown) => {
      if (typeof hook === 'function') {
        runtime.chromeCancelEvaluation = hook as (evaluationId: string, reason: string) => Promise<void> | void
      }
    }
    globals.__dotcraftClearChromeCancelHook = () => {
      runtime.chromeCancelEvaluation = undefined
    }
    const setupBrowserRuntime = async (
      options?: { globals?: Record<string, unknown>; backend?: string }
    ) => {
      ensureActive()
      const targetGlobals = options?.globals ?? globals
      targetGlobals.agent = browserRuntime.agent
      targetGlobals.display = display
      targetGlobals.nodeRepl = nodeReplApi
      targetGlobals.dotcraft = globals.dotcraft
      return { backend: options?.backend ?? 'iab' }
    }
    globals.__dotcraftSetupBrowserRuntime = setupBrowserRuntime
  }

  private disposeReplRuntime(threadId: string, runtime: NodeReplThreadRuntime): void {
    if (this.runtimes.get(threadId) !== runtime) return
    runtime.activeEvaluationId = undefined
    runtime.activeAbortController = undefined
    runtime.chromeCancelEvaluation = undefined
    this.runtimes.delete(threadId)
  }

  private async cancelChromeCommands(
    runtime: NodeReplThreadRuntime,
    evaluationId: string,
    reason: string
  ): Promise<void> {
    try {
      await runtime.chromeCancelEvaluation?.(evaluationId, reason)
    } catch (error) {
      runtime.logs.push(`Chrome cancel hook failed: ${error instanceof Error ? error.message : String(error)}`)
    }
  }

  private withTimeout<T>(
    promise: Promise<T>,
    timeoutMs: number,
    signal: AbortSignal,
    phase: () => string | undefined
  ): Promise<T> {
    return new Promise((resolve, reject) => {
      let settled = false
      const cleanup = () => {
        clearTimeout(timeout)
        signal.removeEventListener('abort', onAbort)
      }
      const finish = (callback: () => void) => {
        if (settled) return
        settled = true
        cleanup()
        callback()
      }
      const onAbort = () => {
        const reason = signal.reason
        finish(() => reject(reason instanceof Error
          ? reason
          : new NodeReplEvaluationCancelledError(phase())))
      }
      const timeout = setTimeout(
        () => finish(() => reject(new NodeReplEvaluationTimeoutError(timeoutMs, phase()))),
        timeoutMs)
      if (signal.aborted) {
        onAbort()
        return
      }
      signal.addEventListener('abort', onAbort, { once: true })
      promise.then(
        (value) => {
          finish(() => resolve(value))
        },
        (error) => {
          finish(() => reject(error))
        }
      )
    })
  }
}

export const nodeReplManager = new NodeReplManager()
