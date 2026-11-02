import { useEffect } from 'react'
import { useConnectionStore } from '../stores/connectionStore'
import { selectModelCatalog, useModelCatalogStore, type ModelCatalogState } from '../stores/modelCatalogStore'

export function useModelCatalog(providerId?: string | null, enabled = true): ModelCatalogState {
  const key = providerId?.trim().toLowerCase() || null
  const available = useConnectionStore((state) =>
    state.status === 'connected' && state.capabilities?.modelCatalogManagement === true)
  const revision = useModelCatalogStore((state) => state.revision)
  const catalog = useModelCatalogStore((state) => selectModelCatalog(state, key))
  useEffect(() => {
    if (available && enabled) void useModelCatalogStore.getState().loadIfNeeded(false, key)
  }, [available, enabled, key, revision])
  return catalog
}
