import { create } from 'zustand'
import { readConfigValue, useConfigStore } from './configStore'
import type { ConversationTurn } from '../types/conversation'
import { generatePromptSuggestion, logPromptSuggestion, type PromptSuggestionDiagnostic } from '../utils/promptSuggestion'

const SUGGESTION_GENERATION_WINDOW_MS = 5 * 60_000

type SuggestionTurn = Pick<ConversationTurn, 'id'>

interface PromptSuggestionEntry {
  turnId: string
  suggestion: string | null
  controller: AbortController | null
}

interface PromptSuggestionStore {
  entries: Map<string, PromptSuggestionEntry>
  request(threadId: string, turn: Pick<ConversationTurn, 'id' | 'completedAt'>): void
  dismiss(threadId: string, turn: SuggestionTurn): void
  invalidate(threadId: string, turn?: SuggestionTurn): void
  forget(threadId: string): void
  reset(): void
}

export const usePromptSuggestionStore = create<PromptSuggestionStore>((set, get) => ({
  entries: new Map(),

  request(threadId, turn) {
    const turnId = turn.id
    const previous = get().entries.get(threadId)
    if (previous?.turnId === turnId) return
    const elapsed = Date.now() - Date.parse(turn.completedAt ?? '')
    if (!(elapsed >= 0 && elapsed < SUGGESTION_GENERATION_WINDOW_MS)) return
    previous?.controller?.abort()
    const controller = new AbortController()
    const entry: PromptSuggestionEntry = { turnId, suggestion: null, controller }
    set((state) => ({ entries: new Map(state.entries).set(threadId, entry) }))
    let diagnostic: PromptSuggestionDiagnostic = {
      parentThreadId: threadId, parentTurnId: turnId, threadId: null, turnId: null, outcome: 'requested'
    }
    void generatePromptSuggestion(threadId, turnId, controller.signal, (event) => { diagnostic = event }).then((suggestion) => {
      if (get().entries.get(threadId) !== entry) {
        logPromptSuggestion({ ...diagnostic, outcome: 'stale' })
        return
      }
      if (suggestion) logPromptSuggestion({ ...diagnostic, outcome: 'cached', textLength: suggestion.length })
      set((state) => ({ entries: new Map(state.entries).set(threadId, { ...entry, suggestion, controller: null }) }))
    })
  },

  dismiss(threadId, turn) {
    const entry = get().entries.get(threadId)
    if (entry && (entry.turnId !== turn.id || (!entry.controller && entry.suggestion === null))) return
    entry?.controller?.abort()
    set((state) => ({ entries: new Map(state.entries).set(threadId, {
      turnId: turn.id, suggestion: null, controller: null
    }) }))
  },

  invalidate(threadId, turn) {
    const entry = get().entries.get(threadId)
    if (!entry || (!entry.controller && entry.suggestion === null)) return
    if (turn?.id === entry.turnId) return
    get().forget(threadId)
  },

  forget(threadId) {
    const entry = get().entries.get(threadId)
    if (!entry) return
    entry.controller?.abort()
    set((state) => {
      const entries = new Map(state.entries)
      entries.delete(threadId)
      return { entries }
    })
  },

  reset() {
    for (const entry of get().entries.values()) entry.controller?.abort()
    set({ entries: new Map() })
  }
}))

export function initPromptSuggestionStore(): () => void {
  const stopConfig = useConfigStore.subscribe((state, previous) => {
    if (readConfigValue(state.config, 'PromptSuggestions.Enabled') !== readConfigValue(previous.config, 'PromptSuggestions.Enabled')) {
      usePromptSuggestionStore.getState().reset()
    }
  })
  const stopNotifications = window.api.appServer.onNotification((notification) => {
    if (notification.foreground === false) return
    const store = usePromptSuggestionStore.getState()
    switch (notification.method) {
      case 'thread/deleted':
      case 'thread/archived':
      case 'thread/rolledBack':
        store.forget(notification.params.threadId)
        break
      case 'turn/started':
        store.invalidate(notification.params.turn.threadId, notification.params.turn)
        break
      case 'thread/queue/updated': {
        const { threadId, queuedInputs } = notification.params
        if (threadId && queuedInputs?.length) store.invalidate(threadId)
        break
      }
    }
  })
  return () => {
    stopConfig()
    stopNotifications()
    usePromptSuggestionStore.getState().reset()
  }
}
