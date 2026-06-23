/// <reference types="vite/client" />
import hljsDarkUrl from 'highlight.js/styles/github-dark.css?url'
import hljsLightUrl from 'highlight.js/styles/github.css?url'
import {
  THEME_CHANGED_EVENT,
  resolveAppliedTheme,
  resolveThemeMode,
  type ResolvedTheme,
  type ThemeMode
} from '../../shared/theme'

export type { ThemeMode }

const HLJS_LINK_ID = 'dotcraft-hljs-theme'

/**
 * Normalize a persisted or unknown value to a valid theme preference (may be `system`).
 */
export function resolveTheme(raw: unknown): ThemeMode {
  return resolveThemeMode(raw)
}

function systemPrefersDark(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : false
}

function getHljsHref(applied: ResolvedTheme): string {
  return applied === 'light' ? hljsLightUrl : hljsDarkUrl
}

let currentMode: ThemeMode = 'light'
let osThemeListenerInstalled = false

function applyResolved(applied: ResolvedTheme, syncTitleBarOverlay: boolean): void {
  document.documentElement.setAttribute('data-theme', applied)
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode: applied } }))

  let link = document.getElementById(HLJS_LINK_ID) as HTMLLinkElement | null
  if (!link) {
    link = document.createElement('link')
    link.id = HLJS_LINK_ID
    link.rel = 'stylesheet'
    document.head.appendChild(link)
  }
  link.href = getHljsHref(applied)

  if (
    syncTitleBarOverlay &&
    typeof window !== 'undefined' &&
    window.api?.platform !== 'darwin'
  ) {
    void window.api.window.setTitleBarOverlayTheme(applied)
  }
}

/** Re-apply the resolved theme whenever the OS appearance changes while in `system` mode. */
function ensureOsThemeListener(): void {
  if (osThemeListenerInstalled) return
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
  const mql = window.matchMedia('(prefers-color-scheme: dark)')
  const handler = (): void => {
    if (currentMode === 'system') {
      applyResolved(resolveAppliedTheme('system', mql.matches), true)
    }
  }
  if (typeof mql.addEventListener === 'function') {
    mql.addEventListener('change', handler)
  } else if (typeof mql.addListener === 'function') {
    // Fallback for older MediaQueryList implementations.
    mql.addListener(handler)
  }
  osThemeListenerInstalled = true
}

/**
 * Sets `data-theme` on `<html>` from the given preference (resolving `system` via the OS),
 * swaps the highlight.js stylesheet, syncs the native title-bar overlay, and installs an OS
 * appearance listener so `system` keeps tracking the OS after the first apply.
 */
export function applyTheme(
  mode: ThemeMode,
  options: { syncTitleBarOverlay?: boolean } = {}
): void {
  currentMode = mode
  ensureOsThemeListener()
  applyResolved(resolveAppliedTheme(mode, systemPrefersDark()), options.syncTitleBarOverlay !== false)
}
