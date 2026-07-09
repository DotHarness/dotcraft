import { describe, expect, it } from 'vitest'
import { applyNativeThemeSource, resolveNativeThemeSource } from '../nativeThemeSource'

describe('nativeThemeSource', () => {
  it('normalizes settings theme values to Electron native theme sources', () => {
    expect(resolveNativeThemeSource({ theme: 'system' })).toBe('system')
    expect(resolveNativeThemeSource({ theme: 'light' })).toBe('light')
    expect(resolveNativeThemeSource({ theme: 'dark' })).toBe('dark')
    expect(resolveNativeThemeSource({ theme: undefined })).toBe('light')
    expect(resolveNativeThemeSource({ theme: 'unexpected' as never })).toBe('light')
  })

  it('writes the resolved source to nativeTheme.themeSource', () => {
    const nativeTheme = { themeSource: 'system' as 'system' | 'light' | 'dark' }

    expect(applyNativeThemeSource(nativeTheme, { theme: 'dark' })).toBe('dark')
    expect(nativeTheme.themeSource).toBe('dark')

    expect(applyNativeThemeSource(nativeTheme, { theme: 'system' })).toBe('system')
    expect(nativeTheme.themeSource).toBe('system')
  })
})
