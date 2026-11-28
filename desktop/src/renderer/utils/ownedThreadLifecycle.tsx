import { Archive, Pause, Play } from 'lucide-react'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import type { ContextMenuEntry } from '../components/ui/ContextMenu'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { leaveSpace, useSpaceStore } from '../stores/spaceStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export async function toggleOwnedThreadPaused(
  record: OwnedThreadRecord,
  t: Translate
): Promise<OwnedThreadRecord | null> {
  const nextPaused = !record.paused
  try {
    const updated = await window.api.threadTasks.setOwnedPaused(record.threadId, nextPaused)
    if (updated) {
      const store = useOwnedThreadsStore.getState()
      store.setRecords(
        store.records.map((candidate) => (candidate.threadId === updated.threadId ? updated : candidate))
      )
    }
    return updated
  } catch (error) {
    addToast(
      t('conductor.lifecycle.actionFailed', { error: error instanceof Error ? error.message : String(error) }),
      'error'
    )
    return null
  }
}

export async function confirmAndArchiveOwnedThread(
  record: OwnedThreadRecord,
  t: Translate
): Promise<boolean> {
  const confirmed = await requestConfirmDialog({
    title: t('conductor.archiveConfirm.title'),
    message: t('conductor.archiveConfirm.message'),
    confirmLabel: t('conductor.archiveConfirm.confirm'),
    cancelLabel: t('common.cancel'),
    danger: true
  }).result
  if (!confirmed) return false

  try {
    const wasViewing = useThreadStore.getState().activeThreadId === record.threadId
    const inSpace = Boolean(useSpaceStore.getState().returnTo) || wasViewing
    const archived = await window.api.threadTasks.archiveOwned(record.threadId)
    if (!archived) return false
    const store = useOwnedThreadsStore.getState()
    store.setRecords(store.records.filter((candidate) => candidate.threadId !== record.threadId))
    if (wasViewing) {
      useThreadStore.getState().setActiveThreadId(null)
      useThreadStore.getState().setActiveThread(null)
    }
    if (inSpace) await leaveSpace()
    return true
  } catch (error) {
    addToast(
      t('conductor.lifecycle.actionFailed', { error: error instanceof Error ? error.message : String(error) }),
      'error'
    )
    return false
  }
}

export function buildOwnedThreadMenuEntries(
  record: OwnedThreadRecord,
  t: Translate,
  onDone?: () => void
): ContextMenuEntry[] {
  return [
    {
      label: record.paused ? t('conductor.resume') : t('conductor.pause'),
      icon: record.paused ? <Play size={14} aria-hidden /> : <Pause size={14} aria-hidden />,
      onClick: () => {
        onDone?.()
        void toggleOwnedThreadPaused(record, t)
      }
    },
    {
      label: t('conductor.archive'),
      icon: <Archive size={14} aria-hidden />,
      danger: true,
      onClick: () => {
        onDone?.()
        void confirmAndArchiveOwnedThread(record, t)
      }
    }
  ]
}
