import { DEFAULT_THEME_MODE, resolveThemeMode, type ThemeMode } from './theme'
import {
  EMPTY_THEME_SEEDS,
  normalizeHexColor,
  normalizeThemeSeeds,
  type ThemeSeedOverrides,
  type ThemeVariant
} from './themeSeed'

/** How code diffs are rendered: tinted line backgrounds, or +/- gutter markers with colored text. */
export type DiffMarkerMode = 'color' | 'sign'

export type ComposerEffectsMode = 'colorful' | 'plain'

export const COMPOSER_TYPING_EFFECTS = ['rhythm', 'bloom', 'sparks', 'resolve', 'wake', 'off'] as const
export type ComposerTypingEffect = (typeof COMPOSER_TYPING_EFFECTS)[number]

/** Motion preference: follow the OS, force-reduce, or always animate. */
export type ReduceMotionMode = 'system' | 'on' | 'off'

/**
 * Desktop-local appearance preferences. All are persisted in settings.json and applied
 * to the renderer document (theme via `data-theme`, the rest via CSS vars / attributes).
 */
export interface AppearanceSettings {
  themeMode: ThemeMode
  /** Accent color hex (`#rrggbb`), or null to use the per-theme token default. */
  accent: string | null
  /** Background and contrast overrides, per variant; an empty object means the tokens answer. */
  themeSeeds: Record<ThemeVariant, ThemeSeedOverrides>
  /** Code font size in px, or null to use the `--text-code-size` token default. */
  codeFontSize: number | null
  diffMarkers: DiffMarkerMode
  reduceMotion: ReduceMotionMode
  composerEffects: ComposerEffectsMode
  composerTyping: ComposerTypingEffect
  pointerCursors: boolean
  /** Whole-interface zoom factor (1 = 100%). Applied via the renderer zoom, not a CSS var. */
  interfaceZoom: number
  /** Whether the window chrome around the sidebar stays translucent. */
  translucentSidebar: boolean
  welcomeBackdrop: boolean
}

export const CODE_FONT_SIZE_MIN = 10
export const CODE_FONT_SIZE_MAX = 20
/** Matches the `--text-code-size` default in tokens.css. */
export const DEFAULT_CODE_FONT_SIZE = 12

/** The UI font size in px at 100% interface zoom, matching `--type-body-size`. The control shows a font size but persists a zoom factor. */
export const DEFAULT_UI_FONT_SIZE = 14
export const UI_FONT_SIZE_MIN = 11
export const UI_FONT_SIZE_MAX = 21
export const DEFAULT_INTERFACE_ZOOM = 1

export function interfaceZoomToUiFontPx(zoom: number): number {
  return Math.round(zoom * DEFAULT_UI_FONT_SIZE)
}

export function uiFontPxToInterfaceZoom(px: number): number {
  return px / DEFAULT_UI_FONT_SIZE
}

export const DEFAULT_APPEARANCE: AppearanceSettings = {
  themeMode: DEFAULT_THEME_MODE,
  accent: null,
  themeSeeds: EMPTY_THEME_SEEDS,
  codeFontSize: null,
  diffMarkers: 'color',
  reduceMotion: 'system',
  composerEffects: 'colorful',
  composerTyping: 'rhythm',
  pointerCursors: true,
  interfaceZoom: DEFAULT_INTERFACE_ZOOM,
  translucentSidebar: true,
  welcomeBackdrop: true
}

/** The accent is one field of the theme seed, so it normalizes the same way every color does. */
export const normalizeAccentHex = normalizeHexColor

export function normalizeCodeFontSize(raw: unknown): number | null {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return null
  const value = Math.round(raw)
  if (value < CODE_FONT_SIZE_MIN || value > CODE_FONT_SIZE_MAX) return null
  return value
}

export function normalizeDiffMarkers(raw: unknown): DiffMarkerMode {
  return raw === 'sign' ? 'sign' : 'color'
}

export function normalizeReduceMotion(raw: unknown): ReduceMotionMode {
  return raw === 'on' || raw === 'off' ? raw : 'system'
}

export function normalizeComposerEffects(raw: unknown): ComposerEffectsMode {
  return raw === 'plain' ? 'plain' : DEFAULT_APPEARANCE.composerEffects
}

export function normalizeComposerTyping(raw: unknown): ComposerTypingEffect {
  return COMPOSER_TYPING_EFFECTS.find((effect) => effect === raw) ?? DEFAULT_APPEARANCE.composerTyping
}

export function normalizePointerCursors(raw: unknown): boolean {
  return raw !== false
}

/**
 * Snap a zoom factor to the UI-font px grid and clamp it to the supported size range, defaulting
 * to 100%. The control steps by whole px and persists `px / 14`, so snapping here keeps those
 * values stable across reloads — a raw 1-decimal round would drift (12px -> 0.857 -> 0.9 -> 13px).
 */
export function normalizeInterfaceZoom(raw: unknown): number {
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return DEFAULT_INTERFACE_ZOOM
  const px = Math.min(UI_FONT_SIZE_MAX, Math.max(UI_FONT_SIZE_MIN, Math.round(raw * DEFAULT_UI_FONT_SIZE)))
  return px / DEFAULT_UI_FONT_SIZE
}

export function normalizeTranslucentSidebar(raw: unknown): boolean {
  return raw !== false
}

export function normalizeWelcomeBackdrop(raw: unknown): boolean {
  return raw !== false
}

/** Build a fully-normalized AppearanceSettings from loosely-typed persisted input. */
export function resolveAppearanceSettings(raw: {
  theme?: unknown
  accent?: unknown
  themeSeeds?: unknown
  codeFontSize?: unknown
  diffMarkers?: unknown
  reduceMotion?: unknown
  composerEffects?: unknown
  composerTyping?: unknown
  pointerCursors?: unknown
  interfaceZoom?: unknown
  translucentSidebar?: unknown
  welcomeBackdrop?: unknown
} | null | undefined): AppearanceSettings {
  const source = raw ?? {}
  return {
    themeMode: resolveThemeMode(source.theme),
    accent: normalizeAccentHex(source.accent),
    themeSeeds: normalizeThemeSeeds(source.themeSeeds),
    codeFontSize: normalizeCodeFontSize(source.codeFontSize),
    diffMarkers: normalizeDiffMarkers(source.diffMarkers),
    reduceMotion: normalizeReduceMotion(source.reduceMotion),
    composerEffects: normalizeComposerEffects(source.composerEffects),
    composerTyping: normalizeComposerTyping(source.composerTyping),
    pointerCursors: normalizePointerCursors(source.pointerCursors),
    interfaceZoom: normalizeInterfaceZoom(source.interfaceZoom),
    translucentSidebar: normalizeTranslucentSidebar(source.translucentSidebar),
    welcomeBackdrop: normalizeWelcomeBackdrop(source.welcomeBackdrop)
  }
}
