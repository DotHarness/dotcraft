import type { ConversationItem, QueuedTurnInput } from '../types/conversation'

/** Notifications that a background command, subagent, or workflow ended are for the model only. */
export function isBackgroundTaskNotice(input: Pick<ConversationItem, 'triggerKind' | 'triggerStatus'>): boolean {
  return input.triggerKind === 'terminal' || input.triggerKind === 'subagent'
    || (input.triggerKind === 'workflow' && !!input.triggerStatus)
}

export function isMidTurnTaskNotice(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance' && isBackgroundTaskNotice(item)
}

export function isVisibleUserMessage(item: ConversationItem): boolean {
  return (
    item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind !== 'subagentMailbox' &&
    !isBackgroundTaskNotice(item)
  )
}

export function isVisibleQueuedInput(item: QueuedTurnInput): boolean {
  return !isBackgroundTaskNotice(item)
}

/** Applies a new order of the visible queued inputs while hidden inputs keep their positions. */
export function mergeVisibleQueueOrder(queue: readonly QueuedTurnInput[], visibleOrder: readonly string[]): string[] {
  const reordered = [...visibleOrder]
  return queue.map((item) => (isVisibleQueuedInput(item) ? reordered.shift() ?? item.id : item.id))
}
