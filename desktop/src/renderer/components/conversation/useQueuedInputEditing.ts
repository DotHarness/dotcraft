import { useCallback, useEffect, useRef, useState } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import type { QueuedTurnInput } from '../../types/conversation'
import { queuedInputToComposerDraft, type ComposerDraftSnapshot } from '../../utils/composerHistory'
import type { ComposerFollowUpResult } from '../../utils/composerFollowUp'

export interface QueuedEditPosition {
  previousId: string | null
  nextId: string | null
}

/** Moves `id` back between its former neighbors when either is still queued. */
export function placeAtQueuedEditPosition(ids: readonly string[], id: string, position: QueuedEditPosition): string[] {
  const rest = ids.filter((value) => value !== id)
  if (rest.length === ids.length) return [...ids]
  const afterPrevious = position.previousId ? rest.indexOf(position.previousId) : -1
  const index = afterPrevious >= 0
    ? afterPrevious + 1
    : position.nextId && rest.includes(position.nextId) ? rest.indexOf(position.nextId) : -1
  if (index < 0) return [...ids]
  return [...rest.slice(0, index), id, ...rest.slice(index)]
}

export function useQueuedInputEditing({ threadId, applyDraft, onError }: {
  threadId: string
  applyDraft: (draft: ComposerDraftSnapshot) => void
  onError: (error: unknown) => void
}) {
  const [editingQueuedInputId, setEditingQueuedInputId] = useState<string | null>(null)
  const [isEditingQueuedInput, setIsEditingQueuedInput] = useState(false)
  const editingRef = useRef<string | null>(null)
  const positionRef = useRef<QueuedEditPosition | null>(null)

  const clearEditPosition = useCallback((): void => {
    positionRef.current = null
    setIsEditingQueuedInput(false)
  }, [])

  useEffect(() => clearEditPosition, [threadId, clearEditPosition])

  const editQueuedInput = useCallback(async (queuedInputId: string): Promise<void> => {
    if (editingRef.current) return
    const queue = useConversationStore.getState().queuedInputs
    const index = queue.findIndex((item) => item.id === queuedInputId)
    const queued = queue[index]
    if (!queued || (queued.status !== 'queued' && queued.status !== 'guidancePending') || queued.triggerKind || queued.sentAsGoal === true) return

    editingRef.current = queuedInputId
    setEditingQueuedInputId(queuedInputId)
    try {
      const draft = await queuedInputToComposerDraft(queued)
      const res = await window.api.appServer.sendRequest('turn/queue/remove', { threadId, queuedInputId }) as {
        queuedInputs?: unknown[]
      }
      useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
      positionRef.current = { previousId: queue[index - 1]?.id ?? null, nextId: queue[index + 1]?.id ?? null }
      setIsEditingQueuedInput(true)
      applyDraft(draft)
    } catch (err) {
      onError(err)
    } finally {
      editingRef.current = null
      setEditingQueuedInputId(null)
    }
  }, [applyDraft, onError, threadId])

  /** Returns the edited submission to its original place in the queue. */
  const placeEditedSubmission = useCallback(async (result: ComposerFollowUpResult): Promise<void> => {
    const position = positionRef.current
    clearEditPosition()
    const id = result?.queuedInput?.id
    const queuedInputs = result?.queuedInputs
    if (!position || !id || !queuedInputs) return
    const ids = queuedInputs.map((item) => item.id)
    const ordered = placeAtQueuedEditPosition(ids, id, position)
    if (ordered.every((value, index) => value === ids[index])) return
    const res = await window.api.appServer.sendRequest('turn/queue/reorder', { threadId, orderedQueuedInputIds: ordered }) as {
      queuedInputs?: unknown[]
    }
    useConversationStore.getState().setQueuedInputs((res.queuedInputs ?? []) as QueuedTurnInput[])
  }, [clearEditPosition, threadId])

  const hasEditPosition = useCallback((): boolean => positionRef.current !== null, [])

  return { editingQueuedInputId, isEditingQueuedInput, editQueuedInput, placeEditedSubmission, hasEditPosition, clearEditPosition }
}
