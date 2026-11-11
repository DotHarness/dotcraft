import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import type { AppServerConnectionSource } from '../../shared/appServerBoundary'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

export interface WorkspaceResourceScope extends AppServerConnectionSource {
  projectKey: string
}

export function workspaceResourceKey(scope: WorkspaceResourceScope): string {
  return JSON.stringify([normalizeWorkspaceProjectKey(scope.projectKey), scope.connectionKey, scope.connectionId])
}

export function requestWorkspaceResource<M extends keyof ClientRequestMethods>(
  scope: WorkspaceResourceScope,
  method: M,
  params: ClientRequestMethods[M]['params'],
  timeoutMs?: number
): Promise<ClientRequestMethods[M]['result']> {
  return window.api.appServer.sendConnectionRequest(scope.connectionKey, method, params, timeoutMs, {
    connectionId: scope.connectionId
  })
}
