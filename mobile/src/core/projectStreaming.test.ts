import { afterEach, describe, expect, it } from 'vitest'
import { FakeComputer, type FakeItem, type FakeSocketSink, type FakeThread } from '../demo/fakeComputer'
import { DEMO_CREDENTIAL } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { applyEvent, type HistoryDelta } from './history'

const STAMP = '2026-10-25T12:00:00Z'
const keyOf = (threadId: string) => `project:${threadId}`

function thread(id: string): FakeThread {
  return { id, displayName: id, profileId: null, createdAt: STAMP, lastActiveAt: STAMP,
    turns: [{ id: 'turn', threadId: id, status: 'running', startedAt: STAMP }],
    items: [], pending: [], stream: null, continuation: '' }
}

class StreamingComputer extends FakeComputer {
  readonly eventSinks = new Map<string, FakeSocketSink>()
  readonly delayedReads: (() => void)[] = []
  pauseReads = false
  pauseProfiles = false
  readonly delayedProfiles: (() => void)[] = []
  afterItemsRead?: () => void

  constructor() {
    super({ id: 'stream-pc', name: 'Stream PC', version: '0.9.4', port: 47610, fingerprint: 'a'.repeat(64),
      addresses: ['192.168.1.10'], credentials: { [DEMO_CREDENTIAL]: 'device' },
      projects: [{ id: 'project', name: 'Project', running: true, cantStart: false, lastActiveAt: STAMP, threads: [thread('a'), thread('b')] }] })
  }

  override socket(path: string, headers: Record<string, string>, sink: FakeSocketSink): string | null {
    const id = super.socket(path, headers, sink)
    if (id && path.endsWith('/appserver')) this.eventSinks.set(id, sink)
    return id
  }

  override socketClosed(id: string): void {
    super.socketClosed(id)
    this.eventSinks.delete(id)
  }

  override receive(id: string, text: string): void {
    const method = JSON.parse(text).method
    if (this.pauseReads && method === 'thread/resume') this.delayedReads.push(() => super.receive(id, text))
    else if (this.pauseProfiles && method === 'agent/profiles/list') this.delayedProfiles.push(() => super.receive(id, text))
    else super.receive(id, text)
    if (method === 'thread/items/list') this.afterItemsRead?.()
  }

  publish(method: string, params: Record<string, unknown>): void {
    for (const sink of this.eventSinks.values()) sink.message(JSON.stringify({ jsonrpc: '2.0', method, params }))
  }

  started(threadId: string, type = 'agentMessage'): FakeItem {
    const item = this.addItem(this.thread(threadId).thread, 'turn', type, { text: '' }, 'started')
    this.publish('item/started', { threadId, turnId: 'turn', item })
    return item
  }

  delta(threadId: string, item: FakeItem, text: string): HistoryDelta {
    item.payload.text = (item.payload.text as string) + text
    this.publish(item.type === 'reasoningContent' ? 'item/reasoning/delta' : 'item/agentMessage/delta',
      { threadId, turnId: item.turnId, itemId: item.id, delta: text })
    return { kind: 'delta', turnId: item.turnId, itemId: item.id, itemType: item.type as HistoryDelta['itemType'], delta: text }
  }
}

const active: Harness[] = []
afterEach(() => { for (const harness of active.splice(0)) harness.session.dispose() })
const settled = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

async function connected() {
  const computer = new StreamingComputer()
  const harness = createHarness([computer])
  active.push(harness)
  await harness.session.boot()
  await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
  for (const id of ['a', 'b']) harness.link().openChat(keyOf(id))
  await waitFor(() => ['a', 'b'].every((id) => harness.computerState().details[keyOf(id)]?.loading === false))
  const history = (id = 'a') => harness.computerState().details[keyOf(id)].history
  const text = (id: string, item: FakeItem) => history(id).items.find((entry) => entry.id === item.id)?.payload.text
  return { computer, harness, history, text }
}

