import { useMemo } from 'react'
import type { DesktopPluginComposerProvider } from '@dotcraft/plugin'
import { useModelCatalogStore } from '../../stores/modelCatalogStore'
import { useProviderSummary } from '../../stores/providersStore'

export function useComposerProvider(): DesktopPluginComposerProvider | null {
  const providerId = useModelCatalogStore((state) => state.providerId)
  const backendId = useProviderSummary(providerId)?.backendId ?? null
  return useMemo(() => (providerId ? { id: providerId, backendId } : null), [providerId, backendId])
}
