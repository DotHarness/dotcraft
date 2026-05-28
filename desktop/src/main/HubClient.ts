import { spawn } from 'child_process'
import { existsSync, readFileSync } from 'fs'
import { join, resolve as resolvePath } from 'path'
import { homedir } from 'os'
import { resolveBinaryLocation, type ResolvedBinaryInfo } from './AppServerManager'
import type { BinarySource } from './settings'

export interface HubClientOptions {
  binarySource?: BinarySource
  binaryPath?: string
  preferDevBuild?: boolean
  requireDevBuild?: boolean
  restartMismatchedHub?: boolean
}

export interface HubLockInfo {
  pid: number
  apiBaseUrl: string
  token: string
  startedAt: string
  version: string
  binaryPath?: string | null
}

export interface HubAppServerResponse {
  workspacePath: string
  canonicalWorkspacePath: string
  state: string
  pid?: number | null
  endpoints: Record<string, string>
  serviceStatus: Record<string, { state: string; url?: string | null; reason?: string | null }>
  serverVersion?: string | null
  startedByHub: boolean
  exitCode?: number | null
  lastError?: string | null
  recentStderr?: string | null
}

export interface HubRuntimeToolsRequest {
  ripgrepPath?: string
  nodeBin?: string
  nodeRunAsNode?: boolean
  modulesDir?: string
  builtInPluginRoots?: string
}

export interface HubStatusResponse {
  hubVersion: string
  pid: number
  startedAt: string
  statePath: string
  apiBaseUrl: string
  binaryPath?: string | null
  capabilities: {
    appServerManagement: boolean
    portManagement: boolean
    events: boolean
    notifications: boolean
    tray: boolean
  }
}

export interface HubEvent {
  kind: string
  at: string
  workspacePath?: string | null
  data?: unknown
}

export class HubClientError extends Error {
  constructor(
    readonly code: string,
    message: string
  ) {
    super(message)
    this.name = 'HubClientError'
  }
}

const STARTUP_TIMEOUT_MS = 15_000
const SHUTDOWN_TIMEOUT_MS = 5_000
const POLL_MS = 200

function hubLockPath(): string {
  return join(homedir(), '.craft', 'hub', 'hub.lock')
}

function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code
    return code === 'EPERM'
  }
}

function readHubLock(): HubLockInfo | null {
  const lockPath = hubLockPath()
  if (!existsSync(lockPath)) return null
  try {
    const parsed = JSON.parse(readFileSync(lockPath, 'utf8')) as Partial<HubLockInfo>
    if (
      typeof parsed.pid === 'number' &&
      typeof parsed.apiBaseUrl === 'string' &&
      typeof parsed.token === 'string'
    ) {
      return {
        pid: parsed.pid,
        apiBaseUrl: parsed.apiBaseUrl,
        token: parsed.token,
        startedAt: parsed.startedAt ?? '',
        version: parsed.version ?? '',
        binaryPath: typeof parsed.binaryPath === 'string' ? parsed.binaryPath : null
      }
    }
  } catch {
    // Ignore stale or partially written locks.
  }
  return null
}

async function sleep(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms))
}

function normalizePathForCompare(path: string): string {
  const normalized = resolvePath(path)
  return process.platform === 'win32' ? normalized.toLowerCase() : normalized
}

function pathsEqual(left: string, right: string): boolean {
  return normalizePathForCompare(left) === normalizePathForCompare(right)
}

export class HubClient {
  constructor(private readonly options: HubClientOptions = {}) {}

