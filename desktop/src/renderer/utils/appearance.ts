import {
  CODE_FONT_SIZE_MAX,
  CODE_FONT_SIZE_MIN,
  type AppearanceSettings,
  type ReduceMotionMode
} from '../../shared/appearance'
import { THEME_CHANGED_EVENT, type ThemeChangedDetail } from '../../shared/theme'
import { deriveThemeProperties } from '../../shared/themeDerive'
import {
  EMPTY_THEME_SEEDS,
  type ThemeSeed,
  type ThemeSeedOverrides,
  type ThemeVariant
} from '../../shared/themeSeed'

const CODE_SIZE_VAR = '--text-code-size'

let accentOverride: string | null = null
let seedsByVariant: Record<ThemeVariant, ThemeSeedOverrides> = EMPTY_THEME_SEEDS
let desktopPluginSeeds: Partial<Record<ThemeVariant, Partial<ThemeSeed>>> = {}
let seedRevision = 0

/** Rises with every applied seed change; rides {@link THEME_CHANGED_EVENT}. */
export function themeSeedRevision(): number {
  return seedRevision
}

function currentVariant(): ThemeVariant {
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
}

function writeThemeSeed(): boolean {
  const style = document.documentElement.style
  let changed = false
  const variant = currentVariant()
  const overrides = {
    accent: accentOverride ?? undefined,
    ...seedsByVariant[variant],
    ...desktopPluginSeeds[variant]
  }
  for (const [name, value] of Object.entries(deriveThemeProperties(overrides, variant))) {
    if (value == null) {
      if (!style.getPropertyValue(name)) continue
      style.removeProperty(name)
    } else if (style.getPropertyValue(name) !== value) {
      style.setProperty(name, value)
    } else {
      continue
    }
    changed = true
  }
  if (changed) seedRevision += 1
  return changed
}

/**
 * Override the theme seed: one accent across both variants, plus per-variant background and
 * contrast. A field left out falls back to the variant's authored value, and a field equal to
 * its default is removed rather than restated, so tokens.css keeps answering for a default app.
 */
export function applyThemeSeeds(
  accent: string | null,
  seeds: Record<ThemeVariant, ThemeSeedOverrides> = EMPTY_THEME_SEEDS
): void {
  accentOverride = accent
  seedsByVariant = seeds
  if (!writeThemeSeed()) return
  const detail: ThemeChangedDetail = { mode: currentVariant(), seedRevision }
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail }))
}

/** Apply the winning Desktop Plugin theme layer over the user's Appearance seed. */
export function applyDesktopPluginThemeSeedOverride(
  seeds: Partial<Record<ThemeVariant, Partial<ThemeSeed>>> | null
): void {
  desktopPluginSeeds = seeds ?? {}
  if (!writeThemeSeed()) return
  const detail: ThemeChangedDetail = { mode: currentVariant(), seedRevision }
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail }))
}

/** Repaint the stored seed against a newly resolved variant. applyTheme announces it. */
export function reapplyThemeSeed(): void {
  writeThemeSeed()
}

/** Override the code font-size token, or clear it to fall back to the token default. */
export function applyCodeFontSize(px: number | null): void {
  const root = document.documentElement
  if (px != null && px >= CODE_FONT_SIZE_MIN && px <= CODE_FONT_SIZE_MAX) {
    root.style.setProperty(CODE_SIZE_VAR, `${px}px`)
  } else {
    root.style.removeProperty(CODE_SIZE_VAR)
  }
}

/** Reflect the motion preference as `data-reduce-motion` for the CSS rules in tokens.css. */
export function applyReduceMotion(mode: ReduceMotionMode): void {
  document.documentElement.setAttribute('data-reduce-motion', mode)
}

/**
 * Reflect the pointer-cursor preference as `data-pointer-cursors` (`true`/`false`). An explicit
 * value (not removal) lets the off state authoritatively force the native arrow over the
 * `cursor: pointer` many components hardcode inline; see tokens.css.
 */
export function applyPointerCursors(on: boolean): void {
  document.documentElement.setAttribute('data-pointer-cursors', on ? 'true' : 'false')
}

/** Reflect the sidebar translucency preference; the off state repaints the chrome opaque. */
export function applyTranslucentSidebar(on: boolean): void {
  document.documentElement.setAttribute('data-translucent-sidebar', on ? 'true' : 'false')
}

/**
 * Apply the document-level appearance preferences. Theme mode is applied via {@link applyTheme},
 * diff markers are held in the UI store, and interface zoom is applied via the renderer/main
 * zoom factor, so none of those are handled here.
 */
export function applyAppearanceDom(appearance: AppearanceSettings): void {
  applyThemeSeeds(appearance.accent, appearance.themeSeeds)
  applyCodeFontSize(appearance.codeFontSize)
  applyReduceMotion(appearance.reduceMotion)
  applyPointerCursors(appearance.pointerCursors)
  applyTranslucentSidebar(appearance.translucentSidebar)
}
