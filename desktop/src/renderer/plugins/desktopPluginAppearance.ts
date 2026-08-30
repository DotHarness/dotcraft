import type {
  DesktopPluginBackdropPresentation,
  DesktopPluginThemeSeed,
  DesktopPluginThemeSeedOverrides
} from '@dotcraft/plugin'

import { normalizeContrastValue, normalizeHexColor, type ThemeVariant } from '../../shared/themeSeed'
import { applyDesktopPluginThemeSeedOverride } from '../utils/appearance'

interface AppearanceSlot {
  readonly order: number
  theme: DesktopPluginThemeSeedOverrides | null
  backdrop: DesktopPluginBackdropPresentation | null
}

export interface DesktopPluginAppearanceSlot {
  setThemeSeedOverride(value: DesktopPluginThemeSeedOverrides | null): void
  setBackdropPresentation(value: DesktopPluginBackdropPresentation | null): void
  dispose(): void
}

const slots = new Map<string, AppearanceSlot>()
let nextOrder = 1

export function registerDesktopPluginAppearanceSlot(owner: string): DesktopPluginAppearanceSlot {
  const slot: AppearanceSlot = { order: nextOrder++, theme: null, backdrop: null }
  slots.set(owner, slot)
  return {
    setThemeSeedOverride(value) {
      const next = normalizeThemeOverrides(value)
      if (sameThemeOverrides(slot.theme, next)) return
      slot.theme = next
      publishAppearance()
    },
    setBackdropPresentation(value) {
      const next = normalizeBackdrop(value)
      if (slot.backdrop?.surfaceOpacity === next?.surfaceOpacity) return
      slot.backdrop = next
      publishAppearance()
    },
    dispose() {
      if (slots.get(owner) !== slot) return
      slots.delete(owner)
      publishAppearance()
    }
  }
}

function winning<K extends 'theme' | 'backdrop'>(key: K): AppearanceSlot[K] {
  let winner: AppearanceSlot | null = null
  for (const slot of slots.values()) {
    if (slot[key] === null) continue
    if (winner === null || slot.order > winner.order) winner = slot
  }
  return winner?.[key] ?? null
}

function publishAppearance(): void {
  applyDesktopPluginThemeSeedOverride(winning('theme'))
  applyBackdropPresentation(winning('backdrop'))
}

function normalizeThemeOverrides(
  value: DesktopPluginThemeSeedOverrides | null
): DesktopPluginThemeSeedOverrides | null {
  if (value === null) return null
  const result: Partial<Record<ThemeVariant, Partial<DesktopPluginThemeSeed>>> = {}
  for (const variant of ['light', 'dark'] as const) {
    const source = value[variant]
    if (!source) continue
    const normalized: {
      surface?: string
      ink?: string
      accent?: string
      contrast?: number
    } = {}
    const surface = normalizeHexColor(source.surface)
    const ink = normalizeHexColor(source.ink)
    const accent = normalizeHexColor(source.accent)
    if (surface) normalized.surface = surface
    if (ink) normalized.ink = ink
    if (accent) normalized.accent = accent
    if (typeof source.contrast === 'number' && Number.isFinite(source.contrast)) {
      normalized.contrast = normalizeContrastValue(source.contrast, variant)
    }
    if (Object.keys(normalized).length > 0) result[variant] = normalized
  }
  return Object.keys(result).length > 0 ? result : null
}

function normalizeBackdrop(
  value: DesktopPluginBackdropPresentation | null
): DesktopPluginBackdropPresentation | null {
  if (value === null || !Number.isFinite(value.surfaceOpacity)) return null
  return { surfaceOpacity: Math.min(1, Math.max(0, value.surfaceOpacity)) }
}

function sameThemeOverrides(
  current: DesktopPluginThemeSeedOverrides | null,
  next: DesktopPluginThemeSeedOverrides | null
): boolean {
  return JSON.stringify(current) === JSON.stringify(next)
}

function applyBackdropPresentation(value: DesktopPluginBackdropPresentation | null): void {
  const root = document.documentElement
  if (value === null) {
    delete root.dataset.desktopPluginBackdrop
    root.style.removeProperty('--desktop-plugin-backdrop-surface-opacity')
    return
  }
  root.dataset.desktopPluginBackdrop = 'true'
  root.style.setProperty(
    '--desktop-plugin-backdrop-surface-opacity',
    `${Math.round(value.surfaceOpacity * 10000) / 100}%`
  )
}
