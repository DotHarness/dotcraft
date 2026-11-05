import { BrowserWindow, shell } from 'electron'
import type { OpenInTargetRequest } from '../shared/openTargets'
import type {
  SkillMarketBindDotCraftInstallRequest,
  SkillMarketCleanupDotCraftInstallRequest,
  SkillMarketDetailRequest,
  SkillMarketInstallRequest,
  SkillMarketPrepareDotCraftInstallRequest,
  SkillMarketSearchRequest
} from '../shared/skillMarket'
import type { TextContextMenuRequest } from '../shared/textContextMenu'
import type { VisualizationCaptureRect } from '../shared/visualization'
import { saveFileAs, type SaveFileAsRequest } from './fileSaveAs'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import { assertExistingLocalPath, assertPathWithinWorkspaceOrChatFolder } from './ipcBridgePaths'
import { getProtocolHandlerName, openAppHandoffUrl, openExternalHttpUrl, openExternalUrl } from './ipcExternalLinks'
import { listOpenTargets, openInTarget } from './openTargets'
import { showReplyTextContextMenu } from './replyTextContextMenu'
import {
  bindDotCraftSkillInstall,
  cleanupDotCraftSkillInstall,
  getSkillMarketDetail,
  installSkillFromMarket,
  prepareDotCraftSkillInstall,
  searchSkillMarket
} from './skillMarket'
import { copyVisualizationImage } from './visualizationCapture'
import { applyNativeChromeTheme, resolveThemeSurface } from './windowTheme'
interface WindowVisibilityState {
  minimized: boolean
  visible: boolean
  focused: boolean
}

export function registerWindowHandlers(context: WindowIpcContext): void {
  const { handleSafe, workspacePath, callbacks, owner } = context
  handleSafe('visualization:copy-image', async (event, rect: VisualizationCaptureRect) => {
    const owner = BrowserWindow.fromWebContents(event.sender)
    if (!owner || owner.isDestroyed() || event.sender.isDestroyed()) {
      throw new Error('The visualization window is unavailable.')
    }
    return copyVisualizationImage(event.sender, rect)
  })
  handleSafe('window:set-title', (event, title: string) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    win?.setTitle(title)
  })

  handleSafe('window:set-title-bar-overlay-theme', (event, theme: 'dark' | 'light') => {
    if (process.platform === 'darwin') return
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    const t = theme === 'light' ? 'light' : 'dark'
    applyNativeChromeTheme(win, t, resolveThemeSurface(callbacks.getSettings(), t))
  })

  handleSafe('window:minimize', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    win.minimize()
  })

  handleSafe('window:toggle-maximize', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return false
    if (win.isMaximized()) {
      win.unmaximize()
    } else {
      win.maximize()
    }
    return win.isMaximized()
  })

  handleSafe('window:close', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) return
    win.close()
  })

  handleSafe('window:is-maximized', (event) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    return win != null && !win.isDestroyed() && win.isMaximized()
  })

  handleSafe('window:get-visibility-state', (event): WindowVisibilityState => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win || win.isDestroyed()) {
      return { minimized: false, visible: false, focused: false }
    }
    return {
      minimized: win.isMinimized(),
      visible: win.isVisible(),
      focused: win.isFocused()
    }
  })

  handleSafe('window:get-workspace-path', () => workspacePath)

  handleSafe('skill-market:search', async (_event, request: SkillMarketSearchRequest) => {
    return searchSkillMarket(workspacePath, request)
  })

  handleSafe('skill-market:detail', async (_event, request: SkillMarketDetailRequest) => {
    return getSkillMarketDetail(workspacePath, request)
  })

  handleSafe('skill-market:install', async (_event, request: SkillMarketInstallRequest) => {
    return installSkillFromMarket(workspacePath, request)
  })

  handleSafe(
    'skill-market:prepare-dotcraft-install',
    async (_event, request: SkillMarketPrepareDotCraftInstallRequest) => {
      return prepareDotCraftSkillInstall(workspacePath, request)
    }
  )

  handleSafe('skill-market:bind-dotcraft-install', async (_event, request: SkillMarketBindDotCraftInstallRequest) => {
    return bindDotCraftSkillInstall(workspacePath, request)
  })

  handleSafe(
    'skill-market:cleanup-dotcraft-install',
    async (_event, request: SkillMarketCleanupDotCraftInstallRequest) => {
      return cleanupDotCraftSkillInstall(workspacePath, request)
    }
  )

  handleSafe('shell:open-external', async (_event, url: string) => {
    await openExternalUrl(url)
  })

  handleSafe('shell:open-desktop-link', async (_event, url: string) => {
    if (typeof url !== 'string') throw new Error('Invalid URL')
    await callbacks.onOpenDesktopLink(url)
  })

  handleSafe('shell:show-reply-text-menu', (event, request: TextContextMenuRequest) => {
    if (event.senderFrame !== event.sender.mainFrame) throw new Error('Text menus require the main frame')
    return showReplyTextContextMenu(event.sender, request, mainLocale(callbacks), openExternalHttpUrl)
  })

  handleSafe('shell:open-app-handoff', async (_event, url: string) => {
    await openAppHandoffUrl(url, owner.webContents.id)
  })

  handleSafe('shell:get-protocol-handler-name', async (_event, protocol: string) => {
    return getProtocolHandlerName(protocol)
  })

  handleSafe('open-targets:list', async () => {
    return listOpenTargets()
  })

  handleSafe('open-targets:open', async (_event, request: OpenInTargetRequest) => {
    if (!request || typeof request.targetId !== 'string') throw new Error('Invalid open target request')
    const resolved = await assertExistingLocalPath(request.path)
    await openInTarget({ ...request, path: resolved })
  })

  handleSafe('shell:reveal-local-path', async (_event, targetPath: string) => {
    const resolved = await assertExistingLocalPath(targetPath)
    shell.showItemInFolder(resolved)
  })

  handleSafe('shell:save-file-as', async (event, request: SaveFileAsRequest) => {
    return saveFileAs(event.sender, request)
  })

  handleSafe('shell:show-item-in-folder', async (_event, targetPath: string) => {
    const resolved = assertPathWithinWorkspaceOrChatFolder(
      targetPath,
      workspacePath,
      mainLocale(callbacks),
      owner.webContents.id
    )
    shell.showItemInFolder(resolved)
  })
}
