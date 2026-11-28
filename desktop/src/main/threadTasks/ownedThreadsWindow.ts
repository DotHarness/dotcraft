import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { OwnedThreadRecord, OwnedThreadRequest } from '../../shared/ownedThreads'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { openThreadProject } from '../windows/windowThreadProjects'
import { clearOwnedThreadCheckInState } from './ownedThreadCheckIn'
import type { DesktopOwnedThreads } from './ownedThreads'

export async function ensureOwnedThread(
  ctx: DesktopWindowRuntime,
  ownedThreads: DesktopOwnedThreads,
  request: OwnedThreadRequest
): Promise<OwnedThreadRecord> {
  const path = resolveDefaultChatWorkspacePath()
  const entry = await openThreadProject(ctx, { id: ctx.localProjectId(path), name: 'Chats', path })
  const connection = threadTaskConnection(entry.client, entry.workspacePath, ctx.desktopThreadToolOptions(entry))
  return ownedThreads.ensure(request, {
    workspacePath: entry.workspacePath,
    request: (method, params) => connection.request(method, params),
    createChatFolder: (firstMessage) => createChatFolderFromSettings(ctx.settings, firstMessage)
  })
}

export function setOwnedThreadPaused(
  ctx: DesktopWindowRuntime,
  ownedThreads: DesktopOwnedThreads,
  threadId: string,
  paused: boolean
): OwnedThreadRecord | null {
  const previous = ownedThreads.findByThreadId(threadId)
  const updated = ownedThreads.setPaused(threadId, paused)
  if (!updated) return null
  if (previous?.paused === true && !paused) {
    ctx.app.tasks.linkedChats?.flushParent(threadId)
  }
  return updated
}

export async function archiveOwnedThread(
  ctx: DesktopWindowRuntime,
  ownedThreads: DesktopOwnedThreads,
  threadId: string
): Promise<boolean> {
  const record = ownedThreads.findByThreadId(threadId)
  if (!record) return false
  const entry = await openThreadProject(ctx, { id: ctx.localProjectId(record.workspacePath), name: 'Chats', path: record.workspacePath })
  await entry.client.sendRequest('thread/archive', { threadId })
  ownedThreads.removeByThreadId(threadId)
  ctx.app.tasks.linkedChats?.removeForParent(threadId)
  clearOwnedThreadCheckInState(ctx, threadId)
  return true
}
