import { EventEmitter } from 'events'
import { createInterface } from 'readline'
import type { Readable, Writable } from 'stream'
import {
  DotCraftWireClient,
  WebSocketTransport,
  type Transport
} from '@dotcraft/sdk/wire'

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

export type InitializeProfile = 'foreground' | 'secondary'

export interface DesktopAppServerClientOptions {
  defaultTimeoutMs?: number
  autoInitializeOnTransportOpen?: boolean
  initializeTimeoutMs?: number | null
  initializeProfile?: InitializeProfile
  autoReconnect?: boolean
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

/** Desktop host policy around the SDK Wire client. JSON-RPC remains owned by @dotcraft/sdk. */
export class DesktopAppServerClient extends EventEmitter {
  private readonly wire: DotCraftWireClient
  private readonly initializeTimeoutMs: number | null
  private readonly initializeProfile: InitializeProfile
  private notificationCallbacks: NotificationCallback[] = []
  private serverRequestHandler: ServerRequestHandler | null = null
  private disposed = false
  private initializedOnce = false
  private reconnectObserved = false

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
    this.initializeProfile = options.initializeProfile ?? 'foreground'
    this.wire = new DotCraftWireClient(transport, {
      autoReconnect: options.autoReconnect ?? false,
      defaultTimeoutMs: options.defaultTimeoutMs ?? 30_000,
      initializeTimeoutMs: this.initializeTimeoutMs
    })
    this.bindWire()
    if (!(transport instanceof WebSocketTransport)) void this.wire.start()
  }

  static fromWebSocket(
    url: string,
    options: DesktopAppServerClientOptions & { autoReconnect?: boolean } = {}
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
        this.reconnectObserved = true
        this.emit('close')
      } else if (state === 'reconnectError') {
        this.emit('reconnect-error', error)
      } else if (state === 'ready' && this.initializedOnce && this.reconnectObserved) {
        this.reconnectObserved = false
        const result = this.currentInitializeResult()
        if (result) this.emit('reconnected', result)
      }
    })
  }

  private async openWebSocket(): Promise<void> {
    try {
      await this.wire.connect()
      await this.wire.start()
      const result = await this.initialize(undefined, this.initializeProfile)
      this.initializedOnce = true
      this.emit('ready', result)
    } catch (error) {
      this.emit('reconnect-error', error)
    }
  }

  async sendRequest<T = unknown>(
    method: string,
    params?: unknown,
    timeoutMs?: number | null
  ): Promise<T> {
    try {
      return await this.wire.requestRaw<T>(method, params, timeoutMs)
    } catch (error) {
      throw normalizeDesktopRpcError(error)
    }
  }

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

  async initialize(
    clientVersion = '0.1.0',
    profile: InitializeProfile = this.initializeProfile
  ): Promise<InitializeResult> {
    await this.wire.initialize({
      clientName: 'dotcraft-desktop',
      clientTitle: 'DotCraft',
      clientVersion,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      configChange: true,
      optOutNotifications: [],
      extraCapabilities: buildInitializeCapabilities(profile)
    })
    return this.currentInitializeResult()!
  }

  async reInitialize(clientVersion = '0.1.0'): Promise<InitializeResult> {
    const result = await this.initialize(clientVersion, this.initializeProfile)
    this.emit('reconnected', result)
    return result
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
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

function normalizeDesktopRpcError(error: unknown): Error {
  return error instanceof Error ? new Error(error.message) : new Error(String(error))
}

function buildInitializeCapabilities(_profile: InitializeProfile): Record<string, unknown> {
  return {
    commandExecutionStreaming: true,
    toolExecutionLifecycle: true,
    backgroundTerminals: true,
    mcpApps: true,
    inlineVisualizations: true,
    appBindingVersion: 2,
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
    }
  }
}
