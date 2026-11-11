import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useForegroundThreadNavigation } from '../hooks/useForegroundThreadNavigation'
import { useModelCatalog } from '../hooks/useModelCatalog'
import { useConfig, useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { activateWorkspaceResources, applyWorkspaceResourceConfigChange, pruneWorkspaceResources } from '../stores/providerConnectionScope'
import { resetTestWorkspaceResources } from './workspaceResourceFixtures'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

const capabilities = { workspaceConfigManagement: true, modelCatalogManagement: true, providerManagement: true }
const sendConnectionRequest = vi.fn()
const configurations = new Map<string, Record<string, unknown>>()
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
function connect(key: string, id = key + '-1') {
  useConnectionStore.getState().setStatus({
    status: 'connected', connectionKey: key, connectionId: id, capabilities
  })
}
function projects(foreground: string, paths = ['/a', '/b']) {
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: foreground, foregroundProjectId: foreground, secondaryLimit: 8,
    projects: paths.map(path => ({
      path, projectId: path, name: path, loaded: true, running: true, threadCount: 0, threads: [],
      state: path === foreground ? 'foreground' : 'secondary', pinned: false,
      connectionKey: path, connectionId: path + '-1'
    }))
  })
}
function mount(path = '/a') {
  return renderHook(({ path }) => {
    const { foregroundReady } = useForegroundThreadNavigation({
      projectKey: path, protocolWorkspacePath: path, remoteWorkspace: false, locale: 'en'
    })
    return { ready: foregroundReady, config: useConfig(), catalog: useModelCatalog('same-provider', foregroundReady) }
  }, { initialProps: { path } })
}
function reads(key: string, method = 'config/read') {
  return sendConnectionRequest.mock.calls.filter(([source, name]) => source === key && name === method)
}
beforeEach(() => {
  resetTestWorkspaceResources()
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useUIStore.setState({ pendingProjectThreadOpen: null, activeMainView: 'conversation' })
  configurations.clear()
  configurations.set('/a', { ProviderId: 'same-provider', Permissions: { DefaultApprovalPolicy: 'autoApprove' } })
  configurations.set('/b', { ProviderId: 'same-provider', Permissions: { DefaultApprovalPolicy: 'prompt' } })
  sendConnectionRequest.mockReset().mockImplementation(async (key, method) => {
    if (method === 'config/read') return { config: configurations.get(key) }
    if (method === 'provider/list') return { providers: [{ id: 'same-provider', displayName: key, protocol: 'openai' }] }
    if (method === 'model/list') return { success: true, providerId: 'same-provider', models: [{ id: key + '-model' }] }
    return {}
  })
  installDesktopApiMock({ appServer: {
    sendConnectionRequest, sendRequest: async () => ({ data: [] }), onNotification: () => () => undefined
  }, settings: { get: async () => ({}) } })
  connect('/a')
  projects('/a')
})

