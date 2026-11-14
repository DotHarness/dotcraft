import { BrowserWindow, Notification } from 'electron'
import type { ConnectionStatusPayload } from '../shared/connectionStatus'
import type { AppServerConnectionSource } from '../shared/appServerBoundary'
import type { WorkspaceStatusPayload } from '../shared/workspaceSetup'
import { ChannelModuleManager, type ModuleStatusMap } from './channelModuleManager'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import { unregisterDesktopPluginModuleIpc } from './desktopPluginModuleIpc'
import { getCodexRemoteHubBridge } from './codexRemote/codexRemoteHubBridge'
import { registerCodexRemoteHandlers } from './codexRemote/codexRemoteIpc'
import { getMobileHubBridge } from './mobile/mobileHubBridge'
import { registerMobileHandlers } from './mobile/mobileIpc'
import { clearDesktopPluginModuleRoutes } from './pluginFileProtocol'
import type { QrUpdatePayload } from './qrWatcher'
import { getSatellitesHubBridge } from './satellites/satellitesHubBridge'
import { registerSatellitesHandlers } from './satellites/satellitesIpc'
import { registerScreenViewHandlers } from './screenView/screenViewIpc'
import type { AppSettings } from './settings'
import { resolveTaskCompletionNotificationMode } from './settings'
import { registerPairedComputersHandlers } from './pairedComputers/pairedComputersIpc'
import { registerSshMachinesHandlers } from './sshMachines/sshMachinesIpc'
import { viewerTextWatchManager } from './viewerTextWatch'
import {
  activateFileIndexWorkspace,
  cleanupWorkspaceCache,
  releaseFileIndex,
  warmFileSearchIndex
} from './workspaceComposerIpc'

import { registerAppServerHandlers } from './ipcAppServer'
import type { IpcHandlerCallbacks, WindowIpcContext } from './ipcBridgeContracts'
import { registerChannelModuleHandlers, type WindowChannelModules } from './ipcChannelModules'
import { registerDesktopAppHandlers } from './ipcDesktopApps'
import { registerGitHandlers } from './ipcGit'
import { cancelWindowServerRequests } from './ipcServerRequests'
import { registerViewerBrowserHandlers } from './ipcViewerBrowser'
import { registerThreadPanelStateHandlers } from './threadPanelStateIpc'
import { registerWindowHandlers } from './ipcWindow'
import { registerWorkspaceHandlers } from './ipcWorkspace'
import { registerWorkspaceFileHandlers } from './ipcWorkspaceFiles'
import { createWindowIpcMain, type WindowIpcMain } from './windowIpc'

export { withDesktopRequestIdentity } from './ipcAppServer'
export type { IpcHandlerCallbacks, ResolvedBinaryRequest } from './ipcBridgeContracts'
export {
  getProtocolHandlerName,
  openAppHandoffUrl,
  openExternalHttpUrl,
  openExternalUrl,
  sanitizeExternalUrl,
  sanitizeHttpOrHttpsUrl
} from './ipcExternalLinks'
export {
  broadcastServerRequest,
  cancelServerRequestBridge,
  createServerRequestBridge,
  resolveServerRequestBridge
} from './ipcServerRequests'

interface WindowRegistration {
  ipc: WindowIpcMain
  modules: WindowChannelModules
  onClosed: () => void
  disposeProjectActions?: () => void
}
const registrations = new Map<BrowserWindow, WindowRegistration>()

export function getChannelModuleManager(owner: BrowserWindow): ChannelModuleManager | null {
  return registrations.get(owner)?.modules.manager ?? null
}

export async function restoreModuleTrackingByChannelName(names: string[], owner: BrowserWindow): Promise<void> {
  await registrations.get(owner)?.modules.restore(names)
}

