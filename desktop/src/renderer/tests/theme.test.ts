// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { applyTheme, resolveTheme } from '../utils/theme'

describe('theme utilities', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-theme')
    document.getElementById('dotcraft-hljs-theme')?.remove()
    vi.restoreAllMocks()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'win32',
        window: {
          setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined)
        }
      }
    })
  })

  it('resolves missing or unknown values to light', () => {
    expect(resolveTheme(undefined)).toBe('light')
    expect(resolveTheme(null)).toBe('light')
    expect(resolveTheme('system')).toBe('light')
  })

  it('preserves explicit dark and light values', () => {
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
})
