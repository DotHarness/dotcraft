import { create } from 'zustand'
import type { AppServerConnectionSource } from '../../shared/appServerBoundary'
import { hasConfigKeyPathChange } from '../utils/configChanged'
import { useConfigStore } from './configStore'
import { useConnectionStore } from './connectionStore'
import { useModelCatalogStore } from './modelCatalogStore'
import { useProvidersStore } from './providersStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import { workspaceResourceKey, type WorkspaceResourceScope } from './workspaceResourceScope'

interface WorkspaceResources {
  scope: WorkspaceResourceScope | null
  needsRefresh: boolean
}

export const useWorkspaceResourcesStore = create<WorkspaceResources>(() => ({ scope: null, needsRefresh: false }))

export function activateWorkspaceResources(scope: WorkspaceResourceScope | null): void {
  const current = useWorkspaceResourcesStore.getState().scope
  if ((current ? workspaceResourceKey(current) : null) === (scope ? workspaceResourceKey(scope) : null)) return
  useConfigStore.getState().activate(scope)
  useModelCatalogStore.getState().activate(scope)
  useProvidersStore.getState().activate(scope)
  useWorkspaceResourcesStore.setState({ scope, needsRefresh: scope != null })
}

export async function refreshWorkspaceResources(): Promise<void> {
  const { scope, needsRefresh } = useWorkspaceResourcesStore.getState()
  if (!scope || !needsRefresh) return
  useWorkspaceResourcesStore.setState({ needsRefresh: false })
  const capabilities = useConnectionStore.getState().capabilities
  const models = useModelCatalogStore.getState()
  const cachedProviders = new Set(
    [...models.catalogs].filter(([, catalog]) => catalog.status === 'ready')
      .map(([providerId, catalog]) => providerId ?? catalog.providerId?.trim().toLowerCase() ?? null)
  )
  const requests: Promise<unknown>[] = []
  if (capabilities?.providerManagement === true) requests.push(useProvidersStore.getState().reload())
  if (capabilities?.workspaceConfigManagement === true) requests.push(useConfigStore.getState().refresh())
  if (capabilities?.modelCatalogManagement === true) {
    for (const providerId of cachedProviders) requests.push(models.revalidate(providerId))
  }
  await Promise.all(requests)
}

export function applyWorkspaceResourceConfigChange(
  regions: string[],
  source: Partial<AppServerConnectionSource>
): boolean {
  const scope = useWorkspaceResourcesStore.getState().scope
  if (!scope || (source.connectionKey && source.connectionKey !== scope.connectionKey) ||
      (source.connectionId && source.connectionId !== scope.connectionId)) return false
  const capabilities = useConnectionStore.getState().capabilities
  useConfigStore.getState().handleConfigChanged(regions)
  if (capabilities?.modelCatalogManagement === true) useModelCatalogStore.getState().handleConfigChanged(regions)
  if (capabilities?.providerManagement === true &&
      (regions.includes('providers') || hasConfigKeyPathChange(regions, 'ProviderId'))) {
    void useProvidersStore.getState().reload()
  }
  return true
}

export function pruneWorkspaceResources(): void {
  const projects = useWorkspaceProjectsStore.getState()
  if (!projects.foregroundProjectId && !projects.foregroundWorkspacePath) return
  const live = new Set<string>()
  const scope = useWorkspaceResourcesStore.getState().scope
  if (scope) live.add(workspaceResourceKey(scope))
  for (const project of [...projects.projects, ...(projects.chat ? [projects.chat] : [])]) {
    if (project.loaded && project.connectionKey && project.connectionId) {
      live.add(workspaceResourceKey({
        projectKey: project.projectId || project.path,
        connectionKey: project.connectionKey,
        connectionId: project.connectionId
      }))
    }
  }
  useConfigStore.getState().prune(live)
  useModelCatalogStore.getState().prune(live)
  useProvidersStore.getState().prune(live)
}
