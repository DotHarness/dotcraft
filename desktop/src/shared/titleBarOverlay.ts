/**
 * Single source of truth for Electron titleBarOverlay (Windows / Linux) and
 * renderer layout that must align (CustomMenuBar reserve, toast offset).
 * Colors are native titlebar/caption fallbacks; renderer chrome uses translucent shell tokens.
 */

export const TITLE_BAR_OVERLAY_HEIGHT = 36

/** Horizontal space reserved in CustomMenuBar so menu labels do not overlap caption buttons. */
export const TITLE_BAR_OVERLAY_RIGHT_RESERVE = 138

export type TitleBarOverlayTheme = 'dark' | 'light'

export const TITLE_BAR_OVERLAY_BY_THEME: Record<
  TitleBarOverlayTheme,
  { color: string; symbolColor: string }
> = {
  dark: { color: '#202020', symbolColor: '#eeeeec' },
  light: { color: '#f3f3ee', symbolColor: '#1a1c1f' }
}
