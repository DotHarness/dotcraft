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
      if (JSON.stringify(slot.backdrop) === JSON.stringify(next)) return
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

const RAW_COLOR = /^(?:#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})|(?:rgb|hsl)a?\([0-9.,%/ deg]{1,59}\))$/i

function normalizeBackdrop(
  value: DesktopPluginBackdropPresentation | null
): DesktopPluginBackdropPresentation | null {
  if (value === null || !Number.isFinite(value.opacity)) return null
  const tint = typeof value.tint === 'string' && value.tint.length <= 64 && RAW_COLOR.test(value.tint) ? value.tint : undefined
  return {
    blur: Number.isFinite(value.blur) ? Math.min(64, Math.max(0, value.blur)) : 0,
    opacity: Math.min(1, Math.max(0, value.opacity)),
    ...(tint ? { tint } : {})
  }
}

function sameThemeOverrides(
  current: DesktopPluginThemeSeedOverrides | null,
  next: DesktopPluginThemeSeedOverrides | null
): boolean {
  return JSON.stringify(current) === JSON.stringify(next)
}

const BACKDROP_PROPERTIES = [
  '--desktop-plugin-backdrop-surface-opacity',
  '--desktop-plugin-backdrop-blur',
  '--desktop-plugin-backdrop-tint'
] as const
// Relative luminance where dark text and light text are equally readable.
const TINT_LUMINANCE_SPLIT = 0.179
let compositingChecked = false

function tintSuits(tint: string, variant: ThemeVariant): boolean {
  const probe = document.createElement('span')
  probe.style.color = tint
  const match = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/.exec(probe.style.color)
  if (!match) return false
  const channel = (raw: string): number => {
    const value = Number(raw) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  }
  const luminance = 0.2126 * channel(match[1]!) + 0.7152 * channel(match[2]!) + 0.0722 * channel(match[3]!)
  return variant === 'light' ? luminance >= TINT_LUMINANCE_SPLIT : luminance <= TINT_LUMINANCE_SPLIT
}

function markSoftwareCompositing(): void {
  if (compositingChecked) return
  compositingChecked = true
  void (async () => window.api.themes.hasGpuCompositing())()
    .then((accelerated) => {
      if (!accelerated) document.documentElement.dataset.softwareCompositing = 'true'
    })
    .catch(() => undefined)
}

let appearanceObserver: MutationObserver | null = null

function applyBackdropPresentation(value: DesktopPluginBackdropPresentation | null): void {
  const root = document.documentElement
  if (value === null) {
    appearanceObserver?.disconnect()
    appearanceObserver = null
    delete root.dataset.desktopPluginBackdrop
    delete root.dataset.desktopPluginBackdropUnsuited
    for (const property of BACKDROP_PROPERTIES) root.style.removeProperty(property)
    return
  }
  markSoftwareCompositing()
  if (!appearanceObserver) {
    appearanceObserver = new MutationObserver(() => applyBackdropPresentation(winning('backdrop')))
    appearanceObserver.observe(root, { attributes: true, attributeFilter: ['data-theme'] })
  }
  const variant: ThemeVariant = root.dataset.theme === 'light' ? 'light' : 'dark'
  const unsuited = value.tint !== undefined && !tintSuits(value.tint, variant)
  root.dataset.desktopPluginBackdrop = 'true'
  if (unsuited) root.dataset.desktopPluginBackdropUnsuited = 'true'
  else delete root.dataset.desktopPluginBackdropUnsuited
  root.style.setProperty(BACKDROP_PROPERTIES[0], `${Math.round(value.opacity * 10000) / 100}%`)
  root.style.setProperty(BACKDROP_PROPERTIES[1], `${value.blur}px`)
  if (value.tint === undefined || unsuited) root.style.removeProperty(BACKDROP_PROPERTIES[2])
  else root.style.setProperty(BACKDROP_PROPERTIES[2], value.tint)
}
