import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { OwnedThreadRecord, OwnedThreadRequest } from '../../shared/ownedThreads'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { openThreadProject } from '../windows/windowThreadProjects'
import {
  CONDUCTOR_KEY,
  CONDUCTOR_PLUGIN_ID,
  resetConductorProactiveState,
  runConductorProactiveCheckIn
} from './conductorProactive'
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
    if (updated.pluginId === CONDUCTOR_PLUGIN_ID && updated.key === CONDUCTOR_KEY) {
      void runConductorProactiveCheckIn(ctx, 'resume').catch(() => {})
    }
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
  if (record.pluginId === CONDUCTOR_PLUGIN_ID && record.key === CONDUCTOR_KEY) {
    resetConductorProactiveState(ctx)
  }
  return true
}
