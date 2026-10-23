import { describe, expect, it } from 'vitest'
import { applyNativeThemeSource } from '../nativeThemeSource'

describe('nativeThemeSource', () => {
  it('writes the settings theme to nativeTheme.themeSource, falling back to light', () => {
    const nativeTheme = { themeSource: 'system' as 'system' | 'light' | 'dark' }

    expect(applyNativeThemeSource(nativeTheme, { theme: 'dark' })).toBe('dark')
    expect(nativeTheme.themeSource).toBe('dark')
    expect(applyNativeThemeSource(nativeTheme, { theme: 'system' })).toBe('system')
    expect(applyNativeThemeSource(nativeTheme, { theme: undefined })).toBe('light')
    expect(applyNativeThemeSource(nativeTheme, { theme: 'unexpected' as never })).toBe('light')
    expect(nativeTheme.themeSource).toBe('light')
  })
})
