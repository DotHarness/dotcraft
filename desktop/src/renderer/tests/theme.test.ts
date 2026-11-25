// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { applyTheme } from '../utils/theme'
import { installDesktopApiMock } from './desktopApiMock'

function mockMatchMedia(initialMatches = false): {
  setMatches: (matches: boolean) => void
} {
  let matches = initialMatches
  const listeners = new Set<() => void>()
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      get matches() {
        return matches
      },
      media: query,
      addEventListener: vi.fn((event: string, listener: () => void) => {
        if (event === 'change') listeners.add(listener)
      }),
      removeEventListener: vi.fn((event: string, listener: () => void) => {
        if (event === 'change') listeners.delete(listener)
      }),
      addListener: vi.fn((listener: () => void) => listeners.add(listener)),
      removeListener: vi.fn((listener: () => void) => listeners.delete(listener)),
      dispatchEvent: vi.fn()
    }))
  })
  return {
    setMatches(next: boolean) {
      matches = next
      for (const listener of listeners) listener()
    }
  }
}

describe('theme utilities', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-theme')
    vi.restoreAllMocks()
    mockMatchMedia()
    installDesktopApiMock({
      platform: 'win32',
      window: {
        setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined)
      }
    })
  })

  it('applies the theme and emits a renderer-local change event', () => {
    const listener = vi.fn()
    window.addEventListener(THEME_CHANGED_EVENT, listener)

    applyTheme('light')

    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(window.api.window.setTitleBarOverlayTheme).toHaveBeenCalledWith('light')
    expect(listener).toHaveBeenCalledTimes(1)
    window.removeEventListener(THEME_CHANGED_EVENT, listener)
  })

  it('follows Electron nativeTheme in system mode and ignores matchMedia overrides', () => {
    const media = mockMatchMedia(false)
    let nativeListener: ((prefersDark: boolean) => void) | null = null
    const setTitleBarOverlayTheme = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({
      platform: 'win32',
      initialNativePrefersDark: true,
      window: {
        setTitleBarOverlayTheme,
        onNativeThemeUpdated: vi.fn((cb: (prefersDark: boolean) => void) => {
          nativeListener = cb
          return () => {
            if (nativeListener === cb) nativeListener = null
          }
        })
      }
    })

    applyTheme('system')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(setTitleBarOverlayTheme).toHaveBeenLastCalledWith('dark')

    media.setMatches(false)
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')

    nativeListener?.(false)
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(setTitleBarOverlayTheme).toHaveBeenLastCalledWith('light')

    nativeListener?.(true)
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(setTitleBarOverlayTheme).toHaveBeenLastCalledWith('dark')
  })
})
