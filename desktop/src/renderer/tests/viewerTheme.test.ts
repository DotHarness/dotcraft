// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import {
  createTerminalThemeFromDocument,
  getDocumentThemeMode,
  getMonacoTheme
} from '../components/detail/viewers/viewerTheme'

describe('viewer theme helpers', () => {
  beforeEach(() => {
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('style')
  })

  it('defaults viewer mode to light when data-theme is missing or unknown', () => {
    expect(getDocumentThemeMode()).toBe('light')
    document.documentElement.setAttribute('data-theme', 'system')
    expect(getDocumentThemeMode()).toBe('light')
  })

  it('selects Monaco themes for light and dark modes', () => {
    expect(getMonacoTheme('light')).toBe('vs')
    expect(getMonacoTheme('dark')).toBe('vs-dark')
  })

  it('builds terminal colors from CSS variables', () => {
    document.documentElement.style.setProperty('--bg-primary', '#f7f8fa')
    document.documentElement.style.setProperty('--text-primary', '#1e3347')
    document.documentElement.style.setProperty('--ansi-red', '#dc2626')

    const theme = createTerminalThemeFromDocument()

    expect(theme.background).toBe('#f7f8fa')
    expect(theme.foreground).toBe('#1e3347')
    expect(theme.cursor).toBe('#1e3347')
    expect(theme.red).toBe('#dc2626')
  })
})
