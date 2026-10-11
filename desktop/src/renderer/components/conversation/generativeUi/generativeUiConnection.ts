import { readVisualizationLinkUrl } from '../visualizationHost'
import {
  GENERATIVE_UI_LIMITS,
  GENERATIVE_UI_PROTOCOL_VERSION,
  GENERATIVE_UI_RUNNER_FAILURE_STAGES,
  generativeUiActionLimit,
  generativeUiJsonBytes,
  readGenerativeUiSavedState,
  type GenerativeUiAction,
  type GenerativeUiActionKind,
  type GenerativeUiBreakpoint,
  type GenerativeUiFailure,
  type GenerativeUiFailureStage,
  type GenerativeUiJson,
  type GenerativeUiNode,
  type GenerativeUiSavedState,
  type GenerativeUiTheme
} from './generativeUiProtocol'
import { validateGenerativeUiTree } from './generativeUiTree'

export interface GenerativeUiPort {
  postMessage: (message: unknown) => void
  addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void
  removeEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void
  start?: () => void
  close: () => void
}

export interface GenerativeUiConnectionHandlers {
  onSnapshot: (tree: GenerativeUiNode[], version: number) => void
  onAction: (action: GenerativeUiAction) => void
  onState?: (state: GenerativeUiSavedState) => void
  onFailure: (failure: GenerativeUiFailure) => void
}

export interface GenerativeUiStartOptions {
  code: string
  theme: GenerativeUiTheme
  breakpoint: GenerativeUiBreakpoint
  initialState?: GenerativeUiSavedState
}

const ACTION_KINDS = new Set(['sendMessage', 'prefillComposer', 'copy', 'openUrl'])

export class GenerativeUiConnection {
  private closed = false
  private version = 0
  private startTimer: ReturnType<typeof setTimeout> | undefined
  private flushTimer: ReturnType<typeof setTimeout> | undefined
  private callbackTimes: number[] = []
  private readonly pendingCallbacks = new Map<string, GenerativeUiJson[]>()
  private actionGrants: number[] = []
  private lastCallbackAt = Number.NEGATIVE_INFINITY

  constructor(private readonly port: GenerativeUiPort, private readonly handlers: GenerativeUiConnectionHandlers) {
    port.addEventListener('message', this.receive)
    port.start?.()
  }

  start({ code, theme, breakpoint, initialState }: GenerativeUiStartOptions): void {
    if (new TextEncoder().encode(code).byteLength > GENERATIVE_UI_LIMITS.maxSourceBytes) {
      this.fail({ stage: 'payload_too_large', message: `The program is larger than ${GENERATIVE_UI_LIMITS.maxSourceBytes / 1024} KB.` })
      return
    }
    if (initialState !== undefined && generativeUiJsonBytes(initialState) > GENERATIVE_UI_LIMITS.maxSavedStateBytes) {
      this.fail({ stage: 'payload_too_large', message: `The saved state is larger than ${GENERATIVE_UI_LIMITS.maxSavedStateBytes / 1024} KB.` })
      return
    }
    this.send({ type: 'start', code, theme, breakpoint, ...(initialState === undefined ? {} : { initialState }) })
    this.startTimer = setTimeout(() => this.fail({
      stage: 'worker_timeout',
      message: `The program did not render within ${GENERATIVE_UI_LIMITS.firstSnapshotTimeoutMs / 1000} seconds.`
    }), GENERATIVE_UI_LIMITS.firstSnapshotTimeoutMs)
  }

  invoke(id: string, args: GenerativeUiJson[]): void {
    if (this.closed) return
    this.pendingCallbacks.delete(id)
    this.pendingCallbacks.set(id, args)
    this.flushCallbacks()
  }

  setGlobals(theme: GenerativeUiTheme, breakpoint: GenerativeUiBreakpoint): void {
    if (!this.closed) this.send({ type: 'globals', theme, breakpoint })
  }

  restore(state: GenerativeUiSavedState): void {
    if (!this.closed && generativeUiJsonBytes(state) <= GENERATIVE_UI_LIMITS.maxSavedStateBytes) this.send({ type: 'restore', state })
  }

  dispose(): void {
    if (!this.closed) this.close()
  }

  private flushCallbacks(): void {
    clearTimeout(this.flushTimer)
    this.flushTimer = undefined
    const now = Date.now()
    this.callbackTimes = this.callbackTimes.filter(time => now - time < 1000)
    for (const [id, args] of this.pendingCallbacks) {
      if (this.callbackTimes.length >= GENERATIVE_UI_LIMITS.maxCallbacksPerSecond) break
      this.pendingCallbacks.delete(id)
      this.callbackTimes.push(now)
      this.actionGrants.push(now)
      this.lastCallbackAt = now
      this.send({ type: 'callback', id, args })
    }
    if (this.pendingCallbacks.size > 0) {
      this.flushTimer = setTimeout(() => this.flushCallbacks(), Math.max(1, 1000 - (now - this.callbackTimes[0])))
    }
  }

