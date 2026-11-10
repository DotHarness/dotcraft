export type WorkspaceConnectionRole = 'foreground' | 'secondary'

export interface DestroyableWindow {
  isDestroyed(): boolean
}

export interface WorkspaceConnectionRoutingState<TClient, TWindow extends DestroyableWindow> {
  appQuitting: boolean
  mainWindow: TWindow | null
  window?: TWindow | null
  wireClient: TClient | null
  client: TClient
  role: WorkspaceConnectionRole
}

export function getForegroundWorkspaceClient<TClient, TWindow extends DestroyableWindow>(state: {
  appQuitting: boolean
  mainWindow: TWindow | null
  wireClient: TClient | null
  connection: { client: TClient; role: WorkspaceConnectionRole } | undefined
}): TClient | null {
  const { connection } = state
  return connection && isCurrentForegroundWorkspaceConnection({ ...state, ...connection })
    ? connection.client
    : null
}

export const SECONDARY_THREAD_NOTIFICATION_METHODS = new Set([
  'thread/started',
  'thread/renamed',
  'thread/deleted',
  'thread/archived',
  'thread/unarchived',
  'thread/status/changed',
  'thread/readState/changed'
])

export function isCurrentForegroundWorkspaceConnection<TClient, TWindow extends DestroyableWindow>(
  state: WorkspaceConnectionRoutingState<TClient, TWindow>
): boolean {
  const win = state.window ?? state.mainWindow
  return (
    !state.appQuitting &&
    state.role === 'foreground' &&
    state.wireClient === state.client &&
    state.mainWindow === win &&
    win != null &&
    !win.isDestroyed()
  )
}

export function getWorkspaceNotificationForeground<TClient extends { hasWarmThread(threadId: string): boolean }, TWindow extends DestroyableWindow>(
  method: string,
  state: WorkspaceConnectionRoutingState<TClient, TWindow>,
  params?: unknown
): boolean | null {
  if (state.role === 'secondary') {
    if (!canBridgeRendererInteractiveServerRequest(state)) return null
    if (SECONDARY_THREAD_NOTIFICATION_METHODS.has(method)) return false
    const threadId = readNotificationThreadId(method, params)
    return threadId && isThreadNotification(method) && state.client.hasWarmThread(threadId) ? false : null
  }

  return isCurrentForegroundWorkspaceConnection(state) ? true : null
}

function isThreadNotification(method: string): boolean {
  return ['thread/', 'turn/', 'item/', 'terminal/', 'plan/', 'subagent/'].some(prefix => method.startsWith(prefix))
    || method === 'system/event' || method === 'system/jobResult'
    || method === 'mcpServer/startupStatus/updated'
}

function readNotificationThreadId(method: string, params: unknown): string | undefined {
  if (!params || typeof params !== 'object') return undefined
  const p = params as Record<string, unknown>
  const thread = (p.thread ?? p.turn ?? p.terminal) as { id?: string; threadId?: string } | undefined
  const id = p.threadId ?? p.parentThreadId ?? p.rootThreadId
    ?? (method.startsWith('thread/') ? thread?.id : thread?.threadId)
  return typeof id === 'string' ? id : undefined
}

export function shouldBridgeWorkspaceServerRequest<TClient, TWindow extends DestroyableWindow>(
  state: WorkspaceConnectionRoutingState<TClient, TWindow>
): boolean {
  return isCurrentForegroundWorkspaceConnection(state)
}

export const RENDERER_INTERACTIVE_SERVER_REQUEST_METHODS = new Set([
  'item/approval/request',
  'item/tool/requestUserInput',
  'mcpServer/elicitation/request'
])

export function isRendererInteractiveServerRequest(method: string): boolean {
  return RENDERER_INTERACTIVE_SERVER_REQUEST_METHODS.has(method)
}

export function canBridgeRendererInteractiveServerRequest<TClient, TWindow extends DestroyableWindow>(
  state: WorkspaceConnectionRoutingState<TClient, TWindow>
): boolean {
  const win = state.window ?? state.mainWindow
  return !state.appQuitting && win != null && state.mainWindow === win && !win.isDestroyed()
}
