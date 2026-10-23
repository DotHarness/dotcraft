import { describe, expect, it } from 'vitest'
import { resolveInitialTheme, resolveWindowBackdropOptions } from '../windowTheme'
import { titleBarOverlayForSurface } from '../../shared/titleBarOverlay'
import { DEFAULT_SEEDS } from '../../shared/themeSeed'

describe('resolveInitialTheme', () => {
  it('uses the persisted light or dark theme and defaults anything else to light', () => {
    expect(resolveInitialTheme({ theme: 'dark' })).toBe('dark')
    expect(resolveInitialTheme({ theme: 'light' })).toBe('light')
    expect(resolveInitialTheme({})).toBe('light')
    expect(resolveInitialTheme({ theme: 'system' as never })).toBe('light')
  })
})

describe('resolveWindowBackdropOptions', () => {
  it('uses native acrylic with Windows-owned rounded corners', () => {
    expect(resolveWindowBackdropOptions('dark', 'win32')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface(DEFAULT_SEEDS.dark.surface).color,
      backgroundMaterial: 'acrylic',
      roundedCorners: true,
      transparent: false
    })
  })

  it('uses sidebar vibrancy over a transparent base on macOS', () => {
    expect(resolveWindowBackdropOptions('dark', 'darwin')).toMatchObject({
      backgroundColor: '#00000000',
      roundedCorners: true,
      transparent: true,
      vibrancy: 'sidebar',
      visualEffectState: 'active'
    })
  })

  it('uses theme-colored solid fallbacks on Linux that follow a custom background', () => {
    expect(resolveWindowBackdropOptions('light', 'linux')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface(DEFAULT_SEEDS.light.surface).color,
      transparent: false
    })
    expect(resolveWindowBackdropOptions('dark', 'linux', '#101614')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface('#101614').color,
      transparent: false
    })
  })
})
