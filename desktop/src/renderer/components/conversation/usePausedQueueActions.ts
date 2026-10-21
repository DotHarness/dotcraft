import { useCallback } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import type { QueuedTurnInput } from '../../types/conversation'

/** Actions for a queue paused by an interrupted Turn: resume it, start one entry now, or clear it. */
export function usePausedQueueActions(threadId: string, t: (key: string, vars?: Record<string, string | number>) => string) {
  const startQueued = useCallback(async (queuedInputId?: string): Promise<void> => {
    try {
      await window.api.appServer.sendRequest('turn/queue/start', { threadId, queuedInputId })
    } catch (err) {
      addToast(t('composer.queueStartFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }, [threadId, t])

  const clearPausedQueue = useCallback(async (): Promise<void> => {
    const userEntries = useConversationStore.getState().queuedInputs.filter((item) => !item.triggerKind)
    for (const item of userEntries) {
      const res = await window.api.appServer.sendRequest('turn/queue/remove', { threadId, queuedInputId: item.id }) as {
        queuedInputs?: unknown[]
      }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
    }
  }, [threadId])

  return {
    resumeQueue: useCallback(() => startQueued(), [startQueued]),
    sendQueuedNow: useCallback((queuedInputId: string) => startQueued(queuedInputId), [startQueued]),
    clearPausedQueue
  }
}
