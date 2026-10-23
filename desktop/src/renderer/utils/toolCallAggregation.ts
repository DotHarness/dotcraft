import type { ConversationItem } from '../types/conversation'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'
import { resolveDesktopPluginToolRenderer } from '../plugins/desktopPluginRegistry'
import { resolveToolKind } from './toolKind'
import { isMcpAppCandidate } from './mcpAppCandidate'

export type ToolGroupCategory = 'explore' | 'write' | 'shell' | 'web' | 'subagent' | 'board'

export type AggregatedToolCall =
  | { kind: 'single'; item: ConversationItem }
  | { kind: 'reasoning'; item: ConversationItem }
  | { kind: 'group'; items: ConversationItem[] }
  | { kind: 'active'; items: ConversationItem[] }
  | { kind: 'subagents'; items: ConversationItem[] }
  | { kind: 'images'; items: ConversationItem[] }

interface ToolItemLiveContext {
  turnRunning?: boolean
}

type ToolRunRole = 'grouped' | 'own' | 'subagent' | 'images'

const OWN_ROW_FAMILIES = new Set(['createPlan', 'automation', 'agentBuilder', 'requestUserInput', 'task', 'channel'])

function getToolRunRole(item: ConversationItem): ToolRunRole {
  if (isMcpAppCandidate(item)) return 'own'
  if (resolveToolKind(item) === 'workflow') return 'own'
  const presentationId = item.presentation?.presentationId
  if (presentationId && resolveDesktopPluginToolRenderer(presentationId)) return 'own'
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.family === 'readFile' && item.contentItems?.some((content) => content.type === 'image')) return 'images'
  if (plan?.family === 'subagent') {
    return plan.options.operation === 'spawn' ? 'subagent' : 'own'
  }
  return plan && OWN_ROW_FAMILIES.has(plan.family) ? 'own' : 'grouped'
}

function isToolCallAwaitingResult(item: ConversationItem): boolean {
  return item.type === 'toolCall'
    && item.status === 'completed'
    && item.result === undefined
    && item.success === undefined
}

export function isToolItemLive(
  item: ConversationItem,
  context: ToolItemLiveContext = {}
): boolean {
  const plan = resolveCoreFallbackPlan(item)
  if (plan?.family !== 'shell') {
    return item.status !== 'completed'
      || (context.turnRunning === true && isToolCallAwaitingResult(item))
  }

  if (item.terminalRunning) return true
  if (item.executionStatus != null) {
    if (item.executionStatus === 'inProgress') return true
    // Legacy: wire item lifecycle "started" was mistakenly stored as executionStatus.
    if (String(item.executionStatus) === 'started') return true
    return false
  }

  if (item.status !== 'completed') return true
  return context.turnRunning === true && isToolCallAwaitingResult(item)
}

export type ShellBackgroundState = 'backgroundRunning' | 'backgroundStopped'

export function resolveShellBackgroundState(
  item: ConversationItem,
  context: ToolItemLiveContext = {}
): ShellBackgroundState | null {
  if (context.turnRunning === true || resolveCoreFallbackPlan(item)?.family !== 'shell') return null
  if (isToolItemLive(item, context)) return 'backgroundRunning'
  return item.executionStatus === 'cancelled' && item.terminalSessionId && item.success === true ? 'backgroundStopped' : null
}

function resolveCoreFallbackPlan(item: ConversationItem) {
  const presentationId = item.presentation?.presentationId
  return presentationId && resolveDesktopPluginToolRenderer(presentationId)
    ? null
    : resolveCoreToolRenderPlan(item)
}

function isReasoning(item: ConversationItem): boolean {
  return item.type === 'reasoningContent'
}

function pushSettledSegment(entries: AggregatedToolCall[], segment: ConversationItem[], turnRunning: boolean): void {
  let bucket: ConversationItem[] = []
  const flush = (): void => {
    if (bucket.filter((item) => !isReasoning(item)).length > 1) {
      entries.push({ kind: 'group', items: bucket })
    } else {
      for (const item of bucket) entries.push(isReasoning(item) ? { kind: 'reasoning', item } : { kind: 'single', item })
    }
    bucket = []
  }
  for (const item of segment) {
    if (!isReasoning(item) && isToolItemLive(item, { turnRunning })) {
      flush()
      entries.push({ kind: 'single', item })
    } else {
      bucket.push(item)
    }
  }
  flush()
}

export function planToolRunRender(
  run: ConversationItem[],
  context: { isRunning: boolean; isTrailingRun: boolean }
): AggregatedToolCall[] {
  const entries: AggregatedToolCall[] = []
  let segment: ConversationItem[] = []
  const flushSegment = (active: boolean): void => {
    if (active && segment.some((item) => !isReasoning(item))) {
      entries.push({ kind: 'active', items: segment })
    } else {
      pushSettledSegment(entries, segment, context.isRunning)
    }
    segment = []
  }

  for (let i = 0; i < run.length; i++) {
    const item = run[i]
    const role = isReasoning(item) ? 'grouped' : getToolRunRole(item)
    if (role === 'grouped') {
      segment.push(item)
      continue
    }
    flushSegment(false)

    const items = [item]
    const joins = (next: ConversationItem): boolean => role !== 'own' && !isReasoning(next) && getToolRunRole(next) === role
    while (i + 1 < run.length && joins(run[i + 1])) items.push(run[++i])
    entries.push(role === 'images' ? { kind: 'images', items }
      : items.length > 1 ? { kind: 'subagents', items }
        : { kind: 'single', item })
  }
  flushSegment(context.isRunning && context.isTrailingRun)
  return entries
}
