import type { ConversationItem } from '../types/conversation'

const CODE_MODE_TOOL_NAMES = new Set(['CodeMode', 'CodeModeWait'])

export function isCodeModeExecItem(item: ConversationItem): boolean {
  if (!CODE_MODE_TOOL_NAMES.has(item.toolName ?? '') || item.pluginNamespace) return false
  if (item.source) return item.source.kind === 'CoreNative' && item.source.sourceId === 'code-mode'
  return item.status === 'streaming'
}
