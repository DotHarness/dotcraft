import { createInterface } from 'readline'
import { Readable, Writable } from 'stream'
import { EventEmitter } from 'events'
import WebSocket from 'ws'

export interface ServerInfo {
  name: string
  version: string
  protocolVersion?: string
  extensions?: string[]
}

export interface ServerCapabilities {
  threadManagement?: boolean
  threadSubscriptions?: boolean
  approvalFlow?: boolean
  modeSwitch?: boolean
  configOverride?: boolean
  cronManagement?: boolean
  heartbeatManagement?: boolean
  skillsManagement?: boolean
  commandManagement?: boolean
  modelCatalogManagement?: boolean
  workspaceConfigManagement?: boolean
  sourceControlManagement?: boolean
  memoryManagement?: boolean
  dreams?: boolean
  mcpManagement?: boolean
  mcpRuntime?: boolean
  mcpElicitation?: boolean
  hooksManagement?: boolean
  pluginManagement?: boolean
  externalChannelManagement?: boolean
  mcpStatus?: boolean
  subAgentManagement?: boolean
  manualCompaction?: boolean
  backgroundTerminals?: boolean
}

export interface InitializeResult {
  serverInfo: ServerInfo
  capabilities: ServerCapabilities
  /** Present when AppServer hosts DashBoard (initialize result). */
  dashboardUrl?: string
}

export const INITIALIZE_REQUEST_TIMEOUT_MS: number | null = null

export interface WireProtocolClientOptions {
  defaultTimeoutMs?: number
  autoInitializeOnTransportOpen?: boolean
  initializeTimeoutMs?: number | null
  initializeProfile?: InitializeProfile
}

export type InitializeProfile = 'foreground' | 'secondary'

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (reason: Error) => void
  timer: ReturnType<typeof setTimeout> | null
}

/** AppServer puts human-readable detail in `data.detail`; JSON-RPC `message` is often generic (e.g. "Invalid request"). */
function formatJsonRpcError(error: unknown): string {
  if (error == null || typeof error !== 'object') {
    return 'Server returned error'
  }
  const e = error as { message?: string; data?: unknown }
  const base = typeof e.message === 'string' ? e.message : 'Server returned error'
  const data = e.data
  if (data != null && typeof data === 'object' && 'detail' in data) {
    const detail = (data as { detail?: unknown }).detail
    if (typeof detail === 'string' && detail.trim() !== '') {
      return `${base}: ${detail}`
    }
  }
  return base
}

export type NotificationCallback = (method: string, params: unknown) => void
export type ServerRequestHandler = (
  method: string,
  params: unknown
) => Promise<unknown>

/**
 * Pluggable transport interface used by WireProtocolClient.
 * Allows both stdio and WebSocket transports.
 */
interface Transport {
  /**
   * Registers a handler for each complete line received.
   * Returns an unsubscribe function.
   */
  onLine(handler: (line: string) => void): () => void
  /** Sends a line of text (without trailing newline). */
  writeLine(line: string): Promise<void>
  /** Called when the transport is closed. */
  onClose(handler: () => void): () => void
  /** Called when the transport is opened (WebSocket only). */
  onOpen?(handler: () => void): () => void
  /** WebSocket only: send lines queued while disconnected (after initialize/initialized). */
  flushPendingWrites?(): void
  /** Disposes the transport. */
  dispose(): void
}

// ─── Stdio transport ──────────────────────────────────────────────────────────

class StdioTransport implements Transport {
  private rl: ReturnType<typeof createInterface>
  private lineHandlers: Array<(line: string) => void> = []
  private closeHandlers: Array<() => void> = []
  private disposed = false

  constructor(stdout: Readable, private stdin: Writable) {
    this.rl = createInterface({ input: stdout, crlfDelay: Infinity })
    this.rl.on('line', (line) => {
      if (!this.disposed) {
        for (const h of this.lineHandlers) h(line)
      }
    })
    this.rl.on('close', () => {
      for (const h of this.closeHandlers) h()
    })
  }

  onLine(handler: (line: string) => void): () => void {
    this.lineHandlers.push(handler)
    return () => { this.lineHandlers = this.lineHandlers.filter((h) => h !== handler) }
  }

  onClose(handler: () => void): () => void {
    this.closeHandlers.push(handler)
    return () => { this.closeHandlers = this.closeHandlers.filter((h) => h !== handler) }
  }

