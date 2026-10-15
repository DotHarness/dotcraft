import type { ConversationItem, QueuedTurnInput } from '../types/conversation'

/** Whether the transcript renders this item as a user bubble. */
export function isVisibleUserMessage(item: ConversationItem): boolean {
  return (
    item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind !== 'subagentMailbox' &&
    item.triggerKind !== 'terminal'
  )
}

/** Whether the queued-input dock lists this input. Background terminal notices are for the model only. */
export function isVisibleQueuedInput(item: QueuedTurnInput): boolean {
  return item.triggerKind !== 'terminal'
}

/** Applies a new order of the visible queued inputs while hidden inputs keep their positions. */
export function mergeVisibleQueueOrder(queue: readonly QueuedTurnInput[], visibleOrder: readonly string[]): string[] {
  const reordered = [...visibleOrder]
  return queue.map((item) => (isVisibleQueuedInput(item) ? reordered.shift() ?? item.id : item.id))
}
