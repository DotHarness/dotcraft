import { useConnectionStore } from '../stores/connectionStore'
import { activateWorkspaceResources } from '../stores/providerConnectionScope'
import { useConfigStore } from '../stores/configStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import type { WorkspaceResourceScope } from '../stores/workspaceResourceScope'

export const testWorkspaceScope: WorkspaceResourceScope = {
  projectKey: '/workspace', connectionKey: '/workspace', connectionId: 'workspace-1'
}

export function resetTestWorkspaceResources(): void {
  activateWorkspaceResources(null)
  useConfigStore.getState().reset()
  useModelCatalogStore.getState().reset()
  useProvidersStore.getState().reset()
}

export function activateTestWorkspace(scope = testWorkspaceScope): void {
  resetTestWorkspaceResources()
  useConnectionStore.setState({ connectionKey: scope.connectionKey, connectionId: scope.connectionId })
  activateWorkspaceResources(scope)
}
