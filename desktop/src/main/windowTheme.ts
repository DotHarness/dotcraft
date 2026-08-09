import type { AppSettings } from './settings'
import {
  TITLE_BAR_OVERLAY_BY_THEME,
  type TitleBarOverlayTheme
} from '../shared/titleBarOverlay'
import { resolveAppliedTheme, resolveThemeMode } from '../shared/theme'
import type { BrowserWindow, BrowserWindowConstructorOptions } from 'electron'

/**
 * Resolve the persisted theme preference to the applied dark/light theme used for native
 * window chrome. `system` is resolved via the caller-supplied OS preference (defaults to
 * light so this module stays free of an electron runtime import).
 */
export function resolveInitialTheme(
  settings: Pick<AppSettings, 'theme'>,
  prefersDark = false
): TitleBarOverlayTheme {
  return resolveAppliedTheme(resolveThemeMode(settings.theme), prefersDark)
}

type WindowBackdropOptions = Pick<
  BrowserWindowConstructorOptions,
  'backgroundColor' | 'backgroundMaterial' | 'transparent' | 'vibrancy' | 'visualEffectState' | 'roundedCorners'
>

const TRANSPARENT_WINDOW_BACKGROUND = '#00000000'
const WINDOW_FALLBACK_BACKGROUND_BY_THEME: Record<TitleBarOverlayTheme, string> = {
  dark: TITLE_BAR_OVERLAY_BY_THEME.dark.color,
  light: TITLE_BAR_OVERLAY_BY_THEME.light.color
}

export function resolveWindowBackdropOptions(
  theme: TitleBarOverlayTheme,
  platform: NodeJS.Platform = process.platform
): WindowBackdropOptions {
  if (platform === 'win32') {
    return {
      backgroundColor: WINDOW_FALLBACK_BACKGROUND_BY_THEME[theme],
      backgroundMaterial: 'acrylic',
      roundedCorners: true,
      transparent: false
    }
  }

  if (platform === 'darwin') {
    return {
      backgroundColor: TRANSPARENT_WINDOW_BACKGROUND,
      roundedCorners: true,
      transparent: true,
      vibrancy: 'sidebar',
      visualEffectState: 'active'
    }
  }

  return {
    backgroundColor: WINDOW_FALLBACK_BACKGROUND_BY_THEME[theme],
    transparent: false
  }
}

export function applyWindowBackdropTheme(
  win: BrowserWindow,
  theme: TitleBarOverlayTheme,
  platform: NodeJS.Platform = process.platform
): void {
  const options = resolveWindowBackdropOptions(theme, platform)
  win.setBackgroundColor(options.backgroundColor ?? WINDOW_FALLBACK_BACKGROUND_BY_THEME[theme])

  if (platform === 'win32') {
    try {
      win.setBackgroundMaterial(options.backgroundMaterial ?? 'none')
    } catch {
      win.setBackgroundMaterial('none')
    }
    return
  }

  if (platform === 'darwin') {
    try {
      win.setVibrancy(
        options.vibrancy === 'appearance-based' ? null : options.vibrancy ?? null,
        { visualEffectState: options.visualEffectState ?? 'active' } as Parameters<BrowserWindow['setVibrancy']>[1]
      )
    } catch {
      win.setVibrancy(null)
    }
  }
}

