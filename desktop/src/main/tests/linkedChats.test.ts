import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { setImmediate as settle } from 'node:timers/promises'
import { JsonRpcError, RequestTimeoutError } from '@dotcraft/sdk/wire'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  DesktopLinkedChats,
  type LinkedChatChild,
  type LinkedChatConnection,
  type LinkedChatRef
} from '../threadTasks/linkedChats'

vi.mock('node:timers/promises', async importOriginal => ({
  ...await importOriginal<typeof import('node:timers/promises')>(),
  setTimeout: (ms: number) => new Promise(resolve => setTimeout(resolve, ms))
}))

const PARENT: LinkedChatRef = { threadId: 'parent', projectId: 'chats', workspacePath: '/chats' }
const CHILD: LinkedChatChild = {
  threadId: 'child', projectId: 'repo', workspacePath: '/repo', projectName: 'Repository', title: 'Work'
}

function statePath() {
  const directory = mkdtempSync(join(tmpdir(), 'linked-chats-'))
  directories.push(directory)
  return join(directory, 'links.json')
}
const directories: string[] = []
afterEach(() => directories.splice(0).forEach(directory => rmSync(directory, { force: true, recursive: true })))

function agentMessage(text: string, phase = 'final_answer', deliveryMode?: string) {
  return { type: 'agentMessage', payload: { text, phase, deliveryMode } }
}

function turn(id: string, status: string, items?: ReturnType<typeof agentMessage>[], startedAt?: string) {
  return { id, status, items, startedAt }
}

function appServer({ projectId, workspacePath }: LinkedChatRef) {
  const state = {
    header: { status: 'active', runtime: {} as object, displayName: undefined as string | undefined },
    turns: [] as ReturnType<typeof turn>[]
  }
  const notify = vi.fn(async (_params: unknown): Promise<{ queued: boolean }> => ({ queued: true }))
  const readHeader = vi.fn(async (_params?: unknown) => ({ thread: state.header }))
  const readTurns = vi.fn(async (_params?: unknown) => ({ data: state.turns }))
  const request = vi.fn(async (method: string, params?: unknown) => {
    switch (method) {
      case 'thread/read': return await readHeader(params)
      case 'thread/turns/list': return await readTurns(params)
      case 'turn/notify': return await notify(params)
      default: throw new Error(`Unexpected request: ${method}`)
    }
  })
  const connection: LinkedChatConnection = { projectId, workspacePath, request: request as LinkedChatConnection['request'] }
  return { connection, notify, readHeader, readTurns, state }
}

function runtime(chats: DesktopLinkedChats, child: ReturnType<typeof appServer>, value: object) {
  child.state.header.runtime = value
  chats.handleNotification(child.connection, 'thread/status/changed', { threadId: CHILD.threadId, runtime: value })
}

async function fixture(filePath: string | null = null) {
  const chats = new DesktopLinkedChats(filePath)
  const parent = appServer(PARENT)
  const child = appServer(CHILD)
  chats.link(PARENT, CHILD)
  chats.connected(parent.connection)
  chats.connected(child.connection)
  await settle()
  return { chats, parent, child }
}

describe('linked chat registry', () => {
  it('persists links newest first, with project-scoped identity, and removes either endpoint', () => {
    const filePath = statePath()
    const otherParent = { ...PARENT, projectId: 'another-project-with-the-same-path' }
    const nextChild = { ...CHILD, threadId: 'second-child' }
    const first = new DesktopLinkedChats(filePath)
    first.link(PARENT, CHILD)
    first.link(otherParent, nextChild)
    first.link(PARENT, nextChild)

    const reopened = new DesktopLinkedChats(filePath)
    expect(reopened.list(PARENT).map(({ child }) => child.threadId)).toEqual(['second-child', 'child'])
    expect(reopened.find(otherParent, CHILD.threadId)).toBeUndefined()
    expect(reopened.workspacePathBetween(PARENT, CHILD.threadId)).toBe(CHILD.workspacePath)
    expect(reopened.workspacePathBetween(CHILD, PARENT.threadId)).toBe(PARENT.workspacePath)
    expect(reopened.workspacePathBetween(otherParent, CHILD.threadId)).toBeUndefined()

    const parent = appServer(PARENT)
    reopened.connected(parent.connection)
    reopened.handleNotification(parent.connection, 'thread/archived', { threadId: PARENT.threadId })
    expect(reopened.list(PARENT)).toEqual([])
    expect(reopened.list(otherParent)).toHaveLength(1)

    const child = appServer(CHILD)
    reopened.connected(child.connection)
    reopened.handleNotification(child.connection, 'thread/deleted', { threadId: nextChild.threadId })
    expect(new DesktopLinkedChats(filePath).list(otherParent)).toEqual([])
  })

  it('does not deliver reports to a parent archived while Desktop was disconnected', async () => {
    const { chats, parent, child } = await fixture()
    chats.disconnected(parent.connection)
    child.state.turns = [turn('completed', 'completed')]
    runtime(chats, child, { running: false })
    await settle()
    parent.state.header.status = 'archived'

    chats.connected(parent.connection)
    await settle()
    expect(chats.list(PARENT)).toEqual([])
    expect(parent.notify).not.toHaveBeenCalled()
  })
})

