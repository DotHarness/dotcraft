import type { DesktopPluginHost } from '@dotcraft/plugin'
import type { DreamsRunState } from '@dotcraft/sdk/contracts'
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { appServerFs, type MemoryFs } from '../data/memoryFs'
import { errorMessage, isUnsupported } from '../data/rpc'
import { listStores, loadStore, type StoreContents } from '../data/stores'
import { readPolicyDefaults, readPolicyState, type PolicyState } from '../policy/policy'

export type LoadStatus = 'loading' | 'ready' | 'unsupported' | 'error'

export interface Loaded<T> {
  status: LoadStatus
  value: T | null
  error: string | null
  reload(): void
}

export function useWorkspacePath(host: DesktopPluginHost): string | null {
  const session = host.session
  return useSyncExternalStore(session.onChange, () => session.workspacePath, () => session.workspacePath)
}

function useLoader<T>(load: () => Promise<T>, deps: readonly unknown[]): Loaded<T> {
  const [state, setState] = useState<{ status: LoadStatus; value: T | null; error: string | null }>({ status: 'loading', value: null, error: null })
  const [revision, setRevision] = useState(0)
  const sequence = useRef(0)
  useEffect(() => {
    const current = ++sequence.current
    setState((previous) => ({ ...previous, status: previous.value == null ? 'loading' : previous.status }))
    load().then(
      (value) => { if (current === sequence.current) setState({ status: 'ready', value, error: null }) },
      (error: unknown) => {
        if (current !== sequence.current) return
        setState({ status: isUnsupported(error) ? 'unsupported' : 'error', value: null, error: errorMessage(error) })
      }
    )
  }, [...deps, revision])
  const reload = useCallback(() => setRevision((value) => value + 1), [])
  return { ...state, reload }
}

function onConfigRegions(host: DesktopPluginHost, matches: (region: string) => boolean, reload: () => void): () => void {
  return host.appServer.onNotification('config/changed', (params) => {
    if ((params.regions ?? []).some((region) => matches(region.toLowerCase()))) reload()
  })
}

export function useMemoryFs(host: DesktopPluginHost): MemoryFs {
  return useMemo(() => appServerFs(host.appServer), [host])
}

export function useStores(host: DesktopPluginHost): Loaded<StoreContents[]> {
  const workspacePath = useWorkspacePath(host)
  const fs = useMemoryFs(host)
  const loaded = useLoader(async () => {
    const stores = await listStores(host.appServer)
    return Promise.all(stores.map((store) => loadStore(fs, store)))
  }, [host, fs, workspacePath])
  const { reload } = loaded
  useEffect(() => onConfigRegions(host, (region) => region === 'memory', reload), [host, reload])
  return loaded
}

export function useDreamRuns(host: DesktopPluginHost): Loaded<DreamsRunState[]> {
  const workspacePath = useWorkspacePath(host)
  const loaded = useLoader(async () => (await host.appServer.request('dreams/list', {})).runs ?? [], [host, workspacePath])
  const { reload } = loaded
  useEffect(() => onConfigRegions(host, (region) => region === 'memory', reload), [host, reload])
  return loaded
}

export function usePolicyState(host: DesktopPluginHost): Loaded<PolicyState> {
  const workspacePath = useWorkspacePath(host)
  const loaded = useLoader(async () => {
    const [schema, read] = await Promise.all([
      host.appServer.request('config/schema', {}),
      host.appServer.request('config/read', { includeLayers: true })
    ])
    return readPolicyState(readPolicyDefaults(schema), read)
  }, [host, workspacePath])
  const { reload } = loaded
  useEffect(() => onConfigRegions(host, (region) => region.startsWith('memory') || region.startsWith('dreams'), reload), [host, reload])
  return loaded
}
