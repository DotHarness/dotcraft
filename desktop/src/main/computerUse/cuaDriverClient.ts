import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { createInterface } from 'node:readline'

export interface DriverContentBlock {
  type: string
  text?: string
  data?: string
  mimeType?: string
}

export interface DriverToolResult {
  content: DriverContentBlock[]
  structuredContent?: Record<string, unknown>
  isError?: boolean
}

export type SpawnDriver = () => ChildProcessWithoutNullStreams

export class DriverUnavailableError extends Error {
  constructor(message: string) {
    super(`driver_unavailable: ${message}`)
    this.name = 'DriverUnavailableError'
  }
}

class DriverTimeoutError extends Error {
  constructor(tool: string, timeoutMs: number) {
    super(`timeout: ${tool} did not finish within ${timeoutMs}ms; its effect is unknown, observe the window before retrying.`)
    this.name = 'DriverTimeoutError'
  }
}

interface PendingRequest {
  resolve(value: unknown): void
  reject(error: Error): void
  timer?: ReturnType<typeof setTimeout>
}

const STDERR_TAIL_BYTES = 4096
const CLOSE_GRACE_MS = 3000

export function spawnCuaDriver(executablePath: string, env: NodeJS.ProcessEnv): SpawnDriver {
  return () => spawn(executablePath, ['mcp', '--direct'], {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
    env
  })
}

export class CuaDriverClient {
  private child: ChildProcessWithoutNullStreams | null = null
  private readonly pending = new Map<number, PendingRequest>()
  private nextId = 1
  private stderrTail = ''
  private exited: Promise<void> = Promise.resolve()

  constructor(
    private readonly spawnDriver: SpawnDriver,
    private readonly expectedVersion: string
  ) {}

  get running(): boolean {
    return this.child !== null
  }

  async start(startupTimeoutMs = 15_000): Promise<void> {
    if (this.child) return
    let child: ChildProcessWithoutNullStreams
    try {
      child = this.spawnDriver()
    } catch (error) {
      throw new DriverUnavailableError(error instanceof Error ? error.message : String(error))
    }
    this.child = child
    this.exited = new Promise((resolve) => {
      child.once('exit', (code, signal) => {
        if (this.child === child) this.child = null
        this.failPending(new DriverUnavailableError(
          `the driver exited (${signal ?? code ?? 'unknown'})${this.stderrTail ? `: ${this.stderrTail.trim().split(/\r?\n/).pop()}` : ''}`))
        resolve()
      })
    })
    child.once('error', (error) => this.failPending(new DriverUnavailableError(error.message)))
    child.stdin.on('error', () => {})
    child.stderr.on('data', (chunk: Buffer) => {
      this.stderrTail = (this.stderrTail + chunk.toString('utf8')).slice(-STDERR_TAIL_BYTES)
    })
    createInterface({ input: child.stdout }).on('line', (line) => this.onLine(line))

    const init = await this.request('initialize', {
      protocolVersion: '2025-06-18',
      capabilities: {},
      clientInfo: { name: 'dotcraft-desktop', version: '1' }
    }, startupTimeoutMs, 'initialize') as { serverInfo?: { version?: string } }
    const version = init?.serverInfo?.version
    if (version !== this.expectedVersion) {
      this.kill()
      throw new DriverUnavailableError(`expected cua-driver ${this.expectedVersion} but found ${version ?? 'unknown'}`)
    }
  }

  async callTool(name: string, args: Record<string, unknown>, timeoutMs: number): Promise<DriverToolResult> {
    const result = await this.request('tools/call', { name, arguments: args }, timeoutMs, name) as DriverToolResult
    return { content: Array.isArray(result?.content) ? result.content : [], structuredContent: result?.structuredContent, isError: result?.isError === true }
  }

  async close(): Promise<void> {
    const child = this.child
    if (!child) return
    child.stdin.end()
    const timer = setTimeout(() => child.kill(), CLOSE_GRACE_MS)
    await this.exited
    clearTimeout(timer)
  }

  kill(): void {
    const child = this.child
    if (!child) return
    this.child = null
    child.kill()
    this.failPending(new DriverUnavailableError('the driver was stopped'))
  }

  private request(method: string, params: unknown, timeoutMs: number, label: string): Promise<unknown> {
    const child = this.child
    if (!child) return Promise.reject(new DriverUnavailableError('the driver is not running'))
    const id = this.nextId++
    return new Promise((resolve, reject) => {
      const entry: PendingRequest = { resolve, reject }
      entry.timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new DriverTimeoutError(label, timeoutMs))
        this.kill()
      }, timeoutMs)
      this.pending.set(id, entry)
      child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`)
    })
  }

  private onLine(line: string): void {
    let message: { id?: unknown; result?: unknown; error?: { message?: string } }
    try {
      message = JSON.parse(line)
    } catch {
      return
    }
    if (typeof message.id !== 'number') return
    const entry = this.pending.get(message.id)
    if (!entry) return
    this.pending.delete(message.id)
    clearTimeout(entry.timer)
    if (message.error) entry.reject(new DriverUnavailableError(message.error.message ?? 'driver request failed'))
    else entry.resolve(message.result)
  }

  private failPending(error: Error): void {
    for (const [id, entry] of this.pending) {
      clearTimeout(entry.timer)
      entry.reject(error)
      this.pending.delete(id)
    }
  }
}
