import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'
import { needsThreadTreeArchiveConfirmation } from './confirmThreadArchive'
import { getWorkspaceArchiveThreads } from './archiveWorkspaceThread'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export async function archiveProjectThreads({
  projectLabel,
  workspacePath,
  connectionKey,
  threads,
  live,
  t
}: {
  projectLabel: string
  workspacePath: string
  connectionKey: string
  threads: ThreadSummary[]
  live: boolean
  t: Translate
}): Promise<void> {
  const count = threads.length
  if (count === 0) return
  const { threadList, runningTurnThreadIds } = useThreadStore.getState()
  const listedIds = new Set(threadList.map(thread => thread.id))
  const currentThreads = live
    ? [...threadList, ...threads.filter(thread => !listedIds.has(thread.id))]
    : getWorkspaceArchiveThreads(connectionKey, threads)
  const running = threads.some(thread => needsThreadTreeArchiveConfirmation(
    thread.id, currentThreads, live ? runningTurnThreadIds : undefined
  ))
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

  const results = await Promise.allSettled(threads.map((thread) =>
    window.api.workspace.archiveThread(workspacePath, thread.id, connectionKey)))
  const archived = threads.filter((_, index) => results[index].status === 'fulfilled')

  const store = useThreadStore.getState()
  if (live && archived.length > 0 && store.threadListProjectKey === connectionKey) {
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
