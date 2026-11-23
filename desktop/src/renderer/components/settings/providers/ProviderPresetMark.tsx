import type { JSX } from 'react'
import { Plug } from 'lucide-react'
import { findPluginEntry, usePluginStore } from '../../../stores/pluginStore'
import { pluginIconUrl } from '../../plugins/PluginCatalogItem'
import { ProviderMark, type ProviderMarkKind } from '../../ui/ProviderMark'

interface ProviderPresetMarkProps {
  mark: ProviderMarkKind | null
  iconUrl?: string | null
  pluginId?: string | null
  size?: number
}

export function ProviderPresetMark({
  mark,
  iconUrl,
  pluginId,
  size = 16
}: ProviderPresetMarkProps): JSX.Element {
  const pluginSrc = usePluginStore((state) => {
    if (mark || iconUrl || !pluginId) return null
    const plugin = findPluginEntry(state.plugins, pluginId)
    return plugin ? pluginIconUrl(plugin) : null
  })
  if (mark) return <ProviderMark kind={mark} size={size} />
  const src = iconUrl || pluginSrc
  if (src) {
    return (
      <img
        src={src}
        alt=""
        aria-hidden="true"
        width={size}
        height={size}
        style={{ display: 'block', objectFit: 'contain', flexShrink: 0 }}
      />
    )
  }
  return <Plug size={size} strokeWidth={1.8} aria-hidden="true" />
}
