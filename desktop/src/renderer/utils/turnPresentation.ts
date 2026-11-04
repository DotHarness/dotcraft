import type { ConversationTurn } from '../types/conversation'
import { isMidTurnSubAgentEnding } from './subAgentEndings'
import { isBackgroundTaskNotice } from './visibleUserMessage'

export function hasTurnResponseContent(turn: ConversationTurn, hasFileChanges = false): boolean {
  if (hasFileChanges || turn.error || (turn.summary != null && turn.summary.itemCount > turn.items.length)) return true
  return turn.items.some((item) => {
    switch (item.type) {
      case 'agentMessage': return Boolean(item.text?.trim())
      case 'reasoningContent': return Boolean(item.reasoning?.trim())
      case 'userMessage':
        return isMidTurnSubAgentEnding(item)
          || (item.deliveryMode === 'guidance' && !isBackgroundTaskNotice(item))
      default: return true
    }
  })
}

export function isEmptyCompletedTurn(turn: ConversationTurn, hasFileChanges = false): boolean {
  return turn.status === 'completed' && !hasTurnResponseContent(turn, hasFileChanges)
}
