export const OWNED_THREADS_CHANGED_CHANNEL = 'threadTasks:owned-changed'
export const LINKED_CHATS_CHANGED_CHANNEL = 'threadTasks:linked-changed'

export interface OwnedThreadConfig {
  developerInstructions: string
  approvalPolicy?: 'prompt' | 'autoApprove'
}

export interface OwnedThreadRequest {
  pluginId: string
  key: string
  space?: string
  title: string
  config: OwnedThreadConfig
}

export interface OwnedThreadRecord {
  pluginId: string
  key: string
  space: string | null
  title: string
  threadId: string
  workspacePath: string
  fingerprint: string
  paused?: boolean
}

let ownedThreadIds: ReadonlySet<string> = new Set()

export function setOwnedThreadIds(ids: Iterable<string>): void {
  ownedThreadIds = new Set(ids)
}

export function isOwnedThreadId(id: unknown): boolean {
  return typeof id === 'string' && ownedThreadIds.has(id)
}
