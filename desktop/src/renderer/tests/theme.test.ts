// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { applyTheme, resolveTheme } from '../utils/theme'
import { installDesktopApiMock } from './desktopApiMock'

function mockMatchMedia(prefersDark: boolean): void {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: prefersDark,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn()
    }))
  })
}

describe('theme utilities', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-theme')
    document.getElementById('dotcraft-hljs-theme')?.remove()
    vi.restoreAllMocks()
    mockMatchMedia(false)
    installDesktopApiMock({
      platform: 'win32',
      window: {
        setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined)
      }
    })
  })

  it('resolves missing or unknown values to light', () => {
    expect(resolveTheme(undefined)).toBe('light')
    expect(resolveTheme(null)).toBe('light')
    expect(resolveTheme('bogus')).toBe('light')
  })

  it('preserves explicit system, dark, and light preferences', () => {
    expect(resolveTheme('system')).toBe('system')
    expect(resolveTheme('dark')).toBe('dark')
    expect(resolveTheme('light')).toBe('light')
  })

  it('applies the theme and emits a renderer-local change event', () => {
    const listener = vi.fn()
    window.addEventListener(THEME_CHANGED_EVENT, listener)

    applyTheme('light')

    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(document.getElementById('dotcraft-hljs-theme')).toBeInstanceOf(HTMLLinkElement)
    expect(window.api.window.setTitleBarOverlayTheme).toHaveBeenCalledWith('light')
    expect(listener).toHaveBeenCalledTimes(1)
    window.removeEventListener(THEME_CHANGED_EVENT, listener)
  })

  it('resolves system mode to the OS preference', () => {
    mockMatchMedia(true)
    applyTheme('system')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(window.api.window.setTitleBarOverlayTheme).toHaveBeenCalledWith('dark')

    mockMatchMedia(false)
    applyTheme('system')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(window.api.window.setTitleBarOverlayTheme).toHaveBeenLastCalledWith('light')
  })
})
