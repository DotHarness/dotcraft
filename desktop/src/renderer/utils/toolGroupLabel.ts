import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import type { ToolGroupCategory } from './toolCallAggregation'

function getPathArgument(item: ConversationItem): string {
  const args = item.arguments
  return typeof args?.path === 'string' ? args.path : ''
}

function lookupChangedFile(path: string, changedFiles: Map<string, FileDiff>): FileDiff | undefined {
  return (
    changedFiles.get(path)
    ?? changedFiles.get(path.replace(/\\/g, '/'))
    ?? changedFiles.get(path.replace(/\//g, '\\'))
  )
}

function normalizePathKey(path: string): string {
  return path.trim().replace(/\\/g, '/')
}

function getWriteCounts(items: ConversationItem[], changedFiles: Map<string, FileDiff>): {
  createdCount: number
  modifiedCount: number
} {
  const createdPaths = new Set<string>()
  const modifiedPaths = new Set<string>()
  let fallbackPathIndex = 0

  const getPathKey = (item: ConversationItem): string => {
    const rawPath = getPathArgument(item)
    if (rawPath.trim().length > 0) {
      return normalizePathKey(rawPath)
    }

    fallbackPathIndex += 1
    return `__unknown_modified_${fallbackPathIndex}`
  }

  for (const item of items) {
    if (item.toolName === 'EditFile') {
      modifiedPaths.add(getPathKey(item))
      continue
    }

    if (item.toolName === 'WriteFile') {
      const path = getPathArgument(item)
      const diff = path ? lookupChangedFile(path, changedFiles) : undefined
      const key = getPathKey(item)
      if (diff?.isNewFile === true) {
        createdPaths.add(key)
        modifiedPaths.delete(key)
      } else {
        if (!createdPaths.has(key)) {
          modifiedPaths.add(key)
        }
      }
      continue
    }

    modifiedPaths.add(getPathKey(item))
  }

  for (const createdPath of createdPaths) {
    modifiedPaths.delete(createdPath)
  }

  return { createdCount: createdPaths.size, modifiedCount: modifiedPaths.size }
}

export function formatToolGroupLabel(
  category: ToolGroupCategory,
  items: ConversationItem[],
  locale: AppLocale,
  changedFiles: Map<string, FileDiff>
): string {
  const count = items.length

  if (category === 'explore') {
    return translate(locale, 'toolCall.group.explored', { count })
  }

  if (category === 'shell') {
    return translate(locale, 'toolCall.group.ran', { count })
  }

  if (category === 'web') {
    const allSearch = items.every((item) => item.toolName === 'WebSearch')
    const allFetch = items.every((item) => item.toolName === 'WebFetch')
    if (allSearch) {
      return translate(locale, 'toolCall.group.webSearched', { count })
    }
    if (allFetch) {
      return translate(locale, 'toolCall.group.webFetched', { count })
    }
    return translate(locale, 'toolCall.group.webUsed', { count })
  }

  if (category === 'subagent') {
    return translate(locale, 'toolCall.group.spawnedAgents', { count })
  }

  const { createdCount, modifiedCount } = getWriteCounts(items, changedFiles)
  if (createdCount > 0 && modifiedCount > 0) {
    return translate(locale, 'toolCall.group.createdAndModified', {
      created: createdCount,
      modified: modifiedCount,
      count
    })
  }
  if (createdCount > 0) {
    return translate(locale, 'toolCall.group.created', { count: createdCount })
  }
  return translate(locale, 'toolCall.group.modified', { count: modifiedCount || count })
}
