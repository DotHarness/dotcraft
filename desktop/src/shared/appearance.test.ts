import { describe, expect, it } from 'vitest'
import { resolveAppliedTheme, resolveThemeMode } from './theme'
import {
  DEFAULT_APPEARANCE,
  DEFAULT_UI_FONT_SIZE,
  UI_FONT_SIZE_MAX,
  UI_FONT_SIZE_MIN,
  normalizeAccentHex,
  normalizeCodeFontSize,
  normalizeInterfaceZoom,
  resolveAppearanceSettings
} from './appearance'

describe('theme mode resolution', () => {
  it('normalizes theme preferences and resolves system to the OS preference', () => {
    expect(resolveThemeMode('system')).toBe('system')
    expect(resolveThemeMode('dark')).toBe('dark')
    expect(resolveThemeMode(undefined)).toBe('light')
    expect(resolveThemeMode('bogus')).toBe('light')
    expect(resolveAppliedTheme('system', true)).toBe('dark')
    expect(resolveAppliedTheme('system', false)).toBe('light')
    expect(resolveAppliedTheme('light', true)).toBe('light')
  })
})

describe('value normalization', () => {
  it('normalizes accent hex values and rejects invalid ones', () => {
    expect(normalizeAccentHex('#4566CC')).toBe('#4566cc')
    expect(normalizeAccentHex('#abc')).toBe('#aabbcc')
    expect(normalizeAccentHex('4566cc')).toBe('#4566cc')
    for (const invalid of ['', '#12', 'not-a-color', 42]) {
      expect(normalizeAccentHex(invalid)).toBeNull()
    }
  })

  it('rounds in-range code font sizes and rejects out-of-range/invalid', () => {
    expect(normalizeCodeFontSize(12.6)).toBe(13)
    expect(normalizeCodeFontSize(2)).toBeNull()
    expect(normalizeCodeFontSize(99)).toBeNull()
    expect(normalizeCodeFontSize('14')).toBeNull()
  })

  it('snaps interface zoom to the UI-font px grid and clamps out-of-range/invalid to 100%', () => {
    expect(normalizeInterfaceZoom(1)).toBe(1)
    expect(normalizeInterfaceZoom(15 / 14)).toBe(15 / 14)
    expect(normalizeInterfaceZoom(1.2)).toBe(17 / 14)
    expect(normalizeInterfaceZoom(0.5)).toBe(UI_FONT_SIZE_MIN / DEFAULT_UI_FONT_SIZE)
    expect(normalizeInterfaceZoom(3)).toBe(UI_FONT_SIZE_MAX / DEFAULT_UI_FONT_SIZE)
    expect(normalizeInterfaceZoom('big')).toBe(1)
  })
})

describe('resolveAppearanceSettings', () => {
  it('returns defaults for empty input and maps a fully-populated settings object', () => {
    expect(resolveAppearanceSettings({})).toEqual(DEFAULT_APPEARANCE)
    expect(resolveAppearanceSettings(null)).toEqual(DEFAULT_APPEARANCE)
    expect(
      resolveAppearanceSettings({
        theme: 'system',
        accent: '#3E8C64',
        codeFontSize: 15,
        diffMarkers: 'sign',
        reduceMotion: 'on',
        pointerCursors: true,
        interfaceZoom: 1.2,
        translucentSidebar: false,
        welcomeBackdrop: false,
        composerGlow: false
      })
    ).toEqual({
      themeMode: 'system',
      accent: '#3e8c64',
      themeSeeds: { dark: {}, light: {} },
      codeFontSize: 15,
      diffMarkers: 'sign',
      reduceMotion: 'on',
      pointerCursors: true,
      interfaceZoom: 17 / 14,
      translucentSidebar: false,
      welcomeBackdrop: false,
      composerGlow: false
    })
  })
})
