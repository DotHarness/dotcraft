import type { CSSProperties, JSX } from 'react'
import { Plug } from 'lucide-react'
import { usePluginStore } from '../../stores/pluginStore'
import { pluginIconUrl } from '../plugins/PluginCatalogItem'

interface PluginUsageIconProps {
  pluginId: string | null
  size: number
  style?: CSSProperties
}

export function PluginUsageIcon({ pluginId, size, style }: PluginUsageIconProps): JSX.Element {
  const src = usePluginStore((state) => {
    if (!pluginId) return null
    const plugin = state.plugins.find((candidate) => candidate.id === pluginId)
    return plugin ? pluginIconUrl(plugin) : null
  })
  return src
    ? <img src={src} alt="" aria-hidden width={size} height={size} style={{ display: 'block', objectFit: 'contain', flexShrink: 0, ...style }} />
    : <Plug size={size} style={style} aria-hidden />
}
