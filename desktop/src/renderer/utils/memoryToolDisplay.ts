import { translate, type AppLocale, type MessageKey } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { basename } from './path'
import { resolveMemoryOperation, type ToolKindSource } from './toolKind'

type MemoryToolItem = ToolKindSource & Pick<ConversationItem, 'arguments'>

const MEMORY_INDEX_FILE = 'memory.md'

type PluralForm = 'one' | 'other'

const RECALLED_MEMORIES: Readonly<Record<PluralForm, MessageKey>> = {
  one: 'toolCall.group.recalledMemories.one',
  other: 'toolCall.group.recalledMemories.other'
}

const RECALLED_AND_EXPLORED: Readonly<Record<PluralForm | 'none', Readonly<Record<PluralForm, MessageKey>>>> = {
  none: {
    one: 'toolCall.group.recalledAndExplored.none.one',
    other: 'toolCall.group.recalledAndExplored.none.other'
  },
  one: {
    one: 'toolCall.group.recalledAndExplored.one.one',
    other: 'toolCall.group.recalledAndExplored.one.other'
  },
  other: {
    one: 'toolCall.group.recalledAndExplored.other.one',
    other: 'toolCall.group.recalledAndExplored.other.other'
  }
}

function pluralForm(count: number): PluralForm {
  return count === 1 ? 'one' : 'other'
}

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

export interface ExplorationGroupCounts {
  recalls: number
  memoryFiles: number
  explored: number
}

/** Explored counts the group's calls that are not memory recalls. */
export function countExplorationGroup(items: readonly MemoryToolItem[]): ExplorationGroupCounts {
  const recalls = items.filter((item) => resolveMemoryOperation(item) === 'recall')
  return {
    recalls: recalls.length,
    memoryFiles: collectMemoryFileNames(recalls).length,
    explored: items.length - recalls.length
  }
}

export function formatExplorationGroupLabel(items: readonly MemoryToolItem[], locale: AppLocale): string {
  const { recalls, memoryFiles, explored } = countExplorationGroup(items)
  if (recalls === 0) return translate(locale, 'toolCall.group.explored', { count: explored })
  if (explored === 0) {
    return memoryFiles === 0
      ? translate(locale, 'toolCall.memory.recalled')
      : translate(locale, RECALLED_MEMORIES[pluralForm(memoryFiles)], { count: memoryFiles })
  }
  const memoryForm = memoryFiles === 0 ? 'none' : pluralForm(memoryFiles)
  return translate(locale, RECALLED_AND_EXPLORED[memoryForm][pluralForm(explored)], {
    memories: memoryFiles,
    files: explored
  })
}

export function formatMemoryUpdatedLabel(fileCount: number, locale: AppLocale): string {
  return translate(locale, fileCount > 1 ? 'toolCall.memory.updated.other' : 'toolCall.memory.updated.one')
}
