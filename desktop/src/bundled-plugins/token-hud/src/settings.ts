import type { DesktopPluginSettings, DesktopPluginSettingsMutation } from '@dotcraft/plugin'

export interface TokenHudSettings {
  readonly visible: boolean
  readonly opacity: number
}

let current: TokenHudSettings | null = null
let store: DesktopPluginSettings | null = null
let stopStoreSubscription: (() => void) | null = null
let mutationRevision = 0
const listeners = new Set<(settings: TokenHudSettings) => void>()

export async function initializeSettings(settings: DesktopPluginSettings): Promise<void> {
  stopStoreSubscription?.()
  store = settings
  publish((await settings.get<TokenHudSettings>()).value)
  stopStoreSubscription = settings.onChange<TokenHudSettings>((snapshot) => {
    publish(snapshot.value)
  })
}

export function getSettings(): TokenHudSettings | null {
  return current
}

export function setSettings(patch: Partial<TokenHudSettings>): TokenHudSettings | null {
  if (!current) return null
  publish({ ...current, ...patch })
  const revision = ++mutationRevision
  void store?.mutate<TokenHudSettings>('personal', mutationsOf(patch))
    .then((snapshot) => {
      if (revision === mutationRevision) publish(snapshot.value)
    })
    .catch((error: unknown) => console.error('Token HUD could not write its settings:', error))
  return current
}

export function previewSettings(patch: Partial<TokenHudSettings>): TokenHudSettings | null {
  if (!current) return null
  publish({ ...current, ...patch })
  return current
}

export function subscribeSettings(listener: (settings: TokenHudSettings) => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function mutationsOf(patch: Partial<TokenHudSettings>): DesktopPluginSettingsMutation[] {
  return Object.entries(patch).map(([key, value]) => ({ op: 'set', key, value }))
}

function publish(next: TokenHudSettings): void {
  if (current !== null && current.visible === next.visible && current.opacity === next.opacity) return
  current = next
  for (const listener of listeners) listener(next)
}
