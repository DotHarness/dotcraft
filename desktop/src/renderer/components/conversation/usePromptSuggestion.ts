import { useCallback, useEffect } from 'react'
import { useConfigValue } from '../../stores/configStore'
import { useConversationStore } from '../../stores/conversationStore'
import { usePromptSuggestionStore } from '../../stores/promptSuggestionStore'
import { useThreadStore } from '../../stores/threadStore'
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
  const entry = usePromptSuggestionStore((state) => state.entries.get(threadId))
  const lastTurn = turns.at(-1)
  const currentTurn = lastTurn?.threadId === threadId ? lastTurn : null
  const eligibleTurn = currentTurn?.status === 'completed'
    && hasTurnResponseContent(currentTurn, (turnDiffs.get(currentTurn.id)?.files.length ?? 0) > 0)
    ? currentTurn : null
  const available = enabled && canSuggest && activeThreadId === threadId && turnStatus === 'idle' && queuedCount === 0
  const suggestion = available && visible && entry?.turnId === eligibleTurn?.id ? entry?.suggestion ?? null : null

  const dismiss = useCallback((): void => {
    if (currentTurn) usePromptSuggestionStore.getState().dismiss(threadId, currentTurn.id)
  }, [currentTurn, threadId])

  const accept = useCallback((): string | null => {
    dismiss()
    return suggestion
  }, [dismiss, suggestion])

  useEffect(() => {
    if (!enabled || activeThreadId !== threadId || !currentTurn) return
    const store = usePromptSuggestionStore.getState()
    if (turnStatus !== 'idle' || queuedCount > 0 || !eligibleTurn) {
      store.invalidate(threadId)
      return
    }
    if (canSuggest && visible) store.request(threadId, eligibleTurn.id)
  }, [activeThreadId, canSuggest, currentTurn, eligibleTurn, enabled, queuedCount, threadId, turnStatus, visible])

  return { suggestion, dismiss, accept }
}
