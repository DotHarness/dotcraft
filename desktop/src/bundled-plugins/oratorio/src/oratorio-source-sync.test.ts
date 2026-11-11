import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { installOratorioHost } from './runtime'
import { oratorioClient } from './oratorio-client'
import type { SourceProviderStatusDto, SourceSyncJobDto } from './oratorio-contracts'
import { pollSyncJob, syncConfiguredSources } from './oratorio-source-sync'

vi.mock('./oratorio-client', () => ({
  describeOratorioError: (error: unknown) => error instanceof Error ? error.message : String(error),
  oratorioClient: { sources: vi.fn(), sync: vi.fn(), syncJob: vi.fn() }
}))

function job(provider: string, status: SourceSyncJobDto['status'] = 'running', extra: Partial<SourceSyncJobDto> = {}): SourceSyncJobDto {
  return {
    jobId: `${provider}-job`, provider, status, mode: 'incremental', createdAt: '', updatedAt: '',
    issuesImported: 0, reviewTargetsImported: 0, errorCode: null, errorMessage: null, ...extra
  }
}

const providers: SourceProviderStatusDto[] = [
  { provider: 'github', displayName: 'GitHub', configuredProjectCount: 1 },
  { provider: 'gitlab', displayName: 'GitLab', configuredProjectCount: 1 }
]

describe('source synchronization', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.mocked(oratorioClient.sources).mockResolvedValue({ providers })
  })
  afterEach(() => { vi.useRealTimers(); vi.resetAllMocks() })

  it('submits configured sources through the neutral endpoint and waits before reloading', async () => {
    const client = await vi.importActual<typeof import('./oratorio-client')>('./oratorio-client')
    const request = vi.fn().mockResolvedValue({ data: job('github') })
    const dispose = installOratorioHost({ oratorio: { request } } as unknown as DesktopPluginHost)
    vi.mocked(oratorioClient.sources).mockResolvedValue({ providers: [providers[0], { ...providers[1], configuredProjectCount: 0 }] })
    vi.mocked(oratorioClient.sync).mockImplementation(client.oratorioClient.sync)
    vi.mocked(oratorioClient.syncJob).mockResolvedValueOnce(job('github')).mockResolvedValueOnce(job('github', 'succeeded'))
    const reload = vi.fn().mockResolvedValue(true)
    try {
      const pending = syncConfiguredSources(reload, new AbortController().signal)
      await vi.advanceTimersByTimeAsync(1500)
      expect(reload).not.toHaveBeenCalled()
      expect(request).toHaveBeenCalledExactlyOnceWith({
        method: 'POST', path: '/api/v1/sources/sync-jobs',
        body: { provider: 'github', mode: 'incremental', projects: undefined }
      })
      await vi.advanceTimersByTimeAsync(1500)
      expect((await pending).kind).toBe('succeeded')
      expect(oratorioClient.syncJob).toHaveBeenCalledWith('github', 'github-job')
      expect(reload).toHaveBeenCalledOnce()
    } finally { dispose() }
  })

  it.each(['request', 'partialFailed'])('retains the GitLab %s failure when GitHub succeeds', async (failure) => {
    vi.mocked(oratorioClient.sync).mockImplementation(async (provider) => {
      if (provider === 'github') return job(provider, 'succeeded')
      if (failure === 'request') throw new Error('GitLab unavailable')
      return job(provider, 'partialFailed', { errorMessage: 'GitLab project denied' })
    })
    const reload = vi.fn().mockResolvedValue(true)

    const result = await syncConfiguredSources(reload, new AbortController().signal)

    expect(result.kind).toBe('partialFailed')
    expect(result.providers).toEqual([
      { provider: 'github', name: 'GitHub', kind: 'succeeded' },
      { provider: 'gitlab', name: 'GitLab', kind: 'failed', message: failure === 'request' ? 'GitLab unavailable' : 'GitLab project denied' }
    ])
    expect(reload).toHaveBeenCalledOnce()
  })

  it.each([false, true])('retains in-progress sources at the waiting window with a failed peer: %s', async (failedPeer) => {
    vi.mocked(oratorioClient.sources).mockResolvedValue({ providers: failedPeer ? providers : [providers[0]] })
    vi.mocked(oratorioClient.sync).mockImplementation(async (provider) => {
      if (provider === 'gitlab') throw new Error('Missing credentials')
      return job(provider)
    })
    vi.mocked(oratorioClient.syncJob).mockResolvedValue(job('github'))
    const pending = syncConfiguredSources(vi.fn().mockResolvedValue(true), new AbortController().signal)

    await vi.advanceTimersByTimeAsync(45_000)
    const result = await pending

    expect(result.kind).toBe(failedPeer ? 'partialFailed' : 'running')
    expect(result.providers).toContainEqual({ provider: 'github', name: 'GitHub', kind: 'running' })
    if (failedPeer) expect(result.providers).toContainEqual({ provider: 'gitlab', name: 'GitLab', kind: 'failed', message: 'Missing credentials' })
  })

  it('does not report success if the board reload fails after successful jobs', async () => {
    vi.mocked(oratorioClient.sync).mockImplementation(async (provider) => job(provider, 'succeeded'))

    const result = await syncConfiguredSources(vi.fn().mockResolvedValue(false), new AbortController().signal)

    expect(result.kind).toBe('failed')
    expect(result.boardLoaded).toBe(false)
    expect(result.providers.every((provider) => provider.kind === 'succeeded')).toBe(true)
  })

  it('stops local polling when the surface leaves without cancelling the server job', async () => {
    const controller = new AbortController()
    const pending = pollSyncJob('gitlab', job('gitlab'), controller.signal)
    const rejected = expect(pending).rejects.toMatchObject({ name: 'AbortError' })

    controller.abort()
    await rejected
    await vi.advanceTimersByTimeAsync(45_000)

    expect(oratorioClient.syncJob).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })
})
