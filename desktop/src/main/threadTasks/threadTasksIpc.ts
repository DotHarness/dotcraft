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
import { callingThread } from './threadProjects'
import type { LinkedChatSummary } from '../../shared/linkedChats'
import type {
  BackgroundCheckInRequest,
  BackgroundCheckInResult,
  OwnedThreadRecord,
  OwnedThreadRequest,
  RunningThreadsSnapshot
} from '../../shared/ownedThreads'
import type { DesktopLinkedChats } from './linkedChats'
import type { DesktopOwnedThreads } from './ownedThreads'
import { DesktopThreadTasks } from './threadTaskService'
import { ThreadTaskStore } from './threadTaskStore'

const NOT_CONNECTED = 'DotCraft is not connected.'

export interface DesktopThreadTasksRuntimeOptions {
  filePath: string
  linkedChats: DesktopLinkedChats
  getNewTaskPrompt(): NewTaskPromptMode
  notifyOffer(task: ThreadTask, ownerId?: number): void
  notifyWrapUpQuestion(threadId: string, turnId: string | undefined, callId: string, ownerId?: number): void
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
    linkedChats: options.linkedChats,
    getNewTaskPrompt: options.getNewTaskPrompt,
    notifyOffer: options.notifyOffer,
    notifyWrapUpQuestion: options.notifyWrapUpQuestion
  })
}

export interface ThreadTasksIpcHost {
  tasks: DesktopThreadTasks
  ownedThreads: DesktopOwnedThreads
  getConnection(threadId?: string): ThreadTaskConnection | null
  ensureOwnedThread(request: OwnedThreadRequest): Promise<OwnedThreadRecord>
  setOwnedThreadPaused?(threadId: string, paused: boolean): OwnedThreadRecord | null
  archiveOwnedThread?(threadId: string): Promise<boolean>
  listRunningThreads?(options?: { excludeThreadId?: string; finishedSinceMs?: number }): Promise<RunningThreadsSnapshot>
  startOwnedCheckIn?(request: BackgroundCheckInRequest): Promise<BackgroundCheckInResult>
}

