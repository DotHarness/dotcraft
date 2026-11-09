import { EventEmitter } from 'events'
import { randomUUID } from 'crypto'
import { BrowserTurnSubscriptions } from './browserTurnSubscriptions'
import { readBrowserTurnNotification } from './browserTabLifecycle'
import { createInterface } from 'readline'
import type { Readable, Writable } from 'stream'
import {
  DotCraftWireClient,
  WebSocketTransport,
  type Transport
} from '@dotcraft/sdk/wire'
import {
  fromAppServerErrorEnvelope,
  toAppServerErrorEnvelope,
  type AppServerRequestError
} from '../shared/appServerError'

export interface ServerInfo {
  name: string
  version: string
  protocolVersion?: string
  extensions?: string[]
}

export type ServerCapabilities = Record<string, unknown>

export interface InitializeResult {
  serverInfo: ServerInfo
  capabilities: ServerCapabilities
  dashboardUrl?: string
}

export const INITIALIZE_REQUEST_TIMEOUT_MS: number | null = null

export interface DesktopAppServerClientOptions {
  defaultTimeoutMs?: number
  initializeTimeoutMs?: number | null
  autoReconnect?: boolean
  remoteHost?: boolean
}

export type NotificationCallback = (method: string, params: unknown) => void
export type ServerRequestHandler = (method: string, params: unknown) => Promise<unknown>

class DesktopStreamTransport implements Transport {
  private readonly reader: ReturnType<typeof createInterface>
  private readonly lines: AsyncIterator<string>
  private closed = false

  constructor(stdout: Readable, private readonly stdin: Writable) {
    this.reader = createInterface({ input: stdout, crlfDelay: Infinity })
    this.lines = this.reader[Symbol.asyncIterator]()
  }

  async readMessage(): Promise<Record<string, unknown>> {
    while (!this.closed) {
      const { value, done } = await this.lines.next()
      if (done) throw new Error('Desktop stream transport closed')
      const text = value.trim()
      if (text) return JSON.parse(text) as Record<string, unknown>
    }
    throw new Error('Desktop stream transport closed')
  }

  async writeMessage(message: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new Error('Desktop stream transport closed')
    await new Promise<void>((resolve, reject) => {
      this.stdin.write(`${JSON.stringify(message)}\n`, 'utf8', (error) => {
        if (error) reject(error)
        else resolve()
      })
    })
  }

  async close(): Promise<void> {
    this.closed = true
    this.reader.close()
  }
}

export class DesktopAppServerClient extends EventEmitter {
  private readonly wire: DotCraftWireClient
  private readonly browserSubscriptions: BrowserTurnSubscriptions
  private readonly initializeTimeoutMs: number | null
  private readonly remoteHost: boolean
  private notificationCallbacks: NotificationCallback[] = []
  private serverRequestHandler: ServerRequestHandler | null = null
  private disposed = false
  private initializedOnce = false
  private reconnectObserved = false
  private connectionLifetimeId: string | null = null

  constructor(
    stdoutOrTransport: Readable | Transport,
    stdinOrUndefined?: Writable,
    options: DesktopAppServerClientOptions = {}
  ) {
    super()
    const transport = isTransport(stdoutOrTransport)
      ? stdoutOrTransport
      : new DesktopStreamTransport(stdoutOrTransport, stdinOrUndefined!)
    this.initializeTimeoutMs = options.initializeTimeoutMs ?? INITIALIZE_REQUEST_TIMEOUT_MS
    this.remoteHost = options.remoteHost ?? false
    this.wire = new DotCraftWireClient(transport, {
      autoReconnect: options.autoReconnect ?? false,
      defaultTimeoutMs: options.defaultTimeoutMs ?? 30_000,
      initializeTimeoutMs: this.initializeTimeoutMs
    })
    this.browserSubscriptions = new BrowserTurnSubscriptions(
      (method, params, onResponse) => this.wire.requestRaw(method, params, undefined, { onResponse }),
      (threadId, turnId) => this.emit('retained-turn-ended', threadId, turnId)
    )
    this.bindWire()
    if (!(transport instanceof WebSocketTransport)) void this.wire.start()
  }

  static fromWebSocket(
    url: string,
    options: DesktopAppServerClientOptions = {}
  ): DesktopAppServerClient {
    const transport = new WebSocketTransport({ url })
    const client = new DesktopAppServerClient(transport, undefined, {
      ...options,
      autoReconnect: options.autoReconnect ?? true,
      initializeTimeoutMs: options.initializeTimeoutMs
    })
    void client.openWebSocket()
    return client
  }

