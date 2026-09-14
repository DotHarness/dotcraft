import { type BrowserWindow } from 'electron'
import { browserUseManager, type BrowserUseImageResult, type BrowserUseManager } from './browserUseManager'
import { NodeReplWorkerClient, defaultForkReplWorker, type ForkReplWorker } from './repl/NodeReplWorkerClient'
import { createReplHostContext, handleChromeHostCall } from './repl/nodeReplHost'

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
  worker: NodeReplWorkerClient
  activeEvaluationId?: string
  activeAbortController?: AbortController
  phase?: string
}

interface NodeReplQueueState {
  tail: Promise<void>
  generation: number
  pending: number
}

interface NodeReplEvaluationSlot {
  generation: number
  ready: Promise<void>
  release(): void
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

export class NodeReplManager {
  private readonly runtimes = new Map<string, NodeReplThreadRuntime>()
  private readonly evaluationQueues = new Map<string, NodeReplQueueState>()
  private readonly cancelledEvaluations = new Map<string, Set<string>>()

  constructor(
    private readonly browserManager: BrowserUseManager = browserUseManager,
    private readonly forkWorker: ForkReplWorker = defaultForkReplWorker
  ) {}

  async evaluate(owner: BrowserWindow, params: NodeReplEvaluateParams): Promise<NodeReplEvaluateResult> {
    if (!params.threadId || typeof params.code !== 'string') {
      return { error: 'Invalid Node REPL evaluate request.', images: [], logs: [] }
    }

    const evaluationId = params.evaluationId?.trim() || newEvaluationId()
    const slot = this.enqueueEvaluation(params.threadId)
    try {
      await slot.ready
      if (this.isQueueGenerationStale(params.threadId, slot.generation) ||
        this.consumeCancelledEvaluation(params.threadId, evaluationId)) {
        return { error: 'NodeReplJs cancelled before it started.', images: [], logs: [] }
      }
      return await this.evaluateNow(owner, params, evaluationId)
    } catch (error) {
      return { error: formatError(error, 'prepare'), images: [], logs: [] }
    } finally {
      slot.release()
    }
  }

  private async evaluateNow(
    owner: BrowserWindow,
    params: NodeReplEvaluateParams,
    evaluationId: string
  ): Promise<NodeReplEvaluateResult> {
    const runtime = this.getOrCreateRuntime(params.threadId)
    const browserSession = normalizeBrowserSession(params, evaluationId)
    const abortController = new AbortController()
    runtime.activeEvaluationId = evaluationId
    runtime.activeAbortController = abortController
    runtime.phase = 'prepare'
    let browserRuntime: Awaited<ReturnType<BrowserUseManager['prepareNodeRepl']>> | undefined
    const timeoutMs = Math.max(1_000, Math.min(params.timeoutMs ?? 30_000, 120_000))
    try {
      const result = await this.withTimeout((async () => {
        browserRuntime = await this.browserManager.prepareNodeRepl(owner, {
          threadId: params.threadId, workspacePath: params.workspacePath, evaluationId,
          signal: abortController.signal, browserSession
        })
        if (abortController.signal.aborted) throw new NodeReplEvaluationCancelledError(runtime.phase)
        const workspacePath = params.workspacePath || process.cwd()
        runtime.phase = 'js-runtime'
        return await runtime.worker.evaluate({
          threadId: params.threadId, evaluationId, workspacePath,
          dotcraft: createReplHostContext(workspacePath, { ...browserSession }), browserSession: { ...browserSession }
        }, params.code, async (method, value) => {
          if (abortController.signal.aborted) throw new NodeReplEvaluationCancelledError(runtime.phase)
          if (method === 'emitImage') return await browserRuntime!.display(value)
          if (method === 'createElicitation') return await this.browserManager.handleBrowserUseElicitation(params.threadId, value)
          return await handleChromeHostCall(method, workspacePath)
        })
      })(), timeoutMs, abortController.signal, () => runtime.phase)
      const collected = browserRuntime?.collect()
      return { ...result, images: collected?.images ?? [], logs: [...result.logs, ...(collected?.logs ?? [])] }
    } catch (error: unknown) {
      const outer = error instanceof NodeReplEvaluationTimeoutError || abortController.signal.aborted
      if (outer) {
        abortController.abort()
        this.browserManager.abortEvaluation(params.threadId, evaluationId)
        await runtime.worker.stop(evaluationId)
        this.disposeReplRuntime(params.threadId, runtime)
      } else if (runtime.worker.closed) {
        this.disposeReplRuntime(params.threadId, runtime)
      }
      const collected = browserRuntime?.collect()
      return { error: outer && error instanceof Error ? error.message : formatError(error, runtime.phase),
        images: collected?.images ?? [], logs: [...runtime.worker.logs, ...(collected?.logs ?? [])] }
    } finally {
      if (runtime.activeEvaluationId === evaluationId) {
        runtime.activeEvaluationId = undefined
        runtime.activeAbortController = undefined
        runtime.phase = 'idle'
      }
    }
  }

