import { useConfigStore } from './configStore'
import { useModelCatalogStore } from './modelCatalogStore'
import { useProvidersStore } from './providersStore'

export function resetProviderConnectionScope(): void {
  useConfigStore.getState().reset()
  useModelCatalogStore.getState().reset()
  useProvidersStore.getState().reset()
}
