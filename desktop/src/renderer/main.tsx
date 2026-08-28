import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { LocaleProvider } from './contexts/LocaleContext'
import { applyTheme, resolveTheme } from './utils/theme'
import { applyAppearanceDom } from './utils/appearance'
import { resolveAppearanceSettings } from '../shared/appearance'
import { useUIStore } from './stores/uiStore'
import { installAutomationBridge } from './e2e/automationBridge'
import { startDesktopPluginRuntime } from './plugins/desktopPluginRuntime'
import { DesktopPluginSurface } from './components/desktopPlugins/DesktopPluginSurface'
import './styles/index.css'

installAutomationBridge()
const stopDesktopPluginRuntime = startDesktopPluginRuntime()
window.addEventListener('beforeunload', stopDesktopPluginRuntime, { once: true })

const params = new URLSearchParams(window.location.search)
const initialTheme = resolveTheme(params.get('theme') ?? window.api?.initialTheme)
applyTheme(initialTheme)

// Apply the remaining (non-theme) appearance preferences once settings load. Defaults are
// no-ops, so only users who customized see a one-tick adjustment after first paint.
void window.api?.settings
  ?.get?.()
  .then((settings) => {
    const appearance = resolveAppearanceSettings(settings)
    applyAppearanceDom(appearance)
    useUIStore.getState().setDiffMarkers(appearance.diffMarkers)
  })
  .catch(() => {
    // Non-fatal: fall back to token defaults.
  })

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element not found — check index.html for <div id="root">')
}

const appSurfaceContext = { rootElement }

createRoot(rootElement).render(
  <StrictMode>
    <LocaleProvider>
      <DesktopPluginSurface name="app.background" context={appSurfaceContext} />
      <DesktopPluginSurface name="app" context={appSurfaceContext}>
        <App />
      </DesktopPluginSurface>
    </LocaleProvider>
  </StrictMode>
)