  writeLine(line: string): Promise<void> {
    return new Promise((resolve, reject) => {
      if (this.disposed) {
        reject(new Error('StdioTransport is disposed'))
        return
      }
      this.stdin.write(line + '\n', 'utf8', (err) => {
        if (err) reject(err)
        else resolve()
      })
    })
  }

  dispose(): void {
    this.disposed = true
    this.rl.close()
  }
}

// ─── WebSocket transport ──────────────────────────────────────────────────────

const WS_RECONNECT_BASE_MS = 1000
const WS_RECONNECT_MAX_MS = 30000

class WebSocketTransport implements Transport {
  private ws: WebSocket | null = null
  private lineHandlers: Array<(line: string) => void> = []
  private closeHandlers: Array<() => void> = []
  private openHandlers: Array<() => void> = []
  private disposed = false
  private retryMs = WS_RECONNECT_BASE_MS
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private pendingWrites: Array<{ line: string; resolve: () => void; reject: (e: Error) => void }> = []
  private readonly autoReconnect: boolean

  constructor(private url: string, options: { autoReconnect?: boolean } = {}) {
    this.autoReconnect = options.autoReconnect ?? true
    this.connect()
  }

  private connect(): void {
    if (this.disposed) return
    const ws = new WebSocket(this.url)
    this.ws = ws

    ws.on('open', () => {
      this.retryMs = WS_RECONNECT_BASE_MS
      for (const h of this.openHandlers) h()
    })

    ws.on('message', (data) => {
      const text = data.toString()
      for (const line of text.split('\n')) {
        const trimmed = line.trim()
        if (trimmed) {
          for (const h of this.lineHandlers) h(trimmed)
        }
      }
    })

    ws.on('close', () => {
      this.rejectPendingWrites(new Error('Connection closed'))
      if (this.disposed) {
        for (const h of this.closeHandlers) h()
        return
      }
      // Notify close handlers (WireProtocolClient will reject pending)
      for (const h of this.closeHandlers) h()
      if (!this.autoReconnect) {
        return
      }
      // Schedule reconnect
      const jitteredRetryMs = Math.round(this.retryMs * (0.8 + Math.random() * 0.4))
      this.retryTimer = setTimeout(() => {
        if (!this.disposed) this.connect()
      }, jitteredRetryMs)
      this.retryMs = Math.min(this.retryMs * 2, WS_RECONNECT_MAX_MS)
    })

    ws.on('error', () => {
      // The 'close' event will fire after error; reconnection handled there
    })
  }

  onLine(handler: (line: string) => void): () => void {
    this.lineHandlers.push(handler)
    return () => { this.lineHandlers = this.lineHandlers.filter((h) => h !== handler) }
  }

  onClose(handler: () => void): () => void {
    this.closeHandlers.push(handler)
    return () => { this.closeHandlers = this.closeHandlers.filter((h) => h !== handler) }
  }

  onOpen(handler: () => void): () => void {
    this.openHandlers.push(handler)
    return () => { this.openHandlers = this.openHandlers.filter((h) => h !== handler) }
  }

  flushPendingWrites(): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return
    const pending = this.pendingWrites.splice(0)
    for (const item of pending) {
      this.ws.send(item.line + '\n', (err) => {
        if (err) item.reject(err as Error)
        else item.resolve()
      })
    }
  }

  private rejectPendingWrites(reason: Error): void {
    const pending = this.pendingWrites.splice(0)
    for (const item of pending) {
      item.reject(reason)
    }
  }

  writeLine(line: string): Promise<void> {
    if (this.disposed) return Promise.reject(new Error('WebSocketTransport is disposed'))

    return new Promise((resolve, reject) => {
      if (this.ws?.readyState === WebSocket.OPEN) {
        this.ws.send(line + '\n', (err) => {
          if (err) reject(err as Error)
          else resolve()
        })
      } else {
        // Queue until connected
        this.pendingWrites.push({ line, resolve, reject })
      }
    })
  }

  dispose(): void {
    this.disposed = true
    if (this.retryTimer) clearTimeout(this.retryTimer)
    this.rejectPendingWrites(new Error('WebSocketTransport is disposed'))
    this.ws?.close()
    this.ws = null
  }
}

/**
 * JSON-RPC 2.0 client over a pluggable transport (stdio or WebSocket).
 *
 * Mirrors the C# AppServerWireClient:
 * - Reads lines via transport
 * - Writes requests/notifications as JSONL
 * - Correlates responses to requests by id
 * - Dispatches server-initiated requests to a registered handler
 * - Forwards notifications to registered callbacks
 */
