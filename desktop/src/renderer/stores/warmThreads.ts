import type { Thread } from '../types/thread'
import type { ThreadSubscriptionTarget } from '../utils/threadSubscriptionCoordinator'

const WARM_THREAD_LIMIT = 10
const WARM_THREAD_TTL_MS = 3 * 60 * 60 * 1000
export const WARM_THREAD_SWEEP_MS = 60 * 1000

export interface WarmThread {
  thread: Thread
  target: ThreadSubscriptionTarget
  viewedAt: number
}

export interface WarmThreads {
  /** Returns the least recently viewed threads pushed over the limit. */
  keep(warm: WarmThread): string[]
  take(threadId: string): WarmThread | undefined
  updateThread(thread: Thread): void
  remove(threadId: string): void
  entries(): Array<[string, WarmThread]>
  expired(now: number): string[]
}

export function createWarmThreads(): WarmThreads {
  const warmThreads = new Map<string, WarmThread>()
  return {
    keep(warm) {
      warmThreads.delete(warm.target.threadId)
      warmThreads.set(warm.target.threadId, warm)
      const evicted = [...warmThreads.keys()].slice(0, Math.max(0, warmThreads.size - WARM_THREAD_LIMIT))
      for (const threadId of evicted) warmThreads.delete(threadId)
      return evicted
    },
    take(threadId) {
      const warm = warmThreads.get(threadId)
      warmThreads.delete(threadId)
      return warm
    },
    updateThread(thread) {
      const warm = warmThreads.get(thread.id)
      if (warm) warmThreads.set(thread.id, { ...warm, thread })
    },
    remove(threadId) {
      warmThreads.delete(threadId)
    },
    entries: () => [...warmThreads.entries()],
    expired: (now) => [...warmThreads.entries()]
      .filter(([, warm]) => now - warm.viewedAt >= WARM_THREAD_TTL_MS)
      .map(([threadId]) => threadId)
  }
}
