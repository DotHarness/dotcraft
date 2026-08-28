import {
  THEME_CHANGED_EVENT,
  resolveAppliedTheme,
  resolveThemeMode,
  type ResolvedTheme,
  type ThemeMode
} from '../../shared/theme'

export type { ThemeMode }

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

let currentMode: ThemeMode = 'light'
let osThemeListenerInstalled = false

function applyResolved(applied: ResolvedTheme, syncTitleBarOverlay: boolean): void {
  document.documentElement.setAttribute('data-theme', applied)
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode: applied } }))

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
 * syncs the native title-bar overlay, and installs an OS appearance listener so `system`
 * keeps tracking the OS after the first apply.
 *
 * Nothing else has to happen for code to recolor: every highlighted run carries
 * both its light and dark value, and `code-tokens.css` picks between them from
 * the same `data-theme` attribute. There is no stylesheet to swap and nothing
 * to re-tokenize.
 */
export function applyTheme(
  mode: ThemeMode,
  options: { syncTitleBarOverlay?: boolean } = {}
): void {
  currentMode = mode
  ensureOsThemeListener()
  applyResolved(resolveAppliedTheme(mode, systemPrefersDark()), options.syncTitleBarOverlay !== false)
}
