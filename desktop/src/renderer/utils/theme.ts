import { useEffect, useRef, useState } from 'react'
import {
  THEME_CHANGED_EVENT,
  resolveAppliedTheme,
  resolveThemeMode,
  type ResolvedTheme,
  type ThemeChangedDetail,
  type ThemeMode
} from '../../shared/theme'
import { reapplyThemeSeed, themeSeedRevision } from './appearance'

export type { ThemeMode }

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
  // The seed defaults differ per variant, so a stored override has to be re-derived here.
  reapplyThemeSeed()
  const detail: ThemeChangedDetail = { mode: applied, seedRevision: themeSeedRevision() }
  window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail }))

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
 * Also installs an OS appearance listener so `system` keeps tracking after the first
 * apply. Nothing else is needed to recolor code: every highlighted run carries both
 * its light and dark value, and `code-tokens.css` picks from the same attribute.
 */
export function applyTheme(
  mode: ThemeMode,
  options: { syncTitleBarOverlay?: boolean } = {}
): void {
  currentMode = mode
  ensureOsThemeListener()
  applyResolved(resolveAppliedTheme(mode, systemPrefersDark()), options.syncTitleBarOverlay !== false)
}

export function getDocumentThemeMode(doc: Document = document): ThemeMode {
  return resolveThemeMode(doc.documentElement.getAttribute('data-theme'))
}

/**
 * The applied theme, re-read whenever the mode or the seed moves. State is a pair so a
 * recolor that leaves the mode alone still re-renders; returning `prev` keeps React's
 * bail-out for events that changed neither.
 */
export function useDocumentThemeMode(): ThemeMode {
  const [state, setState] = useState(() => ({
    mode: getDocumentThemeMode(),
    seedRevision: themeSeedRevision()
  }))
  // Compared before the update rather than inside it: the observer fires after the event has
  // already synced, and an unconditional setState there would be a state change outside act.
  const applied = useRef(state)

  useEffect(() => {
    const sync = (): void => {
      const next = { mode: getDocumentThemeMode(), seedRevision: themeSeedRevision() }
      if (applied.current.mode === next.mode && applied.current.seedRevision === next.seedRevision) {
        return
      }
      applied.current = next
      setState(next)
    }
    window.addEventListener(THEME_CHANGED_EVENT, sync)
    const observer = new MutationObserver(sync)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    sync()
    return () => {
      window.removeEventListener(THEME_CHANGED_EVENT, sync)
      observer.disconnect()
    }
  }, [])

  return state.mode
}