describe('project streaming', () => {
  it('keeps the summary preview read-only until the resume snapshot, mode and staged requests/deltas have been applied', async () => {
    const computer = new StreamingComputer()
    const source = computer.thread('a').thread
    source.profileId = 'reviewer'
    source.config = { mode: 'plan' }
    const item = computer.addItem(source, 'turn', 'agentMessage', { text: 'preview' }, 'started')
    computer.pauseReads = true
    computer.pauseProfiles = true
    const harness = createHarness([computer])
    active.push(harness)
    await harness.session.boot()
    await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
    const key = keyOf('a')
    harness.link().openChat(key)
    await waitFor(() => computer.delayedReads.length === 1 && harness.computerState().details[key]?.history.items.length === 1)
    expect(harness.computerState().details[key]).toMatchObject({ loading: true, config: null })
    item.payload.text = 'snapshot'
    computer.pauseReads = false
    computer.delayedReads.shift()!()
    await waitFor(() => computer.delayedProfiles.length === 1)
    computer.delta('a', item, ' + live')
    computer.ask('a', { kind: 'approval', requestId: 'restored', approvalType: 'shell', operation: 'run', target: '', reason: '' })
    await settled()
    expect(harness.computerState().details[key].history.items[0].payload.text).toBe('preview')
    expect(harness.computerState().pending[key]).toBeUndefined()
    computer.pauseProfiles = false
    computer.delayedProfiles.shift()!()
    await waitFor(() => harness.computerState().details[key]?.loading === false)
    expect(harness.computerState().details[key].config?.mode).toBe('plan')
    expect(harness.computerState().details[key].history.items[0].payload.text).toBe('snapshot + live')
    expect(harness.computerState().pending[key].map((request) => request.requestId)).toEqual(['restored'])
    expect(harness.computerState().chats[key].runtime?.waitingOnApproval).toBe(true)
  })

  it('applies a current Item page before its following deltas and keeps omitted Items', async () => {
    const { computer, harness, text } = await connected()
    const reasoning = computer.started('a', 'reasoningContent')
    const reply = computer.started('a')
    await settled()
    computer.delta('a', reply, 'existing reply')
    await settled()
    reasoning.payload.text = 'server baseline'
    computer.afterItemsRead = () => computer.delta('a', reasoning, ' + live')
    await harness.link().loadTurnItems(keyOf('a'), 'turn')
    await settled()
    harness.timers.fire(50)
    expect(text('a', reasoning)).toBe('server baseline + live')
    expect(text('a', reply)).toBe('existing reply')
  })

  it('ensures send has an applied resume on this connection before turn admission', async () => {
    const computer = new StreamingComputer()
    computer.thread('a').thread.turns = []
    computer.pauseReads = true
    const harness = createHarness([computer])
    active.push(harness)
    await harness.session.boot()
    await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
    const sending = harness.link().send(keyOf('a'), { text: 'go', files: [], photos: [], references: [] })
    await waitFor(() => computer.delayedReads.length === 1)
    expect(computer.calls.filter((call) => call.method === 'turn/start')).toHaveLength(0)
    computer.pauseReads = false
    computer.delayedReads.shift()!()
    await sending
    expect(computer.calls.filter((call) => ['thread/resume', 'turn/start'].includes(call.method)).map((call) => call.method)).toEqual(['thread/resume', 'turn/start'])
    expect(harness.computerState().details[keyOf('a')].loading).toBe(false)
  })

  it('applies 1,000 deltas in 100 fixed windows, with the same final history as individual events', async () => {
    const { computer, harness, history } = await connected()
    const message = computer.started('a')
    const reasoning = computer.started('a', 'reasoningContent')
    await settled()
    let expected = history()
    let updates = 0
    let previous = history()
    const unsubscribe = harness.session.store.subscribe(() => {
      const next = history()
      if (next !== previous) updates += 1
      previous = next
    })
    for (let window = 0; window < 100; window++) {
      for (let index = 0; index < 10; index++) expected = applyEvent(expected, computer.delta('a', index % 2 ? message : reasoning, `${window}:${index};`))
      await settled()
      harness.timers.fire(50)
    }
    unsubscribe()
    expect(updates).toBe(100)
    expect(history()).toEqual(expected)
    expect(harness.timers.delays()).not.toContain(50)
  })

  it('flushes only the event thread before approvals, questions, status and terminal snapshots', async () => {
    const { computer, harness, history, text } = await connected()
    const a = computer.started('a')
    const b = computer.started('b')
    await settled()
    computer.delta('a', a, 'before approval')
    computer.delta('b', b, 'other thread')
    await settled()
    expect(text('a', a)).toBe('')
    computer.ask('a', { kind: 'approval', requestId: 'approval', approvalType: 'shell', operation: 'test', target: 'test', reason: '' })
    await waitFor(() => harness.computerState().pending[keyOf('a')]?.length === 1)
    expect(text('a', a)).toBe('before approval')
    expect(text('b', b)).toBe('')
    computer.delta('a', a, ' then question')
    computer.ask('a', { kind: 'question', requestId: 'question', isBlocking: true, questions: [] })
    await waitFor(() => harness.computerState().pending[keyOf('a')]?.length === 2)
    expect(text('a', a)).toBe('before approval then question')
    computer.delta('a', a, ' then status')
    computer.publish('thread/status/changed', { threadId: 'a', runtime: { running: true, activeTurnId: 'turn' } })
    await settled()
    expect(text('a', a)).toBe('before approval then question then status')
    computer.delta('a', a, ' queued')
    const turn = { ...computer.thread('a').thread.turns[0], status: 'completed', completedAt: STAMP,
      items: [{ ...a, status: 'completed', completedAt: STAMP, payload: { text: 'authoritative' } }] }
    computer.publish('turn/completed', { turn })
    await settled()
    expect(history().turns.at(-1)?.status).toBe('completed')
    expect(text('a', a)).toBe('authoritative')
    harness.timers.fire(50)
    expect(text('a', a)).toBe('authoritative')
    expect(text('b', b)).toBe('other thread')
  })

  it('flushes before closing a chat and ignores late frames, including a cancelled history read', async () => {
    const { computer, harness, history, text } = await connected()
    const a = computer.started('a')
    await settled()
    computer.delta('a', a, 'accepted')
    await settled()
    harness.link().closeChat(keyOf('a'))
    expect(text('a', a)).toBe('accepted')
    const closed = history()
    computer.delta('a', a, 'late')
    await settled()
    harness.timers.fire(50)
    expect(history()).toBe(closed)

    computer.pauseReads = true
    harness.link().openChat(keyOf('a'))
    await waitFor(() => computer.delayedReads.length > 0)
    computer.delta('a', a, ' captured')
    await settled()
    harness.link().closeChat(keyOf('a'))
    expect(text('a', a)).not.toContain(' captured')
    const cancelled = history()
    computer.pauseReads = false
    for (const read of computer.delayedReads.splice(0)) read()
    await settled()
    expect(history()).toBe(cancelled)
  })

  it('reuses a warm attached view while new deltas continue without another restore', async () => {
    const { computer, harness, text } = await connected()
    const a = computer.started('a')
    await settled()
    computer.delta('a', a, 'before')
    await settled()
    const restores = computer.calls.filter((call) => call.method === 'thread/resume').length
    harness.link().openChat(keyOf('a'))
    harness.link().closeChat(keyOf('a'))
    await settled()
    expect(computer.calls.filter((call) => call.method === 'thread/resume')).toHaveLength(restores)
    computer.delta('a', a, ' during')
    await settled()
    harness.timers.fire(50)
    expect(text('a', a)).toBe('before during')
  })

  it('flushes accepted text on disconnect and never applies old connection frames after reconnect', async () => {
    const { computer, harness, text } = await connected()
    const a = computer.started('a')
    await settled()
    const old = [...computer.eventSinks.values()][0]
    computer.delta('a', a, 'offline snapshot')
    await settled()
    computer.dropConnections()
    await waitFor(() => harness.computerState().link === 'connecting')
    expect(text('a', a)).toBe('offline snapshot')
    expect(harness.timers.delays()).not.toContain(50)
    harness.timers.runAll()
    await waitFor(() => harness.computerState().link === 'online' && !harness.computerState().syncing)
    old.message(JSON.stringify({ jsonrpc: '2.0', method: 'item/agentMessage/delta', params: { threadId: 'a', turnId: 'turn', itemId: a.id, delta: 'stale' } }))
    await settled()
    harness.timers.fire(50)
    expect(text('a', a)).toBe('offline snapshot')
  })
})