  private bindWire(): void {
    this.wire.onAnyNotificationRaw((method, params) => {
      const turn = readBrowserTurnNotification(method, params)
      if (turn) this.browserSubscriptions.observeTurn(turn.threadId, turn.turnId, turn.terminal)
      for (const callback of [...this.notificationCallbacks]) {
        try {
          callback(method, params)
        } catch {
          // A host callback cannot break the SDK read loop.
        }
      }
      this.emit('notification', method, params)
    })
    const bridge = async (_id: string | number, params: Record<string, unknown>, method: string) => {
      if (!this.serverRequestHandler) return undefined
      return await this.serverRequestHandler(method, params)
    }
    for (const method of ['item/approval/request', 'item/tool/requestUserInput'] as const) {
      this.wire.registerServerRequestHandlerRaw(method, (id, params) => bridge(id, params, method))
    }
    this.wire.registerServerRequestFallbackRaw((method, id, params) => bridge(id, params, method))
    this.wire.onStateChanged((state, error) => {
      if (state === 'disconnected') {
        const connectionId = this.connectionLifetimeId
        this.connectionLifetimeId = null
        this.browserSubscriptions.clear()
        this.reconnectObserved = true
        this.emit('close', connectionId)
      } else if (state === 'reconnectError') {
        this.emit('reconnect-error', error)
      } else if (state === 'ready' && this.initializedOnce) {
        this.connectionLifetimeId ??= randomUUID()
        if (this.reconnectObserved) {
          this.reconnectObserved = false
          const result = this.currentInitializeResult()
          if (result) this.emit('reconnected', result)
        }
      }
    })
  }

  private async openWebSocket(): Promise<void> {
    try {
      await this.wire.connect()
      await this.wire.start()
      const result = await this.initialize()
      this.emit('ready', result)
    } catch (error) {
      this.emit('reconnect-error', error)
    }
  }

  async sendRequest<T = unknown>(
    method: string,
    params?: unknown,
    timeoutMs?: number | null,
    attachmentOwner?: string,
    onResponse?: (result: T) => void,
    onError?: (error: unknown) => void
  ): Promise<T> {
    try {
      return await this.browserSubscriptions.request(method, params, hook => this.wire.requestRaw<T>(method, params, timeoutMs, { onResponse: hook, onError }), attachmentOwner, onResponse)
    } catch (error) {
      throw normalizeDesktopRpcError(error)
    }
  }

  retainBrowserTurn(threadId: string, turnId: string): Promise<boolean> {
    return this.browserSubscriptions.retain(threadId, turnId)
  }

  releaseBrowserTurn(threadId: string, turnId: string): Promise<void> {
    return this.browserSubscriptions.release(threadId, turnId)
  }

  releaseViewAttachments(): Promise<void> { return this.browserSubscriptions.releaseViews() }

  get hasRetainedThreadAttachments(): boolean { return this.browserSubscriptions.retained }

  hasWarmThread(threadId: string): boolean { return this.browserSubscriptions.hasWarmThread(threadId) }

  get connectionId(): string | null { return this.connectionLifetimeId }

  get isDisposed(): boolean { return this.disposed }

  async sendNotification(method: string, params?: unknown): Promise<void> {
    await this.wire.notifyRaw(method, (params ?? {}) as Record<string, unknown>)
  }

  async listModels(timeoutMs = 20_000): Promise<unknown> {
    return await this.sendRequest('model/list', {}, timeoutMs)
  }

  onNotification(callback: NotificationCallback): () => void {
    this.notificationCallbacks.push(callback)
    return () => {
      this.notificationCallbacks = this.notificationCallbacks.filter((item) => item !== callback)
    }
  }

  onServerRequest(handler: ServerRequestHandler): void {
    this.serverRequestHandler = handler
  }

  async initialize(clientVersion = '0.1.0'): Promise<InitializeResult> {
    await this.wire.initialize({
      clientName: 'dotcraft-desktop',
      clientTitle: 'DotCraft',
      clientVersion,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      configChange: true,
      optOutNotifications: [],
      extraCapabilities: {
        ...buildInitializeCapabilities(),
        ...(this.remoteHost ? { remoteHost: true } : {})
      }
    })
    this.connectionLifetimeId ??= randomUUID()
    this.initializedOnce = true
    return this.currentInitializeResult()!
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    const connectionId = this.connectionLifetimeId
    this.connectionLifetimeId = null
    this.emit('disposed', connectionId)
    this.browserSubscriptions.clear()
    this.notificationCallbacks = []
    this.serverRequestHandler = null
    void this.wire.stop()
  }

  private currentInitializeResult(): InitializeResult | null {
    const result = this.wire.initializeResult
    if (!result) return null
    return {
      serverInfo: {
        name: result.serverInfo.name,
        version: result.serverInfo.version,
        protocolVersion: result.serverInfo.protocolVersion,
        extensions: result.serverInfo.extensions ?? undefined
      },
      capabilities: { ...result.capabilities },
      dashboardUrl: result.dashboardUrl ?? undefined
    }
  }
}

function isTransport(value: Readable | Transport): value is Transport {
  return typeof (value as Transport).readMessage === 'function'
}

function normalizeDesktopRpcError(error: unknown): AppServerRequestError {
  return fromAppServerErrorEnvelope(toAppServerErrorEnvelope(error))
}

function buildInitializeCapabilities(): Record<string, unknown> {
  return {
    commandExecutionStreaming: true,
    toolExecutionLifecycle: true,
    backgroundTerminals: true,
    mcpApps: true,
    visualizations: true,
    generativeUi: true,
    appBindingVersion: 1,
    mcpElicitation: true,
    nodeRepl: { backend: 'desktop-node' },
    browserUse: {
      backend: 'desktop-iab',
      backends: ['desktop-iab'],
      protocolVersion: 2,
      supportsCancel: true,
      browserSessionProtocolVersion: 1,
      defaultCommandTimeoutMs: 10_000,
      maxCommandTimeoutMs: 120_000,
      supportsTypedFinalize: true
    },
    ...(process.platform === 'win32' ? { computerUse: { backend: 'cua-driver' } } : {})
  }
}
