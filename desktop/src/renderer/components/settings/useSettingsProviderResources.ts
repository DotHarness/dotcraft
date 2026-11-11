import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { useConnectionStore } from '../../stores/connectionStore'
import { useProvidersStore } from '../../stores/providersStore'
import { useWorkspaceResourcesStore } from '../../stores/providerConnectionScope'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from '../../stores/workspaceResourceScope'
import { normalizeProviderList } from './providerInfo'

export function isSettingsResourceScopeCurrent(scope: WorkspaceResourceScope): boolean {
  const current = useWorkspaceResourcesStore.getState().scope
  const connection = useConnectionStore.getState()
  return current != null && workspaceResourceKey(current) === workspaceResourceKey(scope) &&
    connection.status === 'connected' && connection.connectionKey === scope.connectionKey &&
    connection.connectionId === scope.connectionId
}

export function useSettingsResourceScope(enabled: boolean): WorkspaceResourceScope | null {
  const scope = useWorkspaceResourcesStore((state) => state.scope)
  const connected = useConnectionStore((state) => scope != null && state.status === 'connected' &&
    state.connectionKey === scope.connectionKey && state.connectionId === scope.connectionId)
  return enabled && connected ? scope : null
}

export function useSettingsProviderResources(enabled: boolean) {
  const scope = useSettingsResourceScope(enabled)
  const listResult = useProvidersStore((state) => state.listResult)
  const status = useProvidersStore((state) => state.status)
  const error = useProvidersStore((state) => state.error)
  const [backendSnapshot, setBackendSnapshot] = useState<{ key: string; backends: ProviderBackendInfo[] | null } | null>(null)
  const backendRequest = useRef<{ key: string; promise: Promise<void> } | null>(null)
  const issued = useRef(0)
  const providers = useMemo(() => scope ? normalizeProviderList(listResult) : [], [listResult, scope])
  const key = scope ? workspaceResourceKey(scope) : null

  const reloadBackends = useCallback((): Promise<void> => {
    if (!scope || !isSettingsResourceScopeCurrent(scope)) return Promise.resolve()
    const requestKey = workspaceResourceKey(scope)
    if (backendRequest.current?.key === requestKey) return backendRequest.current.promise
    const sequence = ++issued.current
    const promise = requestWorkspaceResource(scope, 'provider/backend/list', {}, 20_000)
      .then((result) => {
        if (sequence !== issued.current || !isSettingsResourceScopeCurrent(scope)) return
        const backends = (result as { backends?: unknown } | null)?.backends
        setBackendSnapshot({ key: requestKey, backends: Array.isArray(backends) ? backends as ProviderBackendInfo[] : null })
      }, () => {
        if (sequence === issued.current && isSettingsResourceScopeCurrent(scope)) {
          setBackendSnapshot({ key: requestKey, backends: null })
        }
      })
      .finally(() => {
        if (backendRequest.current?.promise === promise) backendRequest.current = null
      })
    backendRequest.current = { key: requestKey, promise }
    return promise
  }, [scope])

  useEffect(() => {
    if (scope && status === 'idle' && isSettingsResourceScopeCurrent(scope)) void useProvidersStore.getState().reload()
  }, [scope, status])

  useEffect(() => {
    void reloadBackends()
    return () => { issued.current += 1; backendRequest.current = null }
  }, [reloadBackends])

  const reloadProviders = useCallback(async (): Promise<boolean> => {
    if (!scope || !isSettingsResourceScopeCurrent(scope)) return false
    await Promise.all([useProvidersStore.getState().reload(), reloadBackends()])
    return isSettingsResourceScopeCurrent(scope) && useProvidersStore.getState().status === 'ready' &&
      useProvidersStore.getState().error == null
  }, [reloadBackends, scope])

  return {
    providers,
    providersManagedRemotely: scope != null && (listResult as { managedBy?: string } | null)?.managedBy === 'modelService',
    providersLoading: scope != null && (status === 'idle' || status === 'loading'),
    providersReady: scope != null && status === 'ready',
    providerError: scope ? error : null,
    providerBackends: backendSnapshot?.key === key ? backendSnapshot.backends : null,
    reloadProviders
  }
}
