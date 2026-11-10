import { isHiddenThread } from '../../shared/hiddenThreads'
import { getThreadConversation } from './conversationStore'
import { getThreadConnection, isThreadObservationCurrent } from './threadConnectionStore'
import { isSameThreadObservationTarget } from '../utils/threadObservationScope'
import type { Thread } from '../types/thread'
import type { ThreadObservationTarget } from '../utils/threadObservationScope'

const WARM_THREAD_LIMIT = 10
const WARM_THREAD_TTL_MS = 3 * 60 * 60 * 1000
export const WARM_THREAD_SWEEP_MS = 60 * 1000

export interface WarmThread {
  thread: Thread
  target: ThreadObservationTarget
  viewedAt: number
}

export interface WarmThreads {
  /** Returns the least recently viewed threads pushed over the limit. */
  keep(warm: WarmThread): string[]
  peek(threadId: string): WarmThread | undefined
  take(threadId: string): WarmThread | undefined
  updateThread(thread: Thread): void
  remove(threadId: string): void
  entries(): Array<[string, WarmThread]>
  expired(now: number): string[]
}

export function readyWarmThread(warm: WarmThreads, threadId: string): WarmThread | undefined {
  const entry = warm.peek(threadId)
  return entry && entry.thread.status !== 'archived' && !isHiddenThread(entry.thread)
    && getThreadConversation(threadId) && isThreadObservationCurrent(entry.target)
    && isSameThreadObservationTarget(getThreadConnection(threadId), entry.target)
    ? entry : undefined
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
    peek(threadId) {
      const warm = warmThreads.get(threadId)
      return warm && Date.now() - warm.viewedAt < WARM_THREAD_TTL_MS ? warm : undefined
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
