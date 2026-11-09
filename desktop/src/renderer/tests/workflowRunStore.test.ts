import { describe, expect, it, vi } from 'vitest'
import type { WorkflowRunSummary, WorkflowRunView } from '@dotcraft/sdk/contracts'
import { workflowRunsFor, selectWorkflowRunEntry, type WorkflowRunClient } from '../stores/workflowRunCache'
import { startSummaryFeed, type SummaryState } from '../components/conversation/summary/feed'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((yes) => { resolve = yes })
  return { promise, resolve }
}

function run(status = 'running'): WorkflowRunView {
  return {
    runId: 'run-1', threadId: 'thread-1', name: 'review', description: 'Review the release.', status,
    controls: { canPause: status === 'running', canStop: status === 'running', canResume: status !== 'running' },
    totals: { agentCount: 1, queuedCount: 0, runningCount: 0, completedCount: 0, failedCount: 0, stoppedCount: 0, replayedCount: 0, inputTokens: 0, outputTokens: 0, toolCallCount: 0 },
    phases: [], unphasedAgents: [], createdAt: '2026-11-09T00:00:00Z',
    ...(status !== 'running' ? { completedAt: '2026-11-09T00:01:00Z' } : {})
  }
}

function host() {
  let scope = 'workspace-a'
  const notifications = new Map<string, (params: unknown) => void>()
  const connections = new Set<(connected: boolean) => void>()
  const request = vi.fn(async (method: string, _params?: unknown): Promise<unknown> => {
    if (method === 'workflow/run/read') return { run: run() }
    if (method === 'workflow/run/list') return { runs: [] }
    if (method === 'workflow/run/stop') return { run: run('stopped') }
    return {}
  })
  const client = {
    connectionKey: () => scope,
    request,
    onConnectionChange(listener: (connected: boolean) => void) {
      connections.add(listener)
      return () => { connections.delete(listener) }
    },
    onNotification(method: string, listener: (params: unknown) => void) {
      notifications.set(method, listener)
      return () => { notifications.delete(method) }
    }
  } as WorkflowRunClient
  const services = {
    appServer: client,
    subagents: { list: () => [], onChange: () => () => undefined }
  } as unknown as SummaryServices
  const cache = workflowRunsFor(client)
  return {
    services, cache, request,
    entry: () => selectWorkflowRunEntry(cache.getState().entries, 'thread-1', 'run-1'),
    notify: () => notifications.get('workflow/run/updated')?.({ threadId: 'thread-1', runId: 'run-1', reason: 'terminal' }),
    connection(connected: boolean, nextScope = scope) {
      scope = nextScope
      for (const listener of connections) listener(connected)
    }
  }
}

