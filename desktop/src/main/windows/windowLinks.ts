import { parsePairingUrl } from '@dotcraft/sdk/gateway'
import { PAIRED_COMPUTERS_EVENT_CHANNEL } from '../pairedComputers/pairedComputersIpc'
import { BrowserWindow } from 'electron'
import {
  parseNewThreadDeepLink,
  parseSatelliteJoinDeepLink,
  parseWorkspaceOpenDeepLink,
  type NewThreadDeepLink
} from '../../shared/desktopDeepLink'
import { openNewThreadDeepLink } from '../newThreadDeepLink'
import { forwardSatelliteJoinDeepLink } from '../satellites/satelliteJoinLink'
import { hasRemoteEndpointArg } from '../workspaceArgs'

import type { DesktopWindowRuntime } from './windowRuntime'

export function isChromeSettingsDeepLink(_ctx: DesktopWindowRuntime, value: string): boolean {
  try {
    const parsed = new URL(value)
    return (
      parsed.protocol === 'dotcraft:' &&
      parsed.hostname === 'settings' &&
      parsed.pathname.replace(/\/+$/, '') === '/computer-control/chrome'
    )
  } catch {
    return false
  }
}

export function sendOpenThread(ctx: DesktopWindowRuntime, win: BrowserWindow, threadId: string): void {
  const id = threadId.trim()
  const projectKey = ctx.lastConnectionStatus.connectionKey
  if (!id || !projectKey || win.isDestroyed()) return
  const payload = { threadId: id, projectKey, workspacePath: ctx.getProtocolWorkspacePath() }
  const send = (): void => {
    if (!win.isDestroyed()) win.webContents.send('app:open-thread', payload)
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

export function flushPendingWorkspaceOpenThread(ctx: DesktopWindowRuntime, win: BrowserWindow): void {
  const request = ctx.pendingWorkspaceOpenThread
  if (!request || request.projectKey !== ctx.lastConnectionStatus.connectionKey) return
  ctx.pendingWorkspaceOpenThread = null
  ctx.sendOpenThread(win, request.threadId)
}

export function openCurrentWorkspaceThread(ctx: DesktopWindowRuntime, threadId?: string | null): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return

  ctx.showWindowSafely(win)
  const id = threadId?.trim()
  if (!id) return
  const projectKey = ctx.activeRemoteProject?.projectId ?? ctx.localProjectId(ctx.currentWorkspacePath)
  ctx.pendingWorkspaceOpenThread = { projectKey, threadId: id }
  ctx.flushPendingWorkspaceOpenThread(win)
}

export function sendOpenChromeSettings(_ctx: DesktopWindowRuntime, win: BrowserWindow): void {
  if (win.isDestroyed()) return
  const send = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('app:open-chrome-settings')
    }
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

export function openChromeSettingsFromDeepLink(ctx: DesktopWindowRuntime): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    ctx.pendingChromeSettingsDeepLink = true
    return
  }
  ctx.pendingChromeSettingsDeepLink = false
  ctx.showWindowSafely(win)
  ctx.sendOpenChromeSettings(win)
}

export function satelliteJoinLinkWindow(ctx: DesktopWindowRuntime): BrowserWindow | null {
  return ctx.mainWindow
}

export function sendOpenNewChat(
  _ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  prompt: string | undefined
): void {
  const send = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('app:open-new-chat', { prompt })
    }
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

export async function handleNewThreadDeepLink(
  ctx: DesktopWindowRuntime,
  link: NewThreadDeepLink
): Promise<void> {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    ctx.pendingNewThreadLink = link
    return
  }
  ctx.showWindowSafely(win)
  await openNewThreadDeepLink(link, {
    canOpenLocalProject: () =>
      !hasRemoteEndpointArg(ctx.argv) && ctx.resolveConnectionMode(ctx.settings) === 'local',
    isForegroundWorkspace: ctx.isWorkspaceForeground,
    switchWorkspace: ctx.switchForegroundWorkspace,
    openNewChat: (prompt) => {
      if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) ctx.sendOpenNewChat(ctx.mainWindow, prompt)
    }
  })
}

export function flushPendingNewThreadLink(ctx: DesktopWindowRuntime): void {
  const link = ctx.pendingNewThreadLink
  if (!link) return
  ctx.pendingNewThreadLink = null
  void ctx.handleNewThreadDeepLink(link).catch((error) => {
    console.warn('[desktop] failed to open new chat link', error)
  })
}

export function openPairingDeepLink(ctx: DesktopWindowRuntime, link: string): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed() || win.webContents.isLoading()) {
    ctx.pendingPairingLink = link
    return
  }
  ctx.pendingPairingLink = null
  ctx.showWindowSafely(win)
  win.webContents.send(PAIRED_COMPUTERS_EVENT_CHANNEL, { kind: 'pairingLink', link })
}

export function handleDesktopDeepLink(ctx: DesktopWindowRuntime, url: string): Promise<void> | null {
  const workspaceOpen = parseWorkspaceOpenDeepLink(url)
  if (workspaceOpen) return ctx.app.openDesktopWindow(workspaceOpen.workspacePath, workspaceOpen.threadId)

  const newThread = parseNewThreadDeepLink(url)
  if (newThread) return ctx.handleNewThreadDeepLink(newThread)

  if (parsePairingUrl(url)) {
    ctx.openPairingDeepLink(url)
    return Promise.resolve()
  }

  const satelliteJoin = parseSatelliteJoinDeepLink(url)
  if (satelliteJoin) {
    return forwardSatelliteJoinDeepLink(satelliteJoin, ctx.satelliteJoinLinkWindow).then(() => undefined)
  }

  if (!ctx.isChromeSettingsDeepLink(url)) return null
  ctx.openChromeSettingsFromDeepLink()
  return Promise.resolve()
}
