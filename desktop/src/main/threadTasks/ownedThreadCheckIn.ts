import type { Notification } from 'electron'
import {
  PROJECT_CONNECTED_CHANNEL,
  type BackgroundCheckInRequest,
  type BackgroundCheckInResult,
  type OwnedThreadRecord,
  type RecentFinishedThreadDigest,
  type RunningThreadDigest,
  type RunningThreadsSnapshot
} from '../../shared/ownedThreads'
import { createDesktopNotification } from '../desktopNotification'
import { sendDesktopAppServerRequest, threadTaskConnection } from '../desktopRuntimeThreadTools'
import { notificationThreadActivation } from '../windows/windowNotifications'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { listThreadProjects, openThreadProject } from '../windows/windowThreadProjects'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'
import { classifyActiveThread, type ActiveThreadSummaryWire } from './activeThreads'
import type { DesktopOwnedThreads } from './ownedThreads'

interface ThreadSummaryWithReadStateWire extends ActiveThreadSummaryWire {
  readState?: { firstUnread?: unknown } | null
}

interface OwnedThreadNotificationState {
  silentTokensByTurnId: Map<string, string>
  titleByThreadId: Map<string, string>
  activeNotifications: Map<string, Notification>
}

const states = new WeakMap<DesktopWindowRuntime, OwnedThreadNotificationState>()

function stateFor(ctx: DesktopWindowRuntime): OwnedThreadNotificationState {
  let state = states.get(ctx)
  if (!state) {
    state = {
      silentTokensByTurnId: new Map(),
      titleByThreadId: new Map(),
      activeNotifications: new Map()
    }
    states.set(ctx, state)
  }
  return state
}

export function isSilentReplyToken(text: string | null | undefined, token: string): boolean {
  const trimmed = text?.trim()
  if (!trimmed) return true
  const unwrapped = trimmed.replace(/^`+/, '').replace(/`+$/, '').trim()
  return unwrapped === token
}

export function formatOwnedNotificationPreview(text: string): string {
  const collapsed = text
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/[#>*_`]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
  return collapsed.length > 160 ? `${collapsed.slice(0, 159)}…` : collapsed
}

export function emitProjectConnected(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed() || ctx.runtimeIsStopping()) return
  win.webContents.send(PROJECT_CONNECTED_CHANNEL, {
    id: entry.projectId,
    workspacePath: entry.workspacePath
  })
}

export function attachOwnedThreadNotificationCleanup(ctx: DesktopWindowRuntime): () => void {
  return () => {
    const state = states.get(ctx)
    if (!state) return
    for (const notification of state.activeNotifications.values()) notification.close()
    state.activeNotifications.clear()
    state.silentTokensByTurnId.clear()
    state.titleByThreadId.clear()
  }
}

export function clearOwnedThreadCheckInState(ctx: DesktopWindowRuntime, threadId: string): void {
  const state = states.get(ctx)
  if (!state) return
  state.titleByThreadId.delete(threadId)
  dismissOwnedNotification(state, threadId)
}

export async function collectRunningThreadsSnapshot(
  ctx: DesktopWindowRuntime,
  options?: { excludeThreadId?: string; finishedSinceMs?: number }
): Promise<RunningThreadsSnapshot> {
  const projects = (await listThreadProjects(ctx)).filter((project) => project.running)
  const activeThreads: RunningThreadDigest[] = []
  const recentFinished: RecentFinishedThreadDigest[] = []
  const excludeThreadId = options?.excludeThreadId
  const finishedSinceMs = options?.finishedSinceMs

  await Promise.allSettled(
    projects.map(async (project) => {
      const entry = ctx.workspaceConnections.get(project.id)
      if (!entry?.connected) return
      const connection = threadTaskConnection(entry.client, entry.workspacePath, ctx.desktopThreadToolOptions(entry))
      const result = await connection.request<{ data?: ThreadSummaryWithReadStateWire[] }>('thread/list', {
        status: 'active',
        limit: 50
      })
      for (const thread of result.data ?? []) {
        if (!thread.id || (excludeThreadId && thread.id === excludeThreadId)) continue
        const classified = classifyActiveThread(thread)
        if (classified) {
          activeThreads.push({
            id: thread.id,
            projectId: project.id,
            activeState: classified.activeState,
            waitingFor: classified.waitingFor
          })
          continue
        }
        if (finishedSinceMs != null && thread.readState?.firstUnread != null && typeof thread.lastActiveAt === 'string') {
          const updatedMs = Date.parse(thread.lastActiveAt)
          if (Number.isFinite(updatedMs) && updatedMs >= finishedSinceMs) {
            recentFinished.push({
              id: thread.id,
              projectId: project.id,
              lastActiveAt: thread.lastActiveAt
            })
          }
        }
      }
    })
  )

  return { activeThreads, recentFinished }
}

