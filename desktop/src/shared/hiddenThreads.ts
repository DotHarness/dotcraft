const INTERNAL_METADATA_KEY = 'dotcraft.internal'
const INTERNAL_ORIGINS = new Set(['commit-suggest', 'dreams'])

export function isHiddenThread(thread: unknown): boolean {
  if (thread == null || typeof thread !== 'object') return false
  const { ephemeral, metadata, originChannel } = thread as {
    ephemeral?: unknown
    metadata?: unknown
    originChannel?: unknown
  }
  if (ephemeral === true) return true
  if (
    metadata != null &&
    typeof metadata === 'object' &&
    Object.prototype.hasOwnProperty.call(metadata, INTERNAL_METADATA_KEY) &&
    String((metadata as Record<string, unknown>)[INTERNAL_METADATA_KEY] ?? '').trim().length > 0
  ) {
    return true
  }
  return typeof originChannel === 'string' && INTERNAL_ORIGINS.has(originChannel.toLowerCase())
}
