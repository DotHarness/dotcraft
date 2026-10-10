import { afterEach, describe, expect, it } from 'vitest'
import type { FakeComputer } from '../demo/fakeComputer'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { isLive, needsYou } from './chatState'
import { LIVE_END_MS, type LiveAction, type LiveNotice, type LiveNotifier, type LiveStatus } from './liveSession'
import { runningChats, stateOf, type ComputerState } from './state'

class FakeNotifier implements LiveNotifier {
  enabled = true
  prepared = 0
  stopped = 0
  readonly statuses: LiveStatus[] = []
  readonly shown = new Map<string, LiveNotice>()
  private listener: ((action: LiveAction) => void) | null = null

  async prepare() {
    this.prepared += 1
  }

  start(status: LiveStatus) {
    if (!this.enabled) return false
    this.statuses.push(status)
    return true
  }

  update(status: LiveStatus) {
    this.statuses.push(status)
  }

  stop() {
    this.stopped += 1
  }

  post(notice: LiveNotice) {
    this.shown.set(notice.id, notice)
  }

  cancel(id: string) {
    this.shown.delete(id)
  }

  subscribe(listener: (action: LiveAction) => void) {
    this.listener = listener
    return () => undefined
  }

  act(action: LiveAction) {
    this.listener?.(action)
  }

  requests(): Extract<LiveNotice, { kind: 'request' }>[] {
    return [...this.shown.values()].filter((notice) => notice.kind === 'request')
  }
}

const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

async function backgrounded(computer: FakeComputer, notifier = new FakeNotifier()) {
  computer.streamDelayMs = 1
  const harness = createHarness([computer], { live: notifier })
  harnesses.push(harness)
  const state = () => harness.computerState()
  await harness.session.boot()
  await waitFor(() => state().link === 'online' && !state().syncing)
  harness.session.setForeground(false)
  return { ...harness, notifier, state }
}

function chatKey(state: ComputerState, title: string): string {
  return Object.values(state.chats).find((chat) => chat.title === title)!.key
}

function live(state: ComputerState) {
  return runningChats(state).filter((chat) => isLive(stateOf(chat)))
}

async function twoComputers() {
  const studio = createStudio(new Date())
  const office = createStudio(new Date(Date.now() + 3_600_000), { id: 'office-pc', name: 'Office PC', addresses: ['192.168.1.30'] })
  for (const computer of [studio, office]) computer.streamDelayMs = 1
  const notifier = new FakeNotifier()
  const harness = createHarness([studio, office], { live: notifier, paired: 2 })
  harnesses.push(harness)
  await harness.session.boot()
  await waitFor(() => [studio, office].every((computer) => harness.computerState(computer.id).link === 'online' && !harness.computerState(computer.id).syncing))
  harness.session.setForeground(false)
  return { studio, office, notifier, harness }
}

describe('starting a live session', () => {
  it('asks for notifications once work runs in the foreground, then keeps the connections open in the background', async () => {
    const computer = createStudio(new Date())
    const { notifier, state } = await backgrounded(computer)
    expect(notifier.prepared).toBe(1)
    expect(notifier.statuses[0]).toMatchObject({ computer: 'Studio PC', running: 3, needsYou: 3, reachable: true })
    await waitFor(() => notifier.statuses.at(-1)?.focus?.request != null)
    expect(state().link).toBe('online')
    expect(computer.connectionCount).toBeGreaterThan(0)
  })

  it('closes the connections as before when nothing is running or notifications are off', async () => {
    const idle = createStudio(new Date(), { chats: false })
    const quiet = await backgrounded(idle)
    expect(quiet.notifier.statuses).toEqual([])
    await waitFor(() => idle.connectionCount === 0)

    const busy = createStudio(new Date())
    const blocked = new FakeNotifier()
    blocked.enabled = false
    await backgrounded(busy, blocked)
    await waitFor(() => busy.connectionCount === 0)
  })
})

