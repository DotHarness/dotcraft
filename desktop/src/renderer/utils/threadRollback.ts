import { useConversationStore } from '../stores/conversationStore'
import { restartThreadHistory } from '../stores/threadHistoryStore'
import { useThreadStore } from '../stores/threadStore'
import type { Thread } from '../types/thread'
import { wireTurnToConversationTurn } from '../types/conversation'
import { readThreadHistoryHead } from './threadHistory'

export async function reloadThreadAfterRollback(threadId: string): Promise<void> {
  const refreshed = await readThreadHistoryHead((method, params) => window.api.appServer.sendRequest(method, params), threadId)
  if (useThreadStore.getState().activeThreadId !== threadId) return
  const turns = refreshed.thread.turns ?? []
  useConversationStore.getState().setTurns(
    turns.map((turn) => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>))
  )
  useConversationStore.getState().setContextUsage(refreshed.thread.contextUsage ?? null)
  useThreadStore.getState().setActiveThread(refreshed.thread as Thread)
  restartThreadHistory(threadId, turns, refreshed.turnCursor)
}
