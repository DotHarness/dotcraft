import type { AppSettings } from './settings'
import {
  TITLE_BAR_OVERLAY_HEIGHT,
  titleBarOverlayForSurface,
  type TitleBarOverlayTheme
} from '../shared/titleBarOverlay'
import { DEFAULT_SEEDS } from '../shared/themeSeed'
import { resolveAppliedTheme, resolveThemeMode } from '../shared/theme'
import type { BrowserWindow, BrowserWindowConstructorOptions } from 'electron'

/**
 * `system` resolves through a caller-supplied OS preference, defaulting to light, so this
 * module stays free of an electron runtime import.
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

/** The background a variant was seeded with, so the pre-paint flash matches the custom theme. */
export function resolveThemeSurface(
  settings: Pick<AppSettings, 'themeSeeds'>,
  theme: TitleBarOverlayTheme
): string {
  return settings.themeSeeds?.[theme]?.surface ?? DEFAULT_SEEDS[theme].surface
}

export function resolveWindowBackdropOptions(
  theme: TitleBarOverlayTheme,
  platform: NodeJS.Platform = process.platform,
  surface: string = DEFAULT_SEEDS[theme].surface
): WindowBackdropOptions {
  const chrome = titleBarOverlayForSurface(surface).color
  if (platform === 'win32') {
    return {
      backgroundColor: chrome,
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
    backgroundColor: chrome,
    transparent: false
  }
}

/**
 * Recolor everything native: the window backdrop and, off macOS, the caption bar. Both follow
 * the seeded surface, so changing the background in Appearance repaints the frame too.
 */
export function applyNativeChromeTheme(
  win: BrowserWindow,
  theme: TitleBarOverlayTheme,
  surface: string,
  platform: NodeJS.Platform = process.platform
): void {
  applyWindowBackdropTheme(win, theme, platform, surface)
  if (platform === 'darwin') return
  const { color, symbolColor } = titleBarOverlayForSurface(surface)
  try {
    win.setTitleBarOverlay({ color, symbolColor, height: TITLE_BAR_OVERLAY_HEIGHT })
  } catch (error) {
    // A window built without an overlay simply has no caption bar to recolor.
    if (error instanceof Error && error.message.includes('Titlebar overlay is not enabled')) return
    throw error
  }
}

export function applyWindowBackdropTheme(
  win: BrowserWindow,
  theme: TitleBarOverlayTheme,
  platform: NodeJS.Platform = process.platform,
  surface: string = DEFAULT_SEEDS[theme].surface
): void {
  const options = resolveWindowBackdropOptions(theme, platform, surface)
  win.setBackgroundColor(options.backgroundColor ?? titleBarOverlayForSurface(surface).color)

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