describe('notifications during a live session', () => {
  it('posts waiting requests quietly, alerts for a new approval, and answers Allow once through the session', async () => {
    const computer = createStudio(new Date())
    const { notifier, state } = await backgrounded(computer)
    await waitFor(() => notifier.requests().length === 4)
    expect(notifier.requests().every((notice) => !notice.alert)).toBe(true)

    const key = chatKey(state(), 'Fix the flaky turn-diff test')
    const threadId = state().chats[key].threadId
    computer.ask(threadId, { kind: 'approval', requestId: 'approval_tests', approvalType: 'shell', operation: 'pnpm test', target: 'dotcraft', reason: '' })
    await waitFor(() => notifier.requests().length === 5)
    const notice = notifier.requests().find((entry) => entry.chat.key === key)!
    expect(notice).toMatchObject({ alert: true, request: { requestId: 'approval_tests' } })
    expect(notifier.statuses.at(-1)).toMatchObject({ running: 2, needsYou: 4 })

    notifier.act({ type: 'allow', computerId: computer.id, key, requestId: 'approval_tests' })
    await waitFor(() => computer.decisions.some((entry) => entry.requestId === 'approval_tests'))
    expect(computer.decisions.find((entry) => entry.requestId === 'approval_tests')?.decision).toBe('accept')
    expect(notifier.shown.has(notice.id)).toBe(false)
    await waitFor(() => notifier.shown.has(`turn:${computer.id}:${key}`))
    expect(notifier.shown.get(`turn:${computer.id}:${key}`)).toMatchObject({ kind: 'turnEnded', failed: false })
  })

  it('follows chats on every paired computer and answers an approval on the computer it came from', async () => {
    const studio = createStudio(new Date())
    const office = createStudio(new Date(), { id: 'office-pc', name: 'Office PC', addresses: ['192.168.1.30'] })
    for (const computer of [studio, office]) computer.streamDelayMs = 1
    const notifier = new FakeNotifier()
    const harness = createHarness([studio, office], { live: notifier, paired: 2 })
    harnesses.push(harness)
    await harness.session.boot()
    await waitFor(() => [studio, office].every((computer) => harness.computerState(computer.id).link === 'online' && !harness.computerState(computer.id).syncing))
    harness.session.setForeground(false)
    await waitFor(() => notifier.requests().length === 8)
    expect(new Set(notifier.requests().map((notice) => notice.computer))).toEqual(new Set(['Studio PC', 'Office PC']))
    expect(notifier.statuses.at(-1)).toMatchObject({ running: 6, needsYou: 6 })

    const notice = notifier.requests().find((entry) => entry.computerId === office.id && entry.request.kind === 'approval')!
    notifier.act({ type: 'allow', computerId: office.id, key: notice.chat.key, requestId: notice.request.requestId })
    await waitFor(() => office.decisions.some((entry) => entry.requestId === notice.request.requestId))
    expect(studio.decisions).toEqual([])
  })

  it('follows the most recent waiting chat across computers, not the first paired one', async () => {
    const { office, notifier } = await twoComputers()
    await waitFor(() => notifier.requests().length === 8)
    expect(notifier.statuses.at(-1)?.focus).toMatchObject({ computerId: office.id, state: expect.stringMatching(/^needs-/) })
  })

  it('follows the most recent running chat across computers when nothing waits', async () => {
    const { studio, office, notifier, harness } = await twoComputers()
    await waitFor(() => notifier.requests().length === 8)
    for (const computer of [studio, office]) {
      for (const chat of runningChats(harness.computerState(computer.id)).filter((entry) => needsYou(stateOf(entry)))) computer.endTurn(chat.threadId, 'completed')
    }
    await waitFor(() => notifier.statuses.at(-1)?.needsYou === 0)
    expect(notifier.statuses.at(-1)?.focus).toMatchObject({ computerId: office.id, state: 'running' })
  })

  it('posts one notification when a turn fails in the background', async () => {
    const computer = createStudio(new Date())
    const { notifier, state } = await backgrounded(computer)
    const key = chatKey(state(), 'Audit the dialog headers')
    await waitFor(() => notifier.requests().length === 4)
    computer.endTurn(state().chats[key].threadId, 'failed')
    await waitFor(() => notifier.shown.get(`turn:${computer.id}:${key}`)?.kind === 'turnEnded')
    expect(notifier.shown.get(`turn:${computer.id}:${key}`)).toMatchObject({ failed: true })
  })
})

