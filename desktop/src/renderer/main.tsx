import { BrowserGuestHost } from './browser/BrowserGuestHostMount'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { LocaleProvider } from './contexts/LocaleContext'
import { applyTheme, resolveTheme } from './utils/theme'
import { applyAppearanceDom, applyThemeSeeds } from './utils/appearance'
import { resolveAppearanceSettings } from '../shared/appearance'
import { useUIStore } from './stores/uiStore'
import { startDesktopPluginRuntime } from './plugins/desktopPluginRuntime'
import { DesktopPluginSurface } from './components/desktopPlugins/DesktopPluginSurface'
import { HighlightProvider } from './highlight/react/HighlightProvider'
import { CdpDebugIndicator } from './components/layout/CdpDebugIndicator'
import { ColorPickerDialogHost } from './components/ui/ColorPickerDialog'
import { AddProjectActionDialogHost } from './components/projectActions/AddProjectActionDialog'
import { SetUpProjectDialogHost } from './components/projectActions/SetUpProjectDialog'
import { SelectMenuHost } from './components/ui/SelectMenu'
import './styles/index.css'
import { observeDesktopPetOwnership } from './components/desktopPet/desktopPetOwnership'
import { DesktopPet } from './components/desktopPet/DesktopPet'

const isPetWindow = document.documentElement.hasAttribute('data-desktop-pet-window')
const stopPetOwnership = isPetWindow ? () => {} : observeDesktopPetOwnership()
window.addEventListener('beforeunload', stopPetOwnership, { once: true })

const stopDesktopPluginRuntime = isPetWindow ? () => {} : startDesktopPluginRuntime()
window.addEventListener('beforeunload', stopDesktopPluginRuntime, { once: true })

const params = new URLSearchParams(window.location.search)
const initialTheme = resolveTheme(params.get('theme') ?? window.api?.initialTheme)
// Adopt what preload already painted before applying the theme: applyTheme re-derives the
// seed, and with nothing stored yet that re-derive would clear it until settings load.
const initialAppearance = window.api?.initialAppearance
applyThemeSeeds(initialAppearance?.accent ?? null, initialAppearance?.themeSeeds)
applyTheme(initialTheme, { syncTitleBarOverlay: !isPetWindow })

void window.api?.settings
  ?.get?.()
  .then((settings) => {
    const appearance = resolveAppearanceSettings(settings)
    applyAppearanceDom(appearance)
    useUIStore.getState().setDiffMarkers(appearance.diffMarkers)
  })
  .catch(() => {
  })

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element not found — check index.html for <div id="root">')
}

const appSurfaceContext = { rootElement }

createRoot(rootElement).render(
  <StrictMode>
    <LocaleProvider loadSettings={!isPetWindow}>
      <HighlightProvider>
        {isPetWindow ? <DesktopPet /> : <>
        <div className="dotcraft-plugin-background-seat">
          <DesktopPluginSurface name="app.background" context={appSurfaceContext} />
        </div>
        <div className="dotcraft-plugin-app-seat">
          <BrowserGuestHost />
          <DesktopPluginSurface name="app" context={appSurfaceContext}>
            <App />
          </DesktopPluginSurface>
        </div>
        <div className="dotcraft-plugin-overlay-seat">
          <DesktopPluginSurface name="app.overlay" context={appSurfaceContext} />
        </div>
        <div className="dotcraft-plugin-status-seat">
          <DesktopPluginSurface name="app.status" context={appSurfaceContext} />
          <CdpDebugIndicator enabled={window.api?.initialCdpDebuggingEnabled ?? false} />
        </div>
        <ColorPickerDialogHost />
        <AddProjectActionDialogHost />
        <SetUpProjectDialogHost />
        <SelectMenuHost />
        </>}
      </HighlightProvider>
    </LocaleProvider>
  </StrictMode>
)
