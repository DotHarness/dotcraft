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

let currentMode: ThemeMode = 'light'
let nativePrefersDark: boolean | null = null
let installedNativeApi: unknown = null
let unsubscribeNativeTheme: (() => void) | null = null
let osThemeListenerInstalled = false

function getNativeThemeApi(): {
  initial: boolean
  subscribe: (callback: (prefersDark: boolean) => void) => () => void
} | null {
  if (typeof window === 'undefined') return null
  const api = window.api
  if (!api || typeof api.initialNativePrefersDark !== 'boolean') return null
  const subscribe = api.window?.onNativeThemeUpdated
  if (typeof subscribe !== 'function') return null
  return { initial: api.initialNativePrefersDark, subscribe }
}

function systemPrefersDark(): boolean {
  if (nativePrefersDark !== null) return nativePrefersDark
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : false
}

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

/** Prefers Electron's nativeTheme over matchMedia so CDP media emulation cannot override system mode. */
function ensureOsThemeListener(): void {
  const native = getNativeThemeApi()
  if (native) {
    if (installedNativeApi === window.api) return
    unsubscribeNativeTheme?.()
    installedNativeApi = window.api
    nativePrefersDark = native.initial
    unsubscribeNativeTheme = native.subscribe((prefersDark) => {
      nativePrefersDark = prefersDark
      if (currentMode === 'system') {
        applyResolved(resolveAppliedTheme('system', prefersDark), true)
      }
    })
    return
  }
  if (unsubscribeNativeTheme) {
    unsubscribeNativeTheme()
    unsubscribeNativeTheme = null
    installedNativeApi = null
    nativePrefersDark = null
  }
  if (osThemeListenerInstalled) return
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
  const mql = window.matchMedia('(prefers-color-scheme: dark)')
  const handler = (): void => {
    if (installedNativeApi !== null) return
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