export class WireProtocolClient extends EventEmitter {
  private transport: Transport
  private nextId = 1
  private pending = new Map<number, PendingRequest>()
  private notificationCallbacks: NotificationCallback[] = []
  private serverRequestHandler: ServerRequestHandler | null = null
  private disposed = false
  private defaultTimeoutMs: number
  private initializeTimeoutMs: number | null
  private autoInitializeOnTransportOpen: boolean
  private initializeProfile: InitializeProfile
  private hasInitializedWebSocket = false
  private websocketInitializeInFlight = false

  constructor(
    stdoutOrTransport: Readable | Transport,
    stdinOrUndefined?: Writable,
    options: WireProtocolClientOptions = {}
  ) {
    super()
    this.defaultTimeoutMs = options.defaultTimeoutMs ?? 30_000
    this.initializeTimeoutMs =
      options.initializeTimeoutMs === undefined
        ? INITIALIZE_REQUEST_TIMEOUT_MS
        : options.initializeTimeoutMs
    this.autoInitializeOnTransportOpen = options.autoInitializeOnTransportOpen ?? false
    this.initializeProfile = options.initializeProfile ?? 'foreground'

    // Accept either a raw stdio pair or a pre-built Transport object
    if (stdoutOrTransport instanceof Readable && stdinOrUndefined) {
      this.transport = new StdioTransport(stdoutOrTransport, stdinOrUndefined)
    } else {
      this.transport = stdoutOrTransport as Transport
    }

    this.transport.onLine((line) => this.handleLine(line))
    this.transport.onClose(() => {
      this.rejectAllPending(new Error('Connection closed'))
      this.emit('close')
    })
    if (this.transport.onOpen) {
      this.transport.onOpen(() => {
        this.emit('transport-open')
        if (this.autoInitializeOnTransportOpen) {
          void this.initializeForWebSocketOpen()
        }
      })
    }
  }

  /**
   * Creates a WireProtocolClient connected via WebSocket (remote mode).
   */
  static fromWebSocket(
    url: string,
    options: WireProtocolClientOptions & { autoReconnect?: boolean } = {}
  ): WireProtocolClient {
    const transport = new WebSocketTransport(url, { autoReconnect: options.autoReconnect })
    return new WireProtocolClient(transport, undefined, {
      ...options,
      autoInitializeOnTransportOpen: options.autoInitializeOnTransportOpen ?? true
    })
  }

  // ─── Public API ──────────────────────────────────────────────────────────────

