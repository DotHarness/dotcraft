import type { McpServerRuntimeStatus } from '@dotcraft/sdk/contracts'
import type { AppServerConnectionSource } from '../../shared/appServerBoundary'
import { useConnectionStore } from './connectionStore'

export function currentMcpManagementScope(): AppServerConnectionSource | null {
  const { status, connectionKey, connectionId } = useConnectionStore.getState()
  return status === 'connected' && connectionKey && connectionId ? { connectionKey, connectionId } : null
}

export function isCurrentMcpManagementScope(scope: AppServerConnectionSource): boolean {
  const current = currentMcpManagementScope()
  return current?.connectionKey === scope.connectionKey && current.connectionId === scope.connectionId
}

export async function listMcpManagementStatuses(scope: AppServerConnectionSource): Promise<McpServerRuntimeStatus[]> {
  const statuses: McpServerRuntimeStatus[] = []
  let cursor: string | null | undefined
  do {
    const page = await window.api.appServer.sendConnectionRequest(scope.connectionKey, 'mcpServerStatus/list', {
      detail: 'toolsAndAuthOnly',
      ...(cursor ? { cursor } : {})
    }, undefined, { connectionId: scope.connectionId })
    statuses.push(...(page.data ?? []))
    cursor = page.nextCursor
  } while (cursor && isCurrentMcpManagementScope(scope))
  return statuses
}
