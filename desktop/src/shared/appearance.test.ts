import { describe, expect, it } from 'vitest'
import { resolveAppliedTheme, resolveThemeMode } from './theme'
import {
  DEFAULT_APPEARANCE,
  DEFAULT_UI_FONT_SIZE,
  UI_FONT_SIZE_MAX,
  UI_FONT_SIZE_MIN,
  normalizeAccentHex,
  normalizeCodeFontSize,
  normalizeDiffMarkers,
  normalizeInterfaceZoom,
  normalizePointerCursors,
  normalizeReduceMotion,
  normalizeTranslucentSidebar,
  resolveAppearanceSettings
} from './appearance'

describe('theme mode resolution', () => {
  it('normalizes theme preferences, defaulting unknown to light', () => {
    expect(resolveThemeMode('system')).toBe('system')
    expect(resolveThemeMode('dark')).toBe('dark')
    expect(resolveThemeMode('light')).toBe('light')
    expect(resolveThemeMode(undefined)).toBe('light')
    expect(resolveThemeMode('bogus')).toBe('light')
  })

  it('resolves system to the OS preference and passes through explicit modes', () => {
    expect(resolveAppliedTheme('system', true)).toBe('dark')
    expect(resolveAppliedTheme('system', false)).toBe('light')
    expect(resolveAppliedTheme('dark', false)).toBe('dark')
    expect(resolveAppliedTheme('light', true)).toBe('light')
  })
})

describe('accent normalization', () => {
  it('accepts and lowercases #rrggbb', () => {
    expect(normalizeAccentHex('#4566CC')).toBe('#4566cc')
  })

  it('expands #rgb shorthand and tolerates a missing leading #', () => {
    expect(normalizeAccentHex('#abc')).toBe('#aabbcc')
    expect(normalizeAccentHex('4566cc')).toBe('#4566cc')
  })

  it('rejects invalid or empty values', () => {
    expect(normalizeAccentHex('')).toBeNull()
    expect(normalizeAccentHex('#12')).toBeNull()
    expect(normalizeAccentHex('not-a-color')).toBeNull()
    expect(normalizeAccentHex(42)).toBeNull()
  })
})

describe('code font size normalization', () => {
  it('rounds in-range numbers and rejects out-of-range/invalid', () => {
    expect(normalizeCodeFontSize(13)).toBe(13)
    expect(normalizeCodeFontSize(12.6)).toBe(13)
    expect(normalizeCodeFontSize(2)).toBeNull()
    expect(normalizeCodeFontSize(99)).toBeNull()
    expect(normalizeCodeFontSize('14')).toBeNull()
  })
})

describe('enum normalization', () => {
  it('defaults diff markers to color', () => {
    expect(normalizeDiffMarkers('sign')).toBe('sign')
    expect(normalizeDiffMarkers('color')).toBe('color')
    expect(normalizeDiffMarkers(undefined)).toBe('color')
  })

  it('defaults reduce motion to system', () => {
    expect(normalizeReduceMotion('on')).toBe('on')
    expect(normalizeReduceMotion('off')).toBe('off')
    expect(normalizeReduceMotion('whatever')).toBe('system')
  })

  it('defaults pointer cursors to on, disabled only by explicit false', () => {
    expect(normalizePointerCursors(false)).toBe(false)
    expect(normalizePointerCursors(true)).toBe(true)
    expect(normalizePointerCursors(undefined)).toBe(true)
  })

  it('snaps interface zoom to the UI-font px grid and clamps out-of-range/invalid to 100%', () => {
    expect(normalizeInterfaceZoom(1)).toBe(1) // 14px base
    expect(normalizeInterfaceZoom(15 / 14)).toBe(15 / 14) // 15px round-trips exactly
    expect(normalizeInterfaceZoom(1.2)).toBe(17 / 14) // raw 1.2 snaps to the nearest px (17px)
    expect(normalizeInterfaceZoom(0.5)).toBe(UI_FONT_SIZE_MIN / DEFAULT_UI_FONT_SIZE) // clamps to 11px
    expect(normalizeInterfaceZoom(3)).toBe(UI_FONT_SIZE_MAX / DEFAULT_UI_FONT_SIZE) // clamps to 21px
    expect(normalizeInterfaceZoom('big')).toBe(1)
  })

  it('defaults translucent sidebar to on, disabled only by explicit false', () => {
    expect(normalizeTranslucentSidebar(false)).toBe(false)
    expect(normalizeTranslucentSidebar(undefined)).toBe(true)
  })
})

describe('resolveAppearanceSettings', () => {
  it('returns defaults for empty input', () => {
    expect(resolveAppearanceSettings({})).toEqual(DEFAULT_APPEARANCE)
    expect(resolveAppearanceSettings(null)).toEqual(DEFAULT_APPEARANCE)
  })

  it('maps a fully-populated settings object', () => {
    expect(
      resolveAppearanceSettings({
        theme: 'system',
        accent: '#3E8C64',
        codeFontSize: 15,
        diffMarkers: 'sign',
        reduceMotion: 'on',
        pointerCursors: true,
        interfaceZoom: 1.2,
        translucentSidebar: false
      })
    ).toEqual({
      themeMode: 'system',
      accent: '#3e8c64',
      themeSeeds: { dark: {}, light: {} },
      codeFontSize: 15,
      diffMarkers: 'sign',
      reduceMotion: 'on',
      pointerCursors: true,
      interfaceZoom: 17 / 14, // resolves 1.2 by snapping to 17px
      translucentSidebar: false
    })
  })
})
