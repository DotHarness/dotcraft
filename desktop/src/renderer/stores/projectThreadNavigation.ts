import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

export interface PendingProjectThreadOpenInput {
  projectKey: string
  workspacePath: string
  threadId: string
  includeArchived?: boolean
}

export interface PendingProjectThreadOpen extends PendingProjectThreadOpenInput {
  requestId: number
  startedAt: number
}

export interface ProjectThreadNavigationState {
  pendingProjectThreadOpen: PendingProjectThreadOpen | null
}

export interface ProjectThreadNavigationActions {
  setPendingProjectThreadOpen(payload: PendingProjectThreadOpenInput | null): PendingProjectThreadOpen | null
  consumePendingProjectThreadOpen(projectKey: string, threadIds: Iterable<string>, expectedRequestId?: number): PendingProjectThreadOpen | null
  clearPendingProjectThreadOpen(projectKey?: string, threadId?: string, expectedRequestId?: number): void
}

let navigationSequence = 0

export function beginThreadNavigation(): number {
  return ++navigationSequence
}

export function isThreadNavigationCurrent(requestId: number): boolean {
  return requestId === navigationSequence
}

function createProjectThreadNavigation(payload: PendingProjectThreadOpenInput): PendingProjectThreadOpen | null {
  const projectKey = normalizeWorkspaceProjectKey(payload.projectKey || payload.workspacePath)
  const threadId = payload.threadId.trim()
  if (!projectKey || !threadId) return null
  const request = {
    projectKey,
    workspacePath: payload.workspacePath,
    threadId,
    ...(payload.includeArchived ? { includeArchived: true } : {}),
    requestId: beginThreadNavigation(),
    startedAt: performance.now()
  }
  performance.mark('app:thread-navigation-start', {
    detail: { requestId: request.requestId, projectKey, threadId }
  })
  return request
}

export function projectThreadNavigationActions(
  set: (state: ProjectThreadNavigationState) => void,
  get: () => ProjectThreadNavigationState
): ProjectThreadNavigationActions {
  return {
    setPendingProjectThreadOpen(payload) {
      const pending = payload ? createProjectThreadNavigation(payload) : null
      set({ pendingProjectThreadOpen: pending })
      return pending
    },

    consumePendingProjectThreadOpen(projectKey, threadIds, expectedRequestId) {
      const pending = get().pendingProjectThreadOpen
      if (!pending || (expectedRequestId != null && pending.requestId !== expectedRequestId)) return null
      if (normalizeWorkspaceProjectKey(projectKey) !== pending.projectKey) return null
      set({ pendingProjectThreadOpen: null })
      return [...threadIds].includes(pending.threadId) ? pending : null
    },

    clearPendingProjectThreadOpen(projectKey, threadId, expectedRequestId) {
      const pending = get().pendingProjectThreadOpen
      if (!pending || (expectedRequestId != null && pending.requestId !== expectedRequestId)) return
      if (projectKey != null && normalizeWorkspaceProjectKey(projectKey) !== pending.projectKey) return
      if (threadId != null && pending.threadId !== threadId) return
      set({ pendingProjectThreadOpen: null })
    }
  }
}
