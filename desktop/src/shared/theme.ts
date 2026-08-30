/** User-selectable theme preference. `system` follows the OS appearance. */
export type ThemeMode = 'system' | 'dark' | 'light'

/** The theme actually applied to the document (`system` resolved to one of these). */
export type ResolvedTheme = 'dark' | 'light'

/** Default theme preference when none is persisted. Preserves historical light-first behavior. */
export const DEFAULT_THEME_MODE: ThemeMode = 'light'

/** Applied-theme default (used where a concrete dark/light value is required up front). */
export const DEFAULT_THEME: ResolvedTheme = 'light'

export const THEME_CHANGED_EVENT = 'dotcraft:theme-changed'

/**
 * Detail of {@link THEME_CHANGED_EVENT}. `seedRevision` moves whenever the palette is
 * rewritten, which the mode alone cannot express: recoloring without switching theme
 * leaves `mode` identical, and consumers that cache computed tokens would never re-read.
 */
export interface ThemeChangedDetail {
  readonly mode: ResolvedTheme
  readonly seedRevision: number
}

export function resolveThemeMode(raw: unknown): ThemeMode {
  return raw === 'dark'
    ? 'dark'
    : raw === 'light'
      ? 'light'
      : raw === 'system'
        ? 'system'
        : DEFAULT_THEME_MODE
}

/** `system` resolves using the supplied OS dark-mode preference; explicit modes pass through. */
export function resolveAppliedTheme(mode: ThemeMode, systemPrefersDark: boolean): ResolvedTheme {
  if (mode === 'dark') return 'dark'
  if (mode === 'light') return 'light'
  return systemPrefersDark ? 'dark' : 'light'
}
