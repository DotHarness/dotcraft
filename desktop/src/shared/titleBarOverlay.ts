/**
 * Single source of truth for Electron titleBarOverlay (Windows / Linux) and
 * renderer layout that must align (CustomMenuBar reserve, toast offset).
 * Colors are native titlebar/caption fallbacks; renderer chrome uses translucent shell tokens.
 */

import { inkForSurface, mixHex } from './themeDerive'
import { DEFAULT_SEEDS, type ThemeVariant } from './themeSeed'

export const TITLE_BAR_OVERLAY_HEIGHT = 36

/** Horizontal space reserved in CustomMenuBar so menu labels do not overlap caption buttons. */
export const TITLE_BAR_OVERLAY_RIGHT_RESERVE = 138

export type TitleBarOverlayTheme = ThemeVariant

/** Matches `--shell-chrome-tone`: the caption bar sits on the same tone as the window chrome. */
const CHROME_MIX = 0.052

export interface TitleBarOverlayColors {
  color: string
  symbolColor: string
}

/** Both variants derive the same way, so a custom background recolors the native chrome too. */
export function titleBarOverlayForSurface(surface: string): TitleBarOverlayColors {
  const ink = inkForSurface(surface)
  return { color: mixHex(surface, ink, CHROME_MIX), symbolColor: ink }
}

export const TITLE_BAR_OVERLAY_BY_THEME: Record<TitleBarOverlayTheme, TitleBarOverlayColors> = {
  dark: titleBarOverlayForSurface(DEFAULT_SEEDS.dark.surface),
  light: titleBarOverlayForSurface(DEFAULT_SEEDS.light.surface)
}
