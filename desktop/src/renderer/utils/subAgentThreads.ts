import type { ThreadSummary } from '../types/thread'

export function isSubAgentThread(thread: Pick<ThreadSummary, 'originChannel' | 'source'>): boolean {
  return thread.source?.kind?.toLowerCase() === 'subagent'
    || thread.originChannel?.toLowerCase() === 'subagent'
}

export function getSubAgentParentThreadId(thread: ThreadSummary): string | null {
  const sourceParent = thread.source?.subAgent?.parentThreadId?.trim()
  if (sourceParent) return sourceParent
  const context = thread.channelContext?.trim()
  if (context?.startsWith('thread_')) return context
  const metadataContext = typeof thread.metadata?.channelContext === 'string'
    ? thread.metadata.channelContext.trim()
    : ''
  if (metadataContext?.startsWith('thread_')) return metadataContext
  return null
}

/**
 * Origin thread id for a thread spawned by another (non-subagent) thread, e.g. via
 * the Desktop CreateThread tool. Stored in thread metadata so it survives restart.
 * Returns null for normal threads and for subagent children (those use the dock).
 */
export function getSpawnedFromThreadId(thread: Pick<ThreadSummary, 'metadata'>): string | null {
  const raw = thread.metadata?.spawnedFromThreadId
  return typeof raw === 'string' && raw.trim() ? raw.trim() : null
}

export function getSubAgentDepth(thread: ThreadSummary): number {
  const depth = thread.source?.subAgent?.depth
  return typeof depth === 'number' && Number.isFinite(depth) && depth > 0
    ? Math.min(4, Math.floor(depth))
    : isSubAgentThread(thread)
      ? 1
      : 0
}
