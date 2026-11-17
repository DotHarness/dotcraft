import { EventEmitter } from 'node:events'
import { readdir } from 'node:fs/promises'
import { createConnection, type Socket } from 'node:net'
import { platform, tmpdir } from 'node:os'
import { join } from 'node:path'
import { BrowserUseBackendFrameDecoder, encodeBrowserUseBackendFrame } from '../browserUseBackendServer'

const PIPE_PREFIX = 'dotcraft-chrome-'
export const CHROME_RELAY_PROTOCOL_VERSION = 4
const HELLO_TIMEOUT_MS = 2_000
const DEFAULT_REQUEST_TIMEOUT_MS = 120_000
export const BRIDGE_DISCONNECTED_MESSAGE =
  'BridgeDisconnected: Chrome is not connected to DotCraft. Confirm Chrome is open and the DotCraft extension is enabled.'

export interface ChromeRelay {
  request<T = unknown>(method: string, params?: Record<string, unknown>, timeoutMs?: number): Promise<T>
  on(event: 'event', listener: (name: string, params: Record<string, unknown>) => void): this
  on(event: 'disconnected', listener: () => void): this
}

interface PendingRequest {
  resolve(value: unknown): void
  reject(error: Error): void
  timer: ReturnType<typeof setTimeout>
}

export interface ChromeRelayClientOptions {
  listPipes?: () => Promise<string[]>
  connect?: (path: string) => Socket
}

export async function chromeRelayPipeCandidates(): Promise<string[]> {
  if (platform() === 'win32') {
    try {
      return (await readdir('\\\\.\\pipe\\'))
        .filter((name) => name.startsWith(PIPE_PREFIX))
        .map((name) => `\\\\.\\pipe\\${name}`)
    } catch {
      return []
    }
  }
  try {
    return (await readdir(tmpdir()))
      .filter((name) => name.startsWith(PIPE_PREFIX) && name.endsWith('.sock'))
      .map((name) => join(tmpdir(), name))
  } catch {
    return []
  }
}

class RelayConnection {
  private readonly decoder = new BrowserUseBackendFrameDecoder()
  private readonly pending = new Map<number, PendingRequest>()
  private nextId = 1
  closed = false

  constructor(
    private readonly socket: Socket,
    private readonly onEvent: (name: string, params: Record<string, unknown>) => void,
    private readonly onClose: (connection: RelayConnection) => void
  ) {
    socket.on('data', (chunk: Buffer) => this.handleData(chunk))
    socket.on('error', () => this.close())
    socket.on('close', () => this.close())
  }

  request<T>(method: string, params: Record<string, unknown>, timeoutMs: number): Promise<T> {
    if (this.closed) return Promise.reject(new Error(BRIDGE_DISCONNECTED_MESSAGE))
    const id = this.nextId++
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`CommandTimeout: Chrome relay method ${method} timed out after ${timeoutMs}ms.`))
      }, timeoutMs)
      this.pending.set(id, { resolve: resolve as (value: unknown) => void, reject, timer })
      this.socket.write(encodeBrowserUseBackendFrame({ id, method, params }), (error) => {
        if (!error) return
        clearTimeout(timer)
        this.pending.delete(id)
        reject(new Error(BRIDGE_DISCONNECTED_MESSAGE))
      })
    })
  }

  close(): void {
    if (this.closed) return
    this.closed = true
    this.socket.destroy()
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer)
      pending.reject(new Error(BRIDGE_DISCONNECTED_MESSAGE))
    }
    this.pending.clear()
    this.onClose(this)
  }

  private handleData(chunk: Buffer): void {
    let frames: string[]
    try {
      frames = this.decoder.push(chunk)
    } catch {
      this.close()
      return
    }
    for (const frame of frames) {
      let message: Record<string, unknown>
      try {
        message = JSON.parse(frame) as Record<string, unknown>
      } catch {
        continue
      }
      if (typeof message.event === 'string') {
        const params = message.params && typeof message.params === 'object' ? message.params as Record<string, unknown> : {}
        this.onEvent(message.event, params)
        continue
      }
      const pending = this.pending.get(Number(message.id))
      if (!pending) continue
      this.pending.delete(Number(message.id))
      clearTimeout(pending.timer)
      if (message.error) pending.reject(new Error((message.error as { message: string }).message))
      else pending.resolve(message.result)
    }
  }
}

export class ChromeRelayClient extends EventEmitter implements ChromeRelay {
  private connection: RelayConnection | null = null
  private connecting: Promise<RelayConnection> | null = null

  constructor(private readonly options: ChromeRelayClientOptions = {}) {
    super()
  }

  get connected(): boolean {
    return this.connection !== null && !this.connection.closed
  }

  async request<T = unknown>(method: string, params: Record<string, unknown> = {}, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS): Promise<T> {
    const connection = await this.ensureConnected()
    return await connection.request<T>(method, params, timeoutMs)
  }

  close(): void {
    this.connection?.close()
  }

  private async ensureConnected(): Promise<RelayConnection> {
    if (this.connection && !this.connection.closed) return this.connection
    this.connecting ??= this.discover().finally(() => { this.connecting = null })
    return await this.connecting
  }

  private async discover(): Promise<RelayConnection> {
    const candidates = await (this.options.listPipes ?? chromeRelayPipeCandidates)()
    for (const path of candidates) {
      const connection = await this.open(path).catch(() => null)
      if (!connection) continue
      try {
        const info = await connection.request<{ protocolVersion?: number }>('hello', {}, HELLO_TIMEOUT_MS)
        if (info?.protocolVersion === CHROME_RELAY_PROTOCOL_VERSION) {
          this.connection = connection
          return connection
        }
      } catch {}
      connection.close()
    }
    throw new Error(BRIDGE_DISCONNECTED_MESSAGE)
  }

  private open(path: string): Promise<RelayConnection> {
    return new Promise((resolve, reject) => {
      const socket = this.options.connect?.(path) ?? createConnection(path)
      const timer = setTimeout(() => {
        socket.destroy()
        reject(new Error('Chrome relay pipe connection timed out.'))
      }, HELLO_TIMEOUT_MS)
      socket.once('error', (error) => {
        clearTimeout(timer)
        reject(error)
      })
      socket.once('connect', () => {
        clearTimeout(timer)
        resolve(new RelayConnection(socket,
          (name, params) => this.emit('event', name, params),
          (closed) => {
            if (this.connection !== closed) return
            this.connection = null
            this.emit('disconnected')
          }))
      })
    })
  }
}
