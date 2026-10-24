import { BrowserWindow } from 'electron'
import { spawn, type IPty } from '@lydell/node-pty'
import { composeShellCommandLine, resolveShellCommand } from './terminalShell'
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

export interface TerminalSpawnParams {
  tabId: string
  threadId: string
  cwd: string
  cols: number
  rows: number
  env?: Record<string, string>
}

interface WindowRuntime {
  tabs: Map<string, TerminalTabRuntime>
}

function trimBuffer(input: string): string {
  if (input.length <= MAX_BUFFER_CHARS) return input
  return input.slice(input.length - MAX_BUFFER_CHARS)
}

function describe(tab: TerminalTabRuntime): TerminalCreateResult {
  return { tabId: tab.tabId, pid: tab.pid, shell: tab.shell, cwd: tab.cwd }
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

  createTab(win: BrowserWindow, params: TerminalSpawnParams): TerminalCreateResult {
    const existing = this.getTab(win, params.tabId)
    return describe(existing ?? this.spawnTab(win, params))
  }

  restartTab(win: BrowserWindow, params: TerminalSpawnParams & { command: string }): TerminalCreateResult {
    this.destroyTab(win, params.tabId)
    const tab = this.spawnTab(win, params)
    tab.proc?.write(`${composeShellCommandLine(tab.shell, tab.cwd, params.command)}\r`)
    return describe(tab)
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
    const proc = tab.proc
    tab.proc = null
    try {
      proc?.kill()
    } catch {
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

  private spawnTab(win: BrowserWindow, params: TerminalSpawnParams): TerminalTabRuntime {
    const { shell, args } = resolveShellCommand()
    const proc = spawn(shell, args, {
      name: 'xterm-256color',
      cols: Math.max(2, Math.round(params.cols)),
      rows: Math.max(2, Math.round(params.rows)),
      cwd: params.cwd,
      env: { ...process.env, ...params.env } as Record<string, string>
    })
    const tab: TerminalTabRuntime = {
      tabId: params.tabId,
      threadId: params.threadId,
      cwd: params.cwd,
      shell,
      pid: proc.pid,
      proc,
      buffer: ''
    }
    this.ensureWindowRuntime(win).tabs.set(params.tabId, tab)
    this.bindPtyEvents(win, tab, proc)
    return tab
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
      if (tab.proc !== proc) return
      tab.buffer = trimBuffer(`${tab.buffer}${data}`)
      emitTerminalData(win, { tabId: tab.tabId, data })
    })
    proc.onExit(({ exitCode, signal }) => {
      if (tab.proc !== proc) return
      tab.exited = { code: exitCode, signal: signal ?? null }
      tab.proc = null
      emitTerminalExit(win, {
        tabId: tab.tabId,
        code: exitCode,
        signal: signal ?? null
      })
    })
  }
}

export const viewerTerminalManager = new ViewerTerminalManager()
export { TERMINAL_DATA_CHANNEL, TERMINAL_EXIT_CHANNEL }
