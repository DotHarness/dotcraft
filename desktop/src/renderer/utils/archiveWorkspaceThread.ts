import { confirmThreadArchive, needsArchiveConfirmation } from './confirmThreadArchive'
import { addToast } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'

export function archiveWorkspaceThread(
  workspacePath: string,
  thread: ThreadSummary,
  t: (key: string, vars?: Record<string, string | number>) => string
): Promise<boolean> {
  return confirmThreadArchive(JSON.stringify([workspacePath, thread.id]), needsArchiveConfirmation(thread), t, async () => {
    try {
      await window.api.workspace.archiveThread(workspacePath, thread.id)
      return true
    } catch (error) {
      addToast(t('threadArchive.toast.archiveFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
      return false
    }
  })
}
