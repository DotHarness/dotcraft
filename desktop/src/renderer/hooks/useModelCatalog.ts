import { useEffect } from 'react'
import { useConnectionStore } from '../stores/connectionStore'
import { selectModelCatalog, useModelCatalogStore, type ModelCatalogState } from '../stores/modelCatalogStore'

export function useModelCatalog(providerId?: string | null, enabled = true): ModelCatalogState {
  const key = providerId?.trim().toLowerCase() || null
  const scope = useModelCatalogStore((state) => state.scope)
  const available = useConnectionStore((state) =>
    state.status === 'connected' && state.capabilities?.modelCatalogManagement === true && scope != null &&
    state.connectionKey === scope.connectionKey && state.connectionId === scope.connectionId)
  const revision = useModelCatalogStore((state) => state.revision)
  const scopeKey = useModelCatalogStore((state) => state.scopeKey)
  const catalog = useModelCatalogStore((state) => selectModelCatalog(state, key))
  useEffect(() => {
    const current = useModelCatalogStore.getState()
    const connection = useConnectionStore.getState()
    if (available && enabled && scopeKey && current.scopeKey === scopeKey &&
        connection.status === 'connected' && connection.connectionKey === scope?.connectionKey &&
        connection.connectionId === scope?.connectionId) void current.loadIfNeeded(false, key)
  }, [available, enabled, key, revision, scopeKey, scope])
  return catalog
}
