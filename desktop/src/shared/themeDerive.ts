import { DEFAULT_SEEDS, type ThemeSeed, type ThemeVariant } from './themeSeed'

const CONTRAST_SLOPE = 0.7
const CONTRAST_SPAN = 60
const CONTRAST_SLOPE_ABOVE = 2

/** sRGB luminance where black becomes more legible than white. */
const DARK_TEXT_LUMINANCE = 0.179

/**
 * Map the 0-100 contrast onto the multiplier the token ramps read as `--contrast-k`. The
 * variant baseline normalizes to `baseline / 100`, which is why the authored percentages in
 * `tokens.css` are solved against 0.6 (dark) and 0.45 (light).
 */
export function normalizeContrast(contrast: number, variant: ThemeVariant): number {
  const baseline = DEFAULT_SEEDS[variant].contrast
  const atBaseline = baseline / 100
  const linear = contrast / 100 + ((contrast - baseline) / CONTRAST_SPAN) * CONTRAST_SLOPE
  return contrast <= baseline ? linear : atBaseline + (linear - atBaseline) * CONTRAST_SLOPE_ABOVE
}

interface Rgb {
  red: number
  green: number
  blue: number
}

function toRgb(hex: string): Rgb {
  const value = hex.slice(1)
  return {
    red: Number.parseInt(value.slice(0, 2), 16),
    green: Number.parseInt(value.slice(2, 4), 16),
    blue: Number.parseInt(value.slice(4, 6), 16)
  }
}

function linearize(component: number): number {
  const value = component / 255
  return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
}

function toHex(rgb: Rgb): string {
  const channel = (value: number): string => Math.round(value).toString(16).padStart(2, '0')
  return `#${channel(rgb.red)}${channel(rgb.green)}${channel(rgb.blue)}`
}

export function mixHex(a: string, b: string, t: number): string {
  const from = toRgb(a)
  const to = toRgb(b)
  const ratio = Math.min(1, Math.max(0, t))
  return toHex({
    red: from.red + (to.red - from.red) * ratio,
    green: from.green + (to.green - from.green) * ratio,
    blue: from.blue + (to.blue - from.blue) * ratio
  })
}

export function relativeLuminance(hex: string): number {
  const { red, green, blue } = toRgb(hex)
  return linearize(red) * 0.2126 + linearize(green) * 0.7152 + linearize(blue) * 0.0722
}

/**
 * White or black over the accent. The threshold is the crossover where both reach about
 * 4.58:1, so whichever wins clears the 4.5:1 that `--on-accent` needs as a text color.
 */
export function textOnAccent(accent: string): string {
  return relativeLuminance(accent) > DARK_TEXT_LUMINANCE ? '#000000' : '#ffffff'
}

export function inkForSurface(surface: string): string {
  return relativeLuminance(surface) > DARK_TEXT_LUMINANCE
    ? DEFAULT_SEEDS.light.ink
    : DEFAULT_SEEDS.dark.ink
}

/**
 * The properties to write on the document element. A field left at its variant default
 * yields null, meaning "remove the inline override and let the stylesheet answer".
 */
export function deriveThemeProperties(
  overrides: Partial<ThemeSeed> | null,
  variant: ThemeVariant
): Record<string, string | null> {
  const defaults = DEFAULT_SEEDS[variant]
  // Explicit fallback also handles callers that pass a cleared field as undefined.
  const surface = overrides?.surface ?? defaults.surface
  const seed: ThemeSeed = {
    surface,
    ink: overrides?.ink ?? inkForSurface(surface),
    accent: overrides?.accent ?? defaults.accent,
    contrast: overrides?.contrast ?? defaults.contrast
  }
  return {
    '--seed-surface': seed.surface === defaults.surface ? null : seed.surface,
    '--seed-ink': seed.ink === defaults.ink ? null : seed.ink,
    '--seed-accent': seed.accent === defaults.accent ? null : seed.accent,
    '--seed-contrast': seed.contrast === defaults.contrast ? null : String(seed.contrast),
    '--contrast-k':
      seed.contrast === defaults.contrast ? null : String(normalizeContrast(seed.contrast, variant)),
    '--on-accent': seed.accent === defaults.accent ? null : textOnAccent(seed.accent)
  }
}
