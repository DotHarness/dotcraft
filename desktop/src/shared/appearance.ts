import { resolveThemeMode, type ThemeMode } from './theme'

/** How code diffs are rendered: tinted line backgrounds, or +/- gutter markers with colored text. */
export type DiffMarkerMode = 'color' | 'sign'

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
  /** Code font size in px, or null to use the `--text-code-size` token default. */
  codeFontSize: number | null
  diffMarkers: DiffMarkerMode
  reduceMotion: ReduceMotionMode
  pointerCursors: boolean
  /** Whole-interface zoom factor (1 = 100%). Applied via the renderer zoom, not a CSS var. */
  interfaceZoom: number
  /** Whether the window chrome around the sidebar stays translucent. */
  translucentSidebar: boolean
}

export const CODE_FONT_SIZE_MIN = 10
export const CODE_FONT_SIZE_MAX = 20
/** Matches the `--text-code-size` default in tokens.css. */
export const DEFAULT_CODE_FONT_SIZE = 12

/**
 * The base UI font size in px that a 100% interface zoom represents — matches `--type-body-size`
 * in tokens.css. The Appearance control reads as a UI font size anchored on this value, but it is
 * applied as a whole-interface zoom (not a font CSS var), so the persisted value stays a zoom factor.
 */
export const DEFAULT_UI_FONT_SIZE = 14
/** UI font size bounds (px), spanning roughly the historical 0.8-1.5 interface-zoom range. */
export const UI_FONT_SIZE_MIN = 11
export const UI_FONT_SIZE_MAX = 21
/** Interface zoom factor (1 = 100% = the base UI font size). Applied via the renderer zoom. */
export const DEFAULT_INTERFACE_ZOOM = 1

/** The UI font size (px) an interface-zoom factor renders, anchored on {@link DEFAULT_UI_FONT_SIZE}. */
export function interfaceZoomToUiFontPx(zoom: number): number {
  return Math.round(zoom * DEFAULT_UI_FONT_SIZE)
}

/** The interface-zoom factor that renders the UI at the given font size (px). */
export function uiFontPxToInterfaceZoom(px: number): number {
  return px / DEFAULT_UI_FONT_SIZE
}

export const DEFAULT_APPEARANCE: AppearanceSettings = {
  themeMode: 'light',
  accent: null,
  codeFontSize: null,
  diffMarkers: 'color',
  reduceMotion: 'system',
  // Default true preserves DotCraft's historical pointer-on-clickable look; turning it off
  // switches interactive elements to the native arrow cursor.
  pointerCursors: true,
  interfaceZoom: DEFAULT_INTERFACE_ZOOM,
  translucentSidebar: true
}

/** Normalize an accent value to `#rrggbb` (expanding `#rgb`), or null when invalid/empty. */
export function normalizeAccentHex(raw: unknown): string | null {
  if (typeof raw !== 'string') return null
  let value = raw.trim().toLowerCase()
  if (!value) return null
  if (!value.startsWith('#')) value = `#${value}`
  if (/^#[0-9a-f]{3}$/.test(value)) {
    value = `#${value.slice(1).split('').map((c) => c + c).join('')}`
  }
  return /^#[0-9a-f]{6}$/.test(value) ? value : null
}

/** Normalize a code font size to an integer within bounds, or null when invalid/out of range. */
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

/** Pointer cursors default to on; only an explicit `false` disables them. */
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

/** Sidebar translucency defaults to on; only an explicit `false` makes it opaque. */
export function normalizeTranslucentSidebar(raw: unknown): boolean {
  return raw !== false
}

/** Build a fully-normalized AppearanceSettings from loosely-typed persisted input. */
export function resolveAppearanceSettings(raw: {
  theme?: unknown
  accent?: unknown
  codeFontSize?: unknown
  diffMarkers?: unknown
  reduceMotion?: unknown
  pointerCursors?: unknown
  interfaceZoom?: unknown
  translucentSidebar?: unknown
} | null | undefined): AppearanceSettings {
  const source = raw ?? {}
  return {
    themeMode: resolveThemeMode(source.theme),
    accent: normalizeAccentHex(source.accent),
    codeFontSize: normalizeCodeFontSize(source.codeFontSize),
    diffMarkers: normalizeDiffMarkers(source.diffMarkers),
    reduceMotion: normalizeReduceMotion(source.reduceMotion),
    pointerCursors: normalizePointerCursors(source.pointerCursors),
    interfaceZoom: normalizeInterfaceZoom(source.interfaceZoom),
    translucentSidebar: normalizeTranslucentSidebar(source.translucentSidebar)
  }
}
