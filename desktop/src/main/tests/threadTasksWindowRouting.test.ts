import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

const handlers = vi.hoisted(() => new Map<string, (...args: any[]) => any>())
vi.mock('electron', () => ({
  BrowserWindow: { getAllWindows: () => [] },
  ipcMain: {
    handle: (channel: string, listener: (...args: any[]) => any) => handlers.set(channel, listener),
    removeHandler: (channel: string) => handlers.delete(channel),
    on: vi.fn(), removeListener: vi.fn()
  }
}))

import { createWindowIpcMain } from '../windowIpc'
import { registerThreadTasksIpc } from '../threadTasks/threadTasksIpc'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'
import type { ThreadTaskConnection } from '../threadTasks/childThreads'

const releases: Array<() => void> = []
afterEach(() => releases.splice(0).forEach((release) => release()))

function owner(id: number, workspacePath: string, tasks: DesktopThreadTasks) {
  const sender = Object.assign(new EventEmitter(), { id, isDestroyed: () => false })
  const window = Object.assign(new EventEmitter(), { isDestroyed: () => false, webContents: sender })
  const scope = createWindowIpcMain(window as never)
  releases.push(scope.dispose)
  const request = vi.fn(async (method: string) => method === 'thread/start'
    ? { thread: { id: `child-${id}` } } : { turn: { id: `turn-${id}` } })
  const connection = { ownerId: id, workspacePath, request } as ThreadTaskConnection
  registerThreadTasksIpc({ tasks, getConnection: () => connection }, scope)
  return { scope, connection, request, invoke: (channel: string, params: unknown) => handlers.get(channel)?.({ sender }, params) }
}

function sharedTasks() {
  return new DesktopThreadTasks({ store: new ThreadTaskStore(null), getNewTaskPrompt: () => 'ask' })
}

const proposal = (threadId: string, workspacePath: string) => ({ threadId, workspacePath, title: threadId, prompt: `Do ${threadId}`, defaults: {} })

describe('window-owned thread task requests', () => {
  it('uses each sender connection for concurrent proposal starts and closing one window leaves the other registered', async () => {
    const tasks = sharedTasks()
    const a = owner(1, '/workspace/a', tasks)
    const b = owner(2, '/workspace/b', tasks)
    const first = tasks.propose(proposal('thread-a', a.connection.workspacePath)).task
    const second = tasks.propose(proposal('thread-b', b.connection.workspacePath)).task
    expect(a.invoke('threadTasks:list', 'thread-b')).toMatchObject({ tasks: [] })
    await expect(a.invoke('threadTasks:start', { threadId: 'thread-b', taskIds: [second.id] })).resolves.toMatchObject([{ ok: false }])
    expect(a.request).not.toHaveBeenCalled()

    const results = await Promise.all([
      a.invoke('threadTasks:start', { threadId: 'thread-a', taskIds: [first.id] }),
      b.invoke('threadTasks:start', { threadId: 'thread-b', taskIds: [second.id] })
    ])
    expect(results).toMatchObject([[{ ok: true, childThreadId: 'child-1' }], [{ ok: true, childThreadId: 'child-2' }]])
    a.scope.dispose()
    expect(() => a.invoke('threadTasks:list', 'thread-a')).toThrow()
    expect(b.invoke('threadTasks:list', 'thread-b')).toMatchObject({ tasks: [{ childThreadId: 'child-2' }] })
  })

  it('abandons only the closing owner offers and lets another owner answer', async () => {
    const tasks = sharedTasks()
    const a = owner(3, '/workspace/a', tasks)
    const b = owner(4, '/workspace/b', tasks)
    const first = tasks.runOffer(a.connection, proposal('thread-a', a.connection.workspacePath), 'ask')
    const second = tasks.runOffer(b.connection, proposal('thread-b', b.connection.workspacePath), 'ask')
    const secondTask = tasks.snapshot('thread-b').tasks[0]
    tasks.abandonOffersForOwner(3)
    await expect(first).resolves.toMatchObject({ kind: 'abandoned' })
    expect(tasks.snapshot('thread-b').tasks[0].state).toBe('pending')
    await expect(b.invoke('threadTasks:answer-offer', { threadId: 'thread-b', taskId: secondTask.id, answer: { choice: 'here' } })).resolves.toEqual({ ok: true })
    await expect(second).resolves.toMatchObject({ kind: 'kept' })
  })
})
