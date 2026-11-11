// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { useRef } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useWelcomeModelControls } from '../components/conversation/useWelcomeModelControls'
import { useConfig, useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import type { WelcomeDraft } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'
import { activateTestWorkspace, testWorkspaceScope } from './workspaceResourceFixtures'
import { activateWorkspaceResources, refreshWorkspaceResources } from '../stores/providerConnectionScope'

const listModels = vi.fn()
const sendRequest = vi.fn()
const preference = {
  model: 'claude-opus-5-5',
  reasoning: { enabled: true, effort: 'high', output: 'full' },
  speed: 'standard'
}
let serverConfig: Record<string, unknown>

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function catalog(providerId: string, model: string) {
  return { success: true, providerId, models: [{ id: model, reasoning: {
    supportsDisable: false, supportedEfforts: [{ effort: 'high', label: 'High', description: '' }],
    defaultEffort: 'high', supportedOutputs: ['full'], defaultOutput: 'full'
  } }] }
}

function mountControls(draft: WelcomeDraft | null = null) {
  return renderHook(() => {
    const workspaceConfig = useConfig()
    const initialDraftRef = useRef(draft)
    return useWelcomeModelControls({ workspaceConfig, initialDraftRef })
  }, { wrapper: LocaleProvider })
}

function writes() {
  return sendRequest.mock.calls.filter(([method]) => method === 'config/batchWrite' || method === 'config/value/write')
}

beforeEach(() => {
  vi.resetAllMocks()
  serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: { anthropicapi: preference } }
  useConnectionStore.setState({ status: 'connected', capabilities: {
    modelCatalogManagement: true, workspaceConfigManagement: true
  } })
  listModels.mockImplementation(async (providerId: string) => catalog(providerId, preference.model))
  sendRequest.mockImplementation(async (method: string, params?: { edits: Array<{ keyPath: string; value: unknown }> }) => {
    if (method === 'config/read') return { config: serverConfig }
    if (method === 'config/batchWrite') {
      const next = { ...serverConfig }
      for (const edit of params?.edits ?? []) {
        if (edit.keyPath === 'ProviderId') next.ProviderId = edit.value
        else if (edit.keyPath.startsWith('ProviderPreferences.')) {
          next.ProviderPreferences = { ...(next.ProviderPreferences as object), [edit.keyPath.slice('ProviderPreferences.'.length)]: edit.value }
        }
      }
      serverConfig = next
    }
    return {}
  })
  installDesktopApiMock({ appServer: { listModels, sendRequest }, settings: { get: async () => null } })
  activateTestWorkspace()
})

