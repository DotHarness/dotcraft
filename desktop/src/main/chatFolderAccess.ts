import { isAbsolute } from 'path'
import { chatFolderContaining } from './chatFolders'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'
import { getWindowFileAccess } from './windowFileAccess'

export function chatFolderFor(workspacePath: string, targetPath: unknown, ownerId: number): string | null {
  if (typeof targetPath !== 'string' || !isAbsolute(targetPath) || !isDefaultChatWorkspace(workspacePath)) return null
  const context = getWindowFileAccess(ownerId)
  if (!context || context.getWorkspaceRoot() !== workspacePath) return null
  for (const root of context.getChatFolderRoots?.() ?? []) {
    const chatFolder = chatFolderContaining(targetPath, root)
    if (chatFolder) return chatFolder
  }
  return null
}
