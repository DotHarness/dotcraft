import { afterEach, describe, expect, it, vi } from 'vitest'
import type { PinnedSocketEvent } from './pinned'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { chatKey } from './state'
import { chatTaskTree, createWorkingSubagentSelector } from './taskTree'

const parent = 'thread_release_review'
const child = 'thread_handoff_owner'
const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
  vi.restoreAllMocks()
})

function scenario(running = true) {
  const computer = createStudio(new Date())
  computer.streamDelayMs = 1
  computer.background.terminals.length = 0
  computer.background.agents.length = 0
  computer.background.workflows.length = 0
  const { project, thread } = computer.thread(parent)
  const at = new Date().toISOString()
  project.threads.push({ ...thread, id: child, source: 'subagent', items: [], turns: [], pending: [] })
  computer.background.agents.push({ childThreadId: child, parentThreadId: parent, nickname: 'Builder', role: 'worker', running, createdAt: at, updatedAt: at })
  computer.background.terminals.push({ sessionId: 'term_handoff', isBackground: false, threadId: child, command: 'npm test', status: 'running', startedAt: at, output: '', turnId: `turn_${child}` })
  const harness = createHarness([computer])
  harnesses.push(harness)
  const defer = deferReplies(harness)
  const state = () => harness.computerState()
  const key = chatKey(project.id, parent)
  const tasks = () => chatTaskTree(state(), key)
  const working = createWorkingSubagentSelector()
  const boot = async () => {
    await harness.session.boot()
    await waitFor(() => state().link === 'online' && !state().syncing)
    harness.link().openChat(key)
  }
  return { computer, harness, state, key, tasks, project, boot, working, defer }
}

function deferReplies(harness: Harness) {
  const holds: {
    matches(method: string, params: Record<string, unknown>): boolean
    requestId: string | number | null
    notification?: string
    event: PinnedSocketEvent | null
    deliver: ((event: PinnedSocketEvent) => void) | null
  }[] = []
  const send = harness.network.send.bind(harness.network)
  const subscribe = harness.network.subscribe.bind(harness.network)
  vi.spyOn(harness.network, 'send').mockImplementation((id, text) => {
    const request = JSON.parse(text)
    const hold = holds.find((entry) => entry.requestId === null && entry.matches(request.method ?? '', request.params ?? {}))
    if (hold) hold.requestId = request.id
    send(id, text)
  })
  vi.spyOn(harness.network, 'subscribe').mockImplementation((listener) => subscribe((event) => {
    const message = event.type === 'message' ? JSON.parse(event.text) : null
    const hold = holds.find((entry) => !entry.event && (entry.notification ? entry.notification === message?.method : entry.requestId === message?.id))
    if (hold) {
      hold.event = event
      hold.deliver = listener
    } else listener(event)
  }))
  const controls = (hold: typeof holds[number]) => {
    holds.push(hold)
    return {
      ready: () => hold.event !== null,
      release: () => { if (hold.event) hold.deliver?.(hold.event) },
    }
  }
  return {
    hold(matches: (method: string, params: Record<string, unknown>) => boolean) {
      return controls({ matches, requestId: null, event: null, deliver: null })
    },
    holdNotification(method: string) {
      return controls({ matches: () => false, requestId: null, event: null, deliver: null, notification: method })
    },
  }
}