  private enqueueEvaluation(threadId: string): NodeReplEvaluationSlot {
    const state = this.getOrCreateQueueState(threadId)
    const generation = state.generation
    const previous = state.tail
    let release!: () => void
    const current = new Promise<void>((resolve) => {
      release = resolve
    })
    state.pending += 1
    state.tail = previous.then(() => current)
    return {
      generation,
      ready: previous,
      release: () => {
        state.pending -= 1
        release()
        if (state.pending === 0 && this.evaluationQueues.get(threadId) === state) {
          this.evaluationQueues.delete(threadId)
        }
      }
    }
  }

  private getOrCreateQueueState(threadId: string): NodeReplQueueState {
    const existing = this.evaluationQueues.get(threadId)
    if (existing) return existing
    const created: NodeReplQueueState = {
      tail: Promise.resolve(),
      generation: 0,
      pending: 0
    }
    this.evaluationQueues.set(threadId, created)
    return created
  }

  private isQueueGenerationStale(threadId: string, generation: number): boolean {
    const state = this.evaluationQueues.get(threadId)
    return Boolean(state && state.generation !== generation)
  }

  private consumeCancelledEvaluation(threadId: string, evaluationId: string): boolean {
    const cancelled = this.cancelledEvaluations.get(threadId)
    if (!cancelled?.delete(evaluationId)) return false
    if (cancelled.size === 0) this.cancelledEvaluations.delete(threadId)
    return true
  }

  private cancelQueuedEvaluations(threadId: string): void {
    const state = this.evaluationQueues.get(threadId)
    if (!state) return
    state.generation += 1
  }

  cancel(threadId: string, evaluationId: string): { ok: boolean } {
    const runtime = this.runtimes.get(threadId)
    if (!runtime || runtime.activeEvaluationId !== evaluationId) {
      const queue = this.evaluationQueues.get(threadId)
      if (!queue) return { ok: false }
      let cancelled = this.cancelledEvaluations.get(threadId)
      if (!cancelled) {
        cancelled = new Set<string>()
        this.cancelledEvaluations.set(threadId, cancelled)
      }
      cancelled.add(evaluationId)
      return { ok: true }
    }
    this.cancelQueuedEvaluations(threadId)
    runtime.activeAbortController?.abort(new NodeReplEvaluationCancelledError(runtime.phase))
    this.browserManager.abortEvaluation(threadId, evaluationId)
    return { ok: true }
  }

  reset(threadId: string): { ok: boolean } {
    this.cancelQueuedEvaluations(threadId)
    this.cancelledEvaluations.delete(threadId)
    const runtime = this.runtimes.get(threadId)
    if (runtime) {
      runtime.activeAbortController?.abort(new Error('NodeReplJs reset.'))
      if (runtime.activeEvaluationId) {
        this.browserManager.abortEvaluation(threadId, runtime.activeEvaluationId)
      }
      this.disposeReplRuntime(threadId, runtime)
    }
    const browserReset = this.browserManager.reset(threadId)
    return { ok: Boolean(runtime) || browserReset.ok }
  }

  handleNotification(method: string, params: unknown): void {
    if (method !== 'thread/deleted' || !params || typeof params !== 'object') return
    const threadId = (params as { threadId?: unknown }).threadId
    if (typeof threadId === 'string') this.reset(threadId)
  }

  async disposeAll(): Promise<void> {
    for (const threadId of this.evaluationQueues.keys()) this.cancelQueuedEvaluations(threadId)
    for (const [threadId, runtime] of [...this.runtimes]) {
      runtime.activeAbortController?.abort(new Error('NodeReplJs disposed.'))
      if (runtime.activeEvaluationId) this.browserManager.abortEvaluation(threadId, runtime.activeEvaluationId)
      await runtime.worker.stop(runtime.activeEvaluationId)
      this.disposeReplRuntime(threadId, runtime)
    }
    await Promise.all([...this.evaluationQueues.values()].map(state => state.tail))
    this.evaluationQueues.clear()
    this.cancelledEvaluations.clear()
  }

  private getOrCreateRuntime(threadId: string): NodeReplThreadRuntime {
    const existing = this.runtimes.get(threadId)
    if (existing) return existing

    const runtime: NodeReplThreadRuntime = { worker: new NodeReplWorkerClient(this.forkWorker), phase: 'idle' }
    this.runtimes.set(threadId, runtime)
    return runtime
  }

  private disposeReplRuntime(threadId: string, runtime: NodeReplThreadRuntime): void {
    if (this.runtimes.get(threadId) !== runtime) return
    void runtime.worker.stop(runtime.activeEvaluationId)
    this.runtimes.delete(threadId)
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
