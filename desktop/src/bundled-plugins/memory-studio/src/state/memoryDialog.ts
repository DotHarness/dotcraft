import { useSyncExternalStore } from 'react'

export type MemoryDialogRequest =
  | { id: number; mode: 'new'; body: string; storeKey?: string }
  | { id: number; mode: 'existing'; fileName: string; storeKey?: string; path?: string; storeId?: string | null; workspacePath?: string | null }

export type OpenMemoryDialogOptions =
  | { mode: 'new'; body?: string; storeKey?: string }
  | { mode: 'existing'; fileName: string; storeKey?: string; path?: string; storeId?: string | null; workspacePath?: string | null }

let nextId = 1
let current: MemoryDialogRequest | null = null
const requestListeners = new Set<() => void>()
const changeListeners = new Set<() => void>()

export function openMemoryDialog(options: OpenMemoryDialogOptions): void {
  current = options.mode === 'new'
    ? { id: nextId++, mode: 'new', body: options.body ?? '', storeKey: options.storeKey }
    : {
        id: nextId++,
        mode: 'existing',
        fileName: options.fileName,
        storeKey: options.storeKey,
        path: options.path,
        storeId: options.storeId,
        workspacePath: options.workspacePath
      }
  for (const listener of requestListeners) listener()
}

export function closeMemoryDialog(): void {
  if (current == null) return
  current = null
  for (const listener of requestListeners) listener()
}

function subscribeRequest(listener: () => void): () => void {
  requestListeners.add(listener)
  return () => requestListeners.delete(listener)
}

export function useMemoryDialogRequest(): MemoryDialogRequest | null {
  return useSyncExternalStore(subscribeRequest, () => current, () => current)
}

export function notifyMemoryChanged(): void {
  for (const listener of changeListeners) listener()
}

export function onMemoryChanged(listener: () => void): () => void {
  changeListeners.add(listener)
  return () => changeListeners.delete(listener)
}