export function registerIpcHandlers(
  getWireClient: () => DesktopAppServerClient | null,
  workspacePath: string,
  callbacks: IpcHandlerCallbacks,
  owner: BrowserWindow
): void {
  unregisterIpcHandlers(owner, 'rebind')
  activateFileIndexWorkspace(workspacePath, owner.webContents.id)
  if (workspacePath) void cleanupWorkspaceCache(workspacePath).catch(() => {})
  const ipc = createWindowIpcMain(owner)
  const handleSafe = ipc.handle.bind(ipc)
  const context: WindowIpcContext = { owner, ipc, handleSafe, workspacePath, callbacks, getWireClient }
  const modules = registerChannelModuleHandlers(context)
  const onClosed = (): void => unregisterIpcHandlers(owner)
  const registration: WindowRegistration = { ipc, modules, onClosed }
  registrations.set(owner, registration)
  owner.once('closed', onClosed)
  registerAppServerHandlers(context)
  registerWindowHandlers(context)
  registerGitHandlers(context)
  registerWorkspaceHandlers(context)
  registerWorkspaceFileHandlers(context)
  registerDesktopAppHandlers(context)
  registration.disposeProjectActions = registerViewerBrowserHandlers(context)
  registerThreadPanelStateHandlers(context)
  handleSafe('settings:get', () => {
    return callbacks.getSettings()
  })

  handleSafe('settings:set', async (_event, partial: Partial<AppSettings>) => {
    await callbacks.updateSettings(partial)
  })

  const sshMachinesManager = callbacks.getSshMachinesManager?.()
  if (sshMachinesManager) {
    registerSshMachinesHandlers({
      handleSafe,
      manager: sshMachinesManager,
      openRemoteProject: callbacks.onOpenRemoteProject,
      leaveRemoteProject: callbacks.onLeaveRemoteProject,
      connectDockerDeployment: callbacks.onConnectDockerDeployment,
      disconnectDockerDeployment: callbacks.onDisconnectDockerDeployment
    })
  }

  const pairedComputers = callbacks.getPairedComputersManager?.()
  if (pairedComputers) {
    registerPairedComputersHandlers({
      handleSafe,
      manager: pairedComputers,
      openProject: async (computerId, projectId) => {
        if (!callbacks.onOpenPairedComputerProject) throw new Error('Remote projects cannot be opened in this window.')
        await callbacks.onOpenPairedComputerProject(computerId, projectId)
      },
      leaveProject: async (computerId, projectId) => {
        await callbacks.onLeavePairedComputerProject?.(computerId, projectId)
      }
    })
  }

  const hubCallbacks = callbacks
  if (hubCallbacks.getHubClient) {
    const getHubClient = hubCallbacks.getHubClient
    registerSatellitesHandlers({
      handleSafe,
      getHubClient,
      bridge: getSatellitesHubBridge({ getHubClient }),
      getSettings: () => hubCallbacks.getSettings(),
      updateSettings: (partial) => hubCallbacks.updateSettings(partial)
    })
    registerScreenViewHandlers({ handleSafe, getHubClient })
    registerMobileHandlers({ handleSafe, getHubClient, bridge: getMobileHubBridge({ getHubClient }) })
    getCodexRemoteHubBridge(getHubClient)
    registerCodexRemoteHandlers({ handleSafe, getHubClient })
  }
  if (workspacePath) warmFileSearchIndex(workspacePath, owner.webContents.id)
}

export function unregisterIpcHandlers(
  owner: BrowserWindow,
  reason: 'rebind' | 'close' = 'close'
): void {
  const registration = registrations.get(owner)
  if (!registration) return
  registrations.delete(owner)
  owner.removeListener('closed', registration.onClosed)
  registration.ipc.dispose()
  registration.modules.dispose()
  registration.disposeProjectActions?.()
  const rebinding = reason === 'rebind'
  if (!rebinding) cancelWindowServerRequests(owner)
  viewerTextWatchManager.disposeSender(owner.webContents.id)
  releaseFileIndex(owner.webContents.id)
  unregisterDesktopPluginModuleIpc(rebinding, owner.webContents.id)
  if (!rebinding) clearDesktopPluginModuleRoutes(owner.webContents.id)
}

export function broadcastConnectionStatus(win: BrowserWindow, payload: ConnectionStatusPayload): void {
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:connection-status', payload)
  }
}

export function broadcastWorkspaceStatus(win: BrowserWindow, payload: WorkspaceStatusPayload): void {
  if (!win.isDestroyed()) {
    win.webContents.send('workspace:status-changed', payload)
  }
}

export function broadcastModuleStatus(win: BrowserWindow, payload: ModuleStatusMap): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:status-changed', payload)
  }
}

export function broadcastModuleQrUpdate(win: BrowserWindow, payload: QrUpdatePayload): void {
  if (!win.isDestroyed()) {
    win.webContents.send('modules:qr-update', payload)
  }
}

function stripMarkdownForNotify(text: string): string {
  return text
    .replace(/\r?\n/g, ' ')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

export function shouldShowTaskCompletionNotification(win: BrowserWindow, settings?: AppSettings): boolean {
  const mode = resolveTaskCompletionNotificationMode(settings)
  if (mode === 'never') return false
  if (mode === 'always') return true
  return !win.isFocused()
}

export function broadcastNotification(
  win: BrowserWindow,
  method: string,
  params: unknown,
  settings?: AppSettings,
  workspacePath?: string,
  foreground?: boolean,
  source?: AppServerConnectionSource
): void {
  if (method === 'system/jobResult' && !win.isDestroyed() && shouldShowTaskCompletionNotification(win, settings)) {
    const p = (params ?? {}) as Record<string, unknown>
    const jobName = String((p.jobName as string) ?? (p.name as string) ?? 'Job')
    const err = (p.error as string) ?? ''
    const result = (p.result as string) ?? (p.text as string) ?? ''
    const bodyRaw = err || result || 'Job completed'
    const body = stripMarkdownForNotify(bodyRaw).slice(0, 240)
    try {
      if (Notification.isSupported()) {
        new Notification({ title: jobName, body }).show()
      }
    } catch {}
  }
  if (!win.isDestroyed()) {
    win.webContents.send('appserver:delivery', {
      kind: 'notification',
      payload: {
        method,
        params,
        ...source,
        ...(workspacePath !== undefined ? { workspacePath } : {}),
        ...(foreground !== undefined ? { foreground } : {})
      }
    })
  }
}
