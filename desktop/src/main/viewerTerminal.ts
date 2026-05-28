import { BrowserWindow } from 'electron'
import { spawn, type IPty } from '@lydell/node-pty'
import type {
  TerminalAttachResult,
  TerminalCreateResult,
  TerminalExitState
} from '../shared/viewer/types'

const TERMINAL_DATA_CHANNEL = 'viewer:terminal:data'
const TERMINAL_EXIT_CHANNEL = 'viewer:terminal:exit'
const MAX_BUFFER_CHARS = 1_000_000

interface TerminalTabRuntime {
  tabId: string
  threadId: string
  cwd: string
  shell: string
  pid: number
  proc: IPty | null
  buffer: string
  exited?: TerminalExitState
}

interface WindowRuntime {
  tabs: Map<string, TerminalTabRuntime>
}

function isWin32(): boolean {
  return process.platform === 'win32'
}

function resolveShellCommand(): { shell: string; args: string[] } {
  if (isWin32()) {
    const fromEnv = process.env.COMSPEC?.trim()
    if (fromEnv) return { shell: fromEnv, args: [] }
    return { shell: 'powershell.exe', args: ['-NoLogo'] }
  }
  const fromEnv = process.env.SHELL?.trim()
  return { shell: fromEnv || '/bin/bash', args: [] }
}

function trimBuffer(input: string): string {
  if (input.length <= MAX_BUFFER_CHARS) return input
  return input.slice(input.length - MAX_BUFFER_CHARS)
}

function emitTerminalData(win: BrowserWindow, payload: { tabId: string; data: string }): void {
  if (win.isDestroyed() || win.webContents.isDestroyed()) return
  win.webContents.send(TERMINAL_DATA_CHANNEL, payload)
}

function emitTerminalExit(
  win: BrowserWindow,
  payload: { tabId: string; code: number | null; signal: number | null }
): void {
  if (win.isDestroyed() || win.webContents.isDestroyed()) return
  win.webContents.send(TERMINAL_EXIT_CHANNEL, payload)
}

export class ViewerTerminalManager {
  private readonly byWindowId = new Map<number, WindowRuntime>()

  createTab(
    win: BrowserWindow,
    params: { tabId: string; threadId: string; workspacePath: string; cols: number; rows: number }
  ): TerminalCreateResult {
    const runtime = this.ensureWindowRuntime(win)
    const existing = runtime.tabs.get(params.tabId)
    if (existing) {
      return {
        tabId: existing.tabId,
        pid: existing.pid,
        shell: existing.shell,
        cwd: existing.cwd
      }
    }

    const { shell, args } = resolveShellCommand()
    const proc = spawn(shell, args, {
      name: 'xterm-256color',
      cols: Math.max(2, Math.round(params.cols)),
      rows: Math.max(2, Math.round(params.rows)),
      cwd: params.workspacePath,
      env: process.env as Record<string, string>
    })
    const tab: TerminalTabRuntime = {
      tabId: params.tabId,
      threadId: params.threadId,
      cwd: params.workspacePath,
      shell,
      pid: proc.pid,
      proc,
      buffer: ''
    }
    runtime.tabs.set(params.tabId, tab)
    this.bindPtyEvents(win, tab, proc)
    return {
      tabId: tab.tabId,
      pid: tab.pid,
      shell: tab.shell,
      cwd: tab.cwd
    }
  }

  attachTab(win: BrowserWindow, tabId: string): TerminalAttachResult {
    const tab = this.getTab(win, tabId)
    if (!tab) {
      throw new Error(`Terminal tab not found: ${tabId}`)
    }
    return {
      tabId: tab.tabId,
      pid: tab.pid,
      shell: tab.shell,
      cwd: tab.cwd,
      buffer: tab.buffer,
      ...(tab.exited ? { exited: tab.exited } : {})
    }
  }

  write(win: BrowserWindow, params: { tabId: string; data: string }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab?.proc) return
    tab.proc.write(params.data)
  }

  resize(win: BrowserWindow, params: { tabId: string; cols: number; rows: number }): void {
    const tab = this.getTab(win, params.tabId)
    if (!tab?.proc) return
    tab.proc.resize(Math.max(2, Math.round(params.cols)), Math.max(2, Math.round(params.rows)))
  }

  destroyTab(win: BrowserWindow, tabId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    const tab = runtime.tabs.get(tabId)
    if (!tab) return
    if (tab.proc) {
      try {
        tab.proc.kill()
      } catch {
        // Best-effort shutdown.
      }
      tab.proc = null
    }
    runtime.tabs.delete(tabId)
  }

  destroyThread(win: BrowserWindow, threadId: string): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    const tabIds = [...runtime.tabs.values()]
      .filter((tab) => tab.threadId === threadId)
      .map((tab) => tab.tabId)
    for (const tabId of tabIds) {
      this.destroyTab(win, tabId)
    }
  }

  destroyAllTabs(win: BrowserWindow): void {
    const runtime = this.byWindowId.get(win.id)
    if (!runtime) return
    for (const tabId of [...runtime.tabs.keys()]) {
      this.destroyTab(win, tabId)
    }
    this.byWindowId.delete(win.id)
  }

  private ensureWindowRuntime(win: BrowserWindow): WindowRuntime {
    const existing = this.byWindowId.get(win.id)
    if (existing) return existing
    const created: WindowRuntime = { tabs: new Map<string, TerminalTabRuntime>() }
    this.byWindowId.set(win.id, created)
    return created
  }

  private getTab(win: BrowserWindow, tabId: string): TerminalTabRuntime | null {
    return this.byWindowId.get(win.id)?.tabs.get(tabId) ?? null
  }

  private bindPtyEvents(win: BrowserWindow, tab: TerminalTabRuntime, proc: IPty): void {
    proc.onData((data) => {
      tab.buffer = trimBuffer(`${tab.buffer}${data}`)
      emitTerminalData(win, { tabId: tab.tabId, data })
    })
    proc.onExit(({ exitCode, signal }) => {
      tab.exited = { code: exitCode, signal }
      tab.proc = null
      emitTerminalExit(win, {
        tabId: tab.tabId,
        code: exitCode,
        signal
      })
    })
  }
}

export const viewerTerminalManager = new ViewerTerminalManager()
export { TERMINAL_DATA_CHANNEL, TERMINAL_EXIT_CHANNEL }
