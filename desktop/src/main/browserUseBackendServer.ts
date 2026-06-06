import { randomBytes } from 'node:crypto'
import { mkdir, rm } from 'node:fs/promises'
import { createServer, type Server, type Socket } from 'node:net'
import { platform, tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'

const FRAME_HEADER_BYTES = 4
const MAX_FRAME_BYTES = 16 * 1024 * 1024
const WINDOWS_PIPE_PREFIX = '\\\\.\\pipe\\dotcraft-browser-use-dotcraft-'
const UNIX_PIPE_DIR = 'dotcraft-browser-use'

export interface BrowserUseBackendRequestHandler {
  handleBrowserUseBackendRequest(
    method: string,
    params: Record<string, unknown>,
    context: BrowserUseBackendCommandContext
  ): Promise<unknown>
}

export interface BrowserUseBackendCommandContext {
  requestId: unknown
  hasResponse: boolean
  signal: AbortSignal
  cancel(reason?: string): void
}

export class BrowserUseBackendError extends Error {
  constructor(
    message: string,
    readonly code = -32000,
    readonly data?: unknown
  ) {
    super(message)
    this.name = 'BrowserUseBackendError'
  }

  static methodNotFound(method: string): BrowserUseBackendError {
    return new BrowserUseBackendError(`Unsupported browser backend method: ${method}`, -32601)
  }

  static unsupportedApi(api: string): BrowserUseBackendError {
    return new BrowserUseBackendError(`UnsupportedApi: ${api}`, -32004)
  }

  static invalidArgument(message: string): BrowserUseBackendError {
    return new BrowserUseBackendError(`InvalidArgument: ${message}`, -32602)
  }

  static commandTimeout(message: string, data?: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`CommandTimeout: ${message}`, -32010, data)
  }

  static commandCancelled(message: string): BrowserUseBackendError {
    return new BrowserUseBackendError(`CommandCancelled: ${message}`, -32011)
  }

  static navigationFailed(message: string, data?: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`NavigationFailed: ${message}`, -32012, data)
  }

  static resultTooLarge(message: string, data?: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`ResultTooLarge: ${message}`, -32013, data)
  }

  static tabStale(tabId: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`TabStale: Browser tab is no longer available: ${String(tabId)}`, -32014)
  }

  static nodeStale(nodeId: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`NodeStale: Browser node is no longer available: ${String(nodeId)}`, -32015)
  }

  static pageClosed(tabId: unknown): BrowserUseBackendError {
    return new BrowserUseBackendError(`PageClosed: Browser page is closed: ${String(tabId)}`, -32016)
  }
}

interface JsonRpcRequest {
  jsonrpc?: unknown
  id?: unknown
  method?: unknown
  params?: unknown
}

function browserUsePipeDirectory(): string {
  return join(tmpdir(), UNIX_PIPE_DIR)
}

export function createBrowserUsePipePath(nonce = randomBytes(8).toString('hex')): string {
  if (platform() === 'win32') {
    return `${WINDOWS_PIPE_PREFIX}${process.pid}-${nonce}`
  }
  return join(browserUsePipeDirectory(), `dotcraft-${process.pid}-${nonce}.sock`)
}

