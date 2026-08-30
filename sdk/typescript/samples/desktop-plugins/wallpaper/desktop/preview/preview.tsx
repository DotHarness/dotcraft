import { createRoot } from 'react-dom/client'
import type { CSSProperties, JSX } from 'react'
import type { DesktopPluginEnvironmentSnapshot, DesktopPluginHost } from '@dotcraft/plugin'
import { WallpaperLayer } from '../src/WallpaperSurfaces'
import { PRESETS } from '../src/presets'
import { getSettings, initializeSettings, setSettings } from '../src/settings'
import { useResolvedTheme, useSettings } from '../src/hooks'

/**
 * Local harness that mimics the shell elements Core paints from the five surface
 * variables, so the translucency claim can be checked without launching Electron.
 * Not part of the shipped bundle: `dotcraft-plugin build` only reads `src/index.tsx`.
 */
function MockShell(): JSX.Element {
  return (
    <div className="mock-app">
      <div className="mock-titlebar">DotCraft</div>
      <div className="mock-body">
        <aside className="mock-sidebar">
          <div className="mock-project">dotcraft</div>
          <div className="mock-thread mock-thread-active">Desktop plugin samples</div>
          <div className="mock-thread">Release notes</div>
          <div className="mock-thread">Wallpaper research</div>
        </aside>
        <main className="mock-main">
          <div className="mock-message mock-message-user">Replace the background with my own picture.</div>
          <div className="mock-message">
            Sure — the plugin swaps <code>app.background</code> and turns the shell surfaces into glass.
          </div>
          <div className="mock-composer">Ask anything…</div>
        </main>
      </div>
    </div>
  )
}

const themeListeners = new Set<(environment: DesktopPluginEnvironmentSnapshot) => void>()
let previewTheme: 'light' | 'dark' = 'dark'

/** Stands in for the Host's AppServer-backed store: no disk, but the same publish-after-write rule. */
const settingsListeners = new Set<(snapshot: { value: unknown }) => void>()
let stored: Record<string, unknown> = {}

const previewSettings = {
  get: async () => ({ value: stored }),
  mutate: async (_scope: string, operations: readonly { op: string; key: string; value?: unknown }[]) => {
    for (const operation of operations) {
      if (operation.op === 'set') stored = { ...stored, [operation.key]: operation.value }
      else delete stored[operation.key]
    }
    const snapshot = { value: stored }
    for (const listener of settingsListeners) listener(snapshot)
    return snapshot
  },
  onChange: (listener: (snapshot: { value: unknown }) => void) => {
    settingsListeners.add(listener)
    return () => {
      settingsListeners.delete(listener)
    }
  }
}

const previewHost = {
  settings: previewSettings,
  environment: {
    locale: 'en',
    get theme(): 'light' | 'dark' {
      return previewTheme
    },
    onChange(listener: (environment: DesktopPluginEnvironmentSnapshot) => void): () => void {
      themeListeners.add(listener)
      return () => {
        themeListeners.delete(listener)
      }
    }
  }
} as unknown as DesktopPluginHost

function applyTheme(next: 'light' | 'dark'): void {
  previewTheme = next
  document.documentElement.dataset.theme = next
  document.body.classList.toggle('light', next === 'light')
  for (const listener of themeListeners) listener({ locale: 'en', theme: next })
}

function Controls(): JSX.Element {
  const settings = useSettings()
  const theme = useResolvedTheme(previewHost)
  return (
    <div className="controls">
      <button type="button" onClick={() => applyTheme(theme === 'dark' ? 'light' : 'dark')}>
        {theme === 'dark' ? 'Light theme' : 'Dark theme'}
      </button>
      {PRESETS.map((preset) => (
        <button
          key={preset.id}
          type="button"
          onClick={() =>
            setSettings(
              theme === 'dark' ? { dark: { kind: 'preset', id: preset.id } } : { light: { kind: 'preset', id: preset.id } }
            )
          }
        >
          {preset.id}
        </button>
      ))}
      <label>
        surface {settings.surfaceOpacity}%
        <input
          type="range"
          min={30}
          max={100}
          value={settings.surfaceOpacity}
          onChange={(event) => setSettings({ surfaceOpacity: Number(event.target.value) })}
        />
      </label>
      <label>
        blur {settings.blur}
        <input
          type="range"
          min={0}
          max={24}
          value={settings.blur}
          onChange={(event) => setSettings({ blur: Number(event.target.value) })}
        />
      </label>
      <label>
        dim {settings.dim}%
        <input
          type="range"
          min={0}
          max={80}
          value={settings.dim}
          onChange={(event) => setSettings({ dim: Number(event.target.value) })}
        />
      </label>
    </div>
  )
}

const surfaceProps = { host: previewHost, context: { rootElement: document.body } as never }

function App(): JSX.Element {
  const settings = useSettings()
  return (
    <div
      className="preview-frame"
      style={{
        '--preview-shell-surface': `color-mix(in srgb, var(--bg-primary) ${settings.surfaceOpacity}%, transparent)`
      } as CSSProperties}
    >
      <WallpaperLayer {...surfaceProps} />
      <div className="preview-app-seat">
        <MockShell />
      </div>
      <Controls />
    </div>
  )
}

applyTheme('dark')
void initializeSettings(previewHost.settings).then(() => {
  setSettings({ ...getSettings(), enabled: true })
  const root = document.getElementById('root')
  if (root !== null) createRoot(root).render(<App />)
})
