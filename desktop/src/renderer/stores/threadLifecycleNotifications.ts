import { translate } from '../../shared/locales'
import { useThreadStore } from './threadStore'
import { getThreadConversation } from './conversationStore'
import { getThreadConnection } from './threadConnectionStore'
import { useSubAgentStore } from './subAgentStore'
import { useTurnBookmarkStore } from './turnBookmarkStore'
import { addToast } from './toastStore'
import { normalizeThreadRuntime } from '../utils/threadRuntime'
import { getSubAgentParentThreadId, isSubAgentThread } from '../utils/subAgentThreads'
import { reloadThreadAfterRollback } from '../utils/threadRollback'
import type { Thread, ThreadSummary, ThreadGoal, ThreadReadState, ThreadRuntimeSnapshot } from '../types/thread'
import type { QueuedTurnInput } from '../types/conversation'
import type { ThreadNotificationContext } from './threadNotificationContext'

export function applyThreadLifecycleNotification(
  method: string,
  p: Record<string, unknown>,
  context: ThreadNotificationContext
): boolean {
  const { addThread: doAddThread, updateThreadStatus: doUpdateStatus } = useThreadStore.getState()
  const conversationFor = (threadId: string | null | undefined) =>
    threadId ? getThreadConversation(threadId)?.getState() : undefined
  switch (method) {
    case 'thread/started': {
      const pp = p as { thread: ThreadSummary }
      if (context.foreground) doAddThread(pp.thread)
      if (pp.thread && isSubAgentThread(pp.thread)) {
        const parentThreadId = getSubAgentParentThreadId(pp.thread)
        if (parentThreadId) {
          void useSubAgentStore
            .getState()
            .fetchChildren(parentThreadId)
            .catch(() => {})
        }
      }
      return true
    }

    case 'thread/updated': {
      const pp = p as { thread?: Thread }
      if (pp.thread) {
        if (context.foreground) {
          const threads = useThreadStore.getState()
          threads.upsertThreads([{ ...pp.thread, runtime: threads.runtimeSnapshots.get(pp.thread.id) ?? pp.thread.runtime }])
        }
        if (useThreadStore.getState().activeThreadId === pp.thread.id) {
          useThreadStore.getState().setActiveThread(pp.thread)
        }
        context.updateWarmThread(pp.thread)
      }
      return true
    }

    case 'thread/renamed': {
      const pp = p as { threadId: string; displayName: string }
      if (pp.displayName?.trim()) {
        useThreadStore.getState().renameThread(pp.threadId, pp.displayName)
      }
      return true
    }

    case 'thread/deleted': {
      const pp = p as { threadId: string }
      if (pp.threadId) {
        void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: pp.threadId }).catch(() => {})
        useTurnBookmarkStore.getState().forgetThread(pp.threadId)
      }
      context.releaseWarmThread(pp.threadId)
      useThreadStore.getState().removeThreadTree(pp.threadId)
      return true
    }

    case 'thread/rolledBack': {
      const threadId = (p as { threadId?: string }).threadId
      if (!threadId) return true
      if (useThreadStore.getState().activeThreadId === threadId) {
        void reloadThreadAfterRollback(threadId).catch((err: unknown) => {
          console.error('Failed to reload a rolled back thread:', err)
        })
      } else {
        context.releaseWarmThread(threadId)
      }
      return true
    }

    case 'thread/archived': {
      context.releaseWarmThread(String(p.threadId))
      useThreadStore.getState().removeThreadTree(String(p.threadId))
      return true
    }

    case 'thread/unarchived': {
      doUpdateStatus((p as { threadId: string }).threadId, 'active')
      return true
    }

    case 'thread/queue/updated': {
      const pp = p as { threadId?: string; queuedInputs?: unknown[] }
      conversationFor(pp.threadId)?.setQueuedInputs((pp.queuedInputs ?? []) as QueuedTurnInput[])
      return true
    }

    case 'thread/readState/changed': {
      const pp = p as { threadId?: string; readState?: ThreadReadState }
      if (pp.threadId && pp.readState) {
        useThreadStore.getState().setThreadReadState(pp.threadId, pp.readState)
      }
      return true
    }

    case 'thread/goal/updated': {
      const pp = p as { threadId?: string; goal?: ThreadGoal }
      if (pp.goal) {
        useThreadStore.getState().setThreadGoal(pp.goal)
      }
      return true
    }

    case 'thread/goal/cleared': {
      const pp = p as { threadId?: string }
      if (pp.threadId) {
        useThreadStore.getState().clearThreadGoal(pp.threadId)
      }
      return true
    }

    case 'thread/status/changed': {
      const pp = p as {
        threadId?: string
        runtime?: Partial<ThreadRuntimeSnapshot>
      }
      const threadId = typeof pp.threadId === 'string' ? pp.threadId : ''
      if (!threadId) return true

      const threadStore = useThreadStore.getState()
      const threadSummary = threadStore.threadList.find((thread) => thread.id === threadId)
      const runtimeSnapshot = normalizeThreadRuntime(pp.runtime ?? {})
      threadStore.applyRuntimeSnapshot(threadId, runtimeSnapshot, {
        isDesktopOrigin:
          (threadSummary?.originChannel ?? getThreadConnection(threadId)?.originChannel)?.toLowerCase() ===
          'dotcraft-desktop'
      })
      const conversation = conversationFor(threadId)
      if (conversation) {
        const pendingApprovals =
          conversation.pendingApprovals.length > 0
            ? conversation.pendingApprovals
            : conversation.pendingApproval != null
              ? [conversation.pendingApproval]
              : []
        if (!runtimeSnapshot.waitingOnApproval) {
          for (const pendingApproval of pendingApprovals) {
            if (
              pendingApproval.locallySubmittedDecision == null &&
              (pendingApproval.threadId == null || pendingApproval.threadId === threadId)
            ) {
              conversation.onApprovalNoLongerPending({
                threadId,
                turnId: pendingApproval.turnId,
                requestId: pendingApproval.requestId,
                nextTurnStatus: runtimeSnapshot.running ? 'running' : 'idle'
              })
            }
          }
        }
        conversation.setMaintenanceKind(runtimeSnapshot.maintenanceKind)
      }
      return true
    }

    case 'thread/error': {
      const tid = (p.threadId as string | undefined) ?? ''
      const reason = (p.reason as string | undefined) ?? (p.message as string | undefined) ?? 'unknown'
      if (reason === 'not-found' || reason.includes('not found')) {
        useThreadStore.getState().removeThread(tid)
        addToast(translate(context.locale, 'toast.threadNotFound'), 'warning')
      }
      return true
    }

    default:
      return false
  }
}