describe('following linked chats', () => {
  it('holds one connection across background work, then reports the durable final reply once', async () => {
    const { chats, parent, child } = await fixture()
    const secondWindow = appServer(CHILD)
    chats.connected(secondWindow.connection)
    const finalText = 'a'.repeat(2000) + 'extra'
    runtime(chats, child, { activeTurnId: 'first', running: true })
    child.state.turns = [turn('first', 'completed', [
      agentMessage('earlier'),
      agentMessage(finalText),
      agentMessage('async only', 'final_answer', 'async'),
      agentMessage('commentary only', 'commentary')
    ])]
    runtime(chats, child, { hasBackgroundWork: true })
    await settle()

    expect(parent.notify).not.toHaveBeenCalled()
    expect(chats.holdsConnection(child.connection)).toBe(true)
    expect(chats.holdsConnection(secondWindow.connection)).toBe(false)
    expect(chats.needsProjectConnection(CHILD.projectId)).toBe(true)

    chats.handleNotification(child.connection, 'thread/renamed', { threadId: CHILD.threadId, displayName: 'Renamed' })
    parent.notify.mockResolvedValue({ queued: false })
    runtime(chats, child, { hasBackgroundWork: false })
    runtime(chats, secondWindow, { hasBackgroundWork: false })
    await settle()
    runtime(chats, child, { hasBackgroundWork: false })
    await settle()

    expect(parent.notify).toHaveBeenCalledExactlyOnceWith({
      threadId: PARENT.threadId,
      notificationId: expect.any(String),
      label: 'Renamed',
      refId: CHILD.threadId,
      status: 'completed',
      project: CHILD.projectName,
      summary: expect.stringContaining(CHILD.projectName),
      result: 'a'.repeat(2000)
    })
    expect(chats.find(PARENT, CHILD.threadId)?.status).toBe('completed')
    expect(chats.holdsConnection(child.connection)).toBe(false)
    expect(chats.needsProjectConnection(CHILD.projectId)).toBe(false)
  })

  it('reports approval, input, and plan waits without re-reporting a repeated snapshot', async () => {
    const { chats, parent, child } = await fixture()
    runtime(chats, child, { activeTurnId: 'turn', waitingOnApproval: true })
    runtime(chats, child, { activeTurnId: 'turn', waitingOnApproval: true })
    runtime(chats, child, { activeTurnId: 'turn', waitingOnInput: true })
    await settle()
    expect(chats.holdsConnection(child.connection)).toBe(true)
    expect(chats.list(PARENT)[0].status).toBe('waitingForInput')

    child.state.turns = [turn('turn', 'completed', [agentMessage('Here is the plan.')])]
    runtime(chats, child, { waitingOnPlanConfirmation: true })
    await settle()
    runtime(chats, child, { waitingOnPlanConfirmation: true })
    await settle()

    const reports = parent.notify.mock.calls.map(([value]) => value as { status: string; notificationId: string; result?: string })
    expect(reports.map(report => report.status)).toEqual(['waitingForInput', 'waitingForInput', 'completed', 'waitingForInput'])
    expect(new Set(reports.map(report => report.notificationId)).size).toBe(4)
    expect(reports.at(-1)?.result).toBe('Here is the plan.')
    expect(chats.holdsConnection(child.connection)).toBe(true)
  })

  it('catches up a missed ending and preserves an unacknowledged report across restart', async () => {
    const filePath = statePath()
    new DesktopLinkedChats(filePath).link(PARENT, CHILD)
    const child = appServer(CHILD)
    child.state.turns = [turn('missed', 'failed', [agentMessage('Final reply before failure')], '2026-10-10T02:00:00Z')]
    const disconnectedParent = appServer(PARENT)
    disconnectedParent.notify.mockRejectedValueOnce(new Error('disconnected after send'))

    const withoutAck = new DesktopLinkedChats(filePath)
    withoutAck.connected(child.connection)
    withoutAck.connected(disconnectedParent.connection)
    await settle()
    expect(withoutAck.needsProjectConnection(PARENT.projectId)).toBe(true)
    expect(disconnectedParent.notify).toHaveBeenCalledOnce()

    const reopened = new DesktopLinkedChats(filePath)
    const parent = appServer(PARENT)
    reopened.connected(parent.connection)
    await settle()
    expect(parent.notify).toHaveBeenCalledExactlyOnceWith(disconnectedParent.notify.mock.calls[0][0])
    expect(parent.notify.mock.calls[0][0]).toMatchObject({
      status: 'failed', result: 'Final reply before failure'
    })
    expect(reopened.needsProjectConnection(PARENT.projectId)).toBe(false)

    reopened.connected(child.connection)
    await settle()
    expect(parent.notify).toHaveBeenCalledOnce()

    reopened.disconnected(child.connection)
    child.state.turns = [turn('older', 'completed', [], '2026-10-10T01:00:00Z')]
    reopened.connected(child.connection)
    await settle()
    expect(parent.notify).toHaveBeenCalledOnce()
  })

  it('retries a missed acknowledgment on the same connection and preserves the report id', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    try {
      const chats = new DesktopLinkedChats(null)
      const parent = appServer(PARENT)
      const child = appServer(CHILD)
      child.state.turns = [turn('completed', 'completed')]
      chats.link(PARENT, CHILD)
      chats.connected(child.connection)
      await settle()

      parent.readHeader.mockRejectedValueOnce(new JsonRpcError(-32001, 'Server overloaded'))
      parent.notify.mockRejectedValueOnce(new RequestTimeoutError('turn/notify', 1000))
      parent.notify.mockResolvedValue({ queued: false })
      chats.connected(parent.connection)
      await settle()
      expect(parent.notify).not.toHaveBeenCalled()

      await vi.advanceTimersToNextTimerAsync()
      expect(parent.notify).toHaveBeenCalledOnce()

      await vi.advanceTimersToNextTimerAsync()
      expect(parent.notify.mock.calls).toEqual([
        [expect.objectContaining({ status: 'completed', notificationId: expect.any(String) })],
        parent.notify.mock.calls[0]
      ])
      expect(chats.needsProjectConnection(PARENT.projectId)).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })

  it('reports the caller stop separately from an unrelated cancellation', async () => {
    const { chats, parent, child } = await fixture()
    const link = chats.find(PARENT, CHILD.threadId)!
    chats.markStopped(link, 'first')
    runtime(chats, child, { activeTurnId: 'first', running: true })
    child.state.turns = [turn('first', 'cancelled')]
    runtime(chats, child, {})
    await settle()

    chats.markStopped(link, 'second')
    chats.clearStopped(link, 'second')
    runtime(chats, child, { activeTurnId: 'second', running: true })
    child.state.turns.unshift(turn('second', 'cancelled'))
    runtime(chats, child, {})
    await settle()

    expect(parent.notify.mock.calls.map(([params]) => params)).toEqual([
      expect.objectContaining({ status: 'stopped', summary: expect.stringContaining('was stopped by this chat') }),
      expect.objectContaining({ status: 'stopped', summary: expect.stringContaining('was cancelled') })
    ])
    expect(chats.list(PARENT)[0].status).toBe('stopped')
  })

  it('does not replace live waiting state with an older reconnect header', async () => {
    const chats = new DesktopLinkedChats(null)
    const parent = appServer(PARENT)
    const child = appServer(CHILD)
    let finish!: () => void
    child.readHeader.mockImplementationOnce(() => new Promise(resolve => {
      finish = () => resolve({ thread: { status: 'active', runtime: {}, displayName: 'Old title' } })
    }))
    chats.link(PARENT, CHILD)
    chats.connected(parent.connection)
    chats.connected(child.connection)
    chats.handleNotification(child.connection, 'thread/renamed', { threadId: CHILD.threadId, displayName: 'Live title' })
    runtime(chats, child, { activeTurnId: 'active', waitingOnApproval: true })
    finish()
    await settle()

    expect(chats.holdsConnection(child.connection)).toBe(true)
    expect(chats.list(PARENT)[0]).toMatchObject({ child: { title: 'Live title' }, status: 'waitingForInput' })
    expect(parent.notify).toHaveBeenCalledOnce()
  })

  it('still finds an ending when a newer Turn starts before the history response', async () => {
    const { chats, parent, child } = await fixture()
    let finish!: () => void
    child.readTurns.mockImplementationOnce(() => new Promise(resolve => {
      finish = () => resolve({ data: child.state.turns })
    }))
    runtime(chats, child, { activeTurnId: 'earlier', running: true })
    runtime(chats, child, {})
    await settle()
    runtime(chats, child, { activeTurnId: 'newer', running: true })
    child.state.turns = [turn('newer', 'running'), turn('earlier', 'completed')]
    finish()
    await settle()

    expect(parent.notify).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ status: 'completed' }))
    expect(chats.find(PARENT, CHILD.threadId)?.status).toBe('running')
    expect(chats.holdsConnection(child.connection)).toBe(true)
  })

  it('transfers observation to the next window without accepting stale history', async () => {
    const { chats, parent, child } = await fixture()
    const secondWindow = appServer(CHILD)
    let finish!: () => void
    child.readTurns.mockImplementationOnce(() => new Promise(resolve => {
      finish = () => resolve({ data: [turn('obsolete', 'completed')] })
    }))
    runtime(chats, child, { activeTurnId: 'current', running: true })
    runtime(chats, child, {})
    await settle()
    chats.connected(secondWindow.connection)
    chats.disconnected(child.connection)
    secondWindow.state.turns = [turn('current', 'failed')]
    finish()
    await settle()

    expect(parent.notify).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ status: 'failed' }))
    expect(chats.find(PARENT, CHILD.threadId)?.status).toBe('failed')
  })
})
