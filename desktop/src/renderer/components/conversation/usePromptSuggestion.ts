import { useCallback, useEffect, useRef, useState } from 'react'
import { useConfigValue } from '../../stores/configStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { generatePromptSuggestion, logPromptSuggestion, type PromptSuggestionDiagnostic } from '../../utils/promptSuggestion'

interface PromptSuggestionOptions {
  threadId: string
  canSuggest: boolean
}

export function usePromptSuggestion({ threadId, canSuggest }: PromptSuggestionOptions): {
  suggestion: string | null
  dismiss: () => void
  accept: () => string | null
} {
  const enabled = useConfigValue('PromptSuggestions.Enabled') === true
  const [suggestion, setSuggestion] = useState<string | null>(null)
  const turns = useConversationStore((state) => state.turns)
  const turnStatus = useConversationStore((state) => state.turnStatus)
  const queuedCount = useConversationStore((state) => state.queuedInputs.length)
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const activeRequest = useRef<AbortController | null>(null)
  const lastStatus = useRef(turnStatus)
  const lastThreadId = useRef(threadId)
  const requestedTurns = useRef(new Set<string>())

  const dismiss = useCallback((): void => {
    activeRequest.current?.abort()
    activeRequest.current = null
    setSuggestion(null)
  }, [])

  const accept = useCallback((): string | null => {
    const value = suggestion
    dismiss()
    return value
  }, [dismiss, suggestion])

  useEffect(() => {
    if (!enabled || !canSuggest || activeThreadId !== threadId || queuedCount > 0 || turnStatus !== 'idle') {
      dismiss()
    }
  }, [activeThreadId, canSuggest, dismiss, enabled, queuedCount, threadId, turnStatus])

  useEffect(() => {
    if (lastThreadId.current !== threadId) {
      lastThreadId.current = threadId
      lastStatus.current = turnStatus
      requestedTurns.current.clear()
      dismiss()
      return
    }
    const wasRunning = lastStatus.current === 'running'
    lastStatus.current = turnStatus
    if (!wasRunning || turnStatus !== 'idle') return
    const lastTurn = turns.at(-1)
    if (lastTurn?.status !== 'completed' || requestedTurns.current.has(lastTurn.id)) return
    requestedTurns.current.add(lastTurn.id)
    if (!enabled || !canSuggest || activeThreadId !== threadId || queuedCount > 0) return

    dismiss()
    const controller = new AbortController()
    activeRequest.current = controller
    let diagnostic: PromptSuggestionDiagnostic = {
      parentThreadId: threadId, parentTurnId: lastTurn.id, threadId: null, turnId: null, outcome: 'requested'
    }
    void generatePromptSuggestion(threadId, lastTurn.id, controller.signal, (event) => { diagnostic = event }).then((value) => {
      if (!controller.signal.aborted && activeRequest.current === controller) {
        if (value) logPromptSuggestion({ ...diagnostic, outcome: 'displayed', textLength: value.length })
        setSuggestion(value)
        activeRequest.current = null
      } else {
        logPromptSuggestion({ ...diagnostic, outcome: 'stale' })
      }
    })
  }, [activeThreadId, canSuggest, dismiss, enabled, queuedCount, threadId, turnStatus, turns])

  useEffect(() => dismiss, [dismiss, threadId])

  return { suggestion, dismiss, accept }
}
