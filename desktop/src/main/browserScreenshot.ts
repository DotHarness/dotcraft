import type { WebContents } from 'electron'
import { BrowserUseBackendError } from './browserUseBackendServer'

type Size = { width: number; height: number }
type Clip = Size & { x: number; y: number; scale?: number }
type Metrics = {
  cssContentSize: Clip
  cssVisualViewport: { pageX: number; pageY: number; clientWidth: number; clientHeight: number }
}

export interface BrowserScreenshotContext {
  tabId: string
  page: WebContents
  layoutSize: Size
  visible: boolean
  timeoutMs: number
  signal?: AbortSignal
  send(method: string, params?: Record<string, unknown>): Promise<unknown>
  setSurface(size: Size | null): void
  diagnostic(message: string): void
}

const IMAGE = { format: 'jpeg', quality: 80 } as const

function clipOf(source: Size & { x: number; y: number }, scale: number): Clip {
  return {
    x: Math.max(0, source.x), y: Math.max(0, source.y),
    width: Math.max(1, source.width), height: Math.max(1, source.height), scale
  }
}

function clipSize(clip: unknown): Size | undefined {
  const { width, height } = (clip ?? {}) as Partial<Size>
  return typeof width === 'number' && typeof height === 'number' &&
    Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0 ? { width, height } : undefined
}

interface CaptureState {
  queue: Promise<void>
  rawScreencast: boolean
  internalScreencast: boolean
  sessions: Map<number, number>
}

class CaptureOperation {
  readonly controller = new AbortController()
  readonly deadline: number
  stage = 'metrics'
  private readonly timer: ReturnType<typeof setTimeout>
  private hasSurface = false
  private readonly onAbort = () => this.controller.abort(BrowserUseBackendError.commandCancelled('Screenshot was cancelled.'))
  private readonly onClosed = () => this.controller.abort(BrowserUseBackendError.pageClosed(this.context.tabId))

  constructor(readonly context: BrowserScreenshotContext) {
    this.deadline = Date.now() + context.timeoutMs
    this.timer = setTimeout(() => this.controller.abort(this.timeout()), context.timeoutMs)
    context.signal?.addEventListener('abort', this.onAbort, { once: true })
    context.page.once('destroyed', this.onClosed)
    context.page.debugger.on('detach', this.onClosed)
    if (context.signal?.aborted) this.onAbort()
    if (context.page.isDestroyed()) this.onClosed()
  }

  timeout(timeoutMs = this.context.timeoutMs): Error {
    let url = 'about:blank'
    try { url = this.context.page.getURL() || url } catch {}
    return BrowserUseBackendError.commandTimeout(
      `Browser operation 'screenshot' timed out after ${timeoutMs}ms for tab ${this.context.tabId} at ${url} (stage: ${this.stage}).`,
      { operation: 'screenshot', stage: this.stage, tabId: this.context.tabId, url })
  }

  async run<T>(stage: string, timeoutMs: number, action: () => Promise<T>): Promise<T> {
    this.stage = stage
    const signal = this.controller.signal
    signal.throwIfAborted()
    const startedAt = Date.now()
    const remaining = Math.min(timeoutMs, this.deadline - startedAt)
    if (remaining <= 0) throw this.timeout()
    let timer: ReturnType<typeof setTimeout> | undefined
    let abort: (() => void) | undefined
    try {
      const bounded = new Promise<never>((_, reject) => {
        abort = () => reject(signal.reason)
        signal.addEventListener('abort', abort, { once: true })
        timer = setTimeout(() => reject(this.timeout(remaining)), remaining)
      })
      const result = await Promise.race([action(), bounded])
      this.context.diagnostic(`screenshot.${stage} status=completed elapsedMs=${Date.now() - startedAt} timeoutMs=${remaining}`)
      return result
    } catch (error) {
      this.context.diagnostic(`screenshot.${stage} status=failed elapsedMs=${Date.now() - startedAt} timeoutMs=${remaining}`)
      if (error instanceof BrowserUseBackendError) throw error
      throw new BrowserUseBackendError(`Screenshot ${stage} failed for tab ${this.context.tabId}: ${error instanceof Error ? error.message : String(error)}`,
        -32000, { operation: 'screenshot', stage, tabId: this.context.tabId })
    } finally {
      clearTimeout(timer)
      if (abort) signal.removeEventListener('abort', abort)
    }
  }

