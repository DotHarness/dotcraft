import type { BrowserWindow } from 'electron'
import { buildWorkspaceOpenDeepLink } from '../../shared/desktopDeepLink'
import { createDesktopNotification, type DesktopNotificationActivation } from '../desktopNotification'
import { resolveTaskCompletionNotificationMode } from '../settings'
import { isCurrentWorkspaceRequestSource } from './windowNodeReplMcp'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function notificationThreadActivation(
  ctx: DesktopWindowRuntime,
  source: WorkspaceConnectionEntry,
  threadId: string
): DesktopNotificationActivation {
  if (source.kind !== 'remote') {
    const workspacePath = source.localWorkspacePath ?? source.workspacePath
    return {
      url: buildWorkspaceOpenDeepLink(workspacePath, threadId),
      open: () => { void ctx.app.openDesktopWindow(workspacePath, threadId).catch(console.warn) }
    }
  }
  return {
    open: () => { void openRemoteThread(ctx, source.key, threadId).catch(console.warn) }
  }
}

async function openRemoteThread(ctx: DesktopWindowRuntime, projectKey: string, threadId: string): Promise<void> {
  const entry = ctx.workspaceConnections.get(projectKey)
  if (!entry || !isCurrentWorkspaceRequestSource(ctx, entry)) return
  if (entry.role !== 'foreground' || ctx.wireClient !== entry.client) {
    if (!await ctx.promoteWorkspaceConnection(entry)) return
  }
  ctx.openCurrentWorkspaceThread(threadId)
}

export function showJobResultNotification(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  win: BrowserWindow,
  params: unknown
): void {
  const mode = resolveTaskCompletionNotificationMode(ctx.settings)
  if (mode === 'never' || (mode !== 'always' && win.isFocused())) return
  const result = (params ?? {}) as { jobName?: string; result?: string; error?: string; threadId?: string }
  const threadId = typeof result.threadId === 'string' ? result.threadId.trim() : ''
  const title = result.jobName ?? 'Job'
  const body = (result.error || result.result || 'Job completed')
    .replace(/\s+/g, ' ')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/`([^`]+)`/g, '$1')
    .trim()
    .slice(0, 240)

  try {
    createDesktopNotification(
      { title, body },
      threadId ? notificationThreadActivation(ctx, entry, threadId) : undefined
    )?.show()
  } catch (error) {
    console.warn('[desktop] job notification failed', error)
  }
}
