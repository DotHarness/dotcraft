import { isAbsolute } from 'path'
import { chatFolderContaining } from './chatFolders'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'

let localChatsFolderRoots: () => readonly string[] = () => []

export function setLocalChatsFolderRootsResolver(resolver: () => readonly string[]): void {
  localChatsFolderRoots = resolver
}

export function chatFolderFor(workspacePath: string, targetPath: unknown): string | null {
  if (typeof targetPath !== 'string' || !isAbsolute(targetPath) || !isDefaultChatWorkspace(workspacePath)) return null
  for (const root of localChatsFolderRoots()) {
    const chatFolder = chatFolderContaining(targetPath, root)
    if (chatFolder) return chatFolder
  }
  return null
}
