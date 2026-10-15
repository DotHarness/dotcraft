import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { basename } from './path'
import { resolveMemoryOperation, type ToolKindSource } from './toolKind'

type MemoryToolItem = ToolKindSource & Pick<ConversationItem, 'arguments'>

const MEMORY_INDEX_FILE = 'memory.md'

/** The memory file a call targets, or null for the index, a directory, or a search without a file. */
export function getMemoryFileName(args: Record<string, unknown> | undefined): string | null {
  const path = args?.path
  if (typeof path !== 'string' || path.trim().length === 0) return null
  const name = basename(path.trim())
  const lower = name.toLowerCase()
  return lower.endsWith('.md') && lower !== MEMORY_INDEX_FILE ? name : null
}

export function collectMemoryFileNames(items: readonly MemoryToolItem[]): string[] {
  const names = new Set<string>()
  for (const item of items) {
    if (resolveMemoryOperation(item) == null) continue
    const name = getMemoryFileName(item.arguments)
    if (name) names.add(name)
  }
  return [...names]
}

export function formatMemoryRecallLabel(args: Record<string, unknown> | undefined, locale: AppLocale): string {
  const filename = getMemoryFileName(args)
  return filename
    ? translate(locale, 'toolCall.memory.recalledFile', { filename })
    : translate(locale, 'toolCall.memory.recalled')
}

export function formatMemoryUpdatedLabel(fileCount: number, locale: AppLocale): string {
  return translate(locale, fileCount > 1 ? 'toolCall.memory.updated.other' : 'toolCall.memory.updated.one')
}