export function registerThreadTasksIpc(host: ThreadTasksIpcHost, ipc: WindowIpcMain): void {
  ipc.handle('threadTasks:list', async (_event, value: unknown) => {
    const threadId = requireString(value, 'threadId')
    const connection = host.getConnection(threadId)
    if (connection) await host.tasks.reconcileQuestion(connection, threadId)
    const snapshot = host.tasks.snapshot(threadId)
    return { ...snapshot, tasks: snapshot.tasks.filter((task) => connection && sameFilePath(task.workspacePath, connection.workspacePath)) }
  })

  ipc.handle('threadTasks:linked-project', (_event, params: unknown): string | null => {
    const { threadId, targetThreadId } = isRecord(params) ? params : {}
    if (typeof threadId !== 'string' || typeof targetThreadId !== 'string') return null
    const connection = host.getConnection(threadId)
    return connection
      ? host.tasks.linkedChats?.linkedProjectBetween(callingThread(connection, threadId), targetThreadId) ?? null
      : null
  })

  ipc.handle('threadTasks:linked-chats', (_event, value: unknown): LinkedChatSummary[] => {
    const threadId = requireString(value, 'threadId')
    return (host.tasks.linkedChats?.listForThread(threadId) ?? []).map(({ child, status, waitingFor, followed, createdAt }) => ({
      threadId: child.threadId,
      projectId: child.projectId,
      projectName: child.projectName,
      workspacePath: child.workspacePath,
      title: child.title,
      status,
      ...(waitingFor ? { waitingFor } : {}),
      ...(followed ? { followed } : {}),
      createdAt
    }))
  })

  ipc.handle('threadTasks:unfollow-linked-chat', (_event, params: unknown): ThreadTaskActionResult => {
    const record = isRecord(params) ? params : {}
    const threadId = requireString(record.threadId, 'threadId')
    const childThreadId = requireString(record.childThreadId, 'childThreadId')
    const projectId = requireString(record.projectId, 'projectId')
    const connection = host.getConnection(threadId)
    const linkedChats = host.tasks.linkedChats
    if (!connection || !linkedChats) return { ok: false, error: NOT_CONNECTED }
    const link = linkedChats.find(callingThread(connection, threadId), childThreadId, projectId)
    if (!link) return { ok: true }
    return linkedChats.unfollow(link) ? { ok: true } : { ok: false, error: 'Only followed chats can be unfollowed.' }
  })

  ipc.handle('threadTasks:owned-list', (): OwnedThreadRecord[] => host.ownedThreads.list())

  ipc.handle('threadTasks:owned-ensure', (_event, value: unknown): Promise<OwnedThreadRecord> => {
    return host.ensureOwnedThread(readOwnedThreadRequest(value))
  })

  ipc.handle('threadTasks:owned-set-paused', (_event, params: unknown): OwnedThreadRecord | null => {
    const record = isRecord(params) ? params : {}
    const threadId = requireString(record.threadId, 'threadId')
    if (typeof record.paused !== 'boolean') throw new Error('paused must be a boolean.')
    return host.setOwnedThreadPaused?.(threadId, record.paused) ?? null
  })

  ipc.handle('threadTasks:owned-archive', async (_event, value: unknown): Promise<boolean> => {
    const threadId = requireString(value, 'threadId')
    return (await host.archiveOwnedThread?.(threadId)) ?? false
  })

  ipc.handle('threadTasks:running-threads', async (_event, params: unknown): Promise<RunningThreadsSnapshot> => {
    const record = isRecord(params) ? params : {}
    const excludeThreadId = typeof record.excludeThreadId === 'string' ? record.excludeThreadId : undefined
    const finishedSinceMs = typeof record.finishedSinceMs === 'number' && Number.isFinite(record.finishedSinceMs)
      ? record.finishedSinceMs
      : undefined
    return (await host.listRunningThreads?.({ excludeThreadId, finishedSinceMs })) ?? { activeThreads: [], recentFinished: [] }
  })

  ipc.handle('threadTasks:owned-check-in', async (_event, params: unknown): Promise<BackgroundCheckInResult> => {
    const record = isRecord(params) ? params : {}
    const threadId = requireString(record.threadId, 'threadId')
    const prompt = requireString(record.prompt, 'prompt')
    const requireExistingTurns = typeof record.requireExistingTurns === 'boolean' ? record.requireExistingTurns : undefined
    const silentReplyToken = typeof record.silentReplyToken === 'string' && record.silentReplyToken.trim()
      ? record.silentReplyToken.trim()
      : undefined
    const notificationTitle = typeof record.notificationTitle === 'string' && record.notificationTitle.trim()
      ? record.notificationTitle.trim()
      : undefined
    return (await host.startOwnedCheckIn?.({
      threadId,
      prompt,
      requireExistingTurns,
      silentReplyToken,
      notificationTitle
    })) ?? { started: false }
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

  ipc.handle('threadTasks:dismiss-question', (_event, params: unknown): ThreadTaskActionResult => {
    const record = isRecord(params) ? params : {}
    const threadId = requireString(record.threadId, 'threadId')
    const callId = requireString(record.callId, 'callId')
    if (!host.getConnection(threadId)) return { ok: false, error: NOT_CONNECTED }
    host.tasks.dismissQuestion(threadId, callId)
    return { ok: true }
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

function readOwnedThreadRequest(value: unknown): OwnedThreadRequest {
  const record = isRecord(value) ? value : {}
  const config = isRecord(record.config) ? record.config : {}
  const approvalPolicy = config.approvalPolicy === 'prompt' || config.approvalPolicy === 'autoApprove' ? config.approvalPolicy : undefined
  return {
    pluginId: requireString(record.pluginId, 'pluginId'),
    key: requireString(record.key, 'key'),
    ...(typeof record.space === 'string' && record.space.trim() ? { space: record.space.trim() } : {}),
    title: requireString(record.title, 'title'),
    config: {
      developerInstructions: typeof config.developerInstructions === 'string' ? config.developerInstructions : '',
      ...(approvalPolicy ? { approvalPolicy } : {})
    }
  }
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
