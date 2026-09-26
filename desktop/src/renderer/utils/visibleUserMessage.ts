import type { ConversationItem } from '../types/conversation'

/** Whether the transcript renders this item as a user bubble. */
export function isVisibleUserMessage(item: ConversationItem): boolean {
  return (
    item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind !== 'subagentMailbox'
  )
}
