import type { ThreadConfiguration } from '@dotcraft/sdk/contracts'
import { STANDALONE_CHAT_INSTRUCTIONS } from '../../shared/chatFolders'
import { isDefaultChatWorkspacePathCandidate } from '../../shared/defaultChatWorkspace'

export function startsInChatFolder(workspacePath: string, remoteWorkspace: boolean): boolean {
  return !remoteWorkspace && isDefaultChatWorkspacePathCandidate(workspacePath)
}

export async function chatFolderThreadStart(
  firstMessage: string,
  config: ThreadConfiguration
): Promise<{ cwd: string; config: ThreadConfiguration }> {
  const { path } = await window.api.workspace.createChatFolder({ firstMessage })
  return { cwd: path, config: { ...config, developerInstructions: STANDALONE_CHAT_INSTRUCTIONS } }
}
