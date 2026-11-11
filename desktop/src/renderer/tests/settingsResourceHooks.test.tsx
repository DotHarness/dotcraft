import type { PropsWithChildren } from 'react'
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useSettingsModelResources } from '../components/settings/useSettingsModelResources'
import { useSettingsProviderResources } from '../components/settings/useSettingsProviderResources'
import { useConnectionStore } from '../stores/connectionStore'
import { activateWorkspaceResources } from '../stores/providerConnectionScope'
import { installDesktopApiMock } from './desktopApiMock'
import { activateTestWorkspace, testWorkspaceScope } from './workspaceResourceFixtures'

const sendRequest = vi.fn()
const sendConnectionRequest = vi.fn()
const other = { projectKey: '/other', connectionKey: '/other', connectionId: 'other-1' }
function wrapper({ children }: PropsWithChildren) { return <LocaleProvider>{children}</LocaleProvider> }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

beforeEach(() => {
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'provider/list') return {
      managedBy: 'modelService',
      providers: [{ id: 'provider', displayName: 'Provider', protocol: 'openai', hasApiKey: true,
        endPoint: 'https://example.test', isAuthenticated: true }]
    }
    if (method === 'model/list') return { success: true, providerId: 'provider', models: [{ id: 'model' }] }
    return { backends: [] }
  })
  sendConnectionRequest.mockReset().mockImplementation((_key, method, params, timeout) => sendRequest(method, params, timeout))
  installDesktopApiMock({ appServer: { sendRequest, sendConnectionRequest }, settings: { get: async () => ({ locale: 'en' }) } })
  useConnectionStore.getState().reset()
  useConnectionStore.setState({
    status: 'connected', capabilities: { providerManagement: true, modelCatalogManagement: true }
  })
  activateTestWorkspace()
})

it('does not read settings resources until a foreground scope is ready', async () => {
  activateWorkspaceResources(null)
  const hook = renderHook(() => ({
    providers: useSettingsProviderResources(true),
    models: useSettingsModelResources('provider', true, false)
  }), { wrapper })
  await act(async () => {})
  expect(sendRequest).not.toHaveBeenCalled()

  act(() => activateWorkspaceResources(testWorkspaceScope))
  await waitFor(() => {
    expect(hook.result.current.providers.providersReady).toBe(true)
    expect(hook.result.current.models.providerModelCatalog.map((model) => model.id)).toEqual(['model'])
  })
  expect(hook.result.current.providers.providersManagedRemotely).toBe(true)
  expect(hook.result.current.providers.providers[0]).toMatchObject({
    hasApiKey: true, isAuthenticated: true, endPoint: 'https://example.test'
  })
  expect(sendConnectionRequest.mock.calls.every(([key, , , , options]) =>
    key === testWorkspaceScope.connectionKey && options.connectionId === testWorkspaceScope.connectionId)).toBe(true)
})

it('does not read or retry a model catalog for a known missing provider', async () => {
  const hook = renderHook(() => useSettingsModelResources('missing', true, true), { wrapper })
  await act(async () => { await hook.result.current.reloadProviderPreferences() })
  expect(sendRequest).not.toHaveBeenCalled()
  expect(hook.result.current.providerModelCatalog).toEqual([])
  expect(hook.result.current.providerModelError).not.toBe('')
})

it('retries model loading through ordinary shared catalog revalidation', async () => {
  sendRequest.mockResolvedValueOnce({ success: false, errorMessage: 'catalog unavailable' })
  const hook = renderHook(() => useSettingsModelResources('provider', true, false), { wrapper })
  await waitFor(() => expect(hook.result.current.providerModelError).toBe('catalog unavailable'))

  await act(async () => { await hook.result.current.reloadProviderPreferences() })

  expect(hook.result.current.providerModelCatalog.map((model) => model.id)).toEqual(['model'])
  expect(sendRequest.mock.calls.filter(([method]) => method === 'model/list')).toEqual([
    ['model/list', { providerId: 'provider' }, 20_000],
    ['model/list', { providerId: 'provider' }, 20_000]
  ])
})

it('ignores backend metadata from a previous workspace after switching', async () => {
  const oldBackend = deferred<unknown>()
  sendConnectionRequest.mockImplementation((key, method, params, timeout) => {
    if (method === 'provider/backend/list') return key === testWorkspaceScope.connectionKey
      ? oldBackend.promise : Promise.resolve({ backends: [{ id: 'current-backend' }] })
    return sendRequest(method, params, timeout)
  })
  const hook = renderHook(() => useSettingsProviderResources(true), { wrapper })
  await waitFor(() => expect(hook.result.current.providersReady).toBe(true))
  act(() => {
    useConnectionStore.setState({ connectionKey: other.connectionKey, connectionId: other.connectionId })
    activateWorkspaceResources(other)
  })
  await waitFor(() => expect(hook.result.current.providerBackends).toEqual([{ id: 'current-backend' }]))

  await act(async () => { oldBackend.resolve({ backends: [{ id: 'obsolete-backend' }] }) })

  expect(hook.result.current.providerBackends).toEqual([{ id: 'current-backend' }])
})

it('reports a stale provider reload as unsuccessful after its workspace is no longer foreground', async () => {
  const oldProviders = deferred<unknown>()
  sendConnectionRequest.mockImplementation((key, method, params, timeout) => method === 'provider/list' &&
    key === testWorkspaceScope.connectionKey ? oldProviders.promise : sendRequest(method, params, timeout))
  const hook = renderHook(() => useSettingsProviderResources(true), { wrapper })
  const previousReload = hook.result.current.reloadProviders()
  act(() => {
    useConnectionStore.setState({ connectionKey: other.connectionKey, connectionId: other.connectionId })
    activateWorkspaceResources(other)
  })
  await waitFor(() => expect(hook.result.current.providersReady).toBe(true))

  let refreshed = true
  await act(async () => {
    oldProviders.resolve({ providers: [] })
    refreshed = await previousReload
  })

  expect(refreshed).toBe(false)
  expect(hook.result.current.providers[0]?.id).toBe('provider')
})

it('treats unavailable backend metadata as an optional fallback', async () => {
  sendConnectionRequest.mockImplementation((key, method, params, timeout) => method === 'provider/backend/list'
    ? Promise.reject(new Error('backend metadata unavailable')) : sendRequest(method, params, timeout))
  const hook = renderHook(() => useSettingsProviderResources(true), { wrapper })
  await waitFor(() => expect(hook.result.current.providersReady).toBe(true))
  expect(hook.result.current.providerBackends).toBeNull()
  expect(hook.result.current.providerError).toBeNull()
})
