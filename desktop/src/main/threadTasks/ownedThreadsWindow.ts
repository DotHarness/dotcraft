import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { OwnedThreadRecord, OwnedThreadRequest } from '../../shared/ownedThreads'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { openThreadProject } from '../windows/windowThreadProjects'
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
