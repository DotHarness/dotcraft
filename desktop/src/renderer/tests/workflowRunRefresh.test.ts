import { describe, expect, it, vi } from 'vitest'
import type { WorkflowRunView } from '@dotcraft/sdk/contracts'
import { workflowRunsFor, selectWorkflowRunEntry, type WorkflowRunClient } from '../stores/workflowRunCache'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((yes) => { resolve = yes })
  return { promise, resolve }
}

function run(status = 'running'): WorkflowRunView {
  return {
    runId: 'run-1', threadId: 'thread-1', name: 'review', status,
    controls: { canPause: status === 'running', canStop: status === 'running', canResume: false },
    totals: { agentCount: 0, queuedCount: 0, runningCount: 0, completedCount: 0, failedCount: 0, stoppedCount: 0, replayedCount: 0, inputTokens: 0, outputTokens: 0, toolCallCount: 0 },
    phases: [], unphasedAgents: [], createdAt: '2026-11-11T00:00:00Z'
  }
}

function host() {
  let scope = 'workspace-a'
  const connections = new Set<(connected: boolean) => void>()
  const notifications = new Map<string, (params: unknown) => void>()
  const request = vi.fn(async (method: string): Promise<unknown> => method === 'workflow/run/list'
    ? { runs: [] } : { run: run('succeeded') })
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
  const cache = workflowRunsFor(client)
  return {
    cache, request,
    notify: () => notifications.get('workflow/run/updated')?.({ threadId: 'thread-1', runId: 'run-1', reason: 'terminal' }),
    entry: () => selectWorkflowRunEntry(cache.getState().entries, 'thread-1', 'run-1'),
    connection(nextScope: string) {
      scope = nextScope
      for (const listener of connections) listener(false)
    }
  }
}

describe('Workflow run refresh reconciliation', () => {
  it('reads active runs restored by the current list even without a completion notification', async () => {
    const h = host()
    h.request.mockImplementation(async method => method === 'workflow/run/list'
      ? { runs: [run()] } : { run: run('succeeded') })
    const observed: string[][] = []
    const stop = h.cache.getState().observe('thread-1', runs => observed.push(runs.map(value => value.status)))

    await h.cache.getState().refresh('thread-1')

    expect(h.request.mock.calls.map(([method]) => method)).toEqual(['workflow/run/list', 'workflow/run/read'])
    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(observed.at(-1)).toEqual([])
    stop()
  })

  it('rereads a known active run when it is absent from the list', async () => {
    const h = host()
    h.request.mockResolvedValueOnce({ run: run() })
    await h.cache.getState().load('thread-1', 'run-1')

    await h.cache.getState().refresh('thread-1')

    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(h.request.mock.calls.map(([method]) => method)).toEqual([
      'workflow/run/read', 'workflow/run/list', 'workflow/run/read'
    ])
  })

  it('waits for an old list and then discovers runs through a fresh list', async () => {
    const h = host()
    const old = deferred<{ runs: WorkflowRunView[] }>()
    h.request.mockImplementation(async method => method === 'workflow/run/list'
      ? { runs: [run()] } : { run: run('succeeded') })
    h.request.mockImplementationOnce(() => old.promise)
    const listing = h.cache.getState().list('thread-1')
    const refreshing = h.cache.getState().refresh('thread-1')
    expect(h.request).toHaveBeenCalledTimes(1)

    old.resolve({ runs: [] })
    await Promise.all([listing, refreshing])

    expect(h.request.mock.calls.map(([method]) => method)).toEqual([
      'workflow/run/list', 'workflow/run/list', 'workflow/run/read'
    ])
    expect(h.entry()?.run?.status).toBe('succeeded')
  })

  it('requests a trailing read when refresh reaches an older in-flight read', async () => {
    const h = host()
    h.request.mockResolvedValueOnce({ run: run() })
    await h.cache.getState().load('thread-1', 'run-1')
    const old = deferred<{ run: WorkflowRunView }>()
    h.request.mockImplementation(async method => method === 'workflow/run/list'
      ? { runs: [] } : { run: run('succeeded') })
    h.request.mockImplementationOnce(() => old.promise)
    const loading = h.cache.getState().load('thread-1', 'run-1')
    const refreshing = h.cache.getState().refresh('thread-1')
    await h.cache.getState().list('thread-1')

    old.resolve({ run: run() })
    await Promise.all([loading, refreshing])

    expect(h.request.mock.calls.filter(([method]) => method === 'workflow/run/read')).toHaveLength(3)
    expect(h.entry()?.run?.status).toBe('succeeded')
  })

  it('rereads an initial in-flight load even before it has a summary', async () => {
    const h = host()
    const old = deferred<{ run: WorkflowRunView }>()
    h.request.mockImplementationOnce(() => old.promise)
    const loading = h.cache.getState().load('thread-1', 'run-1')
    const refreshing = h.cache.getState().refresh('thread-1')
    await h.cache.getState().list('thread-1')

    old.resolve({ run: run() })
    await Promise.all([loading, refreshing])

    expect(h.request.mock.calls.filter(([method]) => method === 'workflow/run/read')).toHaveLength(2)
    expect(h.entry()?.run?.status).toBe('succeeded')
  })

  it('reads the next invalidation immediately after a result reaches subscribers', async () => {
    const h = host()
    const reached = deferred<void>()
    const stop = h.cache.subscribe(() => {
      if (h.entry()?.run?.status === 'paused') reached.resolve()
    })
    h.request.mockResolvedValueOnce({ run: run('paused') })
    const loading = h.cache.getState().load('thread-1', 'run-1')
    await reached.promise

    h.notify()
    await h.cache.getState().load('thread-1', 'run-1')
    await loading

    expect(h.entry()?.run?.status).toBe('succeeded')
    expect(h.request).toHaveBeenCalledTimes(2)
    stop()
  })

  it('starts a new list after the previous result reaches subscribers', async () => {
    const h = host()
    const reached = deferred<void>()
    const stop = h.cache.subscribe(() => {
      if (h.entry()?.summary?.status === 'paused') reached.resolve()
    })
    h.request.mockResolvedValueOnce({ runs: [run('paused')] }).mockResolvedValueOnce({ runs: [run('succeeded')] })
    const listing = h.cache.getState().list('thread-1')
    await reached.promise

    await h.cache.getState().list('thread-1')
    await listing

    expect(h.entry()?.summary?.status).toBe('succeeded')
    expect(h.request).toHaveBeenCalledTimes(2)
    stop()
  })

  it('does not continue an old refresh after the connection changes during its list wait', async () => {
    const h = host()
    const old = deferred<{ runs: WorkflowRunView[] }>()
    h.request.mockImplementationOnce(() => old.promise)
    const listing = h.cache.getState().list('thread-1')
    const refreshing = h.cache.getState().refresh('thread-1')
    h.connection('workspace-b')
    old.resolve({ runs: [run()] })
    await Promise.all([listing, refreshing])

    expect(h.cache.getState().entries.size).toBe(0)
    expect(h.request).toHaveBeenCalledTimes(1)
  })
})
