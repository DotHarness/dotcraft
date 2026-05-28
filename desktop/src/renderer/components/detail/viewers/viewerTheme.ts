import { useEffect, useState } from 'react'
import type { ITheme } from '@xterm/xterm'
import {
  THEME_CHANGED_EVENT,
  resolveThemeMode,
  type ThemeMode
} from '../../../../shared/theme'

export function getDocumentThemeMode(doc: Document = document): ThemeMode {
  return resolveThemeMode(doc.documentElement.getAttribute('data-theme'))
}

export function getMonacoTheme(mode: ThemeMode): 'vs' | 'vs-dark' {
  return mode === 'light' ? 'vs' : 'vs-dark'
}

export function createTerminalThemeFromDocument(doc: Document = document): ITheme {
  const root = doc.documentElement
  const style = doc.defaultView?.getComputedStyle(root)

  const css = (name: string, fallback: string): string => {
    const value = style?.getPropertyValue(name).trim()
    return value && value.length > 0 ? value : fallback
  }

  return {
    background: css('--bg-primary', '#1e1e1e'),
    foreground: css('--text-primary', '#e5e5e5'),
    cursor: css('--text-primary', '#e5e5e5'),
    cursorAccent: css('--bg-primary', '#1e1e1e'),
    selectionBackground: css('--bg-active', '#3a3a3a'),
    black: css('--ansi-black', '#3b3b3b'),
    red: css('--ansi-red', '#e06c75'),
    green: css('--ansi-green', '#98c379'),
    yellow: css('--ansi-yellow', '#e5c07b'),
    blue: css('--ansi-blue', '#61afef'),
    magenta: css('--ansi-magenta', '#c678dd'),
    cyan: css('--ansi-cyan', '#56b6c2'),
    white: css('--ansi-white', '#dcdfe4'),
    brightBlack: css('--ansi-bright-black', '#6a737d'),
    brightRed: css('--ansi-bright-red', '#ff7b72'),
    brightGreen: css('--ansi-bright-green', '#7ee787'),
    brightYellow: css('--ansi-bright-yellow', '#f2cc60'),
    brightBlue: css('--ansi-bright-blue', '#79c0ff'),
    brightMagenta: css('--ansi-bright-magenta', '#d2a8ff'),
    brightCyan: css('--ansi-bright-cyan', '#a5f3fc'),
    brightWhite: css('--ansi-bright-white', '#f0f6fc')
  }
}

export function useDocumentThemeMode(): ThemeMode {
  const [mode, setMode] = useState(() => getDocumentThemeMode())

  useEffect(() => {
    const sync = (): void => setMode(getDocumentThemeMode())
    window.addEventListener(THEME_CHANGED_EVENT, sync)

    const observer = new MutationObserver(sync)
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme']
    })

    sync()
    return () => {
      window.removeEventListener(THEME_CHANGED_EVENT, sync)
      observer.disconnect()
    }
  }, [])

  return mode
}
