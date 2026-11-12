import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BackgroundTasksSection } from '../components/conversation/summary/BackgroundTasksSection'
import { disposeSummaryFeeds, useRunningTaskCount, useSummaryState } from '../components/conversation/summary/store'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { LocaleProvider } from '../contexts/LocaleContext'
import { workflowRunsFor, selectWorkflowRunEntry } from '../stores/workflowRunCache'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function terminal(sessionId: string, status = 'running') {
  return { sessionId, threadId: 'thread-1', turnId: 't1', callId: null, command: `run ${sessionId}`, status }
}

function createHost(ids: string[]) {
  let terminals = ids.map((id) => terminal(id))
  const pending = new Map(ids.map((id) => [id, deferred<{ terminal: ReturnType<typeof terminal> }>()]))
  const request = vi.fn(async (method: string, params: { sessionId?: string }) => {
    if (method === 'terminal/list') return { terminals }
    if (method === 'terminal/stop') return pending.get(params.sessionId!)!.promise
    if (method === 'thread/items/list') return { data: [], nextCursor: null }
    return {}
  })
  const showError = vi.fn()
  const services = {
    appServer: { request, connectionKey: () => 'workspace', onConnectionChange: () => () => undefined, onNotification: () => () => undefined },
    subagents: { list: () => [], onChange: () => () => undefined },
    navigation: { revealToolCall: vi.fn() },
    ui: { showError }
  } as unknown as SummaryServices
  return { services, request, showError, pending, setTerminals: (next: typeof terminals) => { terminals = next } }
}

function Tasks({ services }: { services: SummaryServices }) {
  const state = useSummaryState(services, 'thread-1')
  const count = useRunningTaskCount(services, 'thread-1')
  return <>
    <output aria-label="Running tasks">{count}</output>
    <BackgroundTasksSection services={services} threadId="thread-1" terminals={state?.terminals ?? []}
      workflows={state?.workflows ?? []} workflowCalls={new Map()} />
  </>
}

function show(services: SummaryServices) {
  return render(<LocaleProvider loadSettings={false}><Tasks services={services} /></LocaleProvider>)
}

const stopButtons = () => screen.queryAllByRole('button', { name: 'Stop background terminal' })
afterEach(() => { cleanup(); disposeSummaryFeeds() })

describe('Background task stop controls', () => {
  it('settles a Workflow from its stop response and ignores an older read without an invalidation', async () => {
    const host = createHost([])
    const workflow = {
      runId: 'run-1', threadId: 'thread-1', name: 'Review', status: 'running',
      createdAt: '2026-11-09T00:00:00Z'
    }
    const oldRead = deferred<{ run: typeof workflow }>()
    host.request.mockImplementation(async (method) => {
      if (method === 'workflow/run/list') return { runs: [workflow] }
      if (method === 'workflow/run/read') return oldRead.promise
      if (method === 'workflow/run/stop') return { run: { ...workflow, status: 'stopped' } }
      return {}
    })
    show(host.services)
    const button = await screen.findByRole('button', { name: 'Stop workflow' })
    const cache = workflowRunsFor(host.services.appServer)
    let loading!: Promise<void>
    act(() => { loading = cache.getState().load('thread-1', 'run-1') })
    fireEvent.click(button)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Stop workflow' })).toBeNull())
    await act(async () => {
      oldRead.resolve({ run: workflow })
      await loading
    })

    expect(selectWorkflowRunEntry(cache.getState().entries, 'thread-1', 'run-1')?.run?.status).toBe('stopped')
    expect(screen.getByRole('status', { name: 'Running tasks' }).textContent).toBe('0')
  })

  it('clears an already completed terminal and its count from the stop response alone', async () => {
    const host = createHost(['done'])
    show(host.services)
    await waitFor(() => expect(stopButtons()).toHaveLength(1))
    fireEvent.click(stopButtons()[0])
    await act(async () => { host.pending.get('done')!.resolve({ terminal: terminal('done', 'completed') }) })
    await waitFor(() => expect(stopButtons()).toHaveLength(0))
    expect(screen.getByRole('status', { name: 'Running tasks' }).textContent).toBe('0')
    expect(host.showError).not.toHaveBeenCalled()
    expect(host.request.mock.calls.filter(([method]) => method === 'terminal/list')).toHaveLength(1)
  })

  it('stops only the selected terminal and allows retry after a failed stop', async () => {
    const host = createHost(['a', 'b', 'c'])
    show(host.services)
    await waitFor(() => expect(stopButtons()).toHaveLength(3))
    fireEvent.click(stopButtons()[0])
    await waitFor(() => expect(host.request).toHaveBeenCalledWith('terminal/stop', { sessionId: 'a' }))
    await act(async () => { host.pending.get('a')!.reject(new Error('Denied')) })
    await waitFor(() => expect(host.showError).toHaveBeenCalledTimes(1))
    expect(stopButtons()).toHaveLength(3)
    expect((stopButtons()[0] as HTMLButtonElement).disabled).toBe(false)
    host.pending.set('a', deferred())
    fireEvent.click(stopButtons()[0])
    await act(async () => { host.pending.get('a')!.resolve({ terminal: terminal('a', 'killed') }) })
    await waitFor(() => expect(stopButtons()).toHaveLength(2))
    expect(screen.getByRole('status', { name: 'Running tasks' }).textContent).toBe('2')
    const stops = host.request.mock.calls.filter(([method]) => method === 'terminal/stop')
    expect(stops.map(([, params]) => params)).toEqual([{ sessionId: 'a' }, { sessionId: 'a' }])
    expect(host.showError).toHaveBeenCalledTimes(1)
  })

  it('does not show a late failure after the section is unmounted', async () => {
    const host = createHost(['a'])
    const view = show(host.services)
    await waitFor(() => expect(stopButtons()).toHaveLength(1))
    fireEvent.click(stopButtons()[0])
    await waitFor(() => expect(host.request).toHaveBeenCalledWith('terminal/stop', { sessionId: 'a' }))
    view.unmount()
    await act(async () => { host.pending.get('a')!.reject(new Error('Denied')) })
    expect(host.showError).not.toHaveBeenCalled()
  })
})
