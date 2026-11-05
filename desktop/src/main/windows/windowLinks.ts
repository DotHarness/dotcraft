import { BrowserWindow } from 'electron'
import {
  parseNewThreadDeepLink,
  parseSatelliteJoinDeepLink,
  parseWorkspaceOpenDeepLink,
  type NewThreadDeepLink,
  type WorkspaceOpenDeepLink
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

export function sendOpenThread(_ctx: DesktopWindowRuntime, win: BrowserWindow, threadId: string): void {
  const id = threadId.trim()
  if (!id || win.isDestroyed()) return
  const send = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('app:open-thread', { threadId: id })
    }
  }
  if (win.webContents.isLoading()) {
    win.webContents.once('did-finish-load', send)
  } else {
    send()
  }
}

export function flushPendingWorkspaceOpenThread(ctx: DesktopWindowRuntime, win: BrowserWindow): void {
  const threadId = ctx.pendingWorkspaceOpenThreadId?.trim()
  if (!threadId) return
  ctx.pendingWorkspaceOpenThreadId = null
  ctx.sendOpenThread(win, threadId)
}

export function openCurrentWorkspaceThread(ctx: DesktopWindowRuntime, threadId?: string | null): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    if (threadId?.trim()) ctx.pendingWorkspaceOpenThreadId = threadId.trim()
    return
  }

  ctx.showWindowSafely(win)
  if (!threadId?.trim()) return

  if (ctx.lastConnectionStatus.status === 'connected') {
    ctx.sendOpenThread(win, threadId)
  } else {
    ctx.pendingWorkspaceOpenThreadId = threadId.trim()
  }
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

export function handleWorkspaceOpenDeepLink(ctx: DesktopWindowRuntime, link: WorkspaceOpenDeepLink): void {
  if (ctx.canActivateWorkspaceInThisWindow(link.workspacePath)) {
    if (ctx.currentWorkspacePath && ctx.isSameWorkspacePath(ctx.currentWorkspacePath, link.workspacePath)) {
      ctx.openCurrentWorkspaceThread(link.threadId)
    } else {
      void ctx
        .connectToAppServer(link.workspacePath)
        .then((connected) => {
          if (connected) {
            ctx.openCurrentWorkspaceThread(link.threadId)
          }
        })
        .catch((error) => {
          console.warn('[desktop] failed to open workspace deep link in current window', error)
        })
    }
    return
  }

  void ctx.app.openDesktopWindow(link.workspacePath, link.threadId)
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

export function handleDesktopDeepLink(ctx: DesktopWindowRuntime, url: string): Promise<void> | null {
  const workspaceOpen = parseWorkspaceOpenDeepLink(url)
  if (workspaceOpen) {
    ctx.handleWorkspaceOpenDeepLink(workspaceOpen)
    return Promise.resolve()
  }

  const newThread = parseNewThreadDeepLink(url)
  if (newThread) return ctx.handleNewThreadDeepLink(newThread)

  const satelliteJoin = parseSatelliteJoinDeepLink(url)
  if (satelliteJoin) {
    return forwardSatelliteJoinDeepLink(satelliteJoin, ctx.satelliteJoinLinkWindow).then(() => undefined)
  }

  if (!ctx.isChromeSettingsDeepLink(url)) return null
  ctx.openChromeSettingsFromDeepLink()
  return Promise.resolve()
}