  async send<T>(stage: string, method: string, params?: Record<string, unknown>, timeoutMs = 5_000): Promise<T> {
    return await this.run(stage, timeoutMs, () => this.context.send(method, params)) as T
  }

  setSurface(size: Size): void {
    this.controller.signal.throwIfAborted()
    this.hasSurface = true
    this.context.setSurface(size)
    this.context.diagnostic(`screenshot.surface width=${size.width} height=${size.height} throttling=${this.context.page.getBackgroundThrottling()}`)
  }

  dispose(): void {
    clearTimeout(this.timer)
    this.context.signal?.removeEventListener('abort', this.onAbort)
    this.context.page.removeListener('destroyed', this.onClosed)
    this.context.page.debugger.off('detach', this.onClosed)
    if (this.hasSurface) this.context.setSurface(null)
  }
}

export class BrowserScreenshot {
  private readonly states = new WeakMap<WebContents, CaptureState>()

  private state(page: WebContents): CaptureState {
    let state = this.states.get(page)
    if (!state) {
      state = { queue: Promise.resolve(), rawScreencast: false, internalScreencast: false, sessions: new Map() }
      this.states.set(page, state)
    }
    return state
  }

  consumesEvent(page: WebContents, method: string, params: Record<string, unknown>, sessionId?: string): boolean {
    if (sessionId) return false
    const state = this.states.get(page)
    if (!state) return false
    for (const [id, expires] of state.sessions) if (expires < Date.now()) state.sessions.delete(id)
    if (method === 'Page.screencastVisibilityChanged') return state.internalScreencast
    if (method !== 'Page.screencastFrame') return false
    const id = Number(params.sessionId)
    if (state.internalScreencast && Number.isFinite(id)) state.sessions.set(id, Date.now() + 10_000)
    return state.internalScreencast || (!state.rawScreencast && state.sessions.has(id))
  }

  reset(page: WebContents): void {
    const state = this.states.get(page)
    if (state) state.rawScreencast = false
  }

  async rawScreencast<T>(context: BrowserScreenshotContext, method: string, send: () => Promise<T>): Promise<T> {
    return await this.serialized(context.page, async () => {
      const operation = new CaptureOperation(context)
      try {
        const result = await operation.run('raw-screencast', 5_000, send)
        this.state(context.page).rawScreencast = method === 'Page.startScreencast'
        return result
      } finally { operation.dispose() }
    })
  }

  private async serialized<T>(page: WebContents, run: () => Promise<T>): Promise<T> {
    const state = this.state(page)
    const previous = state.queue
    let release!: () => void
    state.queue = new Promise<void>(resolve => { release = resolve })
    await previous
    try { return await run() } finally { release() }
  }

  async screenshot(context: BrowserScreenshotContext, options?: { fullPage?: boolean; clip?: Electron.Rectangle }): Promise<string> {
    return await this.serialized(context.page, async () => {
      const operation = new CaptureOperation(context)
      try {
        if (options?.clip || options?.fullPage) {
          const source = options.clip ?? (await operation.send<Metrics>('metrics', 'Page.getLayoutMetrics')).cssContentSize
          const clip = clipOf(source, await this.scale(operation))
          return (await this.capture(operation, { ...IMAGE, captureBeyondViewport: true, clip })).data as string
        }
        if (!context.visible) operation.setSurface(context.layoutSize)
        const { cssVisualViewport: view } = await operation.send<Metrics>('metrics', 'Page.getLayoutMetrics')
        const scale = await this.scale(operation)
        if (scale <= 1 && !this.state(context.page).rawScreencast) {
          const frame = await this.screencast(operation, { width: view.clientWidth, height: view.clientHeight })
          if (frame) return frame
        }
        const clip = clipOf({ x: view.pageX, y: view.pageY, width: view.clientWidth, height: view.clientHeight }, scale)
        return (await this.captureImage(operation, { ...IMAGE, clip })).data as string
      } finally { operation.dispose() }
    })
  }

  async captureCdp(context: BrowserScreenshotContext, params: Record<string, unknown>): Promise<Record<string, unknown>> {
    return await this.serialized(context.page, async () => {
      const operation = new CaptureOperation(context)
      try { return await this.capture(operation, params) } finally { operation.dispose() }
    })
  }