export function isAllowedBrowserUsePipePath(
  path: string,
  currentPlatform: NodeJS.Platform = platform() as NodeJS.Platform
): boolean {
  if (!path) return false
  if (currentPlatform === 'win32') {
    const normalized = path.replace(/\//g, '\\').toLowerCase()
    return normalized.startsWith(WINDOWS_PIPE_PREFIX) && normalized.length > WINDOWS_PIPE_PREFIX.length
  }

  const resolvedPath = resolve(path)
  const allowedDir = resolve(tmpdir(), UNIX_PIPE_DIR)
  return dirname(resolvedPath) === allowedDir &&
    /^dotcraft-[^/\\]+\.sock$/.test(basename(resolvedPath))
}

export function encodeBrowserUseBackendFrame(payload: unknown): Buffer {
  const body = Buffer.from(
    typeof payload === 'string' ? payload : JSON.stringify(payload),
    'utf8')
  const frame = Buffer.allocUnsafe(FRAME_HEADER_BYTES + body.byteLength)
  frame.writeUInt32LE(body.byteLength, 0)
  body.copy(frame, FRAME_HEADER_BYTES)
  return frame
}

export class BrowserUseBackendFrameDecoder {
  private buffer = Buffer.alloc(0)

  push(chunk: Uint8Array): string[] {
    if (chunk.byteLength > 0) {
      this.buffer = Buffer.concat([
        this.buffer,
        Buffer.from(chunk.buffer, chunk.byteOffset, chunk.byteLength)
      ])
    }

    const messages: string[] = []
    for (;;) {
      if (this.buffer.byteLength < FRAME_HEADER_BYTES) break
      const byteLength = this.buffer.readUInt32LE(0)
      if (byteLength > MAX_FRAME_BYTES) {
        this.buffer = Buffer.alloc(0)
        throw new BrowserUseBackendError('Browser backend frame exceeds the maximum allowed size.', -32600)
      }
      const fullFrameLength = FRAME_HEADER_BYTES + byteLength
      if (this.buffer.byteLength < fullFrameLength) break
      messages.push(this.buffer.subarray(FRAME_HEADER_BYTES, fullFrameLength).toString('utf8'))
      this.buffer = this.buffer.subarray(fullFrameLength)
    }
    return messages
  }
}

export class BrowserUseBackendServer {
  private server: Server | null = null
  private listenPromise: Promise<string> | null = null
  private sockets = new Set<Socket>()
  private pipePath: string | null = null

  constructor(private readonly handler: BrowserUseBackendRequestHandler) {}

  get address(): string | null {
    return this.pipePath
  }

  async ensureStarted(): Promise<string> {
    if (this.server?.listening && this.pipePath) return this.pipePath
    if (this.listenPromise) return await this.listenPromise

    const pipePath = createBrowserUsePipePath()
    this.listenPromise = this.listen(pipePath)
    try {
      this.pipePath = await this.listenPromise
      return this.pipePath
    } finally {
      this.listenPromise = null
    }
  }

  async close(): Promise<void> {
    const server = this.server
    const pipePath = this.pipePath
    this.server = null
    this.pipePath = null
    this.listenPromise = null

    for (const socket of this.sockets) {
      socket.destroy()
    }
    this.sockets.clear()

    if (server) {
      await new Promise<void>((resolveClose) => {
        if (!server.listening) {
          resolveClose()
          return
        }
        server.close(() => resolveClose())
      })
    }

    if (pipePath && platform() !== 'win32') {
      await rm(pipePath, { force: true }).catch(() => {})
    }
  }

  sendNotification(method: string, params: Record<string, unknown>): void {
    for (const socket of this.sockets) {
      if (socket.destroyed) continue
      socket.write(encodeBrowserUseBackendFrame({
        jsonrpc: '2.0',
        method,
        params
      }))
    }
  }

  private async listen(pipePath: string): Promise<string> {
    if (platform() !== 'win32') {
      await mkdir(dirname(pipePath), { recursive: true })
      await rm(pipePath, { force: true }).catch(() => {})
    }

    const server = createServer((socket) => this.handleSocket(socket))
    this.server = server
    return await new Promise<string>((resolveListen, rejectListen) => {
      const cleanup = () => {
        server.off('error', onError)
        server.off('listening', onListening)
      }
      const onError = (error: Error) => {
        cleanup()
        rejectListen(error)
      }
      const onListening = () => {
        cleanup()
        resolveListen(pipePath)
      }
      server.once('error', onError)
      server.once('listening', onListening)
      server.listen(pipePath)
    })
  }

  private handleSocket(socket: Socket): void {
    const decoder = new BrowserUseBackendFrameDecoder()
    this.sockets.add(socket)
    socket.once('close', () => {
      this.sockets.delete(socket)
    })
    socket.on('error', () => {
      this.sockets.delete(socket)
    })
    socket.on('data', (chunk) => {
      let frames: string[]
      try {
        frames = decoder.push(chunk)
      } catch (error) {
        this.writeError(socket, null, error)
        socket.destroy()
        return
      }
      for (const frame of frames) {
        void this.handleFrame(socket, frame)
      }
    })
  }

  private async handleFrame(socket: Socket, frame: string): Promise<void> {
    let request: JsonRpcRequest
    try {
      request = JSON.parse(frame) as JsonRpcRequest
    } catch {
      this.writeError(socket, null, new BrowserUseBackendError('Parse error', -32700))
      return
    }

    if (!request || typeof request !== 'object' || Array.isArray(request) || typeof request.method !== 'string') {
      this.writeError(socket, request?.id ?? null, new BrowserUseBackendError('Invalid JSON-RPC request', -32600))
      return
    }

    const hasId = Object.prototype.hasOwnProperty.call(request, 'id')
    const id = hasId ? request.id : null
    const params = request.params && typeof request.params === 'object' && !Array.isArray(request.params)
      ? request.params as Record<string, unknown>
      : {}
    const abortController = new AbortController()
    const context: BrowserUseBackendCommandContext = {
      requestId: id,
      hasResponse: hasId,
      signal: abortController.signal,
      cancel: () => abortController.abort()
    }

    try {
      const result = await this.handler.handleBrowserUseBackendRequest(request.method, params, context)
      if (hasId && !context.signal.aborted) this.writeResponse(socket, id, result)
    } catch (error) {
      if (hasId && !context.signal.aborted) this.writeError(socket, id, error)
    }
  }

  private writeResponse(socket: Socket, id: unknown, result: unknown): void {
    if (socket.destroyed) return
    socket.write(encodeBrowserUseBackendFrame({
      jsonrpc: '2.0',
      id,
      result
    }))
  }

  private writeError(socket: Socket, id: unknown, error: unknown): void {
    if (socket.destroyed) return
    const rpcError = error instanceof BrowserUseBackendError
      ? error
      : new BrowserUseBackendError(error instanceof Error ? error.message : String(error))
    socket.write(encodeBrowserUseBackendFrame({
      jsonrpc: '2.0',
      id,
      error: {
        code: rpcError.code,
        message: rpcError.message,
        data: rpcError.data
      }
    }))
  }
}
