import type { NativeTheme } from 'electron'
import type { AppSettings } from './settings'
import { resolveThemeMode, type ThemeMode } from '../shared/theme'

export type NativeThemeSource = ThemeMode

/**
 * Resolve the persisted DotCraft theme preference to Electron's native theme source.
 * Invalid or missing values intentionally preserve the app's historical light default.
 */
export function resolveNativeThemeSource(settings: Pick<AppSettings, 'theme'>): NativeThemeSource {
  return resolveThemeMode(settings.theme)
}

/**
 * Keep Electron native UI surfaces (native menus, tray context menus, dialogs) aligned with
 * DotCraft's Appearance theme preference.
 */
export function applyNativeThemeSource(
  target: Pick<NativeTheme, 'themeSource'>,
  settings: Pick<AppSettings, 'theme'>
): NativeThemeSource {
  const source = resolveNativeThemeSource(settings)
  if (target.themeSource !== source) {
    target.themeSource = source
  }
  return source
}