  async ensureAppServer(
    workspacePath: string,
    options: {
      clientName?: string
      runtimeTools?: HubRuntimeToolsRequest
    } = {}
  ): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    const clientName = options.clientName ?? 'dotcraft-desktop'
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/ensure',
      {
        method: 'POST',
        body: JSON.stringify({
          workspacePath,
          client: { name: clientName, version: process.env.npm_package_version ?? '0.1.0' },
          startIfMissing: true,
          runtimeTools: options.runtimeTools
        })
      }
    )
  }

  async restartAppServer(workspacePath: string, runtimeTools?: HubRuntimeToolsRequest): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/restart',
      {
        method: 'POST',
        body: JSON.stringify({ workspacePath, runtimeTools })
      }
    )
  }

  async stopAppServer(workspacePath: string): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse>(
      hub,
      '/v1/appservers/stop',
      {
        method: 'POST',
        body: JSON.stringify({ workspacePath })
      }
    )
  }

  async listAppServers(): Promise<HubAppServerResponse[]> {
    const hub = await this.ensureHub()
    return this.requestJson<HubAppServerResponse[]>(hub, '/v1/appservers', { method: 'GET' })
  }

  async getStatus(): Promise<HubStatusResponse> {
    const hub = await this.ensureHub()
    const response = await fetch(`${hub.apiBaseUrl}/v1/status`)
    if (!response.ok) {
      throw await this.toError(response)
    }
    return await response.json() as HubStatusResponse
  }

  async shutdownHub(): Promise<void> {
    const hub = await this.tryGetLiveHub()
    if (!hub) return
    await this.requestJson<{ ok: boolean }>(hub, '/v1/shutdown', { method: 'POST' })
  }

  async subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
    const hub = await this.ensureHub()
    const response = await fetch(`${hub.apiBaseUrl}/v1/events`, {
      headers: { Authorization: `Bearer ${hub.token}` },
      signal
    })
    if (!response.ok || !response.body) {
      throw await this.toError(response)
    }

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    while (!signal.aborted) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      let boundary = findSseBoundary(buffer)
      while (boundary) {
        const raw = buffer.slice(0, boundary.index)
        buffer = buffer.slice(boundary.index + boundary.sequence.length)
        const dataLine = raw.split(/\r?\n/).find((line) => line.startsWith('data:'))
        const data = dataLine?.slice('data:'.length).trim()
        if (data) {
          try {
            onEvent(JSON.parse(data) as HubEvent)
          } catch {
            // Ignore malformed event frames.
          }
        }
        boundary = findSseBoundary(buffer)
      }
    }
  }

  private async ensureHub(): Promise<HubLockInfo> {
    const live = await this.tryGetLiveHub()
    if (live) {
      if (this.options.restartMismatchedHub) {
        const expectedBinaryPath = this.resolveExpectedBinaryPath()
        if (!expectedBinaryPath) {
          throw new HubClientError('binary-not-found', this.missingBinaryMessage())
        }
        if (!this.hubMatchesExpectedBinary(live, expectedBinaryPath)) {
          await this.shutdownMismatchedHub(live)
        } else {
          return live
        }
      } else {
        return live
      }
    }

    this.startHub()

    const started = Date.now()
    while (Date.now() - started < STARTUP_TIMEOUT_MS) {
      const info = await this.tryGetLiveHub()
      if (info) return info
      await sleep(POLL_MS)
    }

    throw new HubClientError('hubUnavailable', 'DotCraft Hub could not be started.')
  }

  private async tryGetLiveHub(): Promise<HubLockInfo | null> {
    const info = readHubLock()
    if (!info || !isProcessAlive(info.pid)) return null

    try {
      const response = await fetch(`${info.apiBaseUrl}/v1/status`)
      if (!response.ok) return null
      let status: Partial<HubStatusResponse> = {}
      try {
        status = await response.json() as Partial<HubStatusResponse>
      } catch {
        // Older Hub responses may not be JSON-consumable in tests or during startup races.
      }
      return {
        ...info,
        binaryPath: typeof status.binaryPath === 'string' ? status.binaryPath : info.binaryPath
      }
    } catch {
      return null
    }
  }

  private resolveConfiguredBinary(): ResolvedBinaryInfo {
    return resolveBinaryLocation({
      binarySource: this.options.binarySource,
      binaryPath: this.options.binaryPath,
      preferDevBuild: this.options.preferDevBuild,
      requireDevBuild: this.options.requireDevBuild
    })
  }

  private resolveExpectedBinaryPath(): string | null {
    return this.resolveConfiguredBinary().path
  }

  private hubMatchesExpectedBinary(hub: HubLockInfo, expectedBinaryPath: string): boolean {
    return typeof hub.binaryPath === 'string' && pathsEqual(hub.binaryPath, expectedBinaryPath)
  }

  private async shutdownMismatchedHub(hub: HubLockInfo): Promise<void> {
    await this.requestJson<{ ok: boolean }>(hub, '/v1/shutdown', { method: 'POST' })

    const started = Date.now()
    while (Date.now() - started < SHUTDOWN_TIMEOUT_MS) {
      if (!isProcessAlive(hub.pid)) {
        return
      }
      await sleep(POLL_MS)
    }

    throw new HubClientError(
      'hubMismatchShutdownTimeout',
      'DotCraft Hub is running from a different binary and did not stop after shutdown. Close DotCraft and tray, then retry Desktop dev.'
    )
  }

  private missingBinaryMessage(): string {
    const resolved = this.resolveConfiguredBinary()
    if (resolved.source === 'custom') {
      const configuredPath = this.options.binaryPath?.trim()
      return configuredPath
        ? `Configured DotCraft binary not found: ${configuredPath}`
        : 'Custom DotCraft binary path is empty. Please choose a binary or switch to another source.'
    }
    if (resolved.source === 'path') {
      return 'DotCraft binary not found on PATH. Install dotcraft or switch to the bundled binary in Settings.'
    }
    if (this.options.preferDevBuild && this.options.requireDevBuild) {
      return 'Local DotCraft build not found. Run build_app.bat from the repository root before starting Desktop dev.'
    }
    return 'DotCraft binary not found. Hub could not be started.'
  }

  private startHub(): void {
    const resolved = this.resolveConfiguredBinary()

    if (!resolved.path) {
      throw new HubClientError('binary-not-found', this.missingBinaryMessage())
    }

    const child = spawn(resolved.path, ['hub'], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true
    })
    child.unref()
  }

  private async requestJson<T>(
    hub: HubLockInfo,
    path: string,
    init: RequestInit
  ): Promise<T> {
    const response = await fetch(`${hub.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${hub.token}`,
        ...(init.headers ?? {})
      }
    })

    if (!response.ok) {
      throw await this.toError(response)
    }

    return await response.json() as T
  }

  private async toError(response: Response): Promise<HubClientError> {
    try {
      const body = await response.json() as { error?: { code?: string; message?: string } }
      if (body.error?.code || body.error?.message) {
        return new HubClientError(
          body.error.code ?? 'hubRequestFailed',
          body.error.message ?? `Hub request failed with HTTP ${response.status}.`
        )
      }
    } catch {
      // Fall through.
    }
    return new HubClientError(
      response.status === 401 ? 'unauthorized' : 'hubRequestFailed',
      `Hub request failed with HTTP ${response.status}.`
    )
  }
}

export function findSseBoundary(buffer: string): { index: number; sequence: '\n\n' | '\r\n\r\n' } | null {
  const lf = buffer.indexOf('\n\n')
  const crlf = buffer.indexOf('\r\n\r\n')
  if (lf === -1 && crlf === -1) return null
  if (lf === -1) return { index: crlf, sequence: '\r\n\r\n' }
  if (crlf === -1) return { index: lf, sequence: '\n\n' }
  return crlf < lf ? { index: crlf, sequence: '\r\n\r\n' } : { index: lf, sequence: '\n\n' }
}
