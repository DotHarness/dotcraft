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

export const SECONDARY_THREAD_NOTIFICATION_METHODS = new Set([
  'thread/started',
  'thread/renamed',
  'thread/deleted',
  'thread/statusChanged',
  'thread/runtimeChanged'
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

export function getWorkspaceNotificationForeground<TClient, TWindow extends DestroyableWindow>(
  method: string,
  state: WorkspaceConnectionRoutingState<TClient, TWindow>
): boolean | null {
  if (state.role === 'secondary') {
    return SECONDARY_THREAD_NOTIFICATION_METHODS.has(method) ? false : null
  }

  return isCurrentForegroundWorkspaceConnection(state) ? true : null
}

export function shouldBridgeWorkspaceServerRequest<TClient, TWindow extends DestroyableWindow>(
  state: WorkspaceConnectionRoutingState<TClient, TWindow>
): boolean {
  return isCurrentForegroundWorkspaceConnection(state)
}

export const RENDERER_INTERACTIVE_SERVER_REQUEST_METHODS = new Set([
  'item/approval/request',
  'item/tool/requestUserInput'
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
