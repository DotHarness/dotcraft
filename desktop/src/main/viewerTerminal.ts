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
  title?: string
  cwd: string
  shell: string
  pid: number
  proc: IPty | null
  buffer: string
  outputTrimmed: boolean
  cols: number
  rows: number
  order: number
  outputWaiters: Set<(event: TerminalWaitResult) => void>
  exited?: TerminalExitState
}

export interface TerminalTabSnapshot {
  tabId: string
  title?: string
  cwd: string
  cols: number
  rows: number
  output: string
  outputTrimmed: boolean
}

export type TerminalWaitResult = 'output' | 'exit' | 'gone' | 'timeout'

export interface TerminalSpawnParams {
  tabId: string
  threadId: string
  title?: string
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

function notifyWaiters(tab: TerminalTabRuntime, event: TerminalWaitResult): void {
  const waiters = [...tab.outputWaiters]
  tab.outputWaiters.clear()
  for (const waiter of waiters) waiter(event)
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
  private nextOrder = 0

  createTab(win: BrowserWindow, params: TerminalSpawnParams): TerminalCreateResult {
    const existing = this.getTab(win, params.tabId)
    return describe(existing ?? this.spawnTab(win, params))
  }

  restartTab(win: BrowserWindow, params: TerminalSpawnParams & { command: string }): TerminalCreateResult {
    const order = this.getTab(win, params.tabId)?.order
    this.destroyTab(win, params.tabId)
    const tab = this.spawnTab(win, params, order)
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
    tab.cols = Math.max(2, Math.round(params.cols))
    tab.rows = Math.max(2, Math.round(params.rows))
    tab.proc.resize(tab.cols, tab.rows)
  }

  threadTabs(win: BrowserWindow, threadId: string): TerminalTabSnapshot[] {
    const tabs = [...(this.byWindowId.get(win.id)?.tabs.values() ?? [])]
    return tabs
      .filter((tab) => tab.threadId === threadId && tab.proc !== null)
      .sort((a, b) => a.order - b.order)
      .map((tab) => ({
        tabId: tab.tabId,
        ...(tab.title ? { title: tab.title } : {}),
        cwd: tab.cwd,
        cols: tab.cols,
        rows: tab.rows,
        output: tab.buffer,
        outputTrimmed: tab.outputTrimmed
      }))
  }

  rebindThread(win: BrowserWindow, fromThreadId: string, toThreadId: string): void {
    for (const tab of this.byWindowId.get(win.id)?.tabs.values() ?? []) {
      if (tab.threadId === fromThreadId) tab.threadId = toThreadId
    }
  }

  waitForOutput(win: BrowserWindow, tabId: string, timeoutMs: number): Promise<TerminalWaitResult> {
    const tab = this.getTab(win, tabId)
    if (!tab) return Promise.resolve('gone')
    if (!tab.proc) return Promise.resolve('exit')
    return new Promise((resolve) => {
      const waiter = (event: TerminalWaitResult): void => {
        clearTimeout(timer)
        resolve(event)
      }
      const timer = setTimeout(() => {
        tab.outputWaiters.delete(waiter)
        resolve('timeout')
      }, timeoutMs)
      tab.outputWaiters.add(waiter)
    })
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
    notifyWaiters(tab, 'gone')
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

  private spawnTab(win: BrowserWindow, params: TerminalSpawnParams, order = this.nextOrder++): TerminalTabRuntime {
    const { shell, args } = resolveShellCommand()
    const cols = Math.max(2, Math.round(params.cols))
    const rows = Math.max(2, Math.round(params.rows))
    const proc = spawn(shell, args, {
      name: 'xterm-256color',
      cols,
      rows,
      cwd: params.cwd,
      env: { ...process.env, ...params.env } as Record<string, string>
    })
    const tab: TerminalTabRuntime = {
      tabId: params.tabId,
      threadId: params.threadId,
      title: params.title,
      cwd: params.cwd,
      shell,
      pid: proc.pid,
      proc,
      buffer: '',
      outputTrimmed: false,
      cols,
      rows,
      order,
      outputWaiters: new Set()
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
      const output = `${tab.buffer}${data}`
      tab.buffer = trimBuffer(output)
      if (tab.buffer.length < output.length) tab.outputTrimmed = true
      emitTerminalData(win, { tabId: tab.tabId, data })
      notifyWaiters(tab, 'output')
    })
    proc.onExit(({ exitCode, signal }) => {
      if (tab.proc !== proc) return
      tab.exited = { code: exitCode, signal: signal ?? null }
      tab.proc = null
      notifyWaiters(tab, 'exit')
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
