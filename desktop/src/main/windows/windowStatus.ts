import { BrowserWindow } from 'electron'
import type { ConnectionStatusPayload } from '../../shared/connectionStatus'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { broadcastConnectionStatus, broadcastWorkspaceStatus, sanitizeHttpOrHttpsUrl } from '../ipcBridge'
import { refreshVoiceChatGptAvailability } from '../voice/voiceIpc'
import type { DesktopWindowRuntime } from './windowRuntime'

export function connectWorkspaceForLoadedWindow(
  ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  workspacePath: string
): Promise<void> {
  return ctx.connectToAppServer(workspacePath).then(
    () => undefined,
    (error) => {
      const message = error instanceof Error ? error.message : String(error)
      console.warn('[desktop] failed to connect restored workspace', message)
      if (!win.isDestroyed()) {
        ctx.emitConnectionStatus(win, { status: 'error', errorMessage: message })
      }
    }
  )
}

export function emitConnectionStatus(
  ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  payload: ConnectionStatusPayload
): void {
  if (payload.status === 'connected') {
    const sanitized = sanitizeHttpOrHttpsUrl(payload.dashboardUrl)
    ctx.lastConnectionStatus = {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    }
    ctx.lastDashboardUrl = sanitized
    broadcastConnectionStatus(win, {
      ...payload,
      dashboardUrl: sanitized ?? undefined
    })
  } else {
    ctx.lastConnectionStatus = { ...payload, dashboardUrl: undefined }
    ctx.lastDashboardUrl = null
    broadcastConnectionStatus(win, payload)
  }
  ctx.app.refreshAppMenu()
  refreshVoiceChatGptAvailability(win.webContents.id)
  if (payload.status === 'connected' && ctx.startupProjectRestore === 'pending') {
    void ctx.app.restoreRecentProjects(ctx)
  }
}

export function emitWorkspaceStatus(
  _ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  payload: WorkspaceStatusPayload
): void {
  broadcastWorkspaceStatus(win, payload)
}
