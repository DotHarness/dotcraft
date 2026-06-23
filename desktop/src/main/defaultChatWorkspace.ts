import { existsSync, mkdirSync, writeFileSync } from 'fs'
import { homedir } from 'os'
import { join, resolve as resolvePath } from 'path'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'

/**
 * Default Chat workspace used by lightweight, projectless conversation entry points.
 *
 * This mirrors the backend/SDK helper (`HubPaths.DefaultChatWorkspacePath` +
 * `DefaultChatWorkspace.Ensure`): a normal local workspace at
 * `~/.craft/workspaces/chats` that Desktop surfaces as a dedicated `Chats` group.
 * There is no new thread kind and no AppServer Protocol change — Desktop just
 * resolves the path, ensures the skeleton, and connects through the existing Hub
 * `ensureAppServer` flow.
 */

/**
 * Resolves the current user's default Chat workspace root: `~/.craft/workspaces/chats`.
 */
export function resolveDefaultChatWorkspacePath(): string {
  return join(homedir(), '.craft', 'workspaces', 'chats')
}

/**
 * Whether a workspace path points at the default Chat workspace. Comparison uses the
 * shared workspace key normalization so platform/casing variants still match.
 */
export function isDefaultChatWorkspace(workspacePath: string): boolean {
  if (!workspacePath.trim()) return false
  return (
    normalizeWorkspaceProjectKey(workspacePath) ===
    normalizeWorkspaceProjectKey(resolveDefaultChatWorkspacePath())
  )
}

/**
 * Creates the default Chat workspace skeleton if missing. Non-interactive and
 * idempotent: makes the workspace root, `.craft/` and its `memory/`, `skills/`,
 * `security/` subdirectories, and writes an empty `config.json` only when absent.
 * Never overwrites existing config or user files. Returns the resolved root path.
 */
export function ensureDefaultChatWorkspace(): string {
  const root = resolvePath(resolveDefaultChatWorkspacePath())
  const craftPath = join(root, '.craft')

  mkdirSync(root, { recursive: true })
  mkdirSync(craftPath, { recursive: true })
  mkdirSync(join(craftPath, 'memory'), { recursive: true })
  mkdirSync(join(craftPath, 'skills'), { recursive: true })
  mkdirSync(join(craftPath, 'security'), { recursive: true })

  const configPath = join(craftPath, 'config.json')
  if (!existsSync(configPath)) {
    writeFileSync(configPath, '{}\n', 'utf8')
  }

  return root
}
