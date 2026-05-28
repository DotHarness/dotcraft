import type { ThreadSummary } from '../types/thread'

const INTERNAL_METADATA_KEY = 'dotcraft.internal'
const INTERNAL_ORIGINS = new Set(['welcome-suggest', 'commit-suggest'])

export function isInternalThread(thread: Pick<ThreadSummary, 'originChannel' | 'metadata'>): boolean {
  const metadata = thread.metadata
  if (
    metadata != null &&
    Object.prototype.hasOwnProperty.call(metadata, INTERNAL_METADATA_KEY) &&
    String(metadata[INTERNAL_METADATA_KEY] ?? '').trim().length > 0
  ) {
    return true
  }

  return INTERNAL_ORIGINS.has((thread.originChannel ?? '').toLowerCase())
}
