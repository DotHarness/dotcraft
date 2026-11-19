import { useSyncExternalStore } from 'react'

export type OpenRequest =
  | { kind: 'entry'; fileName: string; path: string; storeId: string | null; workspacePath: string | null }
  | { kind: 'new'; body: string }

let pending: OpenRequest | null = null
const listeners = new Set<() => void>()

export function requestOpen(request: OpenRequest): void {
  pending = request
  for (const listener of listeners) listener()
}

export function takeOpenRequest(): OpenRequest | null {
  const request = pending
  pending = null
  if (request) for (const listener of listeners) listener()
  return request
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function usePendingOpenRequest(): OpenRequest | null {
  return useSyncExternalStore(subscribe, () => pending, () => pending)
}
