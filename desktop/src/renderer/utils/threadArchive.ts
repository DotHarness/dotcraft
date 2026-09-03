import { collectThreadTreeIds, useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { addToast, showToast } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export interface ArchiveThreadOptions {
  threadId: string
  t: Translate
}

/**
 * Archives immediately and lets Undo reverse it through thread/unarchive; nothing
 * waits for the toast to expire, so quitting mid-window loses nothing.
 */
export async function archiveThreadWithUndo({ threadId, t }: ArchiveThreadOptions): Promise<boolean> {
  const before = useThreadStore.getState()
  const treeIds = collectThreadTreeIds(before.threadList, threadId)
  const captured = before.threadList.filter((thread) => treeIds.has(thread.id))
  const wasActive = before.activeThreadId === threadId

  try {
    await window.api.appServer.sendRequest('thread/archive', { threadId })
  } catch (err) {
    addToast(t('threadArchive.toast.archiveFailed', { error: errorText(err) }), 'error')
    return false
  }

  const store = useThreadStore.getState()
  if (wasActive) store.setActiveThreadId(null)
  store.removeThreadTree(threadId)

  showToast({
    message: t('threadArchive.toast.archived'),
    key: `thread-archive:${threadId}`,
    action: {
      label: t('common.undo'),
      icon: 'undo',
      onClick: () => {
        void restoreThread({ threadId, captured, reactivate: wasActive, t })
      }
    }
  })
  return true
}

async function restoreThread({
  threadId,
  captured,
  reactivate,
  t
}: {
  threadId: string
  captured: ThreadSummary[]
  reactivate: boolean
  t: Translate
}): Promise<void> {
  try {
    await window.api.appServer.sendRequest('thread/unarchive', { threadId })
  } catch (err) {
    addToast(t('threadArchive.toast.restoreFailed', { error: errorText(err) }), 'error')
    return
  }
  // thread/statusChanged only updates rows that are still listed, so put the tree back ourselves.
  const store = useThreadStore.getState()
  store.upsertThreads(captured)
  if (reactivate) {
    store.setActiveThreadId(threadId)
    useUIStore.getState().setActiveMainView('conversation')
  }
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}
