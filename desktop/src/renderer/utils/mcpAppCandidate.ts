import type { ConversationItem } from '../types/conversation'

export function isMcpAppCandidate(item: ConversationItem): boolean {
  return item.type === 'mcpToolCall'
    && item.status === 'completed'
    && /^ui:\/\//i.test(item.mcpAppResourceUri ?? '')
}
