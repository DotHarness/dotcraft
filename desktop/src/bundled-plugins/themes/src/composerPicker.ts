import { useSyncExternalStore } from 'react'

let open = false
const listeners = new Set<() => void>()

function publish(next: boolean): void {
  if (open === next) return
  open = next
  for (const listener of [...listeners]) listener()
}

export function openComposerPicker(): void { publish(true) }
export function closeComposerPicker(): void { publish(false) }
export function isComposerPickerOpen(): boolean { return open }

export function useComposerPickerOpen(): boolean {
  return useSyncExternalStore(
    (listener) => { listeners.add(listener); return () => listeners.delete(listener) },
    isComposerPickerOpen,
    isComposerPickerOpen
  )
}
