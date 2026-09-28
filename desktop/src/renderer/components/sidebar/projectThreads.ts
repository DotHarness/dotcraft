import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../../../shared/workspaceProjectKey'
import type { ThreadSummary } from '../../types/thread'
import { isInternalThread } from '../../utils/internalThreads'
import { isSubAgentThread } from '../../utils/subAgentThreads'

export function projectIdentity(project: WorkspaceProjectSummary): string {
  return project.projectId?.trim() || normalizeWorkspaceProjectKey(project.path)
}

export function isRemoteProject(project: WorkspaceProjectSummary): boolean {
  return project.kind === 'remote'
}

export function isColdProject(project: WorkspaceProjectSummary): boolean {
  return project.state === 'cold'
}

export function isProjectForeground(
  project: WorkspaceProjectSummary,
  foregroundProjectId: string,
  foregroundWorkspacePath: string
): boolean {
  const projectId = projectIdentity(project)
  const foregroundId = foregroundProjectId.trim()
  if (!foregroundId) {
    return sameWorkspaceProjectKey(project.path, foregroundWorkspacePath)
  }
  if (sameWorkspaceProjectKey(projectId, foregroundId)) {
    return true
  }
  if (isRemoteProject(project) || isRemoteProjectKey(foregroundId)) {
    return false
  }
  return sameWorkspaceProjectKey(project.path, foregroundWorkspacePath)
}

export function isForegroundThreadListForProject(
  threadListProjectKey: string | null,
  projectKey: string
): boolean {
  return sameWorkspaceProjectKey(threadListProjectKey, projectKey)
}

function isThreadSummary(value: unknown): value is ThreadSummary {
  return Boolean(value && typeof value === 'object' && typeof (value as { id?: unknown }).id === 'string')
}

export function visibleProjectThreads(threads: unknown[]): ThreadSummary[] {
  return threads
    .filter(isThreadSummary)
    .filter((thread) => !isInternalThread(thread))
    .filter((thread) => thread.status?.toLowerCase() !== 'archived')
    // Subagent threads are surfaced via the dock / Subagents tab, not the sidebar.
    .filter((thread) => !isSubAgentThread(thread))
}

export function filterThreadsByQuery(threads: ThreadSummary[], searchQuery: string): ThreadSummary[] {
  const query = searchQuery.trim().toLowerCase()
  if (!query) return threads
  return threads.filter((thread) => (thread.displayName ?? '').toLowerCase().includes(query))
}

export function filterProjectThreads(project: WorkspaceProjectSummary, searchQuery: string): ThreadSummary[] {
  return filterThreadsByQuery(visibleProjectThreads(project.threads), searchQuery)
}

export function isThreadRunning(thread: ThreadSummary): boolean {
  return thread.runtime?.running === true || thread.runtime?.busy === true
}

export function isThreadWaiting(thread: ThreadSummary): boolean {
  return thread.runtime?.waitingOnApproval === true
    || thread.runtime?.waitingOnInput === true
    || thread.runtime?.waitingOnPlanConfirmation === true
}
