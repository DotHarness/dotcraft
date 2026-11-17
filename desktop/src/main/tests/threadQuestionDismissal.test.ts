import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ThreadTaskConnection } from '../threadTasks/childThreads'
import { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { ThreadTaskStore } from '../threadTasks/threadTaskStore'
import { createDesktopThreadTasks, registerThreadTasksIpc } from '../threadTasks/threadTasksIpc'
import type { WindowIpcMain } from '../windowIpc'
import { THREAD_TASKS_CHANGED_CHANNEL } from '../../shared/threadTasks'

const windows = vi.hoisted(() => [] as Array<{ isDestroyed(): boolean; webContents: { send: ReturnType<typeof vi.fn> } }>)
vi.mock('electron', () => ({ BrowserWindow: { getAllWindows: () => windows } }))

const directories: string[] = []
afterEach(() => {
  directories.splice(0).forEach((directory) => rmSync(directory, { recursive: true, force: true }))
  windows.splice(0)
})

function filePath(): string {
  const directory = mkdtempSync(join(tmpdir(), 'question-dismissal-'))
  directories.push(directory)
  return join(directory, 'tasks.json')
}

function service(store = new ThreadTaskStore(null)): DesktopThreadTasks {
  return new DesktopThreadTasks({ store, getNewTaskPrompt: () => 'ask' })
}

const connection: ThreadTaskConnection = { workspacePath: '/workspace', request: vi.fn() }

describe('Wrap Up dismissal', () => {
  it('persists boxes without waiting state and retains their dismissal through question updates and restart', () => {
    const path = filePath()
    const tasks = service(new ThreadTaskStore(path))
    tasks.dismissQuestion('thread', 'non-blocking-call')
    tasks.dismissQuestion('thread', 'non-blocking-call')
    tasks.waitForQuestion(connection, 'thread', 'turn-2', 'new-call')
    tasks.clearQuestion('thread')

    expect(new ThreadTaskStore(path).snapshot('thread')).toEqual({
      threadId: 'thread', tasks: [], dismissedQuestionCallIds: ['non-blocking-call']
    })
  })

  it('clears only the matching waiting question and leaves proposals and later questions intact', () => {
    const tasks = service()
    const proposal = tasks.propose({ threadId: 'thread', workspacePath: '/workspace', title: 'Other work', prompt: 'Review', defaults: {} }).task
    tasks.waitForQuestion(connection, 'thread', 'turn-1', 'call-1')

    tasks.dismissQuestion('thread', 'call-1')
    expect(tasks.snapshot('thread').question).toBeUndefined()
    expect(tasks.snapshot('thread').tasks).toEqual([proposal])

    tasks.waitForQuestion(connection, 'thread', 'turn-2', 'call-2')
    tasks.dismissQuestion('thread', 'older-unblocked-call')
    expect(tasks.snapshot('thread')).toMatchObject({
      question: { turnId: 'turn-2', callId: 'call-2' },
      dismissedQuestionCallIds: ['call-1', 'older-unblocked-call']
    })
  })

  it('broadcasts the persisted dismissal and cleared waiting state to every active window', () => {
    const path = filePath()
    const first = { isDestroyed: () => false, webContents: { send: vi.fn() } }
    const second = { isDestroyed: () => false, webContents: { send: vi.fn() } }
    windows.push(first, second)
    const tasks = createDesktopThreadTasks({ filePath: path, getNewTaskPrompt: () => 'ask', notifyOffer: vi.fn(), notifyWrapUpQuestion: vi.fn() })
    tasks.waitForQuestion(connection, 'thread', 'turn-1', 'call-1')
    first.webContents.send.mockClear()
    second.webContents.send.mockClear()

    tasks.dismissQuestion('thread', 'call-1')

    const snapshot = { threadId: 'thread', tasks: [], dismissedQuestionCallIds: ['call-1'] }
    expect(first.webContents.send).toHaveBeenCalledExactlyOnceWith(THREAD_TASKS_CHANGED_CHANNEL, snapshot)
    expect(second.webContents.send).toHaveBeenCalledExactlyOnceWith(THREAD_TASKS_CHANGED_CHANNEL, snapshot)
  })

  it('requires the thread connection at the IPC boundary without sending any server request', () => {
    const handlers = new Map<string, Parameters<WindowIpcMain['handle']>[1]>()
    const ipc = { handle: (channel, listener) => { handlers.set(channel, listener) } } as WindowIpcMain
    const tasks = service()
    const getConnection = (threadId: string) => threadId === 'connected' ? connection : null
    registerThreadTasksIpc({ tasks, getConnection }, ipc)
    const dismiss = (params: unknown) => handlers.get('threadTasks:dismiss-question')!({} as never, params)

    expect(dismiss({ threadId: 'disconnected', callId: 'call-1' })).toMatchObject({ ok: false })
    expect(tasks.snapshot('disconnected').dismissedQuestionCallIds).toBeUndefined()
    expect(dismiss({ threadId: 'connected', callId: 'call-1' })).toEqual({ ok: true })
    expect(tasks.snapshot('connected').dismissedQuestionCallIds).toEqual(['call-1'])
    expect(connection.request).not.toHaveBeenCalled()
  })
})
