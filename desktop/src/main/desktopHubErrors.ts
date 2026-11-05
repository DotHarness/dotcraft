import type { BinarySource } from './settings'
import type { ConnectionStatusPayload } from '../shared/connectionStatus'

export class DesktopHubError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly details?: unknown,
    options?: ErrorOptions
  ) {
    super(message, options)
    this.name = 'DesktopHubError'
  }
}

const STARTUP_ERRORS = new Set([
  'hubUnavailable', 'hubBinaryMismatch', 'hubMismatchShutdownTimeout', 'hubMismatchShutdownFailed'
])

export function desktopHubConnectionFailure(error: unknown, binarySource: BinarySource): ConnectionStatusPayload {
  const message = redactHubDiagnostic(error instanceof Error ? error.message : String(error))
  if (!(error instanceof DesktopHubError)) return { status: 'error', errorMessage: message }
  return {
    status: 'error',
    errorMessage: message.split('\n')[0],
    errorDetails: formatDesktopHubError(`[${error.code}] ${message}`, error.details),
    ...(error.code === 'binary-not-found'
      ? { errorType: 'binary-not-found' as const, binarySource }
      : STARTUP_ERRORS.has(error.code) ? { errorType: 'hub-startup-failed' as const } : {})
  }
}

export function formatDesktopHubError(message: string, details?: unknown): string {
  const summary = summarizeDetails(details)
  return redactHubDiagnostic(summary ? `${message}\n\n${summary}` : message)
}

function summarizeDetails(details: unknown): string | null {
  if (details === null || typeof details !== 'object' || Array.isArray(details)) return preview(details)
  const record = details as Record<string, unknown>
  const parts: string[] = []
  for (const key of [
    'stage', 'failureKind', 'error', 'reason', 'lastError', 'exitCode', 'signal', 'recentStderr',
    'workspacePath', 'craftPath', 'lockPath', 'pid', 'expectedPid', 'spawnedPid',
    'expectedExecutable', 'actualExecutable', 'executable', 'elapsedMs', 'budgetMs', 'version', 'binaryPath',
    'startedAt', 'lastProbe', 'spawnError', 'shutdownError'
  ]) {
    const value = preview(record[key])
    if (value !== null) parts.push(`${key}: ${value}`)
  }
  return parts.length > 0 ? parts.join('\n') : preview(details)
}

function preview(value: unknown): string | null {
  let rendered: string
  if (typeof value === 'string') rendered = value.trim()
  else if (typeof value === 'number' || typeof value === 'boolean') rendered = String(value)
  else if (value !== null && typeof value === 'object') {
    try {
      rendered = JSON.stringify(value, (key, child: unknown) =>
        /token|authorization|password|secret/iu.test(key) ? '[redacted]' : child)
    } catch { return null }
  } else return null
  if (!rendered) return null
  const safe = redactHubDiagnostic(rendered)
  return safe.length > 4096 ? `${safe.slice(0, 4096)}…` : safe
}

export function redactHubDiagnostic(message: string): string {
  return message.replace(/Bearer\s+[^\s"<>]+/giu, 'Bearer [redacted]')
    .replace(/(?:https?|wss?):\/\/[^\s"<>]+/giu, (value) => {
      try {
        const url = new URL(value)
        url.username = ''
        url.password = ''
        url.search = ''
        url.hash = ''
        return url.toString()
      } catch { return '[invalid URL]' }
    })
}
