import type { KnownNotificationPayload } from '../../shared/appServerBoundary'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { useThreadStore } from '../stores/threadStore'
import { getSubAgentParentThreadId } from '../utils/subAgentThreads'
import type { ThreadReadState, ThreadSummary } from '../types/thread'

export const THREAD_LIST_PAGE_SIZE = 50

export interface ThreadListPage {
  data: ThreadSummary[]
  nextCursor?: string | null
  readStates?: Record<string, ThreadReadState>
}

export function createThreadListPages(
  projectKey: string,
  connection: { connectionKey: string | null; connectionId: string | null },
  isCurrent: () => boolean
) {
  const removedIds = new Set<string>()
  const updates = new Map<string, Partial<Pick<ThreadSummary, 'displayName' | 'readState'>>>()
  const stopNotifications = window.api.appServer.onNotification((notification: KnownNotificationPayload) => {
    if (!isCurrent() || notification.foreground === false
      || (notification.connectionKey != null && notification.connectionKey !== connection.connectionKey)
      || (notification.connectionId != null && notification.connectionId !== connection.connectionId)) return
    const params = notification.params as { threadId?: string; displayName?: string; readState?: ThreadReadState }
    if (!params.threadId) return
    if (notification.method === 'thread/deleted' || notification.method === 'thread/archived') {
      removedIds.add(params.threadId)
    } else if (notification.method === 'thread/unarchived') {
      removedIds.delete(params.threadId)
    } else if (notification.method === 'thread/readState/changed' && params.readState) {
      updates.set(params.threadId, { ...updates.get(params.threadId), readState: params.readState })
    } else if (notification.method === 'thread/renamed' && params.displayName?.trim()) {
      updates.set(params.threadId, { ...updates.get(params.threadId), displayName: params.displayName })
    }
  })

  const currentThreads = (): ThreadSummary[] => {
    const store = useThreadStore.getState()
    return store.threadListProjectKey == null || normalizeWorkspaceProjectKey(store.threadListProjectKey) === projectKey
      ? store.threadList : []
  }

  return {
    dispose: stopNotifications,
    beginRequest() {
      const baseline = new Map(currentThreads().map((thread) => [thread.id, thread]))
      const runtimeBaseline = useThreadStore.getState().runtimeSnapshots
      return (page: ThreadListPage, replace: boolean): void => {
        const store = useThreadStore.getState()
        const current = currentThreads()
        const currentById = new Map(current.map((thread) => [thread.id, thread]))
        const incoming = new Map<string, ThreadSummary>()
        for (const thread of page.data) {
          const parent = getSubAgentParentThreadId(thread)
          if (removedIds.has(thread.id) || (parent != null && removedIds.has(parent))
            || (thread.source?.subAgent?.rootThreadId != null && removedIds.has(thread.source.subAgent.rootThreadId))) continue
          const existing = currentById.get(thread.id)
          if (!replace && existing) continue
          if (replace && baseline.has(thread.id) && !existing) continue
          const live = existing !== baseline.get(thread.id) ? existing : undefined
          const runtime = store.runtimeSnapshots.get(thread.id)
          incoming.set(thread.id, {
            ...thread,
            readState: page.readStates?.[thread.id] ?? thread.readState,
            ...updates.get(thread.id),
            ...(runtime !== runtimeBaseline.get(thread.id) ? { runtime } : {}),
            ...live
          })
        }
        if (!replace) {
          store.upsertThreads([...incoming.values()], runtimeBaseline)
          return
        }
        for (const thread of current) {
          const changed = thread !== baseline.get(thread.id)
          const retained = thread.id === store.activeThreadId || store.runningTurnThreadIds.has(thread.id)
            || store.runtimeSnapshots.get(thread.id)?.busy || store.parkedApprovals.has(thread.id)
            || store.parkedUserInputs.has(thread.id) || store.pendingApprovalThreadIds.has(thread.id)
            || store.pendingUserInputThreadIds.has(thread.id) || store.pendingPlanConfirmationThreadIds.has(thread.id)
          if ((changed || retained) && !incoming.has(thread.id)) incoming.set(thread.id, thread)
        }
        store.setThreadList([...incoming.values()], projectKey)
      }
    }
  }
}
