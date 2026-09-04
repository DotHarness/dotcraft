import type { DesktopPluginSettings, DesktopPluginSettingsMutation } from '@dotcraft/plugin'

export const WALLPAPER_CHANGED = 'wallpaper.settings-changed'

export type WallpaperFit = 'cover' | 'contain' | 'tile'

export type WallpaperChoice =
  | { readonly kind: 'none' }
  | { readonly kind: 'preset'; readonly id: string }
  | { readonly kind: 'image'; readonly id: string }

export interface WallpaperSettings {
  readonly enabled: boolean
  readonly light: WallpaperChoice
  readonly dark: WallpaperChoice
  readonly blur: number
  readonly dim: number
  readonly surfaceOpacity: number
  readonly fit: WallpaperFit
}

let current: WallpaperSettings | null = null
let persisted: WallpaperSettings | null = null
let store: DesktopPluginSettings | null = null
let stopStoreSubscription: (() => void) | null = null
let mutationRevision = 0
const listeners = new Set<(settings: WallpaperSettings) => void>()

export async function initializeSettings(settings: DesktopPluginSettings): Promise<void> {
  stopStoreSubscription?.()
  store = settings
  persisted = normalize((await settings.get<WallpaperSettings>()).value)
  publish(persisted)
  stopStoreSubscription = settings.onChange<WallpaperSettings>((snapshot) => {
    persisted = normalize(snapshot.value)
    publish(persisted)
  })
}

export function getSettings(): WallpaperSettings | null {
  return current
}

export function setSettings(patch: Partial<WallpaperSettings>): WallpaperSettings | null {
  if (!current) return null
  const next = normalize({ ...current, ...patch })
  publish(next)
  const revision = ++mutationRevision
  void store?.mutate<WallpaperSettings>('personal', mutationsOf(patch))
    .then((snapshot) => {
      if (revision !== mutationRevision) return
      persisted = normalize(snapshot.value)
      publish(persisted)
    })
    .catch((error: unknown) => console.error('Wallpaper could not write its settings:', error))
  return current
}

export function previewSettings(patch: Partial<WallpaperSettings>): WallpaperSettings | null {
  if (!current) return null
  publish(normalize({ ...current, ...patch }))
  return current
}

export function subscribeSettings(listener: (settings: WallpaperSettings) => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function choiceFor(settings: WallpaperSettings, theme: 'light' | 'dark'): WallpaperChoice {
  return theme === 'dark' ? settings.dark : settings.light
}

function mutationsOf(patch: Partial<WallpaperSettings>): DesktopPluginSettingsMutation[] {
  return Object.entries(patch).map(([key, value]) => ({ op: 'set', key, value }))
}

function publish(next: WallpaperSettings): void {
  if (current !== null && JSON.stringify(current) === JSON.stringify(next)) return
  current = next
  for (const listener of [...listeners]) listener(next)
}

function normalize(value: WallpaperSettings): WallpaperSettings {
  return { ...value, light: normalizeChoice(value.light), dark: normalizeChoice(value.dark) }
}

function normalizeChoice(value: unknown): WallpaperChoice {
  if (typeof value !== 'object' || value === null) return { kind: 'none' }
  const choice = value as WallpaperChoice
  if (choice.kind === 'none') return choice
  if ((choice.kind === 'preset' || choice.kind === 'image') && typeof choice.id === 'string') return choice
  return { kind: 'none' }
}
