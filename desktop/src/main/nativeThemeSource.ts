import type { NativeTheme } from 'electron'
import type { AppSettings } from './settings'
import { resolveThemeMode } from '../shared/theme'

/** Covers the surfaces Electron paints itself: native menus, tray context menus, dialogs. */
export function applyNativeThemeSource(
  target: Pick<NativeTheme, 'themeSource'>,
  settings: Pick<AppSettings, 'theme'>
): void {
  target.themeSource = resolveThemeMode(settings.theme)
}
