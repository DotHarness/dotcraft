import { useSyncExternalStore } from 'react'

let open = false
const listeners = new Set<() => void>()

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

function getOpen(): boolean {
  return open
}

export function setLibraryPageOpen(next: boolean): void {
  if (open === next) return
  open = next
  for (const listener of listeners) listener()
}

export function useLibraryPageOpen(): boolean {
  return useSyncExternalStore(subscribe, getOpen, getOpen)
}
