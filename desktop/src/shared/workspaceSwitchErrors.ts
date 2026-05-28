/**
 * Marker prepended to localized workspace-lock error text so the renderer can
 * detect lock failures regardless of locale (IPC only serializes Error.message).
 */
export const WORKSPACE_LOCKED_IPC_MARKER = '[WORKSPACE_LOCKED]'
export const WORKSPACE_LOCKED_IPC_PREFIX = `${WORKSPACE_LOCKED_IPC_MARKER} `

export function isWorkspaceLockedSwitchError(err: unknown): boolean {
  const msg = err instanceof Error ? err.message : String(err)
  return msg.includes(WORKSPACE_LOCKED_IPC_MARKER)
}

export function stripWorkspaceLockedIpcPrefix(message: string): string {
  if (message.startsWith(WORKSPACE_LOCKED_IPC_PREFIX)) {
    return message.slice(WORKSPACE_LOCKED_IPC_PREFIX.length)
  }
  return message
}