describe('shared Workflow run cache', () => {
  it('shares terminal results with the summary and ignores an older initial list', async () => {
    const h = host()
    const oldList = deferred<{ runs: WorkflowRunSummary[] }>()
    h.request.mockImplementation(async (method) => {
      if (method === 'workflow/run/list') return oldList.promise
      if (method === 'workflow/run/read') return { run: run('succeeded') }
      return {}
    })
    let summary: SummaryState | null = null
    const feed = startSummaryFeed(h.services, 'thread-1', (next) => { summary = next })
    await h.cache.getState().load('thread-1', 'run-1')
    oldList.resolve({ runs: [run()] })
    await h.cache.getState().list('thread-1')

    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(summary!.workflows).toEqual([])
    feed.stop()
  })

  it('coalesces reads and performs one dirty read after in-flight invalidations', async () => {
    const h = host()
    const first = deferred<{ run: WorkflowRunView }>()
    h.request.mockResolvedValue({ run: run('succeeded') })
    h.request.mockImplementationOnce(() => first.promise)
    const loading = h.cache.getState().load('thread-1', 'run-1')
    void h.cache.getState().load('thread-1', 'run-1')
    h.notify()
    h.notify()
    expect(h.request).toHaveBeenCalledTimes(1)

    first.resolve({ run: run() })
    await loading
    expect(h.request).toHaveBeenCalledTimes(2)
    expect(h.entry()?.run?.status).toBe('succeeded')
  })

  it('does not discard a stop response when a notification read starts during the stop', async () => {
    const h = host()
    await h.cache.getState().load('thread-1', 'run-1')
    const stopping = deferred<{ run: WorkflowRunView }>()
    h.request.mockImplementation(async (method) => method === 'workflow/run/stop' ? stopping.promise : { run: run() })
    const stop = h.cache.getState().stop('thread-1', 'run-1')
    h.notify()
    await h.cache.getState().load('thread-1', 'run-1')
    stopping.resolve({ run: run('stopped') })
    await stop

    expect(h.entry()?.run?.status).toBe('stopped')
  })

  it('retains a failed read for explicit retry and accepts the recovered result', async () => {
    const h = host()
    await h.cache.getState().load('thread-1', 'run-1')
    h.request.mockRejectedValueOnce(new Error('Disconnected'))
    h.notify()
    await h.cache.getState().load('thread-1', 'run-1')
    expect(h.request).toHaveBeenCalledTimes(2)
    expect(h.entry()?.run?.status).toBe('running')
    expect(h.entry()?.error).toBe('Disconnected')

    h.request.mockResolvedValueOnce({ run: run('succeeded') })
    await h.cache.getState().load('thread-1', 'run-1')
    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(h.entry()?.error).toBeNull()
  })

  it('preserves the last snapshot on repeated errors and recovers known runs on reconnect', async () => {
    const h = host()
    await h.cache.getState().load('thread-1', 'run-1')
    h.request.mockRejectedValue(new Error('Disconnected'))
    await h.cache.getState().load('thread-1', 'run-1')
    expect(h.entry()?.run?.status).toBe('running')
    expect(h.entry()?.error).toBe('Disconnected')

    h.connection(false)
    h.request.mockImplementation(async (method) => method === 'workflow/run/list' ? { runs: [] } : { run: run('succeeded') })
    h.connection(true)
    await h.cache.getState().load('thread-1', 'run-1')
    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(h.entry()?.error).toBeNull()
  })

  it('ignores old workspace responses, including a late control response', async () => {
    const h = host()
    const oldRead = deferred<{ run: WorkflowRunView }>()
    const oldStop = deferred<{ run: WorkflowRunView }>()
    h.request.mockImplementation(async (method) => method === 'workflow/run/stop' ? oldStop.promise : oldRead.promise)
    const loading = h.cache.getState().load('thread-1', 'run-1')
    const stopping = h.cache.getState().stop('thread-1', 'run-1')
    h.connection(false, 'workspace-b')
    expect(h.cache.getState().entries.size).toBe(0)

    h.request.mockResolvedValue({ run: { ...run('succeeded'), name: 'new workspace result' } })
    await h.cache.getState().load('thread-1', 'run-1')
    oldRead.resolve({ run: run() })
    oldStop.resolve({ run: run('stopped') })
    await Promise.all([loading, stopping])
    expect(h.cache.getState().scope).toBe('workspace-b')
    expect(h.entry()?.run?.name).toBe('new workspace result')
    expect(h.entry()?.run?.status).toBe('succeeded')
  })

  it('merges list metadata without dropping details or treating page absence as completion', async () => {
    const h = host()
    const detail = { ...run(), result: { retained: true }, phases: [{ name: 'review', status: 'running', agents: [] }] }
    h.request.mockResolvedValueOnce({ run: detail })
    await h.cache.getState().load('thread-1', 'run-1')
    h.request.mockImplementation(async (_method, params) => {
      const cursor = (params as { cursor?: string }).cursor
      const { phases: _phases, unphasedAgents: _agents, ...summary } = run()
      return cursor ? { runs: [summary] } : { runs: [], nextCursor: 'page-2' }
    })
    await h.cache.getState().list('thread-1')

    expect(h.entry()?.run?.result).toEqual({ retained: true })
    expect(h.entry()?.run?.phases).toEqual(detail.phases)
    expect(h.entry()?.summary?.status).toBe('running')
    expect(h.request).toHaveBeenLastCalledWith('workflow/run/list', { threadId: 'thread-1', limit: 50, cursor: 'page-2' })
  })
})
