import { describe, expect, it } from 'vitest'
import { resolveInitialTheme, resolveWindowBackdropOptions } from '../windowTheme'
import { titleBarOverlayForSurface } from '../../shared/titleBarOverlay'
import { DEFAULT_SEEDS } from '../../shared/themeSeed'

describe('resolveInitialTheme', () => {
  it('uses persisted light theme for the first frame', () => {
    expect(resolveInitialTheme({ theme: 'light' })).toBe('light')
  })

  it('uses persisted dark theme for the first frame', () => {
    expect(resolveInitialTheme({ theme: 'dark' })).toBe('dark')
  })

  it('defaults missing or unknown values to light', () => {
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

  it('uses theme-colored solid fallbacks on Linux', () => {
    expect(resolveWindowBackdropOptions('dark', 'linux')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface(DEFAULT_SEEDS.dark.surface).color,
      transparent: false
    })
    expect(resolveWindowBackdropOptions('light', 'linux')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface(DEFAULT_SEEDS.light.surface).color,
      transparent: false
    })
  })

  it('follows a custom background so the pre-paint flash matches the theme', () => {
    expect(resolveWindowBackdropOptions('dark', 'linux', '#101614')).toMatchObject({
      backgroundColor: titleBarOverlayForSurface('#101614').color
    })
    expect(resolveWindowBackdropOptions('dark', 'linux', '#101614').backgroundColor)
      .not.toBe(resolveWindowBackdropOptions('dark', 'linux').backgroundColor)
  })
})

