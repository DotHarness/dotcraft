import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'
import { stopBeforeArchive } from '../../shared/stopBeforeArchive'
import { needsArchiveConfirmation } from './confirmThreadArchive'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export async function archiveProjectThreads({
  projectLabel,
  workspacePath,
  threads,
  live,
  t
}: {
  projectLabel: string
  workspacePath: string
  threads: ThreadSummary[]
  live: boolean
  t: Translate
}): Promise<void> {
  const count = threads.length
  if (count === 0) return
  const { runningTurnThreadIds } = useThreadStore.getState()
  const running = threads.some((thread) => needsArchiveConfirmation(thread) || runningTurnThreadIds.has(thread.id))
  const one = count === 1
  const confirmed = await requestConfirmDialog({
    title: running
      ? t(one ? 'projectsRail.archiveChatsRunningTitleOne' : 'projectsRail.archiveChatsRunningTitleMany', { count })
      : t(one ? 'projectsRail.archiveChatsTitleOne' : 'projectsRail.archiveChatsTitleMany', { count }),
    message: t(running ? 'projectsRail.archiveChatsRunningMessage' : 'projectsRail.archiveChatsMessage', {
      project: projectLabel
    }),
    confirmLabel: t(running ? 'projectsRail.archiveChatsConfirmRunning' : 'projectsRail.archiveChatsConfirm'),
    cancelLabel: t('common.cancel'),
    danger: running
  }).result
  if (!confirmed) return

  const archiveOne = live
    ? (threadId: string) => stopBeforeArchive((method, params) => window.api.appServer.sendRequest(method, params), threadId)
    : (threadId: string) => window.api.workspace.archiveThread(workspacePath, threadId)
  const results = await Promise.allSettled(threads.map((thread) => archiveOne(thread.id)))
  const archived = threads.filter((_, index) => results[index].status === 'fulfilled')

  if (live && archived.length > 0) {
    const store = useThreadStore.getState()
    if (archived.some((thread) => thread.id === store.activeThreadId)) store.setActiveThreadId(null)
    for (const thread of archived) useThreadStore.getState().removeThreadTree(thread.id)
  }

  if (archived.length === count) {
    addToast(t(one ? 'projectsRail.archiveChatsDoneOne' : 'projectsRail.archiveChatsDoneMany', { count }), 'success')
  } else if (archived.length > 0) {
    addToast(t('projectsRail.archiveChatsPartial', { archived: archived.length, count }), 'error')
  } else {
    addToast(t('projectsRail.archiveChatsFailed', { project: projectLabel }), 'error')
  }
}
