import type { DesktopPluginSettings } from '@dotcraft/plugin'

export interface SummarySettings {
  readonly pinned: boolean
}

let current: SummarySettings | null = null
let store: DesktopPluginSettings | null = null
let stopStoreSubscription: (() => void) | null = null
let mutationRevision = 0
const listeners = new Set<(settings: SummarySettings) => void>()

export async function initializeSettings(settings: DesktopPluginSettings): Promise<void> {
  stopStoreSubscription?.()
  store = settings
  publish((await settings.get<SummarySettings>()).value)
  stopStoreSubscription = settings.onChange<SummarySettings>((snapshot) => {
    publish(snapshot.value)
  })
}

export function getSettings(): SummarySettings | null {
  return current
}

export function setPinned(pinned: boolean): void {
  if (!current) return
  publish({ ...current, pinned })
  const revision = ++mutationRevision
  void store?.mutate<SummarySettings>('personal', [{ op: 'set', key: 'pinned', value: pinned }])
    .then((snapshot) => {
      if (revision === mutationRevision) publish(snapshot.value)
    })
    .catch((error: unknown) => console.error('Summary could not write its settings:', error))
}

export function subscribeSettings(listener: (settings: SummarySettings) => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function publish(next: SummarySettings): void {
  const normalized = { pinned: next?.pinned === true }
  if (current !== null && current.pinned === normalized.pinned) return
  current = normalized
  for (const listener of listeners) listener(normalized)
}
