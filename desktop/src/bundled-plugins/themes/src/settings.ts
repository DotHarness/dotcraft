import type { DesktopPluginSettings, DesktopPluginSettingsMutation } from '@dotcraft/plugin'

export interface ThemeSettings {
  readonly enabled: boolean
  readonly animate: boolean
  readonly blur: number
  readonly surfaceOpacity: number
}

export const MIN_SURFACE_OPACITY = 0.3

let current: ThemeSettings | null = null
let persisted: ThemeSettings | null = null
let store: DesktopPluginSettings | null = null
let stopStoreSubscription: (() => void) | null = null
let mutationRevision = 0
const listeners = new Set<(settings: ThemeSettings) => void>()

export async function initializeSettings(settings: DesktopPluginSettings): Promise<void> {
  stopStoreSubscription?.()
  store = settings
  persisted = normalize((await settings.get<ThemeSettings>()).value)
  publish(persisted)
  stopStoreSubscription = settings.onChange<ThemeSettings>((snapshot) => {
    persisted = normalize(snapshot.value)
    publish(persisted)
  })
}

export function getSettings(): ThemeSettings | null {
  return current
}

export function setSettings(patch: Partial<ThemeSettings>): ThemeSettings | null {
  if (!current) return null
  const next = normalize({ ...current, ...patch })
  publish(next)
  const revision = ++mutationRevision
  void store?.mutate<ThemeSettings>('personal', mutationsOf(patch))
    .then((snapshot) => {
      if (revision !== mutationRevision) return
      persisted = normalize(snapshot.value)
      publish(persisted)
    })
    .catch((error: unknown) => {
      if (revision === mutationRevision && persisted) publish(persisted)
      console.error('Themes could not write its settings:', error)
    })
  return current
}

export function previewSettings(patch: Partial<ThemeSettings>): void {
  if (current) publish(normalize({ ...current, ...patch }))
}

export function subscribeSettings(listener: (settings: ThemeSettings) => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function mutationsOf(patch: Partial<ThemeSettings>): DesktopPluginSettingsMutation[] {
  return Object.entries(patch).map(([key, value]) => ({ op: 'set', key, value }))
}

function publish(next: ThemeSettings): void {
  if (current !== null && JSON.stringify(current) === JSON.stringify(next)) return
  current = next
  for (const listener of [...listeners]) listener(next)
}

function normalize(value: ThemeSettings): ThemeSettings {
  return {
    ...value,
    animate: value.animate !== false,
    blur: within(value.blur, 0, 64, 0),
    surfaceOpacity: within(value.surfaceOpacity, MIN_SURFACE_OPACITY, 1, MIN_SURFACE_OPACITY)
  }
}

function within(value: unknown, min: number, max: number, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback
}
