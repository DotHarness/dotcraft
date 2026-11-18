import { useSyncExternalStore } from 'react'

const claims = new Map<string, number>()
const listeners = new Set<() => void>()

function notify(): void {
  for (const listener of listeners) listener()
}

export function claimTurnTasks(turnId: string): () => void {
  claims.set(turnId, (claims.get(turnId) ?? 0) + 1)
  notify()
  let released = false
  return () => {
    if (released) return
    released = true
    const next = (claims.get(turnId) ?? 1) - 1
    if (next > 0) claims.set(turnId, next)
    else claims.delete(turnId)
    notify()
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useTurnTasksClaimed(turnId: string): boolean {
  return useSyncExternalStore(subscribe, () => claims.has(turnId))
}
