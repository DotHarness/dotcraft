import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTerminalFeed, type SummaryTerminal } from '../components/conversation/summary/terminalFeed'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import type { SummaryItem } from '../components/conversation/summary/sources'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((accept) => { resolve = accept })
  return { promise, resolve }
}

function terminal(sessionId: string, status = 'running') {
  return {
    sessionId, status, threadId: 'thread-1', turnId: 'turn-1', callId: `call-${sessionId}`,
    command: `run ${sessionId}`, startedAt: '2026-10-28T07:00:00Z'
  }
}

function result(sessionId: string, status: string, extra: Record<string, unknown> = {}): SummaryItem {
  return {
    id: `result-${sessionId}`, turnId: 'turn-1', type: 'toolResult', createdAt: '2026-10-28T07:01:00Z',
    payload: {
      toolName: 'Exec', callId: `call-${sessionId}`,
      source: { kind: 'CoreNative', sourceId: 'core-native' },
      presentation: { presentationId: 'core.shell', options: { operation: 'exec' } },
      structuredContent: { sessionId, status, exitCode: status === 'completed' ? 0 : null },
      ...extra
    }
  }
}

const disposals: (() => void)[] = []
afterEach(() => {
  for (const dispose of disposals.splice(0)) dispose()
})

function host(responses: Record<string, (params: Record<string, unknown>) => unknown> = {}) {
  const scope = 'workspace-1'
  let connectionListener: ((connected: boolean) => void) | null = null
  let state: readonly SummaryTerminal[] = []
  const notifications = new Map<string, (params: { terminal: ReturnType<typeof terminal> }) => void>()
  const request = vi.fn(async (method: string, params: Record<string, unknown>) => {
    if (responses[method]) return responses[method](params)
    if (method === 'terminal/list') return { terminals: [] }
    throw new Error(`Unexpected request: ${method}`)
  })
  const services = {
    appServer: {
      connectionKey: () => scope,
      onConnectionChange(listener: (connected: boolean) => void) {
        connectionListener = listener
        return () => { connectionListener = null }
      },
      request,
      onNotification(method: string, listener: (params: { terminal: ReturnType<typeof terminal> }) => void) {
        notifications.set(method, listener)
        return () => { notifications.delete(method) }
      }
    }
  } as unknown as SummaryServices
  const changed = vi.fn((next: readonly SummaryTerminal[]) => { state = next })
  const feed = createTerminalFeed(services, 'thread-1', changed)
  disposals.push(feed.dispose)
  return {
    feed, request, changed,
    ids: () => state.map((entry) => entry.sessionId),
    notify: (method: string, snapshot: ReturnType<typeof terminal>) => notifications.get(method)?.({ terminal: snapshot }),
    connection: (connected: boolean) => connectionListener?.(connected)
  }
}

async function settle(): Promise<void> {
  for (let index = 0; index < 8; index += 1) await Promise.resolve()
}

describe('Summary terminal state reconciliation', () => {
  it('keeps a historical completion final when a later list contains an older running snapshot', async () => {
    const h = host({ 'terminal/list': () => ({ terminals: [terminal('a')] }) })
    h.feed.addItems([result('a', 'completed')])

    await h.feed.refresh()
    h.notify('terminal/started', terminal('a'))

    expect(h.ids()).toEqual([])
  })

  it('reveals the active-turn terminal after its Exec returns running rather than treating the return as exit', () => {
    const h = host()
    h.feed.setActiveTurn('turn-1')
    h.notify('terminal/started', terminal('a'))
    expect(h.ids()).toEqual([])

    h.feed.addItems([result('a', 'running')])

    expect(h.ids()).toEqual(['a'])
  })

  it('ignores untrusted same-name fields and terminal results with a different call or turn', () => {
    const h = host()
    h.notify('terminal/started', terminal('a'))
    const wrongTurn = { ...result('a', 'completed'), turnId: 'another-turn' }
    h.feed.addItems([
      result('a', 'completed', { source: { kind: 'Mcp', sourceId: 'external' } }),
      result('a', 'completed', { presentation: undefined }),
      result('a', 'completed', { callId: 'another-call' }),
      wrongTurn
    ])

    expect(h.ids()).toEqual(['a'])
    h.feed.addItems([result('a', 'completed')])
    expect(h.ids()).toEqual([])
  })

  it('preserves a new start during a list read and never revives a terminal that completed during that read', async () => {
    const list = deferred<unknown>()
    const h = host({ 'terminal/list': () => list.promise })
    h.notify('terminal/started', terminal('old'))
    const refreshing = h.feed.refresh()
    h.notify('terminal/completed', terminal('old', 'completed'))
    h.notify('terminal/started', terminal('new'))

    list.resolve({ terminals: [terminal('old')] })
    await refreshing

    expect(h.ids()).toEqual(['new'])
  })

  it('keeps a running task when refreshing fails, then removes it on a successful authoritative empty list', async () => {
    let fail = true
    const h = host({ 'terminal/list': () => fail ? Promise.reject(new Error('Disconnected')) : { terminals: [] } })
    h.notify('terminal/started', terminal('a'))

    await h.feed.refresh()
    expect(h.ids()).toEqual(['a'])
    fail = false
    await h.feed.refresh()
    expect(h.ids()).toEqual([])
  })

  it('coalesces concurrent refresh requests into one active read and one trailing reconciliation', async () => {
    const list = deferred<unknown>()
    let reads = 0
    const h = host({ 'terminal/list': () => ++reads === 1 ? list.promise : { terminals: [] } })
    const first = h.feed.refresh()
    const second = h.feed.refresh()
    const third = h.feed.refresh()
    expect(reads).toBe(1)

    list.resolve({ terminals: [terminal('a')] })
    await Promise.all([first, second, third])

    expect(reads).toBe(2)
    expect(h.ids()).toEqual([])
  })

  it('ignores pending list and stop results after disposal and unsubscribes from further updates', async () => {
    const list = deferred<unknown>()
    const stop = deferred<unknown>()
    const h = host({ 'terminal/list': () => list.promise, 'terminal/stop': () => stop.promise })
    h.notify('terminal/started', terminal('a'))
    const refreshing = h.feed.refresh()
    const stopping = h.feed.stopMany(['a'])
    h.feed.dispose()
    h.changed.mockClear()

    list.resolve({ terminals: [terminal('new')] })
    stop.resolve({ terminal: terminal('a', 'completed') })
    await Promise.all([refreshing, stopping])
    h.notify('terminal/started', terminal('another'))
    h.connection(true)

    expect(h.changed).not.toHaveBeenCalled()
    expect(h.ids()).toEqual(['a'])
  })

  it('discards old connection responses while allowing a recovered connection to reconcile current state', async () => {
    const oldList = deferred<unknown>()
    const oldStop = deferred<unknown>()
    let reads = 0
    const h = host({
      'terminal/list': () => ++reads === 1 ? oldList.promise : { terminals: [terminal('a')] },
      'terminal/stop': () => oldStop.promise
    })
    h.notify('terminal/started', terminal('a'))
    const refreshing = h.feed.refresh()
    const stopping = h.feed.stopMany(['a'])

    h.connection(false)
    h.connection(true)
    await settle()
    expect(reads).toBe(2)
    oldList.resolve({ terminals: [terminal('old')] })
    oldStop.resolve({ terminal: terminal('a', 'completed') })
    await Promise.all([refreshing, stopping])

    expect(h.ids()).toEqual(['a'])
  })
})
