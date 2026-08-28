import { existsSync, mkdirSync, writeFileSync } from 'fs'
import { homedir } from 'os'
import { join, resolve as resolvePath } from 'path'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'

/**
 * Mirrors the backend helper (`HubPaths.DefaultChatWorkspacePath` +
 * `DefaultChatWorkspace.Ensure`): an ordinary local workspace that Desktop merely surfaces
 * as a `Chats` group. Deliberately no new thread kind and no AppServer Protocol change.
 */

export function resolveDefaultChatWorkspacePath(): string {
  return join(homedir(), '.craft', 'workspaces', 'chats')
}

/** Compares via the shared workspace key normalization so casing variants still match. */
export function isDefaultChatWorkspace(workspacePath: string): boolean {
  if (!workspacePath.trim()) return false
  return (
    normalizeWorkspaceProjectKey(workspacePath) ===
    normalizeWorkspaceProjectKey(resolveDefaultChatWorkspacePath())
  )
}

/**
 * Non-interactive and idempotent: existing config and user files are never overwritten,
 * so this is safe to call on every launch.
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
