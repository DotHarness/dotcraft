import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { CSSProperties, JSX } from 'react'
import type { ThemeBackdrop } from '../../../shared/themes'
import { useChoices } from './choices'
import { useAppearance, useSettings } from './hooks'
import { SceneFrame } from './SceneFrame'
import { activeTheme, themeKey, useThemeLibrary } from './themeLibrary'

export function backdropStyle(backdrop: ThemeBackdrop): CSSProperties {
  if (backdrop.kind === 'color') return { backgroundColor: backdrop.color }
  if (backdrop.kind === 'gradient') {
    const stops = backdrop.stops.join(', ')
    return { backgroundImage: backdrop.shape === 'radial' ? `radial-gradient(ellipse at center, ${stops})` : `linear-gradient(${backdrop.angle}deg, ${stops})` }
  }
  if (backdrop.kind === 'image') {
    const tiled = backdrop.fit === 'tile'
    return {
      backgroundImage: `url("${backdrop.image}")`,
      backgroundSize: tiled ? '360px auto' : backdrop.fit,
      backgroundRepeat: tiled ? 'repeat' : 'no-repeat'
    }
  }
  return {}
}

export function ThemeLayer({ host }: DesktopPluginSurfaceProps<'app.background'>): JSX.Element | null {
  const settings = useSettings()
  const appearance = useAppearance(host)
  useChoices()
  useThemeLibrary()
  const theme = activeTheme(appearance)
  const backdrop = theme?.backdrop
  if (!settings || !theme || !backdrop) return null
  return (
    <div className="dcw-layer" aria-hidden="true" inert>
      <div className="dcw-image" style={backdropStyle(backdrop)}>
        {backdrop.kind === 'scene' && <SceneFrame key={themeKey(theme)} theme={theme} scene={backdrop} animate={settings.animate} />}
      </div>
    </div>
  )
}