export async function startOwnedThreadBackgroundCheckIn(
  ctx: DesktopWindowRuntime,
  ownedThreads: DesktopOwnedThreads,
  request: BackgroundCheckInRequest
): Promise<BackgroundCheckInResult> {
  if (ctx.runtimeIsStopping()) return { started: false }
  const record = ownedThreads.findByThreadId(request.threadId)
  if (!record || record.paused) return { started: false }

  try {
    const entry = await openThreadProject(ctx, {
      id: ctx.localProjectId(record.workspacePath),
      name: 'Chats',
      path: record.workspacePath
    })
    const threadRead = await entry.client.sendRequest<{
      thread?: { status?: string; runtime?: { running?: boolean; activeTurnId?: string | null } }
    }>('thread/read', { threadId: record.threadId })
    if (!threadRead.thread || threadRead.thread.status === 'archived') return { started: false }
    if (threadRead.thread.runtime?.running || threadRead.thread.runtime?.activeTurnId) return { started: false }

    if (request.requireExistingTurns !== false) {
      const recentTurns = await entry.client.sendRequest<{ data?: Array<{ id?: string }> }>('thread/turns/list', {
        threadId: record.threadId,
        itemsView: 'summary',
        sortDirection: 'descending',
        limit: 1
      })
      if (!recentTurns.data?.length) return { started: false }
    }

    const started = await sendDesktopAppServerRequest<{ turn?: { id?: string } }>(
      entry.client,
      'turn/startOrEnqueue',
      {
        threadId: record.threadId,
        kickoff: true,
        input: [{ type: 'text', text: request.prompt }]
      },
      undefined,
      ctx.desktopThreadToolOptions(entry)
    )
    const state = stateFor(ctx)
    if (request.notificationTitle) state.titleByThreadId.set(record.threadId, request.notificationTitle)
    if (started.turn?.id && request.silentReplyToken) {
      state.silentTokensByTurnId.set(started.turn.id, request.silentReplyToken)
    }
    return { started: true, turnId: started.turn?.id }
  } catch {
    return { started: false }
  }
}

export function observeOwnedThreadNotification(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): void {
  if (!params || typeof params !== 'object') return
  const payload = params as Record<string, unknown>
  const threadId = typeof payload.threadId === 'string' ? payload.threadId : ''
  if (!threadId) return
  const record = ctx.app?.ownedThreads?.findByThreadId(threadId)
  if (!record) return

  const state = stateFor(ctx)

  if (method === 'thread/readState/changed') {
    const readState = payload.readState as { firstUnread?: unknown } | undefined
    if (readState && readState.firstUnread == null) {
      dismissOwnedNotification(state, threadId)
    }
    return
  }

  if (method !== 'turn/completed') return
  const turn = payload.turn as { id?: string; status?: string } | undefined
  const turnId = typeof turn?.id === 'string' ? turn.id : (typeof payload.turnId === 'string' ? payload.turnId : '')
  const silentToken = turnId ? state.silentTokensByTurnId.get(turnId) : undefined
  if (turnId) state.silentTokensByTurnId.delete(turnId)

  void handleOwnedTurnCompleted(ctx, entry, record, state, turnId, silentToken).catch(() => {})
}

async function handleOwnedTurnCompleted(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  record: OwnedThreadRecord,
  state: OwnedThreadNotificationState,
  turnId: string,
  silentToken: string | undefined
): Promise<void> {
  const page = await entry.client.sendRequest<{
    data?: Array<{
      id?: string
      status?: string
      items?: Array<{ type?: string; payload?: { text?: string; phase?: string; deliveryMode?: string } }>
    }>
  }>('thread/turns/list', {
    threadId: record.threadId,
    itemsView: 'summary',
    sortDirection: 'descending',
    limit: 1
  })
  const latest = page.data?.[0]
  if (!latest || (turnId && latest.id && latest.id !== turnId)) return

  const reply = latest.items
    ?.findLast(
      (item) => item.type === 'agentMessage' && item.payload?.phase === 'final_answer' && item.payload?.deliveryMode !== 'async' && item.payload?.text
    )
    ?.payload?.text

  if (silentToken && isSilentReplyToken(reply, silentToken)) {
    const threadRead = await entry.client.sendRequest<{
      thread?: { readState?: { revision?: string; firstUnread?: unknown } }
    }>('thread/read', { threadId: record.threadId })
    const revision = threadRead.thread?.readState?.revision
    if (revision && threadRead.thread?.readState?.firstUnread != null) {
      await entry.client.sendRequest('thread/readState/update', {
        threadId: record.threadId,
        expectedRevision: revision,
        operation: { type: 'read' }
      })
    }
    return
  }

  if (!reply?.trim()) return
  const win = ctx.mainWindow
  if (!win || win.isDestroyed() || win.isFocused()) return
  if (ctx.settings.notifications?.questions === false && ctx.settings.notifications?.approvalRequests === false) return
  const activation = notificationThreadActivation(ctx, entry, record.threadId)
  const notification = createDesktopNotification({
    title: state.titleByThreadId.get(record.threadId) || record.title,
    body: formatOwnedNotificationPreview(reply)
  }, {
    ...activation,
    open: () => {
      dismissOwnedNotification(state, record.threadId)
      activation.open()
    }
  })
  if (!notification) return

  dismissOwnedNotification(state, record.threadId)
  notification.on('close', () => {
    if (state.activeNotifications.get(record.threadId) === notification) {
      state.activeNotifications.delete(record.threadId)
    }
  })
  state.activeNotifications.set(record.threadId, notification)
  notification.show()
}

function dismissOwnedNotification(state: OwnedThreadNotificationState, threadId: string): void {
  const existing = state.activeNotifications.get(threadId)
  if (!existing) return
  state.activeNotifications.delete(threadId)
  existing.close()
}
