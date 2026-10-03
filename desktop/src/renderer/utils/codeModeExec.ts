import type { ConversationItem } from '../types/conversation'

export function isCodeModeExecItem(item: ConversationItem): boolean {
  if (item.toolName !== 'CodeMode' || item.pluginNamespace) return false
  if (item.source) return item.source.kind === 'CoreNative' && item.source.sourceId === 'code-mode'
  return item.status === 'streaming'
}
