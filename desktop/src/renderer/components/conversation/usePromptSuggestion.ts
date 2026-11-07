import { useCallback, useEffect } from 'react'
import { useConfigValue } from '../../stores/configStore'
import { useConversationStore } from '../../stores/conversationStore'
import { usePromptSuggestionStore } from '../../stores/promptSuggestionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useThreadRestorationStore } from '../../stores/threadRestoration'
import { hasTurnResponseContent } from '../../utils/turnPresentation'

interface PromptSuggestionOptions {
  threadId: string
  canSuggest: boolean
  visible: boolean
}

export function usePromptSuggestion({ threadId, canSuggest, visible }: PromptSuggestionOptions): {
  suggestion: string | null
  dismiss: () => void
  accept: () => string | null
} {
  const enabled = useConfigValue('PromptSuggestions.Enabled') === true
  const turns = useConversationStore((state) => state.turns)
  const turnDiffs = useConversationStore((state) => state.turnDiffs)
  const turnStatus = useConversationStore((state) => state.turnStatus)
  const queuedCount = useConversationStore((state) => state.queuedInputs.length)
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const restoring = useThreadRestorationStore((state) => state.restoringThreadId === threadId)
  const entry = usePromptSuggestionStore((state) => state.entries.get(threadId))
  const lastTurn = turns.at(-1)
  const currentTurn = lastTurn?.threadId === threadId ? lastTurn : null
  const eligibleTurn = currentTurn?.status === 'completed'
    && hasTurnResponseContent(currentTurn, (turnDiffs.get(currentTurn.id)?.files.length ?? 0) > 0)
    ? currentTurn : null
  const available = enabled && !restoring && canSuggest && activeThreadId === threadId && turnStatus === 'idle' && queuedCount === 0
  const suggestion = available && visible && entry?.turnId === eligibleTurn?.id ? entry?.suggestion ?? null : null

  const dismiss = useCallback((): void => {
    if (currentTurn) usePromptSuggestionStore.getState().dismiss(threadId, currentTurn)
  }, [currentTurn, threadId])

  const accept = useCallback((): string | null => {
    dismiss()
    return suggestion
  }, [dismiss, suggestion])

  useEffect(() => {
    if (!enabled || restoring || activeThreadId !== threadId || !currentTurn) return
    const store = usePromptSuggestionStore.getState()
    if (queuedCount > 0) {
      store.invalidate(threadId)
      return
    }
    store.invalidate(threadId, currentTurn)
    if (turnStatus === 'idle' && eligibleTurn && canSuggest && visible) store.request(threadId, eligibleTurn)
  }, [activeThreadId, canSuggest, currentTurn, eligibleTurn, enabled, queuedCount, restoring, threadId, turnStatus, visible])

  return { suggestion, dismiss, accept }
}