  private readonly receive = (event: MessageEvent): void => {
    if (this.closed) return
    const message = event.data as Record<string, unknown> | null
    if (!message || typeof message !== 'object') {
      this.fail({ stage: 'invalid_payload', message: 'The runner sent an unreadable message.' })
      return
    }
    if (message.protocolVersion !== GENERATIVE_UI_PROTOCOL_VERSION) {
      this.fail({ stage: 'protocol_mismatch', message: `Expected protocol version ${GENERATIVE_UI_PROTOCOL_VERSION}.` })
      return
    }
    if (message.type === 'snapshot') this.receiveSnapshot(message)
    else if (message.type === 'action') this.receiveAction(message)
    else if (message.type === 'state') this.receiveState(message)
    else if (message.type === 'error') this.receiveError(message)
    else this.fail({ stage: 'invalid_payload', message: 'The runner sent an unknown message.' })
  }

  private receiveSnapshot(message: Record<string, unknown>): void {
    const { version } = message
    if (typeof version !== 'number' || !Number.isInteger(version) || version <= this.version) {
      this.fail({ stage: 'invalid_payload', message: 'The runner sent an out-of-order snapshot.' })
      return
    }
    const result = validateGenerativeUiTree(message.tree)
    if (!result.ok) {
      this.fail({ stage: result.stage, message: result.message })
      return
    }
    clearTimeout(this.startTimer)
    this.version = version
    this.handlers.onSnapshot(result.tree, version)
  }

  private receiveAction(message: Record<string, unknown>): void {
    const action = readAction(message)
    if (!action) {
      this.fail({ stage: 'invalid_payload', message: 'The runner sent an invalid action.' })
      return
    }
    const now = Date.now()
    this.actionGrants = this.actionGrants.filter(time => now - time <= GENERATIVE_UI_LIMITS.actionWindowMs)
    if (this.actionGrants.shift() === undefined) return
    this.handlers.onAction(action)
  }

  private receiveState(message: Record<string, unknown>): void {
    const state = readGenerativeUiSavedState(message.state)
    if (!state) {
      this.fail({ stage: 'invalid_payload', message: 'The runner sent invalid saved state.' })
      return
    }
    if (Date.now() - this.lastCallbackAt > GENERATIVE_UI_LIMITS.actionWindowMs) return
    if (generativeUiJsonBytes(state) > GENERATIVE_UI_LIMITS.maxSavedStateBytes) return
    this.handlers.onState?.(state)
  }

  private receiveError(message: Record<string, unknown>): void {
    const stage = message.stage as GenerativeUiFailureStage
    if (!GENERATIVE_UI_RUNNER_FAILURE_STAGES.has(stage)) {
      this.fail({ stage: 'invalid_payload', message: 'The runner reported an unknown failure.' })
      return
    }
    const text = typeof message.message === 'string' ? message.message.slice(0, GENERATIVE_UI_LIMITS.maxErrorMessageLength) : ''
    this.fail({ stage, message: text, line: readPosition(message.line), column: readPosition(message.column) })
  }

  private fail(failure: GenerativeUiFailure): void {
    if (this.closed) return
    this.close()
    this.handlers.onFailure(failure)
  }

  private send(message: Record<string, unknown>): void {
    this.port.postMessage({ protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION, ...message })
  }

  private close(): void {
    this.send({ type: 'dispose' })
    this.closed = true
    clearTimeout(this.startTimer)
    clearTimeout(this.flushTimer)
    this.pendingCallbacks.clear()
    this.port.removeEventListener('message', this.receive)
    this.port.close()
  }
}

function readAction(message: Record<string, unknown>): GenerativeUiAction | null {
  const { kind, payload } = message
  if (typeof kind !== 'string' || !ACTION_KINDS.has(kind) || !payload || typeof payload !== 'object') return null
  const field = kind === 'openUrl' ? 'url' : 'text'
  const value = (payload as Record<string, unknown>)[field]
  if (typeof value !== 'string' || value.length === 0) return null
  if (kind === 'openUrl') {
    const url = readVisualizationLinkUrl(value)
    return url ? { kind, url } : null
  }
  if (new TextEncoder().encode(value).byteLength > generativeUiActionLimit(kind as GenerativeUiActionKind)) return null
  return { kind: kind as 'sendMessage' | 'prefillComposer' | 'copy', text: value }
}

function readPosition(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : undefined
}
