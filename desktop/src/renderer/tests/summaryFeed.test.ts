import { describe, expect, it, vi } from 'vitest'
import { startSummaryFeed, type SummaryState } from '../components/conversation/summary/feed'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'

function wireItem(id: string, turnId: string, createdAt: string) {
  return { id, turnId, type: 'toolCall', status: 'completed', createdAt, payload: { callId: id, toolName: 'ReadFile' } }
}

function createHost(responses: Record<string, (params: any) => unknown>) {
  const notifications = new Map<string, (params: any) => void>()
  const services = {
    subagents: { list: () => [], onChange: () => () => undefined },
    appServer: {
      connectionKey: () => 'workspace',
      onConnectionChange: () => () => undefined,
      request: vi.fn(async (method: string, params: unknown) => responses[method]?.(params) ?? {}),
      onNotification: (method: string, listener: (params: any) => void) => {
        notifications.set(method, listener)
        return () => notifications.delete(method)
      }
    }
  } as unknown as SummaryServices
  return { services, notify: (method: string, params: unknown) => notifications.get(method)?.(params) }
}

async function settle(): Promise<void> {
  for (let index = 0; index < 5; index += 1) await Promise.resolve()
}

describe('Summary feed', () => {
  it('merges live items that arrive during history loading without duplicates', async () => {
    let releaseHistory: (value: unknown) => void = () => undefined
    const { services, notify } = createHost({
      'thread/items/list': () => new Promise((resolve) => { releaseHistory = resolve }),
      'thread/read': () => ({ thread: { plan: null } }),
      'automation/list': () => ({ automations: [] })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })

    notify('item/completed', { threadId: 'thread-1', turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') })
    notify('item/completed', { threadId: 'thread-2', turnId: 'x', item: wireItem('z', 'x', '2026-10-01T10:05:00Z') })
    releaseHistory({
      data: [
        { turnId: 't1', item: wireItem('a', 't1', '2026-10-01T10:00:00Z') },
        { turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') }
      ],
      nextCursor: null
    })
    await settle()

    expect(state!.items.map((item) => item.id)).toEqual(['a', 'b'])
    stop()
  })

  it('keeps items of different turns that share an item id', async () => {
    const { services, notify } = createHost({
      'thread/items/list': () => ({
        data: [{ turnId: 't1', item: wireItem('item_001', 't1', '2026-10-01T10:00:00Z') }],
        nextCursor: null
      }),
      'thread/read': () => ({ thread: { plan: null } }),
      'automation/list': () => ({ automations: [] })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    notify('item/completed', { threadId: 'thread-1', turnId: 't2', item: wireItem('item_001', 't2', '2026-10-01T10:05:00Z') })

    expect(state!.items.map((item) => item.turnId)).toEqual(['t1', 't2'])
    stop()
  })

  it('counts a to-do list without a plan as progress, leaving out cancelled steps', async () => {
    const { services, notify } = createHost({ 'thread/read': () => ({ thread: { plan: null } }) })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    notify('plan/updated', {
      threadId: 'thread-1',
      title: '',
      todos: [
        { id: 'a', content: 'Audit', status: 'completed' },
        { id: 'b', content: 'Migrate', status: 'in_progress' },
        { id: 'c', content: 'Rollback', status: 'cancelled' }
      ]
    })

    expect(state!.plan).toEqual({ title: '', done: 1, total: 2 })
    stop()
  })

  it('drops items of turns removed by a rollback when the next turn starts', async () => {
    const { services, notify } = createHost({
      'thread/items/list': () => ({
        data: [
          { turnId: 't1', item: wireItem('a', 't1', '2026-10-01T10:00:00Z') },
          { turnId: 't2', item: wireItem('b', 't2', '2026-10-01T10:05:00Z') }
        ],
        nextCursor: null
      }),
      'thread/turns/list': () => ({
        data: [
          { id: 't3', startedAt: '2026-10-01T10:06:00Z' },
          { id: 't1', startedAt: '2026-10-01T09:59:00Z' }
        ],
        nextCursor: null
      })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    notify('turn/started', { turn: { id: 't3', threadId: 'thread-1' } })
    await settle()

    expect(state!.items.map((item) => item.id)).toEqual(['a'])
    stop()
  })

  it('lists running terminals that outlived their turn, adding those of the active turn when it ends', async () => {
    const terminal = (sessionId: string, turnId: string, status = 'running', threadId = 'thread-1') =>
      ({ sessionId, threadId, turnId, command: `run ${sessionId}`, status })
    const { services, notify } = createHost({
      'thread/read': () => ({ thread: { plan: null, runtime: { activeTurnId: 't2' } } }),
      'terminal/list': () => ({
        terminals: [terminal('a', 't1'), terminal('b', 't2'), terminal('c', 't1', 'completed'), terminal('d', 't1', 'running', 'thread-2')]
      })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    expect(state!.terminals.map((entry) => entry.sessionId)).toEqual(['a'])

    notify('thread/status/changed', { threadId: 'thread-1', runtime: { running: false } })
    expect(state!.terminals.map((entry) => entry.sessionId)).toEqual(['a', 'b'])
    stop()
  })

  it('lists a command of the active turn once its Exec call has returned', async () => {
    const { services, notify } = createHost({
      'thread/read': () => ({ thread: { plan: null, runtime: { activeTurnId: 't2' } } }),
      'terminal/list': () => ({
        terminals: [
          { sessionId: 'dev', threadId: 'thread-1', turnId: 't2', callId: 'call-dev', command: 'npm run dev', status: 'running' },
          { sessionId: 'build', threadId: 'thread-1', turnId: 't2', callId: 'call-build', command: 'npm run build', status: 'running' }
        ]
      })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    expect(state!.terminals).toEqual([])

    notify('item/completed', {
      threadId: 'thread-1',
      turnId: 't2',
      item: { id: 'r1', turnId: 't2', type: 'toolResult', status: 'completed', createdAt: '2026-10-01T10:05:00Z', payload: { callId: 'call-dev' } }
    })

    expect(state!.terminals.map((entry) => entry.sessionId)).toEqual(['dev'])
    stop()
  })

  it('lists running and paused workflow runs and follows their updates', async () => {
    const runs: Record<string, { status: string }> = { run_b: { status: 'running' } }
    const { services, notify } = createHost({
      'workflow/run/list': () => ({
        runs: [
          { runId: 'run_c', threadId: 'thread-1', name: 'done', status: 'succeeded' },
          { runId: 'run_b', threadId: 'thread-1', name: 'review', status: 'running', createdAt: '2026-10-01T10:00:00Z', startedAt: '2026-10-01T10:00:05Z' },
          { runId: 'run_a', threadId: 'thread-1', name: 'audit', status: 'paused', createdAt: '2026-10-01T09:00:00Z' }
        ]
      }),
      'workflow/run/read': ({ runId }: { runId: string }) => ({ run: { runId, threadId: 'thread-1', name: 'review', ...runs[runId] } })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()

    expect(state!.workflows).toEqual([
      { runId: 'run_a', name: 'audit', paused: true, startedAt: '2026-10-01T09:00:00Z' },
      { runId: 'run_b', name: 'review', paused: false, startedAt: '2026-10-01T10:00:05Z' }
    ])

    runs.run_b = { status: 'succeeded' }
    notify('workflow/run/updated', { threadId: 'thread-1', runId: 'run_b', reason: 'terminal' })
    await settle()
    expect(state!.workflows.map((run) => run.runId)).toEqual(['run_a'])
    stop()
  })

  it('reports ready only after every initial read settles, even when one fails', async () => {
    let releaseItems: (value: unknown) => void = () => undefined
    const { services } = createHost({
      'thread/items/list': () => new Promise((resolve) => { releaseItems = resolve }),
      'workflow/run/list': () => Promise.reject(new Error('unsupported'))
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()
    expect(state!.ready).toBe(false)

    releaseItems({ data: [], nextCursor: null })
    await settle()
    expect(state!.ready).toBe(true)
    stop()
  })

  it('follows terminal notifications without reviving a terminal that ended while the list loaded', async () => {
    let releaseList: (value: unknown) => void = () => undefined
    const { services, notify } = createHost({
      'terminal/list': () => new Promise((resolve) => { releaseList = resolve })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    const running = { sessionId: 'x', threadId: 'thread-1', turnId: 't1', callId: 'call-dev', command: 'npm run dev', status: 'running', startedAt: '2026-10-01T10:00:00Z' }

    notify('terminal/completed', { terminal: { ...running, status: 'killed' } })
    releaseList({ terminals: [running] })
    await settle()
    expect(state!.terminals).toEqual([])

    notify('terminal/started', { terminal: { ...running, sessionId: 'y' } })
    notify('terminal/started', { terminal: { ...running, sessionId: 'z', threadId: 'thread-2' } })
    expect(state!.terminals).toEqual([{ sessionId: 'y', command: 'npm run dev', callId: 'call-dev', startedAt: '2026-10-01T10:00:00Z' }])

    notify('terminal/completed', { terminal: { ...running, sessionId: 'y', status: 'completed' } })
    expect(state!.terminals).toEqual([])
    stop()
  })

  it('settles a completed Exec result even while history is still loading', async () => {
    let releaseHistory!: (value: unknown) => void
    const { services, notify } = createHost({
      'thread/items/list': () => new Promise((resolve) => { releaseHistory = resolve }),
      'terminal/list': () => ({ terminals: [{ sessionId: 'npx', threadId: 'thread-1', turnId: 't1', callId: 'exec-1', status: 'running' }] })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()
    expect(state!.terminals.map((entry) => entry.sessionId)).toEqual(['npx'])
    notify('item/completed', {
      threadId: 'thread-1', turnId: 't1', item: {
        id: 'result', type: 'toolResult', payload: {
          callId: 'exec-1', source: { kind: 'CoreNative' },
          presentation: { presentationId: 'core.shell', options: { operation: 'exec' } },
          structuredContent: { sessionId: 'npx', status: 'completed', exitCode: 0 }
        }
      }
    })
    expect(state!.terminals).toEqual([])
    releaseHistory({ data: [], nextCursor: null })
    await settle()
    expect(state!.terminals).toEqual([])
    stop()
  })

  it('reconciles a missed completion when the active turn ends', async () => {
    let terminals = [{ sessionId: 'npx', threadId: 'thread-1', turnId: 't1', callId: 'exec-1', status: 'running' }]
    const { services, notify } = createHost({
      'thread/read': () => ({ thread: { runtime: { activeTurnId: 't1' } } }),
      'terminal/list': () => ({ terminals })
    })
    let state: SummaryState | null = null
    const { stop } = startSummaryFeed(services, 'thread-1', (next) => { state = next })
    await settle()
    terminals = []
    notify('thread/status/changed', { threadId: 'thread-1', runtime: { running: false } })
    await settle()
    expect(state!.terminals).toEqual([])
    expect(vi.mocked(services.appServer.request).mock.calls.filter(([method]) => method === 'terminal/list')).toHaveLength(2)
    stop()
  })
})
