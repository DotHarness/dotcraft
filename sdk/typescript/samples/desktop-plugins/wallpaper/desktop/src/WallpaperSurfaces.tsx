import type { DesktopPluginHost, DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { useImageUrl, useImagesRevision, useResolvedTheme, useSettings } from './hooks'
import { presetById } from './presets'
import { choiceFor, type WallpaperSettings } from './settings'

function useActiveUrl(host: DesktopPluginHost, settings: WallpaperSettings | null): string | null {
  const theme = useResolvedTheme(host)
  const revision = useImagesRevision()
  const choice = settings ? choiceFor(settings, theme) : null
  const storedUrl = useImageUrl(choice?.kind === 'image' ? choice.id : null, revision)
  if (!settings || choice === null || !settings.enabled || choice.kind === 'none') return null
  if (choice.kind === 'preset') return presetById(choice.id)?.url ?? null
  return storedUrl
}

export function WallpaperLayer({ host }: DesktopPluginSurfaceProps<'app.background'>): JSX.Element | null {
  const settings = useSettings()
  const url = useActiveUrl(host, settings)
  if (settings === null || url === null) return null

  const tiled = settings.fit === 'tile'
  return (
    <div className="dcw-layer" aria-hidden="true">
      <div
        className="dcw-image"
        style={{
          backgroundImage: `url("${url}")`,
          backgroundSize: tiled ? '360px auto' : settings.fit,
          backgroundRepeat: tiled ? 'repeat' : 'no-repeat',
          // Scaled past the edges so a blur radius cannot reveal the viewport border.
          filter: settings.blur > 0 ? `blur(${settings.blur}px)` : undefined,
          transform: settings.blur > 0 ? `scale(${1 + settings.blur / 120})` : undefined
        }}
      />
      <div className="dcw-dim" style={{ opacity: settings.dim / 100 }} />
    </div>
  )
}