  sendRequest<T = unknown>(
    method: string,
    params?: unknown,
    timeoutMs?: number | null
  ): Promise<T> {
    const id = this.nextId++
    const timeoutDuration = timeoutMs === undefined ? this.defaultTimeoutMs : timeoutMs

    return new Promise<T>((resolve, reject) => {
      const timer = timeoutDuration == null
        ? null
        : setTimeout(() => {
            this.pending.delete(id)
            reject(new Error(`Request '${method}' timed out after ${timeoutDuration}ms`))
          }, timeoutDuration)

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timer
      })

      this.transport.writeLine(JSON.stringify({ jsonrpc: '2.0', id, method, params })).catch(
        (err) => {
          if (timer) clearTimeout(timer)
          this.pending.delete(id)
          reject(err)
        }
      )
    })
  }

  async sendNotification(method: string, params?: unknown): Promise<void> {
    await this.transport.writeLine(JSON.stringify({ jsonrpc: '2.0', method, params }))
  }

  async listModels(timeoutMs = 20_000): Promise<unknown> {
    return this.sendRequest('model/list', {}, timeoutMs)
  }

  onNotification(callback: NotificationCallback): () => void {
    this.notificationCallbacks.push(callback)
    return () => {
      const idx = this.notificationCallbacks.indexOf(callback)
      if (idx !== -1) this.notificationCallbacks.splice(idx, 1)
    }
  }

  onServerRequest(handler: ServerRequestHandler): void {
    this.serverRequestHandler = handler
  }

  async initialize(
    clientVersion = '0.1.0',
    profile: InitializeProfile = this.initializeProfile
  ): Promise<InitializeResult> {
    const result = await this.sendRequest<{
      serverInfo: ServerInfo
      capabilities: ServerCapabilities
      dashboardUrl?: string
    }>(
      'initialize',
      {
        clientInfo: {
          name: 'dotcraft-desktop',
          title: 'DotCraft',
          version: clientVersion
        },
        capabilities: buildInitializeCapabilities(profile)
      },
      this.initializeTimeoutMs
    )

    await this.sendNotification('initialized', {})

    return result
  }

  async reInitialize(clientVersion = '0.1.0'): Promise<InitializeResult> {
    const result = await this.initialize(clientVersion, this.initializeProfile)
    this.emit('reconnected', result)
    return result
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    this.rejectAllPending(new Error('WireProtocolClient disposed'))
    this.transport.dispose()
    this.notificationCallbacks = []
    this.serverRequestHandler = null
  }

  // ─── Private helpers ─────────────────────────────────────────────────────────

  private handleLine(line: string): void {
    const trimmed = line.trim()
    if (!trimmed) return

    let msg: Record<string, unknown>
    try {
      msg = JSON.parse(trimmed)
    } catch {
      return
    }

    const hasMethod = typeof msg.method === 'string'
    const hasId =
      msg.id !== undefined && msg.id !== null && (typeof msg.id === 'number' || typeof msg.id === 'string')

    if (!hasMethod && hasId) {
      const pending = this.pending.get(msg.id as number)
      if (pending) {
        if (pending.timer) clearTimeout(pending.timer)
        this.pending.delete(msg.id as number)

        if ('error' in msg && msg.error) {
          pending.reject(new Error(formatJsonRpcError(msg.error)))
        } else {
          pending.resolve(msg.result)
        }
      }
      return
    }

    if (hasMethod && hasId && this.serverRequestHandler) {
      const reqId = msg.id as number
      const method = msg.method as string
      const params = msg.params

      Promise.resolve(this.serverRequestHandler(method, params))
        .then((result) => {
          const response = result === undefined
            ? {
                jsonrpc: '2.0',
                id: reqId,
                error: { code: -32603, message: `Server request ${method} was not handled` }
              }
            : { jsonrpc: '2.0', id: reqId, result }
          return this.transport.writeLine(JSON.stringify(response))
        })
        .catch(() => {
          this.transport.writeLine(
            JSON.stringify({
              jsonrpc: '2.0',
              id: reqId,
              error: { code: -32603, message: 'Internal error' }
            })
          ).catch(() => {})
        })
      return
    }

    if (hasMethod) {
      const method = msg.method as string
      const params = msg.params
      for (const cb of this.notificationCallbacks) {
        try {
          cb(method, params)
        } catch {
          // Silently suppress callback errors
        }
      }
      this.emit('notification', method, params)
    }
  }

  private async initializeForWebSocketOpen(): Promise<void> {
    if (this.disposed || this.websocketInitializeInFlight) return
    this.websocketInitializeInFlight = true
    const isReconnect = this.hasInitializedWebSocket
    try {
      if (isReconnect) {
        await this.reInitialize()
      } else {
        const result = await this.initialize(undefined, this.initializeProfile)
        this.hasInitializedWebSocket = true
        this.emit('ready', result)
      }
      this.transport.flushPendingWrites?.()
    } catch (err) {
      this.emit('reconnect-error', err)
    } finally {
      this.websocketInitializeInFlight = false
    }
  }

  private rejectAllPending(reason: Error): void {
    for (const [, pending] of this.pending) {
      if (pending.timer) clearTimeout(pending.timer)
      pending.reject(reason)
    }
    this.pending.clear()
  }
}

function buildInitializeCapabilities(_profile: InitializeProfile): Record<string, unknown> {
  // Secondary Desktop connections intentionally keep the same protocol capability
  // declaration as foreground connections so they can be promoted without a new
  // initialize handshake. Main-process routing keeps secondary behavior lean.
  return {
          approvalSupport: true,
          requestUserInputSupport: true,
          streamingSupport: true,
          commandExecutionStreaming: true,
          toolExecutionLifecycle: true,
          backgroundTerminals: true,
          configChange: true,
          mcpApps: true,
          appBindingVersion: 2,
          mcpElicitation: true,
          optOutNotificationMethods: [],
          nodeRepl: {
            backend: 'desktop-node'
          },
          browserUse: {
            backend: 'desktop-iab',
            backends: ['desktop-iab'],
            protocolVersion: 2,
            supportsCancel: true,
            browserSessionProtocolVersion: 1,
            defaultCommandTimeoutMs: 10000,
            maxCommandTimeoutMs: 120000,
            supportsTypedFinalize: true
          }
  }
}
