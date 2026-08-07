type VoiceOriginCleanup = (threadIds: readonly string[]) => void

let cleanup: VoiceOriginCleanup | null = null

export function registerVoiceOriginCleanup(next: VoiceOriginCleanup): () => void {
  cleanup = next
  return () => {
    if (cleanup === next) cleanup = null
  }
}

export function discardRemovedVoiceOrigins(threadIds: readonly string[]): void {
  if (threadIds.length > 0) cleanup?.(threadIds)
}
