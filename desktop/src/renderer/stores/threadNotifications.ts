import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { getThreadConversation } from './conversationStore'
import { useConnectionStore } from './connectionStore'
import { getThreadConnection, matchesThreadConnection } from './threadConnectionStore'
import { useSubAgentStore } from './subAgentStore'
import { stageThreadNotification } from './threadRestoration'
import { notificationThreadId, type ThreadNotificationContext } from './threadNotificationContext'
import { applyThreadLifecycleNotification } from './threadLifecycleNotifications'
import { applyThreadTranscriptNotification } from './threadTranscriptNotifications'

export function applyThreadNotification(payload: RawNotificationPayload, context: ThreadNotificationContext): boolean {
  const connection = useConnectionStore.getState()
  if (payload.connectionId)
    context = {
      ...context,
      foreground:
        connection.status === 'connected' &&
        payload.connectionKey === connection.connectionKey &&
        payload.connectionId === connection.connectionId
    }
  const threadId = notificationThreadId(payload)
  let ownerThreadId = threadId
  if (threadId && !getThreadConnection(threadId) && payload.method === 'thread/status/changed') {
    ownerThreadId =
      [...useSubAgentStore.getState().childrenByParent.entries()].find(([, children]) =>
        children.some((child) => child.childThreadId === threadId)
      )?.[0] ?? threadId
  }
  const target = ownerThreadId ? getThreadConnection(ownerThreadId) : undefined
  if (!context.foreground && !target) return false
  if (target && !matchesThreadConnection(ownerThreadId!, payload)) return false
  if (stageThreadNotification(threadId, () => applyThreadNotification(payload, context))) return true
  if (payload.method === 'mcpServer/startupStatus/updated' && threadId) return true
  const p = (payload.params ?? {}) as Record<string, unknown>
  if (payload.method.startsWith('item/') && typeof p.turnId === 'string') {
    const conversation = threadId ? getThreadConversation(threadId)?.getState() : undefined
    conversation?.clearStreamRetry(p.turnId)
  }
  return (
    applyThreadLifecycleNotification(payload.method, p, context) ||
    applyThreadTranscriptNotification(payload.method, p, context)
  )
}
