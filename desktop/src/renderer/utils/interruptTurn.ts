import { useConversationStore } from '../stores/conversationStore'

interface InterruptTurnOptions {
  threadId: string
  turnId: string
  onError(error: unknown): void
}

/** Requests interruption once and keeps the local pending state until a terminal turn event. */
export async function interruptTurn({
  threadId,
  turnId,
  onError
}: InterruptTurnOptions): Promise<boolean> {
  const state = useConversationStore.getState()
  if (
    turnId.startsWith('local-turn-')
    || state.activeTurnId !== turnId
    || state.interruptingTurnId === turnId
  ) {
    return false
  }

  state.setInterruptingTurnId(turnId)
  try {
    await window.api.appServer.sendRequest('turn/interrupt', { threadId, turnId })
    return true
  } catch (error) {
    const latest = useConversationStore.getState()
    if (latest.interruptingTurnId === turnId) {
      latest.setInterruptingTurnId(null)
    }
    onError(error)
    return false
  }
}
