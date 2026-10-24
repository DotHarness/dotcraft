import { confirmThreadArchive, needsThreadTreeArchiveConfirmation } from './confirmThreadArchive'
import { addToast } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ThreadSummary } from '../types/thread'

export function getWorkspaceArchiveThreads(workspacePath: string, fallback: ThreadSummary[]): ThreadSummary[] {
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const key = normalizeWorkspaceProjectKey(workspacePath)
  const project = [...projects, ...(chat ? [chat] : [])].find(entry =>
    entry.kind !== 'remote' && normalizeWorkspaceProjectKey(entry.path) === key)
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
  return confirmThreadArchive(JSON.stringify([workspacePath, thread.id]), running, t, async () => {
    try {
      await window.api.workspace.archiveThread(workspacePath, thread.id)
      return true
    } catch (error) {
      addToast(t('threadArchive.toast.archiveFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
      return false
    }
  })
}
