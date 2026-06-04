import type { ConversationItem } from '../types/conversation'

const EXPLORE_TOOLS = new Set(['ReadFile', 'GrepFiles', 'FindFiles'])
const WRITE_TOOLS = new Set(['WriteFile', 'EditFile'])
const SHELL_TOOLS = new Set(['Exec', 'RunCommand', 'BashCommand'])
const WEB_TOOLS = new Set(['WebSearch', 'WebFetch'])
const SUB_AGENT_TOOLS = new Set([
  'SpawnAgent',
  'SendMessage',
  'FollowupTask',
  'WaitAgent',
  'ListAgents',
  'CloseAgent'
])

export type ToolGroupCategory = 'explore' | 'write' | 'shell' | 'web' | 'subagent'

export type AggregatedToolCall =
  | { kind: 'single'; item: ConversationItem }
  | { kind: 'group'; category: ToolGroupCategory; items: ConversationItem[] }

interface ToolItemLiveContext {
  turnRunning?: boolean
}

function getGroupCategory(toolName: string): ToolGroupCategory | null {
  if (EXPLORE_TOOLS.has(toolName)) return 'explore'
  if (WRITE_TOOLS.has(toolName)) return 'write'
  if (SHELL_TOOLS.has(toolName)) return 'shell'
  if (WEB_TOOLS.has(toolName)) return 'web'
  if (SUB_AGENT_TOOLS.has(toolName)) return 'subagent'
  return null
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
  const toolName = item.toolName ?? ''
  if (!SHELL_TOOLS.has(toolName)) {
    return item.status !== 'completed'
      || (context.turnRunning === true && isToolCallAwaitingResult(item))
  }

  if (item.executionStatus != null) {
    if (item.executionStatus === 'inProgress') return true
    // Legacy: wire item lifecycle "started" was mistakenly stored as executionStatus.
    if (String(item.executionStatus) === 'started') return true
    return false
  }

  if (item.status !== 'completed') return true
  return context.turnRunning === true && isToolCallAwaitingResult(item)
}

/**
 * Groups consecutive tool calls by category (explore/write/shell), while preserving
 * chronological order. Category transitions close the current group.
 *
 * Example: [ReadFile, ReadFile, WriteFile] → [group(2), single(WriteFile)]
 */
export function aggregateToolCalls(
  items: ConversationItem[],
  context: ToolItemLiveContext = {}
): AggregatedToolCall[] {
  const result: AggregatedToolCall[] = []
  let i = 0

  while (i < items.length) {
    const item = items[i]
    const toolName = item.toolName ?? ''
    const category = getGroupCategory(toolName)

    if (category == null) {
      result.push({ kind: 'single', item })
      i++
      continue
    }

    // Collect consecutive items in the same category and aggregate only settled
    // stretches. Live items split runs but do not de-aggregate neighbors.
    const run: ConversationItem[] = [item]
    while (i + 1 < items.length) {
      const next = items[i + 1]
      const nextCategory = getGroupCategory(next.toolName ?? '')
      if (nextCategory !== category) break
      run.push(next)
      i++
    }

    let settledBucket: ConversationItem[] = []
    const flushSettledBucket = (): void => {
      if (settledBucket.length === 0) return
      if (settledBucket.length === 1) {
        result.push({ kind: 'single', item: settledBucket[0] })
      } else {
        result.push({
          kind: 'group',
          category,
          items: settledBucket
        })
      }
      settledBucket = []
    }

    for (const runItem of run) {
      const isBreakingItem = isToolItemLive(runItem, context)
      if (isBreakingItem) {
        flushSettledBucket()
        result.push({ kind: 'single', item: runItem })
      } else {
        settledBucket.push(runItem)
      }
    }
    flushSettledBucket()

    i++
  }

  return result
}

export function planToolRunRender(
  toolRun: ConversationItem[],
  context: { isRunning: boolean; isTrailingRun: boolean }
): { entries: AggregatedToolCall[] } {
  if (toolRun.length === 0) {
    return { entries: [] }
  }

  if (context.isRunning && context.isTrailingRun) {
    return {
      entries: toolRun.map((item) => ({ kind: 'single', item }))
    }
  }

  return { entries: aggregateToolCalls(toolRun, { turnRunning: context.isRunning }) }
}
