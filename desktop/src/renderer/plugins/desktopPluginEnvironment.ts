import type { DesktopPluginEnvironmentSnapshot, DesktopPluginThemeSeed } from '@dotcraft/plugin'

import { normalizeLocale } from '../../shared/locales'
import { THEME_CHANGED_EVENT } from '../../shared/theme'
import { DEFAULT_SEEDS, type ThemeVariant } from '../../shared/themeSeed'

type EnvironmentListener = (environment: DesktopPluginEnvironmentSnapshot) => void

const listeners = new Set<EnvironmentListener>()
let stopWatching: (() => void) | null = null
let current = readDesktopPluginEnvironment()

/** Falls back to the authored seed so a plugin never sees a blank color or a NaN contrast. */
function readThemeSeed(variant: ThemeVariant): DesktopPluginThemeSeed {
  const style = getComputedStyle(document.documentElement)
  const defaults = DEFAULT_SEEDS[variant]
  const read = (name: string, fallback: string): string =>
    style.getPropertyValue(name).trim() || fallback
  const contrast = Number.parseInt(style.getPropertyValue('--seed-contrast'), 10)
  return {
    surface: read('--seed-surface', defaults.surface),
    ink: read('--seed-ink', defaults.ink),
    accent: read('--seed-accent', defaults.accent),
    contrast: Number.isFinite(contrast) ? contrast : defaults.contrast
  }
}

/** The return type keeps Desktop's app locales and the SDK's `DesktopPluginLocale` from drifting apart. */
export function readDesktopPluginEnvironment(): DesktopPluginEnvironmentSnapshot {
  const theme: ThemeVariant = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'
  return {
    locale: normalizeLocale(document.documentElement.lang || navigator.language),
    theme,
    themeSeed: readThemeSeed(theme)
  }
}

/** One Host-owned watcher serves every plugin, so no plugin observes the document itself. */
export function onDesktopPluginEnvironmentChange(listener: EnvironmentListener): () => void {
  listeners.add(listener)
  startWatching()
  return () => {
    if (!listeners.delete(listener)) return
    if (listeners.size === 0) {
      stopWatching?.()
      stopWatching = null
    }
  }
}

function startWatching(): void {
  if (stopWatching) return
  current = readDesktopPluginEnvironment()
  const publish = (): void => {
    const next = readDesktopPluginEnvironment()
    if (
      next.locale === current.locale &&
      next.theme === current.theme &&
      next.themeSeed.surface === current.themeSeed.surface &&
      next.themeSeed.ink === current.themeSeed.ink &&
      next.themeSeed.accent === current.themeSeed.accent &&
      next.themeSeed.contrast === current.themeSeed.contrast
    ) {
      return
    }
    current = next
    for (const listener of [...listeners]) {
      try {
        listener(next)
      } catch (error) {
        console.error('Desktop Plugin environment listener failed:', error)
      }
    }
  }

  window.addEventListener(THEME_CHANGED_EVENT, publish)
  // Core announces theme changes but writes the locale straight onto the document.
  const observer = new MutationObserver(publish)
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['lang'] })
  stopWatching = () => {
    window.removeEventListener(THEME_CHANGED_EVENT, publish)
    observer.disconnect()
  }
}