  private async capture(operation: CaptureOperation, params: Record<string, unknown>): Promise<Record<string, unknown>> {
    const { context } = operation
    const size = params.captureBeyondViewport === true ? clipSize(params.clip) : undefined
    if (size) {
      const surface = {
        width: Math.max(context.layoutSize.width, Math.ceil(size.width)),
        height: Math.max(context.layoutSize.height, Math.ceil(size.height))
      }
      operation.setSurface(surface)
      await this.settle(operation, surface)
    } else if (!context.visible) {
      operation.setSurface(context.layoutSize)
    }
    return await this.captureImage(operation, params)
  }

  private async captureImage(operation: CaptureOperation, params: Record<string, unknown>): Promise<Record<string, unknown>> {
    const result = await operation.send<Record<string, unknown>>('capture', 'Page.captureScreenshot', params)
    if (typeof result.data !== 'string' || !result.data) {
      throw new Error(`Page.captureScreenshot returned no data for tab ${operation.context.tabId}.`)
    }
    return result
  }

  private async settle(operation: CaptureOperation, surface: Size): Promise<void> {
    const { context } = operation
    const surfaceDeadline = Date.now() + 1_000
    try {
      await operation.run('surface-wait', 1_000, async () => {
        while (Date.now() < surfaceDeadline) {
          operation.controller.signal.throwIfAborted()
          const metrics = await context.send('Page.getLayoutMetrics') as Metrics
          operation.controller.signal.throwIfAborted()
          if (Date.now() >= surfaceDeadline) return
          const viewport = metrics.cssVisualViewport
          if (viewport.clientWidth >= surface.width && viewport.clientHeight >= surface.height) return
          await new Promise(resolve => setTimeout(resolve, 16))
        }
      })
    } catch (error) {
      operation.controller.signal.throwIfAborted()
      if (!(error instanceof BrowserUseBackendError) || error.code !== -32010 ||
          (error.data as { stage?: string } | undefined)?.stage !== 'surface-wait') throw error
    }
  }

  private async scale(operation: CaptureOperation): Promise<number> {
    try {
      const result = await operation.send<{ result?: { value?: unknown } }>(
        'scale', 'Runtime.evaluate', { expression: 'window.devicePixelRatio', returnByValue: true })
      const ratio = result.result?.value
      if (typeof ratio === 'number' && Number.isFinite(ratio) && ratio > 0) return 1 / ratio
    } catch {
      operation.controller.signal.throwIfAborted()
    }
    return 1
  }

  private async screencast(operation: CaptureOperation, size: Size): Promise<string | undefined> {
    const { context } = operation
    const state = this.state(context.page)
    let started = false
    let resolveFrame!: (frame: string | undefined) => void
    const frame = new Promise<string | undefined>(resolve => { resolveFrame = resolve })
    const startedAt = Date.now() / 1_000
    const listener = (_event: unknown, method: string, params: Record<string, unknown>, sessionId?: string) => {
      if (sessionId) return
      if (method === 'Page.screencastVisibilityChanged' && params.visible === false) resolveFrame(undefined)
      if (method !== 'Page.screencastFrame') return
      const id = Number(params.sessionId)
      if (!Number.isFinite(id)) return
      state.sessions.set(id, Date.now() + 10_000)
      const metadata = params.metadata as { timestamp?: number } | undefined
      const ack = context.send('Page.screencastFrameAck', { sessionId: id }).catch(() => {})
      if (Number(metadata?.timestamp) >= startedAt && typeof params.data === 'string' && params.data) {
        void ack.then(() => resolveFrame(params.data as string))
      }
    }
    state.internalScreencast = true
    context.page.debugger.on('message', listener)
    try {
      return await operation.run('screencast', 2_000, async () => {
        started = true
        await context.send('Page.startScreencast', {
          ...IMAGE, everyNthFrame: 1, maxWidth: Math.round(size.width), maxHeight: Math.round(size.height)
        })
        return await frame
      })
    } catch {
      operation.controller.signal.throwIfAborted()
      return undefined
    } finally {
      context.page.debugger.off('message', listener)
      if (started && !context.page.isDestroyed()) {
        let timer: ReturnType<typeof setTimeout> | undefined
        try {
          await Promise.race([
            context.send('Page.stopScreencast'),
            new Promise(resolve => { timer = setTimeout(resolve, 250) })
          ])
        } catch {} finally { clearTimeout(timer) }
      }
      state.internalScreencast = false
    }
  }
}
