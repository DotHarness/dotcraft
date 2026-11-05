import { BrowserWindow } from 'electron'
import type { WindowIpcMain } from '../windowIpc'
import { sameFilePath } from '../../shared/workspaceProjectKey'
import {
  THREAD_TASKS_CHANGED_CHANNEL,
  type NewTaskPromptMode,
  type ThreadOfferAnswer,
  type ThreadTask,
  type ThreadTaskActionResult,
  type ThreadTaskStartOptions,
  type ThreadTaskStartResult
} from '../../shared/threadTasks'
import type { ThreadTaskConnection } from './childThreads'
import { DesktopThreadTasks } from './threadTaskService'
import { ThreadTaskStore } from './threadTaskStore'

const NOT_CONNECTED = 'DotCraft is not connected.'

export interface DesktopThreadTasksRuntimeOptions {
  filePath: string
  getNewTaskPrompt(): NewTaskPromptMode
  notifyOffer(task: ThreadTask, ownerId?: number): void
}

export function createDesktopThreadTasks(options: DesktopThreadTasksRuntimeOptions): DesktopThreadTasks {
  const store = new ThreadTaskStore(options.filePath)
  store.onChanged((snapshot) => {
    for (const win of BrowserWindow.getAllWindows()) {
      if (!win.isDestroyed()) win.webContents.send(THREAD_TASKS_CHANGED_CHANNEL, snapshot)
    }
  })
  return new DesktopThreadTasks({
    store,
    getNewTaskPrompt: options.getNewTaskPrompt,
    notifyOffer: options.notifyOffer
  })
}

export interface ThreadTasksIpcHost {
  tasks: DesktopThreadTasks
  getConnection(threadId?: string): ThreadTaskConnection | null
}

export function registerThreadTasksIpc(host: ThreadTasksIpcHost, ipc: WindowIpcMain): void {
  ipc.handle('threadTasks:list', (_event, value: unknown) => {
    const threadId = requireString(value, 'threadId')
    const connection = host.getConnection(threadId)
    const snapshot = host.tasks.snapshot(threadId)
    return { ...snapshot, tasks: snapshot.tasks.filter((task) => connection && sameFilePath(task.workspacePath, connection.workspacePath)) }
  })

  ipc.handle('threadTasks:answer-offer', async (_event, params: unknown): Promise<ThreadTaskActionResult> => {
    const { threadId, taskId } = readTaskRef(params)
    const answer = readOfferAnswer((params as { answer?: unknown }).answer)
    const connection = host.getConnection(threadId)
    if (!connection) return { ok: false, error: NOT_CONNECTED }
    return host.tasks.answerOffer(connection, threadId, taskId, answer)
  })

  ipc.handle('threadTasks:start', async (_event, params: unknown): Promise<ThreadTaskStartResult[]> => {
    const threadId = requireString((params as { threadId?: unknown } | undefined)?.threadId, 'threadId')
    const rawIds = (params as { taskIds?: unknown }).taskIds
    const taskIds = Array.isArray(rawIds) ? rawIds.map((id) => requireString(id, 'taskIds')) : []
    const options = readStartOptions((params as { options?: unknown }).options)
    const connection = host.getConnection(threadId)
    if (!connection) return taskIds.map((taskId) => ({ taskId, ok: false, error: NOT_CONNECTED }))
    return host.tasks.startProposals(connection, threadId, taskIds, options)
  })

  ipc.handle('threadTasks:dismiss', (_event, params: unknown): ThreadTaskActionResult => {
    const { threadId, taskId } = readTaskRef(params)
    const connection = host.getConnection(threadId)
    const task = host.tasks.snapshot(threadId).tasks.find((candidate) => candidate.id === taskId)
    if (!connection || !task || !sameFilePath(task.workspacePath, connection.workspacePath)) return { ok: false, error: NOT_CONNECTED }
    const outcome = host.tasks.dismiss(threadId, taskId)
    return outcome === 'dismissed' ? { ok: true } : { ok: false, error: `This task cannot be dismissed (${outcome}).` }
  })

  ipc.handle('threadTasks:mark-kept-here', (_event, params: unknown): ThreadTaskActionResult => {
    const { threadId, taskId } = readTaskRef(params)
    const connection = host.getConnection(threadId)
    const task = host.tasks.snapshot(threadId).tasks.find((candidate) => candidate.id === taskId)
    if (!connection || !task || !sameFilePath(task.workspacePath, connection.workspacePath)) return { ok: false, error: NOT_CONNECTED }
    return host.tasks.markKeptHere(threadId, taskId)
  })
}

function readTaskRef(params: unknown): { threadId: string; taskId: string } {
  const record = isRecord(params) ? params : {}
  return {
    threadId: requireString(record.threadId, 'threadId'),
    taskId: requireString(record.taskId, 'taskId')
  }
}

function readOfferAnswer(value: unknown): ThreadOfferAnswer {
  if (isRecord(value) && value.choice === 'here') return { choice: 'here' }
  if (isRecord(value) && value.choice === 'newThread') {
    const options = readStartOptions(value.options)
    return { choice: 'newThread', ...(options ? { options } : {}) }
  }
  throw new Error('answer.choice must be "newThread" or "here".')
}

function readStartOptions(value: unknown): ThreadTaskStartOptions | undefined {
  return isRecord(value) ? value as ThreadTaskStartOptions : undefined
}

function requireString(value: unknown, name: string): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${name} must be a non-empty string.`)
  return value.trim()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}