describe('background handoff propagation', () => {
  it('counts an active child plus its handed-off command and keeps the idle family working', async () => {
    const { computer, harness, state, key, tasks, project, boot, working, defer } = scenario()
    await boot()
    await waitFor(() => tasks().running.length === 1 && state().tasks[chatKey(project.id, child)]?.shell !== undefined)
    expect(tasks().running[0].id).toBe(child)
    computer.background.handoffTerminal('term_handoff')
    await waitFor(() => tasks().running.length === 2)
    computer.background.finishAgent(child)
    await waitFor(() => tasks().running.find((task) => task.id === child)?.stop === null)
    expect(tasks().running).toHaveLength(2)
    expect(working(state(), key)).toEqual(new Set(['/root/builder']))
    expect(state().details[chatKey(project.id, child)]).toBeUndefined()

    harness.link().openChat(chatKey(project.id, child))
    await waitFor(() => state().details[chatKey(project.id, child)]?.loading === false)
    expect(chatTaskTree(state(), chatKey(project.id, child)).running.map((task) => task.id)).toEqual(['term_handoff'])
    const completion = defer.holdNotification('terminal/completed')
    await harness.link().stopTask(key, tasks().running.find((task) => task.id === 'term_handoff')!)
    await waitFor(completion.ready)
    expect(tasks().running).toHaveLength(0)
    const late = defer.holdNotification('terminal/backgrounded')
    computer.background.terminals[0].status = 'running'
    computer.background.handoffTerminal('term_handoff')
    await waitFor(late.ready)
    late.release()
    await Promise.resolve()
    expect(tasks().running).toHaveLength(0)
    expect(tasks().completed.find((task) => task.id === 'term_handoff')?.status).toBe('stopped')
    expect(working(state(), key).size).toBe(0)
  })

  it('retains confirmed activity through idle reconciliation and merges a newer handoff over the old list', async () => {
    const { computer, harness, state, tasks, project, boot, defer } = scenario()
    await boot()
    await waitFor(() => tasks().running.length === 1 && state().tasks[chatKey(project.id, child)]?.shell !== undefined)
    const hold = defer.hold((method, params) => method === 'terminal/list' && params.threadId === child)
    const counts: number[] = []
    const unsubscribe = harness.session.store.subscribe(() => counts.push(tasks().running.length))
    computer.background.finishAgent(child)
    await waitFor(hold.ready)
    expect(tasks().running).toHaveLength(1)
    computer.background.handoffTerminal('term_handoff')
    await waitFor(() => tasks().running.length === 2)
    hold.release()
    await waitFor(() => state().tasks[chatKey(project.id, parent)]?.agent?.every((task) => task.kind !== 'agent' || !task.reconciling) === true)
    unsubscribe()
    expect(counts).not.toContain(0)
    expect(tasks().running).toHaveLength(2)
  })

  it('retains lifecycle evidence before child discovery and does not revive completed work from a stale list or handoff', async () => {
    const { computer, state, tasks, project, boot, defer } = scenario(false)
    const discovery = defer.hold((method, params) => method === 'subagent/children/list' && params.parentThreadId === parent)
    const shell = defer.hold((method, params) => method === 'terminal/list' && params.threadId === child)
    await boot()
    await waitFor(discovery.ready)
    computer.background.handoffTerminal('term_handoff')
    await waitFor(() => computer.calls.some((call) => call.method === 'subagent/children/list'))
    discovery.release()
    await waitFor(shell.ready)
    await waitFor(() => tasks().running.length === 2)
    expect(state().details[chatKey(project.id, child)]).toBeUndefined()
    computer.background.finishTerminal('term_handoff')
    await waitFor(() => tasks().running.length === 0)
    const terminal = computer.background.terminals[0]
    terminal.status = 'running'
    computer.background.handoffTerminal(terminal.sessionId)
    shell.release()
    await waitFor(() => state().tasks[chatKey(project.id, child)]?.shell?.[0]?.status === 'completed')
    expect(tasks().running).toHaveLength(0)
  })

  it('retries a failed descendant read when the parent reopens', async () => {
    const { computer, harness, state, key, tasks, project, boot, defer } = scenario(false)
    computer.failingMethods.add('terminal/list')
    await boot()
    await waitFor(() => state().tasks[key]?.agent?.some((task) => task.id === child) === true)
    expect(tasks().running).toHaveLength(0)
    computer.background.terminals[0].isBackground = true
    computer.failingMethods.delete('terminal/list')
    harness.link().closeChat(key)
    harness.link().openChat(key)
    await waitFor(() => tasks().running.length === 2)
    expect(state().details[chatKey(project.id, child)]).toBeUndefined()

    const [terminal] = computer.background.terminals.splice(0)
    harness.link().closeChat(key)
    harness.link().openChat(key)
    await waitFor(() => state().tasks[chatKey(project.id, child)]?.shell?.length === 0)
    expect(tasks().running).toHaveLength(0)
    computer.background.terminals.push(terminal)
    const late = defer.holdNotification('terminal/backgrounded')
    computer.background.handoffTerminal(terminal.sessionId)
    await waitFor(late.ready)
    late.release()
    await Promise.resolve()
    expect(tasks().running).toHaveLength(0)
  })

  it('ignores delayed task replies after replacing the project connection', async () => {
    const { computer, harness, state, tasks, project, boot, defer } = scenario(false)
    computer.background.terminals[0].isBackground = true
    const old = defer.hold((method, params) => method === 'terminal/list' && params.threadId === child)
    await boot()
    await waitFor(old.ready)
    computer.dropConnections()
    await waitFor(() => state().link === 'connecting')
    computer.background.terminals[0].status = 'completed'
    harness.timers.runAll()
    await waitFor(() => state().link === 'online' && !state().syncing)
    await waitFor(() => state().tasks[chatKey(project.id, child)]?.shell?.[0]?.status === 'completed')
    old.release()
    expect(tasks().running).toHaveLength(0)
  })
})
