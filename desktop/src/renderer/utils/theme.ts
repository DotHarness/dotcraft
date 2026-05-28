/// <reference types="vite/client" />
import hljsDarkUrl from 'highlight.js/styles/github-dark.css?url'
import hljsLightUrl from 'highlight.js/styles/github.css?url'
import {
  THEME_CHANGED_EVENT,
  resolveThemeMode,
  type ThemeMode
} from '../../shared/theme'

export type { ThemeMode }

const HLJS_LINK_ID = 'dotcraft-hljs-theme'

/**
 * Normalize persisted or unknown theme values to a valid mode.
 */
export function resolveTheme(raw: unknown): ThemeMode {
  return resolveThemeMode(raw)
}

function getHljsHref(mode: ThemeMode): string {
  return mode === 'light' ? hljsLightUrl : hljsDarkUrl
}

/**
 * Sets `data-theme` on `<html>` and swaps the single highlight.js stylesheet.
 */
export function applyTheme(
  mode: ThemeMode,
  options: { syncTitleBarOverlay?: boolean } = {}
): void {
  document.documentElement.setAttribute('data-theme', mode)
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode } }))

  let link = document.getElementById(HLJS_LINK_ID) as HTMLLinkElement | null
  if (!link) {
    link = document.createElement('link')
    link.id = HLJS_LINK_ID
    link.rel = 'stylesheet'
    document.head.appendChild(link)
  }
  link.href = getHljsHref(mode)

  if (
    options.syncTitleBarOverlay !== false &&
    typeof window !== 'undefined' &&
    window.api?.platform !== 'darwin'
  ) {
    void window.api.window.setTitleBarOverlayTheme(mode)
  }
}
