import { confirmThreadArchive, needsThreadTreeArchiveConfirmation } from './confirmThreadArchive'
import { performThreadArchive } from './threadArchive'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ThreadSummary } from '../types/thread'

export function getWorkspaceArchiveThreads(projectKey: string, fallback: ThreadSummary[]): ThreadSummary[] {
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const key = normalizeWorkspaceProjectKey(projectKey)
  const project = [...projects, ...(chat ? [chat] : [])].find(entry =>
    normalizeWorkspaceProjectKey(entry.projectId ?? entry.path) === key)
  const threads = (project?.threads ?? fallback).filter((entry): entry is ThreadSummary =>
    entry != null && typeof entry === 'object' && typeof (entry as ThreadSummary).id === 'string')
  const ids = new Set(threads.map(entry => entry.id))
  return [...threads, ...fallback.filter(entry => !ids.has(entry.id))]
}

export function archiveWorkspaceThread(
  workspacePath: string,
  thread: ThreadSummary,
  t: (key: string, vars?: Record<string, string | number>) => string
): Promise<boolean> {
  const threads = getWorkspaceArchiveThreads(workspacePath, [thread])
  const running = needsThreadTreeArchiveConfirmation(thread.id, threads)
  const projectKey = normalizeWorkspaceProjectKey(workspacePath)
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const project = [...projects, ...(chat ? [chat] : [])].find(entry =>
    normalizeWorkspaceProjectKey(entry.projectId ?? entry.path) === projectKey)
  const connectionKey = project?.connectionKey ?? workspacePath
  const connectionId = project?.connectionId
  return confirmThreadArchive(JSON.stringify([projectKey, thread.id]), running, t, () => performThreadArchive({
    projectKey, threadId: thread.id, threads, t,
    archive: () => window.api.workspace.archiveThread(workspacePath, thread.id),
    unarchive: () => window.api.appServer.sendConnectionRequest(connectionKey, 'thread/unarchive', {
      threadId: thread.id
    }, undefined, { connectionId })
  }))
}
