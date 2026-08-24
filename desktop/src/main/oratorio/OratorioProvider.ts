import { existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { app } from 'electron'
import WebSocket from 'ws'

import type { OratorioBoardEvent, OratorioHandoffRequest, OratorioRequest, OratorioResponse, OratorioServiceContext } from '../../shared/oratorio'
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
  private pendingHandoff: { publicRequest: OratorioHandoffRequest; approvalUrl: string } | null = null
  private focusedRunId: string | null = null

  constructor(
    private readonly getHubClient: () => DesktopHubClient,
    private readonly getWorkspacePath: () => string | null,
    private readonly resolveExecutable: () => string = resolveOratorioExecutable,
    private readonly onDataChanged: (revision: number, event?: OratorioBoardEvent) => void = () => {},
    private readonly resolveRemoteService: () => Promise<RemoteOratorioService | null> = async () => null
  ) {}

  async getContext(): Promise<OratorioServiceContext> {
    await this.ensure()
    return this.context()
  }

  async request<T>(request: OratorioRequest): Promise<OratorioResponse<T>> {
    const service = await this.ensure()
    if (!service.endpoint || !service.accessToken) {
      throw new OratorioProviderError('oratorio.serviceContextInvalid', 'Oratorio returned incomplete connection metadata.')
    }
    const response = await fetch(`${service.endpoint}${request.path}`, {
      method: request.method,
      headers: {
        Authorization: `Bearer ${service.accessToken}`,
        ...(request.body ? { 'Content-Type': 'application/json' } : {})
      },
      body: request.body ? JSON.stringify(request.body) : undefined
    })
    const data = await readResponseBody(response)
    if (!response.ok) {
      throw new OratorioProviderError(readErrorCode(data), readErrorMessage(data, response.status))
    }
    return { status: response.status, data: data as T }
  }

  async retry(): Promise<OratorioServiceContext> {
    this.disconnect()
    this.service = null
    this.ensurePromise = null
    await this.ensure()
    this.revision += 1
    return this.context()
  }

  async prepareHandoff(rawUrl: string): Promise<OratorioHandoffRequest> {
    const handoff = parseDesktopServiceHandoff(rawUrl)
    const service = await this.ensure()
    const workspacePath = service.workspacePath
    if (!workspacePath || !samePath(workspacePath, handoff.workspacePath)) {
      throw new OratorioProviderError(
        'oratorio.handoffWorkspaceMismatch',
        'The Oratorio handoff does not belong to the active Workspace.'
      )
    }
    let endpoint = service.appServerEndpoint
    let appServerIdentity = service.appServerIdentity
    if (service.provider === 'local') {
      const appServer = await this.getHubClient().ensureAppServer(workspacePath, { startIfMissing: true })
      endpoint = appServer.endpoints.appServerWebSocket
        ?? appServer.serviceStatus.appServerWebSocket?.url
      const canonicalWorkspacePath = appServer.canonicalWorkspacePath || workspacePath
      appServerIdentity = `local:${canonicalWorkspacePath}`
    }
    if (!endpoint) {
      throw new OratorioProviderError(
        'oratorio.appServerUnavailable',
        'DotCraft Hub did not return an AppServer endpoint for the target Workspace.'
      )
    }
    const target = new URL(`oratorio://dotcraft/${handoff.operation}`)
    target.searchParams.set('app', handoff.appId)
    target.searchParams.set('request', handoff.requestId)
    target.searchParams.set('token', handoff.requestToken)
    target.searchParams.set('endpoint', endpoint)
    target.searchParams.set('workspace', workspacePath)
    target.searchParams.set('identity', appServerIdentity || `local:${workspacePath}`)
    const inspection = await this.request<Record<string, unknown>>({
      method: 'POST',
      path: '/api/v1/dotcraft/app-binding/inspect',
      body: { url: target.toString() }
    })
    const publicRequest: OratorioHandoffRequest = {
      requestId: crypto.randomUUID(), operation: handoff.operation, appId: handoff.appId, workspacePath,
      summary: summarizeHandoffInspection(inspection.data, handoff.operation)
    }
    this.pendingHandoff = { publicRequest, approvalUrl: target.toString() }
    return publicRequest
  }

  getPendingHandoff(): OratorioHandoffRequest | null {
    return this.pendingHandoff?.publicRequest ?? null
  }

  async resolveHandoff(requestId: string, approved: boolean): Promise<void> {
    const pending = this.pendingHandoff
    if (!pending || pending.publicRequest.requestId !== requestId) {
      throw new OratorioProviderError('oratorio.invalidHandoffRequest', 'The Oratorio handoff request is no longer available.')
    }
    this.pendingHandoff = null
    if (!approved) return
    await this.request({ method: 'POST', path: '/api/v1/dotcraft/app-binding/approve', body: { url: pending.approvalUrl } })
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

  contextChanged(): number {
    this.disconnect()
    this.service = null
    this.ensurePromise = null
    this.revision += 1
    return this.revision
  }

  private async ensure(): Promise<ResolvedOratorioService> {
    if (this.service?.endpoint && this.service.accessToken) {
      this.ensureSubscription(this.service)
      return this.service
    }
    if (!this.ensurePromise) {
      this.ensurePromise = this.resolveRemoteService().then(async (remote) => {
        if (remote) {
          return {
            ...remote,
            provider: 'remote' as const
          }
        }
        const service = await this.getHubClient().ensureManagedService(SERVICE_ID, this.resolveExecutable())
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
          workspacePath: normalizeWorkspacePath(this.getWorkspacePath())
        }
        }).then((service) => {
          this.service = service
          this.ensureSubscription(service)
          return service
        })
        .finally(() => { this.ensurePromise = null })
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

function summarizeHandoffInspection(inspection: Record<string, unknown>, operation: 'connect' | 'bind'): string {
  const candidate = operation === 'connect' ? inspection.connection : inspection.binding
  if (!candidate || typeof candidate !== 'object') return operation === 'connect' ? 'Connect Oratorio to this DotCraft workspace.' : 'Bind Oratorio to the requested DotCraft thread.'
  const record = candidate as Record<string, unknown>
  const label = [record.accountLabel, record.threadId, record.expiresAt].filter((value) => typeof value === 'string').join(' · ')
  return label || (operation === 'connect' ? 'Connect Oratorio to this DotCraft workspace.' : 'Bind Oratorio to the requested DotCraft thread.')
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

export function resolveOratorioExecutable(): string {
  const extension = process.platform === 'win32' ? '.exe' : ''
  const configured = process.env.DOTCRAFT_ORATORIO_BIN?.trim()
  const candidates = [
    configured,
    app.isPackaged ? resolve(process.resourcesPath, 'bin', `oratorio${extension}`) : undefined,
    resolve(app.getAppPath(), '..', 'build', 'oratorio', `oratorio${extension}`)
  ].filter((value): value is string => Boolean(value))
  const executable = candidates.find(existsSync)
  if (!executable) {
    throw new OratorioProviderError(
      'oratorio.executableNotFound',
      'The local Oratorio server executable could not be found.'
    )
  }
  return executable
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
    const error = (data as { error?: { message?: unknown } }).error
    if (typeof error?.message === 'string') return error.message
  }
  return `Oratorio request failed with HTTP ${status}.`
}

function normalizeWorkspacePath(value: string | null): string | null {
  return typeof value === 'string' && value.trim() ? value : null
}

function parseDesktopServiceHandoff(rawUrl: string): {
  operation: 'connect' | 'bind'
  appId: string
  requestId: string
  requestToken: string
  workspacePath: string
} {
  let url: URL
  try { url = new URL(rawUrl) } catch {
    throw new OratorioProviderError('oratorio.invalidHandoff', 'The Oratorio handoff URL is invalid.')
  }
  const operation = url.pathname.replace(/^\//, '')
  const appId = url.searchParams.get('app')?.trim() ?? ''
  const requestId = url.searchParams.get('request')?.trim() ?? ''
  const requestToken = url.searchParams.get('token')?.trim() ?? ''
  const workspacePath = url.searchParams.get('workspace')?.trim() ?? ''
  if (url.protocol !== 'dotcraft-service:' || url.hostname !== SERVICE_ID
      || (operation !== 'connect' && operation !== 'bind')
      || appId !== 'com.dotharness.oratorio' || !requestId || !requestToken || !workspacePath) {
    throw new OratorioProviderError('oratorio.invalidHandoff', 'The Oratorio handoff URL is invalid.')
  }
  return { operation, appId, requestId, requestToken, workspacePath }
}

function samePath(left: string, right: string): boolean {
  const normalize = (value: string): string => resolve(value).replace(/[\\/]+$/, '')
  return process.platform === 'win32'
    ? normalize(left).toLowerCase() === normalize(right).toLowerCase()
    : normalize(left) === normalize(right)
}
