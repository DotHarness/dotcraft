import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import type { ThreadSummary } from '../types/thread'

type Translate = (key: string, vars?: Record<string, string | number>) => string
const pending = new Set<string>()

export function archiveRunningDialogOptions(t: Translate) {
  return {
    title: t('threadArchive.confirmRunning.title'),
    message: t('threadArchive.confirmRunning.message'),
    confirmLabel: t('threadArchive.confirmRunning.confirm'),
    cancelLabel: t('common.cancel'),
    danger: true
  }
}

export function needsArchiveConfirmation(thread: ThreadSummary): boolean {
  const runtime = thread.runtime
  return !!(runtime?.running || runtime?.busy || runtime?.activeTurnId || runtime?.waitingOnApproval
    || runtime?.waitingOnInput || runtime?.waitingOnPlanConfirmation)
}

export async function confirmThreadArchive(
  key: string,
  running: boolean,
  t: Translate,
  archive: () => Promise<boolean>
): Promise<boolean> {
  if (pending.has(key)) return false
  pending.add(key)
  try {
    if (running && !await requestConfirmDialog(archiveRunningDialogOptions(t)).result) return false
    return await archive()
  } finally {
    pending.delete(key)
  }
}
