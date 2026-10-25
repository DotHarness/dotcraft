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
    if (this.pauseReads && JSON.parse(text).method === 'thread/items/list') this.delayedReads.push(() => super.receive(id, text))
    else super.receive(id, text)
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
    expect(text('a', a)).toBe('accepted captured')
    const cancelled = history()
    computer.pauseReads = false
    for (const read of computer.delayedReads.splice(0)) read()
    await settled()
    expect(history()).toBe(cancelled)
  })

  it('flushes before a catch-up snapshot and keeps captured text from being duplicated', async () => {
    const { computer, harness, text } = await connected()
    const a = computer.started('a')
    await settled()
    computer.delta('a', a, 'before')
    await settled()
    computer.pauseReads = true
    const refreshing = Promise.all([harness.link().refresh(keyOf('a')), harness.link().refresh(keyOf('a'))])
    await waitFor(() => computer.delayedReads.length > 0)
    await settled()
    expect(computer.delayedReads).toHaveLength(1)
    expect(text('a', a)).toBe('before')
    computer.delta('a', a, ' during')
    await settled()
    computer.pauseReads = false
    for (const read of computer.delayedReads.splice(0)) read()
    await refreshing
    expect(text('a', a)).toBe('before during')
    computer.publish('item/completed', { threadId: 'a', turnId: 'turn', item: { ...a, status: 'completed', completedAt: STAMP } })
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
