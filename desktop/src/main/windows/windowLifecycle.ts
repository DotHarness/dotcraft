import type { BrowserWindow } from 'electron'
import { getEnabledEmbeddedModuleChannelNames } from '../../shared/channelModulePersistence'
import { sameWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { DesktopAppServerClient } from '../DesktopAppServerClient'
import { restoreDesktopPet } from '../desktopPet'
import {
  getChannelModuleManager,
  registerIpcHandlers,
  restoreModuleTrackingByChannelName,
  unregisterIpcHandlers
} from '../ipcBridge'
import { nodeReplManager } from '../nodeReplManager'
import { viewerBrowserManager } from '../viewerBrowser'
import type { DesktopWindowRuntime } from './windowRuntime'

export function registerDesktopIpcHandlers(
  ctx: DesktopWindowRuntime,
  workspacePath: string,
  getWireClient: () => DesktopAppServerClient | null
): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return
  registerIpcHandlers(getWireClient, workspacePath, ctx.buildCallbacks(), win)
}

export async function restoreEnabledModuleTracking(ctx: DesktopWindowRuntime): Promise<void> {
  const client = ctx.wireClient
  const win = ctx.mainWindow
  if (!client || !win) return
  try {
    const response = await client.sendRequest<{
      channels?: Array<{
        name?: string
        enabled?: boolean
        transport?: string | null
        builtinModule?: string | null
      }>
    }>('externalChannel/list', {})
    await restoreModuleTrackingByChannelName(
      getEnabledEmbeddedModuleChannelNames(response.channels ?? []),
      win
    )
  } catch (error) {
    console.warn('[desktop] failed to restore channel module tracking', error)
  }
}

export async function teardownRuntime(
  ctx: DesktopWindowRuntime,
  reason: string,
  options?: { clearWorkspace?: boolean; cleanupIpcHandlers?: boolean }
): Promise<void> {
  ctx.startupProjectRestore = undefined
  const win = ctx.mainWindow
  if (win) viewerBrowserManager.destroyAllTabs(win)
  if (win) ctx.getThreadTasks().abandonOffersForOwner(win.webContents.id)
  if (options?.cleanupIpcHandlers) {
    if (win) unregisterIpcHandlers(win)
    ctx.sshMachinesManager?.dispose()
    ctx.sshMachinesManager = null
  }
  if (win) await nodeReplManager.disposeOwner(win.webContents.id)
  if (win) getChannelModuleManager(win)?.dispose()
  ctx.hubEventAbortController?.abort()
  ctx.hubEventAbortController = null
  if (ctx.secondaryRefreshTimer) clearTimeout(ctx.secondaryRefreshTimer)
  ctx.secondaryRefreshTimer = null
  ctx.startupProjectStates.clear()
  ctx.clearPendingDeviceOpenRetry()
  ctx.deviceOpenGeneration += 1
  ctx.resetThreadToolState()
  for (const entry of [...ctx.workspaceConnections.values()]) {
    ctx.disposeWorkspaceConnection(entry)
  }
  ctx.userInputAutoResolution.clear()
  ctx.wireClient?.dispose()
  ctx.wireClient = null
  ctx.setActiveRemoteProject(null)
  ctx.previousLocalForegroundWorkspacePath = null
  ctx.lastAppServerWsUrl = null
  ctx.prewarmedAppServer = null
  if (options?.clearWorkspace) ctx.currentWorkspacePath = ''
  console.info('[desktop] window runtime stopped', { reason, windowId: win?.id })
}

export function showWindowSafely(_ctx: DesktopWindowRuntime, win: BrowserWindow): void {
  if (win.isDestroyed() || restoreDesktopPet(win)) return
  if (win.isMinimized()) win.restore()
  if (!win.isVisible()) win.show()
  win.focus()
}

export function isSameWorkspacePath(_ctx: DesktopWindowRuntime, left: string, right: string): boolean {
  return sameWorkspaceProjectKey(left, right)
}
