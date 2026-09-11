import { BrowserWindow, ipcMain, type IpcMainInvokeEvent } from 'electron'
import {
  SCREEN_VIEW_ACK_CHANNEL,
  SCREEN_VIEW_CLOSE_CHANNEL,
  SCREEN_VIEW_OPEN_CHANNEL,
  SCREEN_VIEW_TUNE_CHANNEL,
  SCREEN_VIEW_WIDTH_MAX,
  SCREEN_VIEW_WIDTH_MIN
} from '../../shared/screenView'
import type { DesktopHubClient } from '../desktopHub'
import { getScreenViewManager } from './screenViewManager'
import { browserWindowTarget } from './screenViewTarget'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

const SCREEN_VIEW_CHANNELS = [
  SCREEN_VIEW_OPEN_CHANNEL,
  SCREEN_VIEW_CLOSE_CHANNEL,
  SCREEN_VIEW_TUNE_CHANNEL
] as const

export interface ScreenViewIpcDeps {
  handleSafe: HandleSafe
  getHubClient: () => DesktopHubClient
}

function asObject(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function width(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return SCREEN_VIEW_WIDTH_MIN
  return Math.min(SCREEN_VIEW_WIDTH_MAX, Math.max(SCREEN_VIEW_WIDTH_MIN, Math.round(value)))
}

export function registerScreenViewHandlers(deps: ScreenViewIpcDeps): void {
  const manager = getScreenViewManager({
    resolveBridge: (peerId, sessionId) => deps.getHubClient().resolveScreenBridge(peerId, sessionId)
  })
  ipcMain.removeAllListeners(SCREEN_VIEW_ACK_CHANNEL)

  deps.handleSafe(SCREEN_VIEW_OPEN_CHANNEL, (event, input) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) return { ok: false }
    const request = asObject(input)
    const viewId = text(request.viewId)
    const peerId = text(request.peerId)
    if (!viewId || !peerId) throw new Error('A view id and a machine are required.')
    manager.open(browserWindowTarget(win), { viewId, peerId, maxWidth: width(request.maxWidth) })
    return { ok: true }
  })

  deps.handleSafe(SCREEN_VIEW_CLOSE_CHANNEL, (event, input) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) return { ok: false }
    const viewId = text(asObject(input).viewId)
    if (viewId) manager.close(browserWindowTarget(win), { viewId })
    return { ok: true }
  })

  deps.handleSafe(SCREEN_VIEW_TUNE_CHANNEL, (event, input) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) return { ok: false }
    const request = asObject(input)
    const viewId = text(request.viewId)
    if (viewId) manager.tune(browserWindowTarget(win), { viewId, maxWidth: width(request.maxWidth) })
    return { ok: true }
  })

  // Send-only: an ack must never wait on a round trip while frames keep arriving.
  ipcMain.on(SCREEN_VIEW_ACK_CHANNEL, (event, input) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) return
    const viewId = text(asObject(input).viewId)
    if (viewId) manager.ack(browserWindowTarget(win), { viewId })
  })
}

export function unregisterScreenViewHandlers(): void {
  for (const channel of SCREEN_VIEW_CHANNELS) ipcMain.removeHandler(channel)
  ipcMain.removeAllListeners(SCREEN_VIEW_ACK_CHANNEL)
}
