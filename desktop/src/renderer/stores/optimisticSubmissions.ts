import type { ConversationStore } from './conversationStore'
import type { ConversationTurn, QueuedTurnInput } from '../types/conversation'
import { isOptimisticTurn, isOptimisticUserMessage, removeRepresentedOptimisticTurns, turnRepresentsOptimisticTurn } from './optimisticMessages'

type State = Readonly<ConversationStore>
type Update = Partial<ConversationStore>

export function addOptimisticSubmission(state: State, turn: ConversationTurn): Update {
  const turns = [...state.turns, turn]
  if (state.activeTurnId && !state.activeTurnId.startsWith('local-turn-')) return { turns }
  return {
    turns, turnStatus: 'running', activeTurnId: turn.id, interruptingTurnId: null,
    streamingMessage: '', streamingMessageLastDeltaAt: null, streamingReasoning: '', activeItemId: null,
    turnStartedAt: Date.now(), inputTokens: 0, outputTokens: 0, systemLabel: null,
    streamRetry: null, maintenanceKind: null
  }
}

export function removeOptimisticSubmission(state: State, turnId: string): Update {
  const turns = state.turns.filter(turn => turn.id !== turnId)
  if (state.activeTurnId !== turnId) return { turns }
  return {
    turns, turnStatus: 'idle', activeTurnId: null, streamingMessage: '', streamingMessageLastDeltaAt: null,
    streamingReasoning: '', activeItemId: null, turnStartedAt: null,
    streamRetry: state.streamRetry?.turnId === turnId ? null : state.streamRetry
  }
}

export function acceptOptimisticSubmission(
  state: State,
  localId: string,
  serverId: string,
  mergeTurn: (incoming: ConversationTurn, existing: ConversationTurn) => ConversationTurn
): Update {
  const local = state.turns.find(turn => turn.id === localId)
  const server = state.turns.find(turn => turn.id === serverId)
  if (!local) return {}
  const update: Update = {
    activeTurnId: state.activeTurnId === localId ? serverId : state.activeTurnId,
    streamRetry: state.streamRetry?.turnId === localId ? { ...state.streamRetry, turnId: serverId } : state.streamRetry
  }
  if (!server) {
    return { ...update, turns: state.turns.map(turn => turn.id === localId ? { ...turn, id: serverId } : turn) }
  }
  if (turnRepresentsOptimisticTurn(server, local)) {
    return {
      ...update,
      turns: state.turns.flatMap(turn => turn.id === localId ? [] : [turn.id === serverId ? mergeTurn(server, local) : turn])
    }
  }
  const pending = local.items.filter(isOptimisticUserMessage).flatMap(item => {
    if (!item.clientUserMessageId || state.queuedInputs.some(input => input.clientUserMessageId === item.clientUserMessageId)) return []
    return [{
      id: `local-${item.clientUserMessageId}`, clientUserMessageId: item.clientUserMessageId,
      threadId: local.threadId, nativeInputParts: item.nativeInputParts, displayText: item.text ?? '',
      status: 'guidancePending', createdAt: item.createdAt, readyAfterTurnId: serverId,
      ...(item.sentAsGoal ? { sentAsGoal: true } : {})
    } satisfies QueuedTurnInput]
  })
  return { ...update, turns: state.turns.filter(turn => turn.id !== localId), queuedInputs: [...state.queuedInputs, ...pending] }
}

export function reconcileQueuedSubmissions(state: State, queuedInputs: QueuedTurnInput[]): Update {
  const ids = new Set(queuedInputs.filter(input => !input.id.startsWith('local-')).map(input => input.clientUserMessageId).filter(Boolean))
  const turns = state.turns.map(turn => {
    const items = turn.items.filter(item => !isOptimisticUserMessage(item) || !ids.has(item.clientUserMessageId))
    return items.length === turn.items.length ? turn : { ...turn, items }
  }).filter(turn => !isOptimisticTurn(turn) || turn.items.length > 0)
  const activeWasRemoved = state.activeTurnId?.startsWith('local-turn-') && !turns.some(turn => turn.id === state.activeTurnId)
  const activeTurnId = activeWasRemoved
    ? queuedInputs.find(input => input.status === 'guidancePending' && input.readyAfterTurnId)?.readyAfterTurnId ?? null
    : state.activeTurnId
  return { turns, queuedInputs, activeTurnId }
}

export function reconcileCanonicalSubmissions(turns: ConversationTurn[], queuedInputs: QueuedTurnInput[]): Update {
  const ids = new Set(turns.flatMap(turn => turn.items.filter(item => item.type === 'userMessage' && !isOptimisticUserMessage(item))
    .map(item => item.clientUserMessageId).filter(Boolean)))
  return {
    turns: removeRepresentedOptimisticTurns(turns),
    queuedInputs: queuedInputs.filter(input => !input.id.startsWith('local-') || !ids.has(input.clientUserMessageId))
  }
}
