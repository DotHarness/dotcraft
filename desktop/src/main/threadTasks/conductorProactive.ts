import { Notification } from 'electron'
import { DEFAULT_LOCALE, normalizeLocale } from '../../shared/locales'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { sendDesktopAppServerRequest, threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { listThreadProjects, openThreadProject } from '../windows/windowThreadProjects'
import { openSourceThread } from '../windows/windowRequests'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'
import { classifyActiveThread, type ActiveThreadSummaryWire } from './activeThreads'
import type { LinkedChat } from './linkedChatStore'

export const CONDUCTOR_PLUGIN_ID = 'oratorio'
export const CONDUCTOR_KEY = 'conductor'
export const AWAY_THRESHOLD_MS = 30 * 60 * 1000
export const CHECKIN_COOLDOWN_MS = 15 * 60 * 1000
const STARTUP_CHECKIN_DELAY_MS = 4_000

export interface ProactiveSignal {
  noteworthy: boolean
  fingerprint: string
}

export interface ActiveThreadDigest {
  id: string
  projectId: string
  activeState: 'needsUser' | 'working'
  waitingFor?: string
}

export function isSilentProactiveReply(text: string | null | undefined): boolean {
  const trimmed = text?.trim()
  if (!trimmed) return true
  const unwrapped = trimmed.replace(/^`+/, '').replace(/`+$/, '').trim()
  return unwrapped === 'DONT_NOTIFY'
}

export function buildProactiveSignal(
  activeThreads: readonly ActiveThreadDigest[],
  linkedChats: readonly LinkedChat[],
  conductorThreadId: string
): ProactiveSignal {
  const linkedIds = new Set(linkedChats.map((link) => link.child.threadId))
  const relevantActive = activeThreads
    .filter((thread) => thread.id !== conductorThreadId)
    .sort((a, b) => `${a.projectId}:${a.id}`.localeCompare(`${b.projectId}:${b.id}`))
  const sortedLinked = [...linkedChats].sort((a, b) =>
    `${a.child.projectId}:${a.child.threadId}`.localeCompare(`${b.child.projectId}:${b.child.threadId}`)
  )

  const hasUnfollowedActive = relevantActive.some((thread) => !linkedIds.has(thread.id))
  const hasWaiting = relevantActive.some((thread) => thread.activeState === 'needsUser')
    || sortedLinked.some((link) => link.status === 'waitingForInput')
  const hasTerminalLinked = sortedLinked.some((link) => link.status === 'completed' || link.status === 'failed')

  const fingerprint = JSON.stringify({
    active: relevantActive.map((thread) => [thread.projectId, thread.id, thread.activeState, thread.waitingFor ?? '', linkedIds.has(thread.id)]),
    linked: sortedLinked.map((link) => [link.child.projectId, link.child.threadId, link.status, link.waitingFor ?? '', link.createdAt])
  })

  return {
    noteworthy: (relevantActive.length > 0 || sortedLinked.length > 0) && (hasUnfollowedActive || hasWaiting || hasTerminalLinked),
    fingerprint
  }
}

export function buildProactiveCheckInPrompt(locale: string, reason: 'startup' | 'return'): string {
  const arrival = reason === 'startup'
    ? 'The person opened DotCraft'
    : 'The person returned to DotCraft after being away'
  return [
    `${arrival} (interface locale: ${locale}).`,
    'Check active chats across running projects with ListThreads (activeOnly: true) and follow any unlinked active chats with FollowThread.',
    'Read active or newly completed chats with ReadThread as needed to verify their status.',
    `If there is something new worth telling the person, reply concisely in ${locale} in 1-3 lines, listing chats waiting on the person first and summarizing completed, failed, or running updates.`,
    'If nothing new requires the person’s attention or warrants interrupting them, reply with exactly DONT_NOTIFY.'
  ].join(' ')
}

export function formatNotificationPreview(text: string): string {
  const collapsed = text
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/[#>*_`]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
  return collapsed.length > 160 ? `${collapsed.slice(0, 159)}…` : collapsed
}

interface ConductorRuntimeState {
  blurredAtMs: number | null
  lastCheckInAtMs: number
  lastFingerprint: string | null
  checkingIn: boolean
  proactiveTurnIds: Set<string>
  activeNotifications: Map<string, Notification>
  startupTimer: ReturnType<typeof setTimeout> | null
}

const states = new WeakMap<DesktopWindowRuntime, ConductorRuntimeState>()

function stateFor(ctx: DesktopWindowRuntime): ConductorRuntimeState {
  let state = states.get(ctx)
  if (!state) {
    state = {
      blurredAtMs: null,
      lastCheckInAtMs: 0,
      lastFingerprint: null,
      checkingIn: false,
      proactiveTurnIds: new Set(),
      activeNotifications: new Map(),
      startupTimer: null
    }
    states.set(ctx, state)
  }
  return state
}

function findConductorRecord(ctx: DesktopWindowRuntime): OwnedThreadRecord | undefined {
  return ctx.app?.ownedThreads?.list().find(
    (record) => record.pluginId === CONDUCTOR_PLUGIN_ID && record.key === CONDUCTOR_KEY
  )
}

export function attachConductorProactiveLifecycle(ctx: DesktopWindowRuntime): () => void {
  const state = stateFor(ctx)
  const win = ctx.mainWindow
  if (!win) return () => {}

  const onBlur = (): void => {
    state.blurredAtMs = Date.now()
  }
  const onFocus = (): void => {
    const awayMs = state.blurredAtMs == null ? 0 : Date.now() - state.blurredAtMs
    state.blurredAtMs = null
    if (awayMs >= AWAY_THRESHOLD_MS) {
      void runConductorProactiveCheckIn(ctx, 'return').catch(() => {})
    }
  }
  const onLoad = (): void => {
    if (state.startupTimer) clearTimeout(state.startupTimer)
    state.startupTimer = setTimeout(() => {
      state.startupTimer = null
      void runConductorProactiveCheckIn(ctx, 'startup').catch(() => {})
    }, STARTUP_CHECKIN_DELAY_MS)
  }

  win.on('blur', onBlur)
  win.on('focus', onFocus)
  win.webContents.once('did-finish-load', onLoad)

  return () => {
    if (state.startupTimer) clearTimeout(state.startupTimer)
    state.startupTimer = null
    for (const notification of state.activeNotifications.values()) notification.close()
    state.activeNotifications.clear()
    if (!win.isDestroyed()) {
      win.off('blur', onBlur)
      win.off('focus', onFocus)
    }
  }
}

export async function runConductorProactiveCheckIn(
  ctx: DesktopWindowRuntime,
  reason: 'startup' | 'return'
): Promise<boolean> {
  const state = stateFor(ctx)
  if (state.checkingIn || ctx.runtimeIsStopping()) return false
  const now = Date.now()
  if (state.lastCheckInAtMs > 0 && now - state.lastCheckInAtMs < CHECKIN_COOLDOWN_MS) return false

  const conductor = findConductorRecord(ctx)
  if (!conductor) return false

  state.checkingIn = true
  try {
    const linkedChats = ctx.app.tasks.linkedChats?.listForThread(conductor.threadId) ?? []
    const activeThreads = await collectRunningActiveThreads(ctx)
    const signal = buildProactiveSignal(activeThreads, linkedChats, conductor.threadId)
    if (!signal.noteworthy || signal.fingerprint === state.lastFingerprint) return false

    const chatPath = resolveDefaultChatWorkspacePath()
    const chatEntry = await openThreadProject(ctx, { id: ctx.localProjectId(chatPath), name: 'Chats', path: chatPath })
    const threadRead = await chatEntry.client.sendRequest<{
      thread?: { status?: string; runtime?: { running?: boolean; activeTurnId?: string | null } }
    }>('thread/read', { threadId: conductor.threadId })
    if (!threadRead.thread || threadRead.thread.status === 'archived') return false
    if (threadRead.thread.runtime?.running || threadRead.thread.runtime?.activeTurnId) return false

    const recentTurns = await chatEntry.client.sendRequest<{ data?: Array<{ id?: string }> }>('thread/turns/list', {
      threadId: conductor.threadId,
      itemsView: 'summary',
      sortDirection: 'descending',
      limit: 1
    })
    if (!recentTurns.data?.length) return false

    const locale = normalizeLocale(ctx.settings.locale ?? DEFAULT_LOCALE)
    const started = await sendDesktopAppServerRequest<{ turn?: { id?: string } }>(
      chatEntry.client,
      'turn/startOrEnqueue',
      {
        threadId: conductor.threadId,
        kickoff: true,
        input: [{ type: 'text', text: buildProactiveCheckInPrompt(locale, reason) }]
      },
      undefined,
      ctx.desktopThreadToolOptions(chatEntry)
    )
    state.lastFingerprint = signal.fingerprint
    state.lastCheckInAtMs = Date.now()
    if (started.turn?.id) state.proactiveTurnIds.add(started.turn.id)
    return true
  } catch {
    return false
  } finally {
    state.checkingIn = false
  }
}

async function collectRunningActiveThreads(ctx: DesktopWindowRuntime): Promise<ActiveThreadDigest[]> {
  const projects = (await listThreadProjects(ctx)).filter((project) => project.running)
  const collected: ActiveThreadDigest[] = []
  await Promise.allSettled(
    projects.map(async (project) => {
      const entry = ctx.workspaceConnections.get(project.id)
      if (!entry?.connected) return
      const connection = threadTaskConnection(entry.client, entry.workspacePath, ctx.desktopThreadToolOptions(entry))
      const result = await connection.request<{ data?: ActiveThreadSummaryWire[] }>('thread/list', {
        status: 'active',
        limit: 50
      })
      for (const thread of result.data ?? []) {
        if (!thread.id) continue
        const classified = classifyActiveThread(thread)
        if (!classified) continue
        collected.push({
          id: thread.id,
          projectId: project.id,
          activeState: classified.activeState,
          waitingFor: classified.waitingFor
        })
      }
    })
  )
  return collected
}

export function observeConductorNotification(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): void {
  const conductor = findConductorRecord(ctx)
  if (!conductor || !params || typeof params !== 'object') return
  const payload = params as Record<string, unknown>
  const threadId = typeof payload.threadId === 'string' ? payload.threadId : ''
  if (threadId !== conductor.threadId) return

  const state = stateFor(ctx)

  if (method === 'thread/readState/changed') {
    const readState = payload.readState as { firstUnread?: unknown } | undefined
    if (readState && readState.firstUnread == null) {
      dismissConductorNotification(state, threadId)
    }
    return
  }

  if (method !== 'turn/completed') return
  const turn = payload.turn as { id?: string; status?: string } | undefined
  const turnId = typeof turn?.id === 'string' ? turn.id : (typeof payload.turnId === 'string' ? payload.turnId : '')
  const wasProactive = turnId ? state.proactiveTurnIds.delete(turnId) : false

  void handleConductorTurnCompleted(ctx, entry, conductor, state, turnId, wasProactive).catch(() => {})
}

async function handleConductorTurnCompleted(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  conductor: OwnedThreadRecord,
  state: ConductorRuntimeState,
  turnId: string,
  wasProactive: boolean
): Promise<void> {
  const page = await entry.client.sendRequest<{
    data?: Array<{
      id?: string
      status?: string
      items?: Array<{ type?: string; payload?: { text?: string; phase?: string; deliveryMode?: string } }>
    }>
  }>('thread/turns/list', {
    threadId: conductor.threadId,
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

  if (wasProactive && isSilentProactiveReply(reply)) {
    const threadRead = await entry.client.sendRequest<{
      thread?: { readState?: { revision?: string; firstUnread?: unknown } }
    }>('thread/read', { threadId: conductor.threadId })
    const revision = threadRead.thread?.readState?.revision
    if (revision && threadRead.thread?.readState?.firstUnread != null) {
      await entry.client.sendRequest('thread/readState/update', {
        threadId: conductor.threadId,
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
  if (!Notification.isSupported()) return

  dismissConductorNotification(state, conductor.threadId)
  const notification = new Notification({
    title: conductor.title || 'Oratorio',
    body: formatNotificationPreview(reply)
  })
  notification.on('click', () => {
    dismissConductorNotification(state, conductor.threadId)
    openSourceThread(ctx, entry, win, conductor.threadId)
  })
  notification.on('close', () => {
    if (state.activeNotifications.get(conductor.threadId) === notification) {
      state.activeNotifications.delete(conductor.threadId)
    }
  })
  state.activeNotifications.set(conductor.threadId, notification)
  notification.show()
}

function dismissConductorNotification(state: ConductorRuntimeState, threadId: string): void {
  const existing = state.activeNotifications.get(threadId)
  if (!existing) return
  state.activeNotifications.delete(threadId)
  existing.close()
}
