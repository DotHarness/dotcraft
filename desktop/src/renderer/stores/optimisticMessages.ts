import type { ConversationItem, ConversationTurn } from '../types/conversation'

export function isOptimisticTurn(turn: ConversationTurn): boolean {
  return turn.id.startsWith('local-turn-')
}

export function isOptimisticUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.id.startsWith('local-')
}

export function optimisticUserMessageRepresentedByIncoming(item: ConversationItem, incoming: ConversationItem[]): boolean {
  return isOptimisticUserMessage(item) && !!item.clientUserMessageId && incoming.some(candidate =>
    candidate.type === 'userMessage' && !isOptimisticUserMessage(candidate) && candidate.clientUserMessageId === item.clientUserMessageId)
}

export function turnRepresentsOptimisticTurn(incoming: ConversationTurn, optimistic: ConversationTurn): boolean {
  return isOptimisticTurn(optimistic) && incoming.threadId === optimistic.threadId
    && optimistic.items.some(item => optimisticUserMessageRepresentedByIncoming(item, incoming.items))
}

export function removeRepresentedOptimisticTurns(turns: ConversationTurn[]): ConversationTurn[] {
  return turns.filter(turn => !isOptimisticTurn(turn) || !turns.some(candidate =>
    !isOptimisticTurn(candidate) && turnRepresentsOptimisticTurn(candidate, turn)))
}
