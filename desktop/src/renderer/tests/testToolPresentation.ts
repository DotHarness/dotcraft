import type { ConversationItem } from '../types/conversation'

export function withTestCorePresentation(
  item: ConversationItem,
  presentationId: string,
  options?: Record<string, unknown>,
  sourceToolId = item.toolName
): ConversationItem {
  return {
    ...item,
    source: {
      kind: 'CoreNative',
      sourceId: 'core-native',
      ...(sourceToolId ? { sourceToolId } : {})
    },
    presentation: {
      presentationId,
      ...(options ? { options } : {})
    }
  }
}
