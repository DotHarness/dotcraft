import WebSocket from 'ws'

import type { OratorioBoardEvent, OratorioRequest, OratorioResponse, OratorioServiceContext } from '../../shared/oratorio'
import type { DesktopHubClient } from '../desktopHub'

const SERVICE_ID = 'oratorio'

export interface RemoteOratorioService {
  endpoint: string
  accessToken: string
  workspacePath: string
  appServerEndpoint: string
  appServerIdentity: string
}

interface ResolvedOratorioService {
  endpoint: string
  accessToken: string
  provider: 'local' | 'remote'
  workspacePath: string | null
  appServerEndpoint?: string
  appServerIdentity?: string
}

export class OratorioProviderError extends Error {
  constructor(readonly code: string, readonly fallbackText: string, options?: ErrorOptions) {
    super(fallbackText, options)
    this.name = 'OratorioProviderError'
  }
}

export class OratorioProvider {
  private ensurePromise: Promise<ResolvedOratorioService> | null = null
  private service: ResolvedOratorioService | null = null
  private socket: WebSocket | null = null
  private revision = 1
  private subscribers = 0
  private focusedRunId: string | null = null
  private generation = 0
  private disposed = false
  private contextAbort = new AbortController()

  constructor(
    private readonly getHubClient: () => DesktopHubClient,
    private readonly getWorkspacePath: () => string | null,
    private readonly onDataChanged: (revision: number, event?: OratorioBoardEvent) => void = () => {},
    private readonly resolveRemoteService: () => Promise<RemoteOratorioService | null> = async () => null
  ) {}

  async getContext(): Promise<OratorioServiceContext> {
    await this.ensure()
    return this.context()
  }

  async request<T>(request: OratorioRequest): Promise<OratorioResponse<T>> {
    const signal = this.contextAbort.signal
    const service = await this.ensure()
    signal.throwIfAborted()
    if (!service.endpoint || !service.accessToken) {
      throw new OratorioProviderError('oratorio.serviceContextInvalid', 'Oratorio returned incomplete connection metadata.')
    }
    const response = await fetch(`${service.endpoint}${request.path}`, {
      method: request.method,
      headers: {
        Authorization: `Bearer ${service.accessToken}`,
        ...(request.body ? { 'Content-Type': 'application/json' } : {})
      },
      body: request.body ? JSON.stringify(request.body) : undefined,
      signal
    })
    const data = await readResponseBody(response)
    signal.throwIfAborted()
    if (!response.ok) {
      throw new OratorioProviderError(readErrorCode(data), readErrorMessage(data, response.status))
    }
    return { status: response.status, data: data as T }
  }

  async retry(): Promise<OratorioServiceContext> {
    this.contextChanged()
    await this.ensure()
    return this.context()
  }

  focusRun(runId: string | null): void {
    if (this.focusedRunId === runId) return
    if (this.focusedRunId) this.sendStreamControl({ type: 'unfocus', runId: this.focusedRunId })
    this.focusedRunId = runId
    if (runId) this.sendStreamControl({ type: 'focus', runId })
  }

  disconnect(): void {
    this.socket?.removeAllListeners()
    this.socket?.close()
    this.socket = null
  }

  subscribe(): void {
    this.subscribers += 1
    if (this.service) this.ensureSubscription(this.service)
  }

  unsubscribe(): void {
    this.subscribers = Math.max(0, this.subscribers - 1)
    if (this.subscribers === 0) this.disconnect()
  }

  dispose(): void {
    this.disposed = true
    this.contextChanged()
    this.subscribers = 0
  }

  contextChanged(): number {
    this.generation += 1
    this.contextAbort.abort()
    this.contextAbort = new AbortController()
    this.focusedRunId = null
    this.disconnect()
    this.service = null
    this.ensurePromise = null
    this.revision += 1
    return this.revision
  }

  private async ensure(): Promise<ResolvedOratorioService> {
    if (this.disposed) throw new OratorioProviderError('oratorio.serviceUnavailable', 'The window has closed.')
    if (this.service?.endpoint && this.service.accessToken) {
      this.ensureSubscription(this.service)
      return this.service
    }
    if (!this.ensurePromise) {
      const generation = this.generation
      const workspacePath = normalizeWorkspacePath(this.getWorkspacePath())
      const pending = this.resolveRemoteService().then(async (remote) => {
        if (remote) {
          return {
            ...remote,
            provider: 'remote' as const
          }
        }
        const service = await this.getHubClient().ensureManagedService(SERVICE_ID)
        if (service.state !== 'running' || !service.endpoint || !service.accessToken) {
          throw new OratorioProviderError(
            'oratorio.serviceUnavailable',
            service.lastError || 'The local Oratorio service is unavailable.'
          )
        }
        return {
          endpoint: service.endpoint,
          accessToken: service.accessToken,
          provider: 'local' as const,
          workspacePath
        }
        }).then((service) => {
          if (generation !== this.generation || this.disposed) {
            throw new OratorioProviderError('oratorio.serviceUnavailable', 'The window context has changed.')
          }
          this.service = service
          this.ensureSubscription(service)
          return service
        })
        .finally(() => { if (this.ensurePromise === pending) this.ensurePromise = null })
      this.ensurePromise = pending
    }
    return this.ensurePromise
  }

