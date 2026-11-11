import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { addToast, removeToast, showToast } from '../stores/toastStore'
import { useConnectionStore } from '../stores/connectionStore'
import { filterPendingProjectArchives, useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { beginPendingThreadArchive, endPendingThreadArchive } from '../stores/pendingThreadArchives'
import { beginThreadNavigation, isThreadNavigationCurrent } from '../stores/projectThreadNavigation'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ThreadSummary } from '../types/thread'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { confirmThreadArchive, needsThreadTreeArchiveConfirmation } from './confirmThreadArchive'
import { collectThreadTreeIds } from './subAgentThreads'
import { stopBeforeArchive } from '../../shared/stopBeforeArchive'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export interface ArchiveThreadOptions {
  threadId: string
  t: Translate
}

export function archiveThreadWithUndo({ threadId, t }: ArchiveThreadOptions): Promise<boolean> {
  const state = useThreadStore.getState()
  const connection = useConnectionStore.getState()
  const projectKey = normalizeWorkspaceProjectKey(state.threadListProjectKey ?? connection.connectionKey)
  const send: typeof window.api.appServer.sendRequest = (method, params) => connection.connectionKey
    ? window.api.appServer.sendConnectionRequest(connection.connectionKey, method, params, undefined, {
        connectionId: connection.connectionId ?? undefined
      })
    : window.api.appServer.sendRequest(method, params)
  const running = needsThreadTreeArchiveConfirmation(threadId, state.threadList, state.runningTurnThreadIds)
  return confirmThreadArchive(JSON.stringify([projectKey, threadId]), running, t, () => performThreadArchive({
    projectKey, threadId, threads: state.threadList, t,
    archive: () => stopBeforeArchive(send, threadId),
    unarchive: () => send('thread/unarchive', { threadId })
  }))
}

export async function performThreadArchive({ projectKey, threadId, threads, archive, unarchive, t }: {
  projectKey: string
  threadId: string
  threads: ThreadSummary[]
  archive: () => Promise<unknown>
  unarchive: () => Promise<unknown>
  t: Translate
}): Promise<boolean> {
  projectKey = normalizeWorkspaceProjectKey(projectKey)
  const key = JSON.stringify([projectKey, threadId])
  const before = useThreadStore.getState()
  const ownsForeground = () => normalizeWorkspaceProjectKey(useThreadStore.getState().threadListProjectKey) === projectKey
  const original = ownsForeground() ? before.threadList : threads
  const ids = collectThreadTreeIds(original, threadId)
  const captured = original.filter(thread => ids.has(thread.id))
  const wasActive = ownsForeground() && before.activeThreadId != null && ids.has(before.activeThreadId)
  const projects = useWorkspaceProjectsStore.getState()
  const project = [...projects.projects, ...(projects.chat ? [projects.chat] : [])]
    .find(entry => normalizeWorkspaceProjectKey(entry.projectId ?? entry.path) === projectKey)
  const projectRows = (project?.threads ?? []) as ThreadSummary[]
  const projectCaptured = projectRows.filter(thread => ids.has(thread.id))

  beginPendingThreadArchive(projectKey, threadId, ids)
  if (ownsForeground()) {
    useThreadStore.setState({ threadList: before.threadList.filter(thread => !ids.has(thread.id)) })
    if (wasActive) {
      before.setActiveThreadId(null)
      before.setActiveThread(null)
    }
  }
  const navigation = wasActive ? beginThreadNavigation() : null
  useWorkspaceProjectsStore.setState(state => ({
    projects: state.projects.map(filterPendingProjectArchives),
    chat: state.chat ? filterPendingProjectArchives(state.chat) : null
  }))

  const restore = (stopped: boolean): void => {
    const rows = (entries: ThreadSummary[]) => entries.map(thread => {
      const runtime = ownsForeground() ? useThreadStore.getState().runtimeSnapshots.get(thread.id) ?? thread.runtime : thread.runtime
      return {
        ...thread,
        runtime: stopped && runtime ? {
          ...runtime, running: false, busy: false, activeTurnId: null, activeTurnStartedAt: null,
          waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: false, maintenanceKind: null
        } : runtime
      }
    })
    if (ownsForeground()) {
      const store = useThreadStore.getState()
      store.upsertThreads(rows(captured))
      if (navigation != null && isThreadNavigationCurrent(navigation)) {
        store.setActiveThreadId(before.activeThreadId)
        if (!stopped) store.setActiveThread(before.activeThread)
        useUIStore.getState().setActiveMainView('conversation')
      }
    }
    const restoreProject = (entry: WorkspaceProjectSummary): WorkspaceProjectSummary => {
      if (normalizeWorkspaceProjectKey(entry.projectId ?? entry.path) !== projectKey) return entry
      const current = entry.threads as ThreadSummary[]
      const existing = new Set(current.map(thread => thread.id))
      const missing = rows(projectCaptured).filter(thread => !existing.has(thread.id))
      return filterPendingProjectArchives({
        ...entry, threads: [...current, ...missing],
        threadCount: entry.threadCount + missing.length
      })
    }
    useWorkspaceProjectsStore.setState(state => ({
      projects: state.projects.map(restoreProject),
      chat: state.chat ? restoreProject(state.chat) : null
    }))
  }

  let toastId: string
  const archived = Promise.resolve().then(archive).then(() => {
    endPendingThreadArchive(projectKey, threadId)
    if (ownsForeground()) {
      for (const id of ids) useThreadStore.getState().removeThread(id)
    }
    return true
  }, error => {
    endPendingThreadArchive(projectKey, threadId)
    removeToast(toastId)
    restore(false)
    addToast(t('threadArchive.toast.archiveFailed', { error: errorText(error) }), 'error')
    return false
  })
  toastId = showToast({
    message: t('threadArchive.toast.archived'),
    key: `thread-archive:${key}`,
    action: {
      label: t('common.undo'),
      icon: 'undo',
      onClick: () => {
        void archived.then(async (succeeded) => {
          if (!succeeded) return
          try {
            await unarchive()
            restore(true)
          } catch (error) {
            addToast(t('threadArchive.toast.restoreFailed', { error: errorText(error) }), 'error')
          }
        })
      }
    }
  })
  return archived
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}
