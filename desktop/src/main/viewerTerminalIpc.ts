import { BrowserWindow, ipcMain, type IpcMainInvokeEvent } from 'electron'
import { isAbsolute, relative, resolve } from 'node:path'
import { viewerTerminalManager, type TerminalSpawnParams } from './viewerTerminal'
import { chatFolderFor } from './chatFolderAccess'

const CHANNELS = [
  'viewer:terminal:create',
  'viewer:terminal:restart',
  'viewer:terminal:attach',
  'viewer:terminal:write',
  'viewer:terminal:resize',
  'viewer:terminal:dispose'
] as const

const cleanupHookedWindows = new Set<number>()

interface ViewerTerminalIpcDeps {
  handleSafe(channel: string, listener: Parameters<typeof ipcMain.handle>[1]): void
  workspacePath: string
  workspaceMismatchMessage(): string
}

function contains(parent: string, child: string): boolean {
  const path = relative(parent, child)
  return path === '' || (!path.startsWith('..') && !isAbsolute(path))
}

/** A terminal may start in the workspace, a folder inside it, a parent that holds its project actions, or a chat's folder. */
export function isTerminalCwdAllowed(workspacePath: string, cwd: string): boolean {
  if (!workspacePath || typeof cwd !== 'string' || !isAbsolute(cwd)) return false
  const workspace = resolve(workspacePath)
  const target = resolve(cwd)
  return contains(workspace, target) || contains(target, workspace) || chatFolderFor(workspacePath, cwd) !== null
}

function windowOf(event: IpcMainInvokeEvent): BrowserWindow | null {
  const win = BrowserWindow.fromWebContents(event.sender)
  return win && !win.isDestroyed() ? win : null
}

function ensureWindowCleanup(win: BrowserWindow): void {
  if (cleanupHookedWindows.has(win.id)) return
  cleanupHookedWindows.add(win.id)
  win.once('closed', () => {
    cleanupHookedWindows.delete(win.id)
    viewerTerminalManager.destroyAllTabs(win)
  })
  win.webContents.on('did-finish-load', () => {
    viewerTerminalManager.destroyAllTabs(win)
  })
}

export function registerViewerTerminalHandlers(deps: ViewerTerminalIpcDeps): void {
  const { handleSafe } = deps
  const spawnWindow = (event: IpcMainInvokeEvent, params: TerminalSpawnParams): BrowserWindow => {
    const win = windowOf(event)
    if (!win) throw new Error('Browser window not available')
    if (!isTerminalCwdAllowed(deps.workspacePath, params?.cwd)) throw new Error(deps.workspaceMismatchMessage())
    ensureWindowCleanup(win)
    return win
  }

  handleSafe('viewer:terminal:create', (event, params: TerminalSpawnParams) =>
    viewerTerminalManager.createTab(spawnWindow(event, params), params))
  handleSafe('viewer:terminal:restart', (event, params: TerminalSpawnParams & { command: string }) => {
    if (typeof params?.command !== 'string' || !params.command.trim()) throw new Error('A command is required')
    return viewerTerminalManager.restartTab(spawnWindow(event, params), params)
  })
  handleSafe('viewer:terminal:attach', (event, params: { tabId: string }) => {
    const win = windowOf(event)
    if (!win) throw new Error('Browser window not available')
    ensureWindowCleanup(win)
    return viewerTerminalManager.attachTab(win, params.tabId)
  })
  handleSafe('viewer:terminal:write', (event, params: { tabId: string; data: string }) => {
    const win = windowOf(event)
    if (win) viewerTerminalManager.write(win, params)
  })
  handleSafe('viewer:terminal:resize', (event, params: { tabId: string; cols: number; rows: number }) => {
    const win = windowOf(event)
    if (win) viewerTerminalManager.resize(win, params)
  })
  handleSafe('viewer:terminal:dispose', (event, params: { tabId: string }) => {
    const win = windowOf(event)
    if (win) viewerTerminalManager.destroyTab(win, params.tabId)
  })
}

export function unregisterViewerTerminalHandlers(): void {
  for (const channel of CHANNELS) ipcMain.removeHandler(channel)
  for (const win of BrowserWindow.getAllWindows()) viewerTerminalManager.destroyAllTabs(win)
  cleanupHookedWindows.clear()
}
