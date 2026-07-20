import type { ConversationItem } from '../types/conversation'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'

export type ToolGroupCategory = 'explore' | 'write' | 'shell' | 'web' | 'subagent'

export type AggregatedToolCall =
  | { kind: 'single'; item: ConversationItem }
  | { kind: 'group'; category: ToolGroupCategory; items: ConversationItem[] }

interface ToolItemLiveContext {
  turnRunning?: boolean
}

function getGroupCategory(item: ConversationItem): ToolGroupCategory | null {
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.groupCategory !== 'subagent') return plan?.groupCategory ?? null
  return plan.options.operation === 'spawn' || plan.options.operation === 'followupTask'
    ? 'subagent'
    : null
}

function getSubAgentGroupOperation(item: ConversationItem): unknown {
  return resolveCoreToolRenderPlan(item)?.options.operation
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
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.family !== 'shell') {
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
    const category = getGroupCategory(item)
    const subAgentOperation = category === 'subagent' ? getSubAgentGroupOperation(item) : null

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
      const nextCategory = getGroupCategory(next)
      if (nextCategory !== category) break
      if (category === 'subagent' && getSubAgentGroupOperation(next) !== subAgentOperation) break
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
