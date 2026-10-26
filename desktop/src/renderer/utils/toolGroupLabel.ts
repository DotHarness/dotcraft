import { translate, type AppLocale, type MessageKey } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'
import { resolveMemoryOperation, resolveSkillOperation } from './toolKind'
import { getStreamingToolDisplay } from './streamingToolDisplay'
import { collectMemoryFileNames, formatMemoryUpdatedLabel } from './memoryToolDisplay'
import { summarizeSkillActivity, type SkillActivity } from './skillOperationDisplay'
import { isBoardReadOperation } from './boardToolDisplay'
import { formatNodeReplSurfaceLabel, getNodeReplSurface } from './nodeReplToolDisplay'
import { formatWebSearchLabel } from './webSearchDisplay'

function getPathArgument(item: ConversationItem): string {
  const args = item.arguments
  return typeof args?.path === 'string' ? args.path : ''
}

function normalizePathKey(path: string): string {
  return path.trim().replace(/\\/g, '/')
}

function getWriteCounts(items: ConversationItem[], itemDiffs: ReadonlyMap<string, FileDiff>): {
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
    const operation = resolveCoreToolRenderPlan(item)?.options.operation
    if (operation === 'edit') {
      modifiedPaths.add(getPathKey(item))
      continue
    }

    if (operation === 'write') {
      const key = getPathKey(item)
      if (itemDiffs.get(item.id)?.isNewFile === true) {
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

type ActivityPart = 'browser' | 'chrome' | 'computer' | 'skill' | 'tool' | 'write' | 'memory' | 'recall' | 'explore' | 'board' | 'boardUpdate' | 'shell' | 'search' | 'fetch'

const PART_ORDER: readonly ActivityPart[] = ['browser', 'chrome', 'computer', 'skill', 'tool', 'write', 'memory', 'recall', 'explore', 'board', 'boardUpdate', 'shell', 'search', 'fetch']

function getActivityPart(item: ConversationItem): ActivityPart | null {
  if (item.type === 'reasoningContent') return null
  if (item.type === 'webSearch') return 'search'
  const memory = resolveMemoryOperation(item)
  if (memory === 'update') return 'memory'
  if (memory === 'recall') return 'recall'
  if (resolveSkillOperation(item)) return 'skill'
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.family === 'skillView') return 'skill'
  if (plan?.family === 'nodeRepl') return getNodeReplSurface(item) ?? 'shell'
  switch (plan?.groupCategory) {
    case 'write': return 'write'
    case 'explore': return 'explore'
    case 'board': return isBoardReadOperation(plan.options.operation) ? 'board' : 'boardUpdate'
    case 'shell': return 'shell'
    case 'web': return plan.options.operation === 'fetch' ? 'fetch' : 'search'
    default: return 'tool'
  }
}

function plural(count: number, one: MessageKey, other: MessageKey): MessageKey {
  return count === 1 ? one : other
}

const SKILL_ACTIVITY_KEYS: Readonly<Record<SkillActivity, readonly [MessageKey, MessageKey]>> = {
  loaded: ['toolCall.activity.loadedSkill.one', 'toolCall.activity.loadedSkill.other'],
  created: ['toolCall.activity.createdSkill.one', 'toolCall.activity.createdSkill.other'],
  updated: ['toolCall.activity.updatedSkill.one', 'toolCall.activity.updatedSkill.other']
}

function formatSkillActivity(items: ConversationItem[], locale: AppLocale): string {
  const { activity, count } = summarizeSkillActivity(items)
  return translate(locale, plural(count, ...SKILL_ACTIVITY_KEYS[activity]))
}

function formatActivityPart(
  part: ActivityPart,
  items: ConversationItem[],
  locale: AppLocale,
  itemDiffs: ReadonlyMap<string, FileDiff>
): string {
  const count = items.length
  switch (part) {
    case 'browser':
    case 'chrome':
    case 'computer': return formatNodeReplSurfaceLabel(part, locale)
    case 'skill': return formatSkillActivity(items, locale)
    case 'tool': return translate(locale, plural(count, 'toolCall.activity.calledTool.one', 'toolCall.activity.calledTool.other'))
    case 'memory': return formatMemoryUpdatedLabel(collectMemoryFileNames(items).length, locale)
    case 'write': {
      const { createdCount, modifiedCount } = getWriteCounts(items, itemDiffs)
      return createdCount > 0 && modifiedCount === 0
        ? translate(locale, plural(createdCount, 'toolCall.activity.created.one', 'toolCall.activity.created.other'))
        : translate(locale, plural(createdCount + modifiedCount, 'toolCall.activity.edited.one', 'toolCall.activity.edited.other'))
    }
    case 'recall': return translate(locale, 'toolCall.memory.recalled')
    case 'explore': return translate(locale, plural(count, 'toolCall.activity.explored.one', 'toolCall.activity.explored.other'))
    case 'board': return translate(locale, 'toolCall.activity.readBoard')
    case 'boardUpdate': return translate(locale, 'toolCall.activity.updatedBoard')
    case 'shell': return translate(locale, plural(count, 'toolCall.activity.ran.one', 'toolCall.activity.ran.other'))
    case 'search': return translate(locale, 'toolCall.activity.searchedWeb')
    case 'fetch': return translate(locale, plural(count, 'toolCall.activity.fetched.one', 'toolCall.activity.fetched.other'))
  }
}

function groupByPart(items: readonly ConversationItem[]): Map<ActivityPart, ConversationItem[]> {
  const byPart = new Map<ActivityPart, ConversationItem[]>()
  for (const item of items) {
    const part = getActivityPart(item)
    if (part) byPart.set(part, [...(byPart.get(part) ?? []), item])
  }
  return new Map(PART_ORDER.filter((part) => byPart.has(part)).map((part) => [part, byPart.get(part)!]))
}

export function formatToolGroupLabel(
  items: readonly ConversationItem[],
  locale: AppLocale,
  itemDiffs: ReadonlyMap<string, FileDiff>
): string {
  const parts = [...groupByPart(items)].map(([part, partItems], index) => {
    const label = formatActivityPart(part, partItems, locale, itemDiffs)
    return index === 0 || locale === 'de' ? label : label.charAt(0).toLocaleLowerCase(locale) + label.slice(1)
  })
  return parts.length > 0 ? parts.join(translate(locale, 'toolCall.activity.separator')) : translate(locale, 'conversation.worked')
}

export function getToolGroupIconItem(items: readonly ConversationItem[]): ConversationItem | undefined {
  return groupByPart(items).values().next().value?.[0] ?? items.find((item) => item.type !== 'reasoningContent')
}

export function formatActiveToolLabel(item: ConversationItem, locale: AppLocale): string {
  if (item.type === 'webSearch') return formatWebSearchLabel(item, locale)
  const preview = item.argumentsPreview ?? (item.arguments ? JSON.stringify(item.arguments) : null)
  return getStreamingToolDisplay(item, preview, locale).label
}
