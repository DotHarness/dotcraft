import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BackgroundTasksSection } from '../components/conversation/summary/BackgroundTasksSection'
import { disposeSummaryFeeds, useRunningTaskCount, useSummaryState } from '../components/conversation/summary/store'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { LocaleProvider } from '../contexts/LocaleContext'

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

const stopButtons = () => screen.queryAllByRole('button', { name: 'Stop all background terminals' })
afterEach(() => { cleanup(); disposeSummaryFeeds() })

describe('Background task stop controls', () => {
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

  it('applies successful stops immediately, waits for all failures, and allows retry', async () => {
    const host = createHost(['a', 'b', 'c'])
    show(host.services)
    await waitFor(() => expect(stopButtons()).toHaveLength(3))
    fireEvent.click(stopButtons()[0])
    fireEvent.click(stopButtons()[1])
    await waitFor(() => expect(host.request.mock.calls.filter(([method]) => method === 'terminal/stop')).toHaveLength(3))
    await act(async () => { host.pending.get('b')!.reject(new Error('Denied')) })
    expect(host.showError).not.toHaveBeenCalled()
    expect(stopButtons().every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
    await act(async () => { host.pending.get('a')!.resolve({ terminal: terminal('a', 'killed') }) })
    expect(stopButtons()).toHaveLength(2)
    expect(screen.getByRole('status', { name: 'Running tasks' }).textContent).toBe('2')
    expect(stopButtons().every((button) => (button as HTMLButtonElement).disabled)).toBe(true)
    host.setTerminals([terminal('a', 'killed'), terminal('b'), terminal('c', 'killed')])
    await act(async () => { host.pending.get('c')!.resolve({ terminal: terminal('c', 'killed') }) })
    await waitFor(() => expect(host.showError).toHaveBeenCalledTimes(1))
    expect(stopButtons()).toHaveLength(1)
    expect((stopButtons()[0] as HTMLButtonElement).disabled).toBe(false)
    host.pending.set('b', deferred())
    fireEvent.click(stopButtons()[0])
    host.setTerminals([terminal('b', 'killed')])
    await act(async () => { host.pending.get('b')!.resolve({ terminal: terminal('b', 'killed') }) })
    await waitFor(() => expect(stopButtons()).toHaveLength(0))
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
