export type ThemeMode = 'dark' | 'light'

export const DEFAULT_THEME: ThemeMode = 'light'
export const THEME_CHANGED_EVENT = 'dotcraft:theme-changed'

export function resolveThemeMode(raw: unknown): ThemeMode {
  return raw === 'dark' ? 'dark' : raw === 'light' ? 'light' : DEFAULT_THEME
}
