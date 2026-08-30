export type ThemeVariant = 'dark' | 'light'

export interface ThemeSeed {
  surface: string
  ink: string
  accent: string
  /** 0-100. */
  contrast: number
}

export const DEFAULT_SEEDS: Record<ThemeVariant, ThemeSeed> = {
  dark: { surface: '#141515', ink: '#eeeeec', accent: '#4566cc', contrast: 60 },
  light: { surface: '#ffffff', ink: '#1a1c1f', accent: '#4f6cce', contrast: 45 }
}

export const CONTRAST_MIN = 0
export const CONTRAST_MAX = 100

export function normalizeHexColor(raw: unknown): string | null {
  if (typeof raw !== 'string') return null
  let value = raw.trim().toLowerCase()
  if (!value) return null
  if (!value.startsWith('#')) value = `#${value}`
  if (/^#[0-9a-f]{3}$/.test(value)) {
    value = `#${value.slice(1).split('').map((c) => c + c).join('')}`
  }
  return /^#[0-9a-f]{6}$/.test(value) ? value : null
}

/** Ink follows the surface; accent is shared across variants. */
export interface ThemeSeedOverrides {
  surface?: string
  contrast?: number
}

export const EMPTY_THEME_SEEDS: Record<ThemeVariant, ThemeSeedOverrides> = { dark: {}, light: {} }

export function normalizeThemeSeedOverrides(raw: unknown, variant: ThemeVariant): ThemeSeedOverrides {
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return {}
  const source = raw as { surface?: unknown; contrast?: unknown }
  const overrides: ThemeSeedOverrides = {}
  const surface = normalizeHexColor(source.surface)
  if (surface && surface !== DEFAULT_SEEDS[variant].surface) overrides.surface = surface
  if (typeof source.contrast === 'number' && Number.isFinite(source.contrast)) {
    const contrast = normalizeContrastValue(source.contrast, variant)
    if (contrast !== DEFAULT_SEEDS[variant].contrast) overrides.contrast = contrast
  }
  return overrides
}

export function normalizeThemeSeeds(raw: unknown): Record<ThemeVariant, ThemeSeedOverrides> {
  const source = (raw ?? {}) as { dark?: unknown; light?: unknown }
  return {
    dark: normalizeThemeSeedOverrides(source.dark, 'dark'),
    light: normalizeThemeSeedOverrides(source.light, 'light')
  }
}

export function normalizeContrastValue(raw: unknown, variant: ThemeVariant): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return DEFAULT_SEEDS[variant].contrast
  return Math.min(CONTRAST_MAX, Math.max(CONTRAST_MIN, Math.round(raw)))
}

export function normalizeThemeSeed(
  raw: { surface?: unknown; ink?: unknown; accent?: unknown; contrast?: unknown } | null | undefined,
  variant: ThemeVariant
): ThemeSeed {
  const defaults = DEFAULT_SEEDS[variant]
  const source = raw ?? {}
  return {
    surface: normalizeHexColor(source.surface) ?? defaults.surface,
    ink: normalizeHexColor(source.ink) ?? defaults.ink,
    accent: normalizeHexColor(source.accent) ?? defaults.accent,
    contrast: normalizeContrastValue(source.contrast, variant)
  }
}
