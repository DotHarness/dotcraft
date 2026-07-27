import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { ConversationStore } from '../stores/conversationStore'
import type { ThreadRuntimeSnapshot } from '../types/thread'
import { isToolItemLive } from './toolCallAggregation'

type ConversationRestoreState = Pick<
  ConversationStore,
  | 'turnStatus'
  | 'activeTurnId'
  | 'turns'
  | 'pendingApproval'
  | 'pendingApprovals'
  | 'pendingUserInput'
  | 'streamingMessage'
  | 'streamingReasoning'
  | 'activeItemId'
  | 'maintenanceKind'
>

function isServerRuntimeActive(runtime: ThreadRuntimeSnapshot): boolean {
  return runtime.running ||
    runtime.busy === true ||
    runtime.waitingOnApproval ||
    runtime.waitingOnInput === true ||
    runtime.waitingOnPlanConfirmation === true ||
    runtime.maintenanceKind != null
}

function isActiveTurnStatus(turn: ConversationTurn): boolean {
  return turn.status === 'running' ||
    turn.status === 'waitingApproval' ||
    turn.status === 'waitingInput'
}

function isToolLikeItem(item: ConversationItem): boolean {
  return item.type === 'toolCall' ||
    item.type === 'dynamicToolCall' ||
    item.type === 'commandExecution'
}

function hasLiveToolItem(turns: ConversationTurn[]): boolean {
  return turns.some((turn) =>
    turn.items.some((item) =>
      isToolLikeItem(item) && isToolItemLive(item, { turnRunning: true })
    )
  )
}

export function conversationNeedsFullSnapshotReconcile({
  conversation,
  runtime
}: {
  conversation: ConversationRestoreState
  runtime: ThreadRuntimeSnapshot
}): boolean {
  if (isServerRuntimeActive(runtime)) return false

  if (conversation.turnStatus !== 'idle') return true
  if (conversation.activeTurnId != null) return true
  if (conversation.pendingApproval != null) return true
  if (conversation.pendingApprovals.length > 0) return true
  if (conversation.pendingUserInput != null) return true
  if (conversation.maintenanceKind != null) return true
  if (conversation.activeItemId != null) return true
  if (conversation.streamingMessage.trim().length > 0) return true
  if (conversation.streamingReasoning.trim().length > 0) return true
  if (conversation.turns.some(isActiveTurnStatus)) return true

  return hasLiveToolItem(conversation.turns)
}
