import {
  CODE_FONT_SIZE_MAX,
  CODE_FONT_SIZE_MIN,
  type AppearanceSettings,
  type ReduceMotionMode
} from '../../shared/appearance'

const ACCENT_VAR = '--accent'
const ACCENT_HOVER_VAR = '--accent-hover'
const CODE_SIZE_VAR = '--text-code-size'

/** Lighten a `#rrggbb` hex toward white by `amt` (0..1) to derive the hover accent. */
function lighten(hex: string, amt: number): string {
  const n = hex.replace('#', '')
  const r = parseInt(n.slice(0, 2), 16)
  const g = parseInt(n.slice(2, 4), 16)
  const b = parseInt(n.slice(4, 6), 16)
  const channel = (v: number): string =>
    Math.round(v + (255 - v) * amt).toString(16).padStart(2, '0')
  return `#${channel(r)}${channel(g)}${channel(b)}`
}

/** Override the accent CSS vars, or clear the override to fall back to the per-theme tokens. */
export function applyAccent(hex: string | null): void {
  const root = document.documentElement
  if (hex) {
    root.style.setProperty(ACCENT_VAR, hex)
    root.style.setProperty(ACCENT_HOVER_VAR, lighten(hex, 0.14))
  } else {
    root.style.removeProperty(ACCENT_VAR)
    root.style.removeProperty(ACCENT_HOVER_VAR)
  }
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
  applyAccent(appearance.accent)
  applyCodeFontSize(appearance.codeFontSize)
  applyReduceMotion(appearance.reduceMotion)
  applyPointerCursors(appearance.pointerCursors)
  applyTranslucentSidebar(appearance.translucentSidebar)
}
