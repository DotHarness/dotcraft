import { DesktopHubError, type DesktopHubClient } from './desktopHub'

const DETAIL_KEYS = [
  'stage', 'elapsedMs', 'budgetMs', 'pid', 'expectedPid', 'version', 'binaryPath', 'startedAt',
  'expectedExecutable', 'actualExecutable', 'executable', 'spawnedPid', 'lockPath',
  'lastProbe', 'spawnError', 'shutdownError', 'exitCode', 'signal'
] as const

function redact(message: string): string {
  return message.replace(/Bearer\s+\S+/giu, 'Bearer [redacted]')
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

export function logDesktopHubFailure(stage: string, error: unknown, startedAt: number, workspacePath: string): void {
  const details: Record<string, unknown> = {}
  if (error instanceof DesktopHubError && error.details && typeof error.details === 'object') {
    const source = error.details as Record<string, unknown>
    for (const key of DETAIL_KEYS) {
      const value = source[key]
      if (value == null) continue
      if (typeof value === 'string') details[key] = redact(value)
      else if (typeof value === 'number' || typeof value === 'boolean') details[key] = value
      else if (typeof value === 'object') {
        const safe: Record<string, unknown> = {}
        for (const childKey of ['kind', 'status', 'httpStatus', 'code', 'name', 'exitCode', 'signal']) {
          const child = (value as Record<string, unknown>)[childKey]
          if (typeof child === 'string') safe[childKey] = redact(child)
          else if (typeof child === 'number') safe[childKey] = child
        }
        const failure = (value as { error?: { name?: unknown; code?: unknown } }).error
        if (failure && typeof failure === 'object') {
          safe.error = {
            name: typeof failure.name === 'string' ? redact(failure.name) : undefined,
            code: typeof failure.code === 'string' ? redact(failure.code) : undefined
          }
        }
        details[key] = safe
      }
    }
  }
  console.warn('[desktop] Hub operation failed', {
    stage,
    workspacePath,
    elapsedMs: Date.now() - startedAt,
    code: error instanceof DesktopHubError ? error.code : undefined,
    message: redact(error instanceof Error ? error.message.split('\n')[0] : String(error)),
    details
  })
}

export function logLiveDesktopHub(client: DesktopHubClient): void {
  void client.getLiveHubIdentity().then((identity) => {
    if (identity) console.info('[desktop] observed live Hub', identity)
  }).catch(() => {})
}