describe('foreground workspace resources', () => {
  it.each(['workspace-first', 'connection-first', 'batched'] as const)(
    'loads configuration once after %s switching, without reading a mismatched connection', async order => {
      const hook = mount()
      await act(async () => {})
      expect(hook.result.current.config).toEqual(configurations.get('/a'))
      const pending = deferred<unknown>()
      sendConnectionRequest.mockImplementation((key, method) => {
        if (key === '/b' && method === 'config/read') return pending.promise
        return Promise.resolve(method === 'model/list'
          ? { success: true, providerId: 'same-provider', models: [{ id: key + '-model' }] }
          : { providers: [] })
      })
      if (order === 'batched') {
        act(() => { connect('/b'); hook.rerender({ path: '/b' }) })
      } else {
        act(() => {
          if (order === 'workspace-first') hook.rerender({ path: '/b' })
          else connect('/b')
        })
        expect(hook.result.current.config).toBeNull()
        expect(reads('/b')).toHaveLength(0)
        act(() => {
          if (order === 'workspace-first') connect('/b')
          else hook.rerender({ path: '/b' })
        })
      }
      expect(reads('/b')).toHaveLength(1)
      await act(async () => { pending.resolve({ config: configurations.get('/b') }) })
      expect(hook.result.current.config).toEqual(configurations.get('/b'))
      expect(reads('/b')).toHaveLength(1)
      expect(reads('/b')[0][4]).toEqual({ connectionId: '/b-1' })
    }
  )

  it('restores all warm resources before revalidation completes and keeps them usable on failure', async () => {
    const hook = mount()
    await act(async () => {})
    act(() => { connect('/b'); hook.rerender({ path: '/b' }) })
    await act(async () => {})
    expect(hook.result.current.config).toEqual(configurations.get('/b'))
    expect(hook.result.current.catalog.modelOptions).toEqual(['/b-model'])
    const pending = deferred<unknown>()
    sendConnectionRequest.mockImplementation((_key, method) => method === 'config/read'
      ? pending.promise : Promise.reject(new Error('refresh failed')))
    act(() => { connect('/a'); hook.rerender({ path: '/a' }) })
    expect(hook.result.current.config).toEqual(configurations.get('/a'))
    expect(hook.result.current.catalog.status).toBe('ready')
    expect(hook.result.current.catalog.modelOptions).toEqual(['/a-model'])
    expect(useProvidersStore.getState().providers[0]?.displayName).toBe('/a')
    await act(async () => { pending.resolve({ config: configurations.get('/a') }) })
    expect(hook.result.current.catalog.status).toBe('ready')
    expect(hook.result.current.catalog.errorMessage).toBe('refresh failed')
    expect(useProvidersStore.getState().status).toBe('ready')
  })

  it('revalidates cached unselected provider catalogs without forcing an upstream refresh', async () => {
    const hook = mount()
    await act(async () => {
      await useModelCatalogStore.getState().loadIfNeeded(false, 'other-provider')
    })
    act(() => { connect('/b'); hook.rerender({ path: '/b' }) })
    await act(async () => {})
    const before = reads('/a', 'model/list').length
    act(() => { connect('/a'); hook.rerender({ path: '/a' }) })
    await act(async () => {})
    const revalidation = reads('/a', 'model/list').slice(before)
    expect(revalidation.map(([, , params]) => params)).toContainEqual({ providerId: 'other-provider' })
    expect(revalidation.every(([, , params]) => params.refresh !== true)).toBe(true)
  })

  it('does not revalidate on duplicate connected reports or hook remounts', async () => {
    const hook = mount()
    await act(async () => {})
    act(() => connect('/a'))
    expect(reads('/a')).toHaveLength(1)
    hook.unmount()
    const remount = mount()
    await act(async () => {})
    expect(remount.result.current.config).toEqual(configurations.get('/a'))
    expect(reads('/a')).toHaveLength(1)
    expect(reads('/a', 'model/list')).toHaveLength(1)
  })

  it('starts a new cache for a replacement connection lifetime', async () => {
    const hook = mount()
    await act(async () => {})
    const pending = deferred<unknown>()
    sendConnectionRequest.mockReturnValue(pending.promise)
    act(() => connect('/a', '/a-2'))
    expect(hook.result.current.config).toBeNull()
    expect(useModelCatalogStore.getState().getCatalog('same-provider').modelOptions).toEqual([])
    await act(async () => { pending.resolve({ config: { ProviderId: 'replacement' }, providers: [] }) })
    expect(hook.result.current.config).toEqual({ ProviderId: 'replacement' })
  })

  it('ignores configuration notifications from an old connection lifetime', async () => {
    const hook = mount()
    await act(async () => {})
    act(() => {
      const accepted = applyWorkspaceResourceConfigChange(['Permissions.DefaultApprovalPolicy'], {
        connectionKey: '/a', connectionId: '/a-old'
      })
      expect(accepted).toBe(false)
    })
    expect(reads('/a')).toHaveLength(1)
    configurations.set('/a', { Permissions: { DefaultApprovalPolicy: 'prompt' } })
    await act(async () => {
      expect(applyWorkspaceResourceConfigChange(['Permissions.DefaultApprovalPolicy'], {
        connectionKey: '/a', connectionId: '/a-1'
      })).toBe(true)
    })
    expect(hook.result.current.config).toEqual(configurations.get('/a'))
    expect(reads('/a')).toHaveLength(2)
  })

  it('isolates a remote project with the same protocol path as a local workspace', async () => {
    const local = mount()
    await act(async () => {})
    local.unmount()
    const remoteKey = 'remote:device:project'
    configurations.set(remoteKey, { ProviderId: 'remote-provider' })
    connect(remoteKey)
    const remote = renderHook(() => {
      useForegroundThreadNavigation({
        projectKey: remoteKey, protocolWorkspacePath: '/a', remoteWorkspace: true, locale: 'en'
      })
      return useConfig()
    })
    expect(remote.result.current).toBeNull()
    await act(async () => {})
    expect(remote.result.current).toEqual({ ProviderId: 'remote-provider' })
    expect(reads(remoteKey)).toHaveLength(1)
    expect(reads(remoteKey)[0][4]).toEqual({ connectionId: remoteKey + '-1' })
  })

  it('evicts closed secondary connection snapshots instead of reviving them', async () => {
    const hook = mount()
    await act(async () => {})
    act(() => { connect('/b'); hook.rerender({ path: '/b' }) })
    await act(async () => {})
    act(() => { projects('/b', ['/b']); pruneWorkspaceResources() })
    act(() => activateWorkspaceResources({ projectKey: '/a', connectionKey: '/a', connectionId: '/a-1' }))
    expect(useConfigStore.getState().config).toBeNull()
    expect(useModelCatalogStore.getState().getCatalog('same-provider').modelOptions).toEqual([])
  })
})
