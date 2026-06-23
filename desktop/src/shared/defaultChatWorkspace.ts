import { normalizeWorkspaceProjectKey } from './workspaceProjectKey'

const DEFAULT_CHAT_WORKSPACE_SUFFIX = '/.craft/workspaces/chats'

/**
 * Renderer-safe default Chat workspace detection.
 *
 * The main process owns the exact resolver because it can read the user's home
 * directory. Renderer code only needs to suppress project affordances for paths
 * that match the product-owned default Chat location shape.
 */
export function isDefaultChatWorkspacePathCandidate(workspacePath: string | null | undefined): boolean {
  const key = normalizeWorkspaceProjectKey(workspacePath)
  return Boolean(key && key.endsWith(DEFAULT_CHAT_WORKSPACE_SUFFIX))
}