describe('ending a live session', () => {
  it('ends two minutes after nothing is running or waiting', async () => {
    const computer = createStudio(new Date())
    const { notifier, state, timers } = await backgrounded(computer)
    await waitFor(() => notifier.requests().length === 4)
    for (const chat of live(state())) computer.endTurn(chat.threadId, 'completed')
    await waitFor(() => live(state()).length === 0 && timers.delays().includes(LIVE_END_MS))
    expect(notifier.statuses.at(-1)).toMatchObject({ running: 0, needsYou: 0 })
    expect(notifier.stopped).toBe(0)

    timers.fire(LIVE_END_MS)
    expect(notifier.stopped).toBe(1)
    expect(notifier.requests()).toEqual([])
    expect(state().link).toBe('idle')
    await waitFor(() => computer.connectionCount === 0)
  })

  it('ends when the user chooses End', async () => {
    const computer = createStudio(new Date())
    const { notifier, state } = await backgrounded(computer)
    notifier.act({ type: 'end' })
    expect(notifier.stopped).toBe(1)
    expect(state().link).toBe('idle')
    await waitFor(() => computer.connectionCount === 0)
  })

  it('ends two minutes after the computer becomes unreachable', async () => {
    const computer = createStudio(new Date())
    const { notifier, state, timers, link } = await backgrounded(computer)
    computer.reachable = false
    computer.dropConnections()
    await waitFor(() => state().link === 'connecting' && link().reconnectPending)
    expect(notifier.statuses.at(-1)).toMatchObject({ reachable: false })
    timers.fire(LIVE_END_MS)
    expect(notifier.stopped).toBe(1)
    expect(state().link).toBe('idle')
    expect(link().reconnectPending).toBe(false)
  })

  it('continues on the open connections when the app returns to the foreground', async () => {
    const computer = createStudio(new Date())
    const { notifier, state, session, network } = await backgrounded(computer)
    await waitFor(() => notifier.requests().length === 4)
    const sockets = network.sockets.length
    session.setForeground(true)
    expect(notifier.stopped).toBe(1)
    expect(notifier.requests()).toEqual([])
    expect(state().link).toBe('online')
    expect(network.sockets.length).toBe(sockets)
    expect(computer.connectionCount).toBeGreaterThan(0)
  })
})

describe('background tasks during a live session', () => {
  it('holds a chat while its background tasks run and reports the turn its workflow starts when it finishes', async () => {
    const computer = createStudio(new Date())
    computer.streamDelayMs = 1
    const notifier = new FakeNotifier()
    const harness = createHarness([computer], { live: notifier })
    harnesses.push(harness)
    const state = () => harness.computerState()
    await harness.session.boot()
    await waitFor(() => state().link === 'online' && !state().syncing)
    const key = chatKey(state(), 'Review the release branch')
    const threadId = state().chats[key].threadId
    harness.link().openChat(key)
    await waitFor(() => Object.keys(state().tasks[key] ?? {}).length === 3)
    harness.link().closeChat(key)
    for (const chat of live(state())) computer.endTurn(chat.threadId, 'completed')
    await waitFor(() => live(state()).length === 0)

    harness.session.setForeground(false)
    expect(notifier.statuses.at(-1)).toMatchObject({ running: 0, needsYou: 0, tasks: 2, focus: { chat: { key }, tasks: 2 } })
    await waitFor(() => computer.calls.filter((call) => call.method === 'thread/subscribe' && call.params.threadId === threadId).length === 2)
    expect(harness.timers.delays()).not.toContain(LIVE_END_MS)

    computer.background.finishTerminal('term_docs_dev')
    computer.background.finishWorkflow('run_review')
    await waitFor(() => notifier.shown.get(`turn:${computer.id}:${key}`)?.kind === 'turnEnded')
    expect(notifier.shown.get(`turn:${computer.id}:${key}`)).toMatchObject({ failed: false })
    await waitFor(() => harness.timers.delays().includes(LIVE_END_MS))
    expect(notifier.statuses.at(-1)).toMatchObject({ tasks: 0 })
  })
})
