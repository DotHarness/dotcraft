import type { ThreadSummary, ThreadGroup } from '../types/thread'
import { THREAD_GROUP_ORDER } from '../types/thread'

/**
 * Returns midnight (start) of the given date in local time.
 */
function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

/**
 * Classifies a thread into a time-based group based on its `lastActiveAt` date.
 * Groups defined in spec §7.2.
 */
export function classifyThread(lastActiveAt: string, now: Date = new Date()): ThreadGroup {
  const threadDate = new Date(lastActiveAt)
  const todayStart = startOfDay(now)
  const yesterdayStart = new Date(todayStart.getTime() - 24 * 60 * 60 * 1000)
  const sevenDaysAgo = new Date(todayStart.getTime() - 7 * 24 * 60 * 60 * 1000)
  const thirtyDaysAgo = new Date(todayStart.getTime() - 30 * 24 * 60 * 60 * 1000)

  if (threadDate >= todayStart) return 'Today'
  if (threadDate >= yesterdayStart) return 'Yesterday'
  if (threadDate >= sevenDaysAgo) return 'Previous 7 Days'
  if (threadDate >= thirtyDaysAgo) return 'Previous 30 Days'
  return 'Older'
}

/**
 * Groups threads by time period.
 * Returns a Map preserving the canonical group order (Today → Older).
 * Empty groups are excluded from the result.
 * Threads within each group retain their original order (server returns lastActiveAt desc).
 */
export function groupThreads(
  threads: ThreadSummary[],
  now: Date = new Date()
): Map<ThreadGroup, ThreadSummary[]> {
  const groups = new Map<ThreadGroup, ThreadSummary[]>()

  for (const thread of threads) {
    const group = classifyThread(thread.lastActiveAt, now)
    const existing = groups.get(group)
    if (existing) {
      existing.push(thread)
    } else {
      groups.set(group, [thread])
    }
  }

  // Return groups in canonical order
  const ordered = new Map<ThreadGroup, ThreadSummary[]>()
  for (const group of THREAD_GROUP_ORDER) {
    if (groups.has(group)) {
      ordered.set(group, groups.get(group)!)
    }
  }
  return ordered
}
