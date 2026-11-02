// @vitest-environment jsdom

import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalog } from '../hooks/useModelCatalog'
import { useConfig, useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { installDesktopApiMock } from './desktopApiMock'

const capabilities = { workspaceConfigManagement: true, modelCatalogManagement: true, providerManagement: true }
const sendRequest = vi.fn()
const listModels = vi.fn()
let serverConfig: Record<string, unknown>

beforeEach(() => {
  useConnectionStore.getState().reset()
  useConfigStore.getState().reset()
  useProvidersStore.getState().reset()
  useModelCatalogStore.getState().reset()
  serverConfig = { ProviderId: 'same-provider', ProviderPreferences: { 'same-provider': { model: 'old-model' } } }
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'config/read') return { config: serverConfig }
    if (method === 'provider/list') return { providers: [{ id: 'same-provider', protocol: 'openai' }] }
    return { available: true, values: { usage: 1 } }
  })
  listModels.mockReset().mockResolvedValue({ success: true, providerId: 'same-provider', models: [{ id: 'model' }] })
  installDesktopApiMock({ appServer: { sendRequest, listModels, onNotification: () => () => undefined } })
  useConnectionStore.getState().setStatus({ status: 'connected', capabilities })
})

describe('provider connection scope', () => {
  it('reloads a nonempty configuration and the unchanged provider after reconnect', async () => {
    const hook = renderHook(() => ({ config: useConfig(), catalog: useModelCatalog('same-provider') }))
    await waitFor(() => {
      expect(hook.result.current.config).toEqual(serverConfig)
      expect(hook.result.current.catalog.status).toBe('ready')
    })
    await useProvidersStore.getState().reload()
    expect(useConfigStore.getState().config).not.toBeNull()

    act(() => useConnectionStore.getState().setStatus({ status: 'connecting' }))
    serverConfig = { ProviderId: 'same-provider', ProviderPreferences: { 'same-provider': { model: 'new-model' } } }
    const publishedConfigurations: unknown[] = []
    const unsubscribe = useConnectionStore.subscribe((state) => {
      if (state.status === 'connected') publishedConfigurations.push(useConfigStore.getState().config)
    })
    act(() => useConnectionStore.getState().setStatus({ status: 'connected', capabilities }))
    unsubscribe()

    expect(publishedConfigurations).toEqual([null])
    expect(useProvidersStore.getState().status).toBe('idle')
    await waitFor(() => {
      expect(hook.result.current.config).toEqual(serverConfig)
      expect(hook.result.current.catalog.status).toBe('ready')
      expect(listModels).toHaveBeenCalledTimes(2)
    })
    expect(sendRequest.mock.calls.filter(([method]) => method === 'config/read')).toHaveLength(2)
  })

  it('does not discard configuration, providers or catalogs for duplicate connected events', async () => {
    const hook = renderHook(() => ({ config: useConfig(), catalog: useModelCatalog('same-provider') }))
    await waitFor(() => {
      expect(hook.result.current.config).toEqual(serverConfig)
      expect(hook.result.current.catalog.status).toBe('ready')
    })
    await useProvidersStore.getState().reload()
    act(() => useConnectionStore.getState().setStatus({ status: 'connected', capabilities }))

    expect(hook.result.current.config).toEqual(serverConfig)
    expect(useProvidersStore.getState().providers).toMatchObject([{ id: 'same-provider' }])
    await useModelCatalogStore.getState().loadIfNeeded(false, 'same-provider')
    expect(listModels).toHaveBeenCalledOnce()
    expect(sendRequest.mock.calls.filter(([method]) => method === 'config/read')).toHaveLength(1)
  })
})
