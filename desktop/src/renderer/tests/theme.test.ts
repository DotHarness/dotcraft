// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { applyTheme } from '../utils/theme'
import { installDesktopApiMock } from './desktopApiMock'

function mockMatchMedia(): void {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
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
})
