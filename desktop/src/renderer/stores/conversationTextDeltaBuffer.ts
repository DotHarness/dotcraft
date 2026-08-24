export interface ConversationTextDeltaBatch {
  agentMessage: string
  agentMessageLastDeltaAt: number | null
  reasoning: string
}

export interface ConversationTextDeltaBuffer {
  queueAgentMessage(delta: string, receivedAt: number): void
  queueReasoning(delta: string): void
  flush(): void
  reset(): void
}

const DEFAULT_FLUSH_MS = 16

/**
 * Coalesces transport-sized text deltas into one store update per frame budget.
 * Completion/lifecycle handlers call flush synchronously before committing an Item.
 */
export function createConversationTextDeltaBuffer(
  commit: (batch: ConversationTextDeltaBatch) => void,
  flushMs = DEFAULT_FLUSH_MS
): ConversationTextDeltaBuffer {
  let agentMessage = ''
  let agentMessageLastDeltaAt: number | null = null
  let reasoning = ''
  let flushTimer: ReturnType<typeof setTimeout> | null = null

  const flush = (): void => {
    if (flushTimer != null) {
      clearTimeout(flushTimer)
      flushTimer = null
    }
    if (!agentMessage && !reasoning) return

    const batch = { agentMessage, agentMessageLastDeltaAt, reasoning }
    agentMessage = ''
    agentMessageLastDeltaAt = null
    reasoning = ''
    commit(batch)
  }

  const schedule = (): void => {
    if (flushTimer != null) return
    flushTimer = setTimeout(flush, flushMs)
  }

  return {
    queueAgentMessage(delta, receivedAt) {
      if (!delta) return
      agentMessage += delta
      agentMessageLastDeltaAt = receivedAt
      schedule()
    },
    queueReasoning(delta) {
      if (!delta) return
      reasoning += delta
      schedule()
    },
    flush,
    reset() {
      agentMessage = ''
      agentMessageLastDeltaAt = null
      reasoning = ''
      if (flushTimer != null) clearTimeout(flushTimer)
      flushTimer = null
    }
  }
}
