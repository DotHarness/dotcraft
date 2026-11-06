import { app, BrowserWindow, session } from 'electron'
import { translate } from '../shared/locales'
import type { BrowserUseApprovalResponsePayload } from '../shared/viewer/types'
import { registerBrowserFeedbackIpc } from './browserFeedbackIpc'
import { browserUseManager } from './browserUseManager'
import { checkChromeSetup, installChromeNativeHost, openChromeWindow, type ChromeOpenRequest } from './chromeSetup'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import { assertWorkspaceOrChatFolder } from './ipcBridgePaths'
import { registerProjectActionsHandlers } from './projectActions/projectActionsIpc'
import { partitionForWorkspace, viewerBrowserManager } from './viewerBrowser'
import { registerViewerTerminalHandlers } from './viewerTerminalIpc'
import { assertBrowserRestoreScope } from './browserRestoreScope'

export function registerViewerBrowserHandlers(context: WindowIpcContext): () => void {
  const { handleSafe, workspacePath, callbacks, ipc, owner } = context
  handleSafe('viewer:browser:host-list', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) throw new Error('Browser window not available.')
    return viewerBrowserManager.hosts.list(win)
  })
  handleSafe('viewer:browser:host-cursor-arrived', async (event, params?: { tabId: string; moveSequence: number }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) throw new Error('Browser window not available.')
    if (!params) throw new Error('Browser host parameters are required.')
    viewerBrowserManager.hosts.cursors.arrived(win, params.tabId, params.moveSequence)
  })
  registerBrowserFeedbackIpc(viewerBrowserManager, ipc)
  handleSafe(
    'viewer:browser:create',
    async (event, params: { tabId: string; threadId?: string; workspacePath: string; projectKey?: string; browserStorageId?: string; initialUrl?: string }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) {
        throw new Error('Browser window not available')
      }
      assertWorkspaceOrChatFolder(params.workspacePath, workspacePath, mainLocale(callbacks), owner.webContents.id)
      return viewerBrowserManager.createTab(win, {
        tabId: params.tabId,
        ...(params.threadId ? { threadId: params.threadId } : {}),
        workspacePath: params.workspacePath,
        ...(params.projectKey ? { projectKey: params.projectKey } : {}),
        ...(params.browserStorageId ? { browserStorageId: params.browserStorageId } : {}),
        ...(params.initialUrl ? { initialUrl: params.initialUrl } : {})
      })
    }
  )
  handleSafe('viewer:browser:restore-state', (event, params: {
    workspacePath: string
    projectKey?: string
    threadId?: string
    tabs: Array<{ tabId: string; browserStorageId: string; initialUrl?: string }>
  }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) throw new Error('Browser window not available')
    assertBrowserRestoreScope(context, params)
    return viewerBrowserManager.restoreState(win, params)
  })
  handleSafe('viewer:browser:set-foreground-scope', (event, params: { projectKey: string; threadId?: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (win && !win.isDestroyed()) viewerBrowserManager.setForegroundScope(win, params)
  })
  handleSafe('viewer:browser:destroy', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.destroyTab(win, params.tabId)
  })
  handleSafe('viewer:browser:rebind-thread', async (event, params: { fromThreadId: string; toThreadId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.rebindThread(win, params.fromThreadId, params.toThreadId)
  })
  handleSafe('viewer:browser:navigate', async (event, params: { tabId: string; url: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    void viewerBrowserManager.navigate(win, params)
  })
  handleSafe('viewer:browser:back', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.goBack(win, params.tabId)
  })
  handleSafe('viewer:browser:forward', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.goForward(win, params.tabId)
  })
  handleSafe('viewer:browser:reload', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.reload(win, params.tabId)
  })
  handleSafe('viewer:browser:stop', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.stop(win, params.tabId)
  })
  handleSafe(
    'viewer:browser:set-bounds',
    async (event, params: { tabId: string; x: number; y: number; width: number; height: number }) => {
      const win = BrowserWindow.fromWebContents(event.sender)
      if (!win || win.isDestroyed()) return
      viewerBrowserManager.setBounds(win, params)
    }
  )
  handleSafe('viewer:browser:set-visible', async (event, params: { tabId: string; visible: boolean }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.setVisible(win, params)
  })
  handleSafe('viewer:browser:set-active', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    viewerBrowserManager.setActiveTab(win, params.tabId)
  })
  handleSafe('viewer:browser:open-external', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    await viewerBrowserManager.openInOsBrowser(win, params.tabId)
  })
  handleSafe('viewer:browser:snapshot', async (event, params: { tabId: string }) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return null
    return viewerBrowserManager.snapshotState(win, params.tabId)
  })
  handleSafe('viewer:browser:approval-response', async (_event, payload: BrowserUseApprovalResponsePayload) => {
    browserUseManager.handleApprovalResponse(payload, owner.webContents.id)
  })
  handleSafe('viewer:browser:clear-cookies', async () => {
    if (!workspacePath) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.noWorkspaceOpen'))
    }
    const partition = partitionForWorkspace(workspacePath)
    await session.fromPartition(partition).clearStorageData({ storages: ['cookies'] })
    return { ok: true }
  })

  registerViewerTerminalHandlers({
    handleSafe,
    workspacePath,
    workspaceMismatchMessage: () => translate(mainLocale(callbacks), 'ipc.workspacePathMismatch')
  })
  const disposeProjectActions = registerProjectActionsHandlers({
    owner,
    handleSafe,
    getPreferences: () => callbacks.getSettings().projectActionPreferences,
    updatePreferences: async (projectActionPreferences) => {
      await callbacks.updateSettings({ projectActionPreferences })
    }
  })

  handleSafe('computerUse:app-icon', async (_event, appId: unknown) => {
    if (typeof appId !== 'string' || !/\.exe$/i.test(appId)) return null
    try {
      const icon = await app.getFileIcon(appId, { size: 'normal' })
      return icon.isEmpty() ? null : icon.toDataURL()
    } catch {
      return null
    }
  })

  handleSafe('chrome:check-setup', async () => {
    return checkChromeSetup(workspacePath)
  })

  handleSafe('chrome:install-native-host', async () => {
    return installChromeNativeHost(workspacePath)
  })

  handleSafe('chrome:open', async (_event, params?: ChromeOpenRequest) => {
    return openChromeWindow(workspacePath, params)
  })
  return disposeProjectActions
}
