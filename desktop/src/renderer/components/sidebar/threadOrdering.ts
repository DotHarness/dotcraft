import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import type { ThreadSummary } from '../../types/thread'
import { getSubAgentParentThreadId, isSubAgentThread } from '../../utils/subAgentThreads'

export type ThreadDropPlacement = 'before' | 'after'

function timestamp(value: string | undefined): number {
  const parsed = Date.parse(value ?? '')
  return Number.isFinite(parsed) ? parsed : 0
}

export function sortThreadsByRecentActivity(threads: ThreadSummary[]): ThreadSummary[] {
  return [...threads].sort((left, right) => timestamp(right.lastActiveAt) - timestamp(left.lastActiveAt))
}

function sortThreadsByCreation(threads: ThreadSummary[]): ThreadSummary[] {
  return [...threads].sort((left, right) =>
    timestamp(right.createdAt || right.lastActiveAt) - timestamp(left.createdAt || left.lastActiveAt)
  )
}

export function applyManualOrder<T>(items: T[], manualOrder: readonly string[], idOf: (item: T) => string): T[] {
  const position = new Map(manualOrder.map((id, index) => [id, index]))
  const unplaced = items.filter((item) => !position.has(idOf(item)))
  const placed = items
    .filter((item) => position.has(idOf(item)))
    .sort((left, right) => position.get(idOf(left))! - position.get(idOf(right))!)
  return [...unplaced, ...placed]
}

export function orderThreadsBySortMode(
  threads: ThreadSummary[],
  mode: SidebarThreadSortMode,
  manualOrder: readonly string[]
): ThreadSummary[] {
  return mode === 'manual'
    ? applyManualOrder(sortThreadsByCreation(threads), manualOrder, (thread) => thread.id)
    : sortThreadsByRecentActivity(threads)
}

export function moveThreadId(
  order: readonly string[],
  movedId: string,
  targetId: string,
  placement: ThreadDropPlacement
): string[] {
  if (movedId === targetId || !order.includes(targetId)) return [...order]
  const remaining = order.filter((id) => id !== movedId)
  const targetIndex = remaining.indexOf(targetId)
  const insertAt = placement === 'before' ? targetIndex : targetIndex + 1
  return [...remaining.slice(0, insertAt), movedId, ...remaining.slice(insertAt)]
}

export function orderSubAgentsAfterParents(threads: ThreadSummary[]): ThreadSummary[] {
  const childrenByParent = new Map<string, ThreadSummary[]>()
  const topLevel: ThreadSummary[] = []
  const emitted = new Set<string>()

  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (parentId) {
      const children = childrenByParent.get(parentId) ?? []
      children.push(thread)
      childrenByParent.set(parentId, children)
    } else {
      topLevel.push(thread)
    }
  }

  const result: ThreadSummary[] = []
  for (const thread of topLevel) {
    result.push(thread)
    emitted.add(thread.id)
    const children = childrenByParent.get(thread.id) ?? []
    for (const child of children) {
      result.push(child)
      emitted.add(child.id)
    }
  }

  for (const thread of threads) {
    if (!emitted.has(thread.id)) {
      result.push(thread)
      emitted.add(thread.id)
    }
  }

  return result
}

export function partitionPinnedThreads(
  threads: ThreadSummary[],
  pinnedThreadIds: string[]
): { pinnedThreads: ThreadSummary[]; unpinnedThreads: ThreadSummary[] } {
  if (pinnedThreadIds.length === 0 || threads.length === 0) {
    return { pinnedThreads: [], unpinnedThreads: threads }
  }

  const byId = new Map(threads.map((thread) => [thread.id, thread]))
  const childrenByParent = new Map<string, ThreadSummary[]>()
  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (!parentId) continue
    const children = childrenByParent.get(parentId) ?? []
    children.push(thread)
    childrenByParent.set(parentId, children)
  }

  const included = new Set<string>()
  const pinnedThreads: ThreadSummary[] = []

  function appendThreadTree(threadId: string): void {
    if (included.has(threadId)) return
    const thread = byId.get(threadId)
    if (!thread) return
    included.add(threadId)
    pinnedThreads.push(thread)
    for (const child of childrenByParent.get(threadId) ?? []) {
      appendThreadTree(child.id)
    }
  }

  for (const threadId of pinnedThreadIds) {
    const thread = byId.get(threadId)
    if (!thread || isSubAgentThread(thread)) continue
    appendThreadTree(threadId)
  }

  return {
    pinnedThreads,
    unpinnedThreads: threads.filter((thread) => !included.has(thread.id))
  }
}

export function excludePinnedThreadTrees(threads: ThreadSummary[], pinnedThreadIds: string[]): ThreadSummary[] {
  return partitionPinnedThreads(threads, pinnedThreadIds).unpinnedThreads
}

export function topLevelThreadIds(threads: ThreadSummary[]): string[] {
  return threads.filter((thread) => !isSubAgentThread(thread)).map((thread) => thread.id)
}