  private ensureSubscription(service: ResolvedOratorioService): void {
    if (this.subscribers === 0 || this.socket || !service.endpoint || !service.accessToken) return
    const url = new URL('/api/v1/stream', service.endpoint)
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
    const socket = new WebSocket(url, { headers: { Authorization: `Bearer ${service.accessToken}` } })
    this.socket = socket
    socket.on('open', () => { if (this.focusedRunId) this.sendStreamControl({ type: 'focus', runId: this.focusedRunId }) })
    socket.on('message', (frame) => {
      this.revision += 1
      this.onDataChanged(this.revision, parseBoardEvent(frame.toString()))
    })
    socket.on('close', () => {
      if (this.socket === socket) this.socket = null
    })
    socket.on('error', () => {
      if (this.socket === socket) this.socket = null
      socket.close()
    })
  }

  private context(): OratorioServiceContext {
    return {
      provider: this.service?.provider ?? 'local',
      workspacePath: this.service?.workspacePath ?? normalizeWorkspacePath(this.getWorkspacePath()),
      connected: Boolean(this.service?.endpoint && this.service.accessToken),
      revision: this.revision
    }
  }

  private sendStreamControl(frame: { type: 'focus' | 'unfocus'; runId: string }): void {
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(JSON.stringify(frame))
  }
}

function parseBoardEvent(value: string): OratorioBoardEvent | undefined {
  try {
    const event = JSON.parse(value) as unknown
    if (!event || typeof event !== 'object' || Array.isArray(event)) return undefined
    const record = event as Record<string, unknown>
    if (typeof record.type !== 'string') return undefined
    const payload = sanitizeDrawerPayload(record.payload)
    return {
      type: record.type,
      ...(typeof record.taskId === 'string' ? { taskId: record.taskId } : {}),
      ...(typeof record.shortId === 'string' ? { shortId: record.shortId } : {}),
      ...(typeof record.runId === 'string' ? { runId: record.runId } : {}),
      ...(typeof record.taskStatus === 'string' ? { taskStatus: record.taskStatus } : {}),
      ...(typeof record.microStatus === 'string' ? { microStatus: record.microStatus } : {}),
      ...(typeof record.boardSortOrder === 'number' ? { boardSortOrder: record.boardSortOrder } : {}),
      ...(typeof record.ts === 'string' ? { ts: record.ts } : {}),
      ...(payload ? { payload } : {})
    }
  } catch {
    return undefined
  }
}

function sanitizeDrawerPayload(value: unknown): OratorioBoardEvent['payload'] | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const record = value as Record<string, unknown>
  const nested = record.payload && typeof record.payload === 'object' ? record.payload as Record<string, unknown> : undefined
  const text = typeof nested?.text === 'string' ? nested.text.slice(-120) : typeof record.text === 'string' ? record.text.slice(-120) : undefined
  const type = typeof record.type === 'string' ? record.type : undefined
  const status = typeof record.status === 'string' ? record.status : undefined
  return type || status || text ? { type, status, text } : undefined
}

async function readResponseBody(response: Response): Promise<unknown> {
  const text = await response.text()
  if (!text) return null
  try { return JSON.parse(text) } catch { return text }
}

function readErrorCode(data: unknown): string {
  if (data && typeof data === 'object' && 'error' in data) {
    const error = (data as { error?: { code?: unknown } }).error
    if (typeof error?.code === 'string') return error.code
  }
  return 'oratorio.request_failed'
}

function readErrorMessage(data: unknown, status: number): string {
  if (data && typeof data === 'object' && 'error' in data) {
    const error = (data as { error?: { code?: unknown; message?: unknown; details?: unknown } }).error
    if (typeof error?.message === 'string') {
      // Only the message crosses IPC, so a rejected configuration carries its field reasons in it.
      const reasons = error.code === 'configurationValidationFailed' && error.details && typeof error.details === 'object'
        ? Object.values(error.details).filter((value): value is string => typeof value === 'string')
        : []
      return [error.message, ...new Set(reasons)].join(' ')
    }
  }
  return `Oratorio request failed with HTTP ${status}.`
}

function normalizeWorkspacePath(value: string | null): string | null {
  return typeof value === 'string' && value.trim() ? value : null
}
