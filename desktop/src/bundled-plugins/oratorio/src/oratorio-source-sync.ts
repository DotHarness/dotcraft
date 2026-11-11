import { describeOratorioError, oratorioClient } from './oratorio-client'
import type { SourceProviderStatusDto, SourceSyncJobDto } from './oratorio-contracts'

const POLL_INTERVAL_MS = 1500
const POLL_WINDOW_MS = 45_000

type ProviderSyncResult = { provider: string; name: string } & (
  | { kind: 'succeeded' | 'running' }
  | { kind: 'failed'; message: string }
)

export interface SourceSyncResult {
  kind: 'succeeded' | 'failed' | 'partialFailed' | 'running' | 'unconfigured'
  providers: ProviderSyncResult[]
  boardLoaded: boolean
}

export async function pollSyncJob(provider: string, initial: SourceSyncJobDto, signal: AbortSignal): Promise<SourceSyncJobDto | null> {
  const active = (job: SourceSyncJobDto): boolean => job.status === 'queued' || job.status === 'running'
  signal.throwIfAborted()
  if (!active(initial)) return initial
  const deadline = Date.now() + POLL_WINDOW_MS
  while (Date.now() < deadline) {
    await waitForPoll(signal)
    const job = await oratorioClient.syncJob(provider, initial.jobId)
    signal.throwIfAborted()
    if (!active(job)) return job
  }
  return null
}

export async function syncConfiguredSources(reloadBoard: () => Promise<boolean>, signal: AbortSignal): Promise<SourceSyncResult> {
  const { providers } = await oratorioClient.sources()
  signal.throwIfAborted()
  const configured = providers.filter((provider) => provider.configuredProjectCount > 0)
  if (configured.length === 0) return { kind: 'unconfigured', providers: [], boardLoaded: true }
  const results = await Promise.all(configured.map((provider) => syncProvider(provider, signal)))
  signal.throwIfAborted()
  const boardLoaded = await reloadBoard()
  signal.throwIfAborted()
  const failures = results.filter((result) => result.kind === 'failed').length
  const kind = failures > 0
    ? failures === results.length ? 'failed' : 'partialFailed'
    : !boardLoaded ? 'failed'
      : results.some((result) => result.kind === 'running') ? 'running' : 'succeeded'
  return { kind, providers: results, boardLoaded }
}

async function syncProvider(provider: SourceProviderStatusDto, signal: AbortSignal): Promise<ProviderSyncResult> {
  const identity = { provider: provider.provider, name: provider.displayName }
  try {
    const job = await oratorioClient.sync(provider.provider)
    const settled = await pollSyncJob(provider.provider, job, signal)
    if (!settled) return { ...identity, kind: 'running' }
    return settled.status === 'succeeded'
      ? { ...identity, kind: 'succeeded' }
      : { ...identity, kind: 'failed', message: settled.errorMessage ?? settled.errorCode ?? settled.status }
  } catch (error) {
    signal.throwIfAborted()
    return { ...identity, kind: 'failed', message: describeOratorioError(error) }
  }
}

function waitForPoll(signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted()
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', abort)
      resolve()
    }, POLL_INTERVAL_MS)
    const abort = (): void => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    signal.addEventListener('abort', abort, { once: true })
  })
}
