import {
  HubClient,
  HubClientError,
  type HubAppServerResponse,
  type HubCreateSatelliteInviteOptions,
  type HubEnsureAppServerOptions,
  type HubEvent,
  type HubManagedServiceResponse,
  type HubRuntimeToolsRequest,
  type HubSatellite,
  type HubSatelliteInvite,
  type HubStatusResponse
} from '@dotcraft/sdk/hub'
import { resolveBinaryLocation } from './AppServerManager'
import type { AppSettings, BinarySource } from './settings'

export type { HubAppServerResponse, HubCreateSatelliteInviteOptions, HubEvent, HubManagedServiceResponse, HubRuntimeToolsRequest, HubSatellite, HubSatelliteInvite, HubStatusResponse } from '@dotcraft/sdk/hub'

export interface DesktopHubPolicyOptions {
  preferDevBuild?: boolean
  requireDevBuild?: boolean
}

export class DesktopHubError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly details?: unknown,
    options?: ErrorOptions
  ) {
    super(formatDesktopHubError(message, details), options)
    this.name = 'DesktopHubError'
  }
}

export class DesktopHubClient {
  constructor(private readonly inner: HubClient, private readonly configurationError?: DesktopHubError) {}

  ensureAppServer(workspacePath: string, options: HubEnsureAppServerOptions = {}): Promise<HubAppServerResponse> {
    return this.run(() => this.inner.ensureAppServer(workspacePath, options))
  }

  restartAppServer(workspacePath: string, runtimeTools?: HubRuntimeToolsRequest): Promise<HubAppServerResponse> {
    return this.run(() => this.inner.restartAppServer(workspacePath, runtimeTools))
  }

  stopAppServer(workspacePath: string): Promise<HubAppServerResponse> {
    return this.run(() => this.inner.stopAppServer(workspacePath))
  }

  listAppServers(): Promise<HubAppServerResponse[]> {
    return this.run(() => this.inner.listAppServers())
  }

  ensureManagedService(serviceId: string, executable: string): Promise<HubManagedServiceResponse> {
    return this.run(() => this.inner.ensureManagedService(serviceId, { executable }))
  }

  restartManagedService(serviceId: string, executable: string): Promise<HubManagedServiceResponse> {
    return this.run(() => this.inner.restartManagedService(serviceId, executable))
  }

  stopManagedService(serviceId: string): Promise<HubManagedServiceResponse> {
    return this.run(() => this.inner.stopManagedService(serviceId))
  }

  getStatus(): Promise<HubStatusResponse> {
    return this.run(() => this.inner.getStatus())
  }

  listSatellites(): Promise<HubSatellite[]> {
    return this.run(() => this.inner.listSatellites())
  }

  createSatelliteInvite(options: HubCreateSatelliteInviteOptions = {}): Promise<HubSatelliteInvite> {
    return this.run(() => this.inner.createSatelliteInvite(options))
  }

  revokeSatellite(peerId: string): Promise<void> {
    return this.run(() => this.inner.revokeSatellite(peerId))
  }

  subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
    return this.run(() => this.inner.subscribeEvents(onEvent, signal))
  }

  shutdownHub(): Promise<void> {
    return this.run(() => this.inner.shutdownHub())
  }

  private async run<T>(operation: () => Promise<T>): Promise<T> {
    if (this.configurationError) throw this.configurationError
    try {
      return await operation()
    } catch (error) {
      if (error instanceof DesktopHubError) throw error
      if (error instanceof HubClientError) {
        throw new DesktopHubError(error.code, error.message, error.details, { cause: error })
      }
      throw error
    }
  }
}

export function resolveDesktopBinarySource(settings: AppSettings): BinarySource {
  const source = settings.binarySource
  if (source === 'bundled' || source === 'path' || source === 'custom') return source
  return 'bundled'
}

export function createDesktopHubClient(
  settings: AppSettings,
  policy: DesktopHubPolicyOptions = {}
): DesktopHubClient {
  const binarySource = resolveDesktopBinarySource(settings)
  const resolved = resolveBinaryLocation({
    binarySource,
    binaryPath: settings.appServerBinaryPath,
    preferDevBuild: policy.preferDevBuild,
    requireDevBuild: policy.requireDevBuild
  })
  const configurationError = resolved.path
    ? undefined
    : new DesktopHubError('binary-not-found', missingBinaryMessage(binarySource, settings.appServerBinaryPath, policy))
  return new DesktopHubClient(new HubClient({
    executable: resolved.path ?? undefined,
    expectedExecutable: resolved.path ?? undefined,
    binaryMatchPolicy: 'restartIfMismatch'
  }), configurationError)
}

export function formatDesktopHubError(message: string, details?: unknown): string {
  const summary = summarizeDetails(details)
  return summary ? `${message}\n${summary}` : message
}

function missingBinaryMessage(source: BinarySource, configuredPath: string | undefined, policy: DesktopHubPolicyOptions): string {
  if (source === 'custom') {
    return configuredPath?.trim()
      ? `Configured DotCraft binary not found: ${configuredPath.trim()}`
      : 'Custom DotCraft binary path is empty. Please choose a binary or switch to another source.'
  }
  if (source === 'path') {
    return 'DotCraft binary not found on PATH. Install dotcraft or switch to the bundled binary in Settings.'
  }
  if (policy.preferDevBuild && policy.requireDevBuild) {
    return 'Local DotCraft build not found. Run the repository build command before starting Desktop development.'
  }
  return 'DotCraft binary not found. Hub could not be started.'
}

function summarizeDetails(details: unknown): string | null {
  const record = asRecord(details)
  if (!record) return preview(details)
  const parts: string[] = []
  for (const key of [
    'error', 'reason', 'lastError', 'stage', 'failureKind', 'exitCode', 'recentStderr',
    'workspacePath', 'craftPath', 'lockPath', 'pid', 'expectedPid',
    'expectedExecutable', 'actualExecutable'
  ]) {
    const value = preview(record[key])
    if (value) parts.push(`${key}: ${value}`)
  }
  return parts.length > 0 ? `Details: ${parts.join('; ')}` : preview(details)
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function preview(value: unknown, maxLength = 1200): string | null {
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (!trimmed) return null
    return trimmed.length > maxLength ? `${trimmed.slice(0, maxLength)}…` : trimmed
  }
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  if (value !== null && typeof value === 'object') {
    try {
      const json = JSON.stringify(value)
      return json.length > maxLength ? `${json.slice(0, maxLength)}…` : json
    } catch {
      return null
    }
  }
  return null
}