describe('Welcome provider-scoped model controls', () => {
  it('keeps Anthropic capabilities when a later default DeepSeek load completes', async () => {
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.modelCatalog[0]?.id).toBe(preference.model))
    const pending = deferred<unknown>()
    listModels.mockReturnValueOnce(pending.promise)
    let defaultLoad!: Promise<unknown>
    act(() => { defaultLoad = useModelCatalogStore.getState().loadIfNeeded(false) })
    await act(async () => {
      pending.resolve(catalog('deepseek', 'deepseek-v4-pro'))
      await defaultLoad
    })
    expect(hook.result.current.providerId).toBe('anthropicapi')
    expect(hook.result.current.modelName).toBe(preference.model)
    expect(hook.result.current.modelCatalog[0]?.reasoning?.defaultEffort).toBe('high')
    expect(hook.result.current.modelOptions).toEqual([preference.model])
    expect(hook.result.current.reasoningConfig.effort).toBe('high')
    expect(writes()).toEqual([])
  })

  it('does not load or persist model settings while connecting or configuration is still loading', async () => {
    useConnectionStore.setState({ status: 'connecting' })
    const pending = deferred<unknown>()
    sendRequest.mockImplementation((method: string) => method === 'config/read' ? pending.promise : Promise.resolve({}))
    const hook = mountControls()
    await act(async () => {
      await hook.result.current.handleProviderChange('deepseek')
      await hook.result.current.handleModelChange('deepseek-v4-pro')
    })
    expect(listModels).not.toHaveBeenCalled()
    expect(writes()).toEqual([])
    act(() => useConnectionStore.setState({ status: 'connected' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('config/read', {}))
    await act(async () => { await hook.result.current.handleProviderChange('deepseek') })
    expect(listModels).not.toHaveBeenCalled()
    expect(writes()).toEqual([])
    await act(async () => pending.resolve({ config: serverConfig }))
    await waitFor(() => expect(hook.result.current.modelName).toBe(preference.model))
    expect(listModels).toHaveBeenCalledWith('anthropicapi')
    expect(writes()).toEqual([])
  })

  it('keeps the configured provider while its missing preference waits for the catalog', async () => {
    serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: {} }
    const pending = deferred<unknown>()
    listModels.mockReturnValue(pending.promise)
    const draft = { text: 'draft', model: 'deepseek-v4-pro', providerId: 'deepseek', mode: 'agent', images: [], updatedAt: 0 } as WelcomeDraft
    const hook = mountControls(draft)
    await waitFor(() => expect(listModels).toHaveBeenCalledWith('anthropicapi'))
    expect(hook.result.current.providerId).toBe('anthropicapi')
    expect(hook.result.current.modelLoading).toBe(true)
    expect(hook.result.current.modelName).toBe('Default')
    expect(writes()).toEqual([])
    hook.unmount()
    await act(async () => pending.resolve(catalog('anthropicapi', preference.model)))
    expect(writes()).toEqual([])
  })

  it('does not seed a preference from a catalog invalidated during workspace startup', async () => {
    serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: {} }
    const pending = deferred<unknown>()
    listModels.mockReturnValue(pending.promise)
    mountControls()
    await waitFor(() => expect(listModels).toHaveBeenCalledWith('anthropicapi'))
    await act(async () => {
      useConnectionStore.setState({ status: 'connecting' })
      useModelCatalogStore.getState().reset()
      useConfigStore.getState().reset()
      pending.resolve(catalog('anthropicapi', preference.model))
    })
    expect(writes()).toEqual([])
    expect(useConfigStore.getState().config).toBeNull()
  })

  it('uses the concrete workspace preference instead of a stale restored draft pair', async () => {
    const draft = {
      text: 'draft', model: 'deepseek-v4-pro', providerId: 'deepseek', reasoning: { enabled: false, effort: 'low', output: 'full' },
      mode: 'agent', images: [], updatedAt: 0
    } as WelcomeDraft
    const hook = mountControls(draft)
    await waitFor(() => expect(hook.result.current.providerId).toBe('anthropicapi'))
    await act(async () => hook.result.current.hydrateDraft(draft))
    expect(hook.result.current.modelName).toBe(preference.model)
    expect(hook.result.current.reasoningConfig.effort).toBe('high')
    expect(writes()).toEqual([])
  })

  it('persists an explicit provider selection without overwriting another provider preference', async () => {
    listModels.mockImplementation(async (providerId: string) => catalog(providerId,
      providerId === 'deepseek' ? 'deepseek-v4-pro' : preference.model))
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.modelName).toBe(preference.model))
    await act(async () => { await hook.result.current.handleProviderChange('deepseek') })
    expect(hook.result.current.providerId).toBe('deepseek')
    expect(hook.result.current.modelName).toBe('deepseek-v4-pro')
    expect(serverConfig.ProviderPreferences).toMatchObject({
      anthropicapi: preference,
      deepseek: { model: 'deepseek-v4-pro' }
    })
    expect(writes()).toHaveLength(1)
  })

  it('does not seed missing preferences from an unconfirmed restored configuration', async () => {
    serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: {} }
    await useConfigStore.getState().ensureLoaded()
    await useModelCatalogStore.getState().loadIfNeeded(false, 'anthropicapi')
    const pending = deferred<unknown>()
    const originalRequest = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation((method: string, ...args: unknown[]) =>
      method === 'config/read' ? pending.promise : originalRequest(method, ...args))
    activateWorkspaceResources(null)
    activateWorkspaceResources(testWorkspaceScope)
    let refresh!: Promise<void>
    await act(async () => { refresh = refreshWorkspaceResources() })
    const hook = mountControls()
    expect(hook.result.current.modelOptions).toEqual([preference.model])
    expect(useConfigStore.getState().confirmed).toBe(false)
    expect(writes()).toEqual([])

    serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: { anthropicapi: preference } }
    await act(async () => { pending.resolve({ config: serverConfig }); await refresh })
    expect(hook.result.current.modelName).toBe(preference.model)
    expect(useConfigStore.getState().confirmed).toBe(true)
    expect(writes()).toEqual([])
  })

  it('does not restore the previous provider during catalog renders for an unconfirmed cache with missing preferences', async () => {
    const otherScope = { projectKey: '/other', connectionKey: '/other', connectionId: 'other-1' }
    const originalConfig = serverConfig
    serverConfig = { ProviderId: 'deepseek', ProviderPreferences: {} }
    useConnectionStore.setState({ connectionKey: otherScope.connectionKey, connectionId: otherScope.connectionId })
    activateWorkspaceResources(otherScope)
    await useConfigStore.getState().ensureLoaded()

    serverConfig = originalConfig
    useConnectionStore.setState({
      connectionKey: testWorkspaceScope.connectionKey, connectionId: testWorkspaceScope.connectionId
    })
    activateWorkspaceResources(testWorkspaceScope)
    const pendingCatalog = deferred<unknown>()
    listModels.mockImplementation((providerId: string) => providerId === 'deepseek'
      ? pendingCatalog.promise : Promise.resolve(catalog(providerId, preference.model)))
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.modelOptions).toEqual([preference.model]))

    await act(async () => {
      useConnectionStore.setState({ connectionKey: otherScope.connectionKey, connectionId: otherScope.connectionId })
      activateWorkspaceResources(otherScope)
    })
    expect(useConfigStore.getState().confirmed).toBe(false)
    expect(listModels).toHaveBeenCalledWith('deepseek')
    expect(hook.result.current.providerId).toBe('deepseek')
    expect(hook.result.current.modelName).toBe('Default')
    expect(hook.result.current.modelLoading).toBe(true)
    hook.rerender()
    expect(hook.result.current.providerId).toBe('deepseek')

    await act(async () => { pendingCatalog.resolve(catalog('deepseek', 'deepseek-v4-pro')) })
    expect(hook.result.current.providerId).toBe('deepseek')
    expect(hook.result.current.modelName).toBe('Default')
    expect(hook.result.current.modelOptions).toEqual(['deepseek-v4-pro'])
    expect(useConfigStore.getState().confirmed).toBe(false)
    expect(writes()).toEqual([])
  })

  it('seeds missing preferences from the requested provider snapshot only', async () => {
    serverConfig = { ProviderId: 'anthropicapi', ProviderPreferences: {} }
    const hook = mountControls()
    await waitFor(() => expect(writes()).toHaveLength(1))
    await waitFor(() => expect(hook.result.current.modelName).toBe(preference.model))
    expect(writes()[0][1]).toMatchObject({ edits: [
      { keyPath: 'ProviderId', value: 'anthropicapi' },
      { keyPath: 'ProviderPreferences.anthropicapi', value: { model: preference.model } }
    ] })
  })
})
