import { useConversationStore } from '../stores/conversationStore'
import { restartThreadHistory } from '../stores/threadHistoryStore'
import { useThreadStore } from '../stores/threadStore'
import type { Thread } from '../types/thread'
import { wireTurnToConversationTurn } from '../types/conversation'
import { readThreadHistoryHead } from './threadHistory'

export async function reloadThreadAfterRollback(threadId: string): Promise<void> {
  const shownBeforeRollback = new Set(useConversationStore.getState().turns.map((turn) => turn.id))
  const refreshed = await readThreadHistoryHead((method, params) => window.api.appServer.sendRequest(method, params), threadId)
  if (useThreadStore.getState().activeThreadId !== threadId) return
  const turns = refreshed.thread.turns ?? []
  const surviving = new Set(turns.map((turn) => turn.id))
  useConversationStore.setState((state) => ({
    turns: state.turns.filter((turn) => surviving.has(turn.id) || !shownBeforeRollback.has(turn.id))
  }))
  useConversationStore.getState().setTurns(
    turns.map((turn) => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)),
    { preserveExistingRealtime: true, realtimeScopeThreadId: threadId }
  )
  useConversationStore.getState().setContextUsage(refreshed.thread.contextUsage ?? null)
  useThreadStore.getState().setActiveThread(refreshed.thread as Thread)
  restartThreadHistory(threadId, turns, refreshed.turnCursor)
}
