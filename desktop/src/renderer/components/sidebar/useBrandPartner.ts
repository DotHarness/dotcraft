import { useEffect } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import { findPluginEntry, usePluginStore } from '../../stores/pluginStore'
import { useProviderSummary } from '../../stores/providersStore'
import type { ProviderMarkKind } from '../ui/ProviderMark'
import { pluginIconUrl, pluginTitle } from '../plugins/PluginCatalogItem'
import { useComposerProvider } from '../conversation/useComposerProvider'

export interface BrandPartner {
  key: string
  name: string
  iconSrc?: string | null
  markKind?: ProviderMarkKind
}

let requestedPlugins = false

export function useBrandPartner(): BrandPartner | null {
  const provider = useProviderSummary(useComposerProvider()?.id)
  const pluginId = provider?.pluginId ?? null
  const plugin = usePluginStore((state) => pluginId ? findPluginEntry(state.plugins, pluginId) ?? null : null)
  const pluginManagement = useConnectionStore((state) => state.capabilities?.pluginManagement === true)

  useEffect(() => {
    if (!pluginId || plugin || requestedPlugins || !pluginManagement) return
    requestedPlugins = true
    void usePluginStore.getState().fetchPlugins()
  }, [plugin, pluginId, pluginManagement])

  if (!provider) return null
  if (pluginId) {
    return {
      key: `plugin:${pluginId}`,
      name: plugin ? pluginTitle(plugin) : provider.displayName,
      iconSrc: plugin ? pluginIconUrl(plugin) : null
    }
  }
  if (provider.authMethod === 'chatgptOAuth') {
    return { key: `provider:${provider.id}`, name: provider.displayName, markKind: 'openai' }
  }
  return null
}
