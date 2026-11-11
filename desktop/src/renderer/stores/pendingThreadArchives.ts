import type { ThreadSummary } from '../types/thread'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { collectThreadTreeIds } from '../utils/subAgentThreads'

const pending = new Map<string, Map<string, Set<string>>>()

export function beginPendingThreadArchive(projectKey: string, threadId: string, ids: Set<string>): void {
  const key = normalizeWorkspaceProjectKey(projectKey)
  const project = pending.get(key) ?? new Map<string, Set<string>>()
  project.set(threadId, ids)
  pending.set(key, project)
}

export function endPendingThreadArchive(projectKey: string, threadId: string): void {
  const key = normalizeWorkspaceProjectKey(projectKey)
  const project = pending.get(key)
  project?.delete(threadId)
  if (project?.size === 0) pending.delete(key)
}

export function isPendingThreadArchive(projectKey: string | null, threadId: string): boolean {
  const project = pending.get(normalizeWorkspaceProjectKey(projectKey))
  return project != null && [...project.values()].some(ids => ids.has(threadId))
}

export function filterPendingThreadArchives(projectKey: string | null, threads: ThreadSummary[]): ThreadSummary[] {
  const project = pending.get(normalizeWorkspaceProjectKey(projectKey))
  if (!project) return threads
  const hidden = new Set<string>()
  for (const [rootId, ids] of project) {
    for (const id of collectThreadTreeIds(threads, rootId)) ids.add(id)
    for (const thread of threads) {
      if (thread.source?.subAgent?.rootThreadId === rootId) ids.add(thread.id)
    }
    for (const id of ids) hidden.add(id)
  }
  return threads.filter(thread => !hidden.has(thread.id))
}
