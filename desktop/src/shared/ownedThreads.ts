export const OWNED_THREADS_CHANGED_CHANNEL = 'threadTasks:owned-changed'
export const LINKED_CHATS_CHANGED_CHANNEL = 'threadTasks:linked-changed'
export const PROJECT_CONNECTED_CHANNEL = 'threadTasks:project-connected'

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

export interface RunningThreadDigest {
  id: string
  projectId: string
  activeState: 'needsUser' | 'working'
  waitingFor?: string
}

export interface RecentFinishedThreadDigest {
  id: string
  projectId: string
  lastActiveAt: string
}

export interface RunningThreadsSnapshot {
  activeThreads: RunningThreadDigest[]
  recentFinished: RecentFinishedThreadDigest[]
}

export interface BackgroundCheckInRequest {
  threadId: string
  prompt: string
  requireExistingTurns?: boolean
  silentReplyToken?: string
  notificationTitle?: string
}

export interface BackgroundCheckInResult {
  started: boolean
  turnId?: string
}

export interface ConnectedProjectEvent {
  id: string
  workspacePath: string
}

let ownedThreadIds: ReadonlySet<string> = new Set()

export function setOwnedThreadIds(ids: Iterable<string>): void {
  ownedThreadIds = new Set(ids)
}

export function isOwnedThreadId(id: unknown): boolean {
  return typeof id === 'string' && ownedThreadIds.has(id)
}
