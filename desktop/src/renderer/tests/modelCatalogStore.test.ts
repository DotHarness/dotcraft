// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { installDesktopApiMock } from './desktopApiMock'

const appServerListModels = vi.fn()

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('modelCatalogStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useModelCatalogStore.getState().reset()
    installDesktopApiMock({ appServer: { listModels: appServerListModels } })
  })

  it('treats model/list failures as retryable errors', async () => {
    appServerListModels
      .mockResolvedValueOnce({
        success: false,
        errorCode: 'EndpointNotSupported',
        errorMessage: 'Endpoint does not support model listing.'
      })
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'gpt-5' }]
      })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledWith(null)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: true,
      errorCode: 'EndpointNotSupported',
      errorMessage: 'Endpoint does not support model listing.'
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'ready',
      modelOptions: ['gpt-5'],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
  })

  it('stores thrown model/list errors and keeps loading after a synchronous failure', async () => {
    appServerListModels.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
    })

    appServerListModels.mockImplementation(() => {
      throw new Error('bridge unavailable')
    })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-a')
    expect(useModelCatalogStore.getState()).toMatchObject({ status: 'error', errorMessage: 'bridge unavailable' })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-b')

    expect(appServerListModels).toHaveBeenCalledTimes(3)
    expect(useModelCatalogStore.getState()).toMatchObject({ status: 'error', requestedProviderId: 'provider-b' })
  })

  it('keeps a pre-connection model list retryable without surfacing an error', async () => {
    appServerListModels.mockResolvedValueOnce(null)

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'idle',
      models: [],
      modelOptions: [],
      errorCode: null,
      errorMessage: null
    })
  })

  it('passes provider id to model/list, reloads when it changes and stores the effective default provider', async () => {
    appServerListModels
      .mockResolvedValueOnce({
        success: true,
        providerId: 'openai',
        models: [{ id: 'gpt-5.5' }]
      })
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'claude-sonnet' }]
      })
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'gpt-5' }]
      })

    await useModelCatalogStore.getState().loadIfNeeded()
    expect(appServerListModels).toHaveBeenCalledWith(null)
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openai',
      requestedProviderId: null,
      modelOptions: ['gpt-5.5']
    })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'anthropic-main')
    await useModelCatalogStore.getState().loadIfNeeded(false, 'openrouter')

    expect(appServerListModels).toHaveBeenNthCalledWith(2, 'anthropic-main')
    expect(appServerListModels).toHaveBeenNthCalledWith(3, 'openrouter')
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openrouter',
      modelOptions: ['gpt-5']
    })
  })

  it('loads different providers independently and keeps the selected provider visible', async () => {
    const first = createDeferred<unknown>()
    const second = createDeferred<unknown>()
    appServerListModels
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const defaultLoad = useModelCatalogStore.getState().loadIfNeeded()
    const providerLoad = useModelCatalogStore.getState().loadIfNeeded(false, 'anthropic-main')

    expect(appServerListModels).toHaveBeenCalledTimes(2)
    first.resolve({
      success: true,
      providerId: 'openai',
      models: [{ id: 'gpt-5' }]
    })
    await defaultLoad
    await Promise.resolve()

    expect(useModelCatalogStore.getState()).toMatchObject({ status: 'loading', requestedProviderId: 'anthropic-main' })

    expect(appServerListModels).toHaveBeenNthCalledWith(2, 'anthropic-main')
    second.resolve({
      success: true,
      providerId: 'anthropic-main',
      models: [{ id: 'claude-sonnet-4-5' }]
    })
    await providerLoad

    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'anthropic-main',
      requestedProviderId: 'anthropic-main',
      modelOptions: ['claude-sonnet-4-5']
    })
  })

  it('keeps reasoning and Fast metadata from model/list, dropping unknown efforts', async () => {
    appServerListModels.mockResolvedValueOnce({
      success: true,
      models: [
        {
          id: 'claude-opus-4-7',
          reasoning: {
            supportsDisable: true,
            supportedEfforts: [
              { effort: 'low', label: 'Low', description: 'Fast' },
              { effort: 'extraHigh', label: 'Extra High', description: 'Deep' },
              { effort: 'ultra', label: 'Ultra', description: 'Orchestrated' },
              { effort: 'future', label: 'Future', description: 'Unknown' }
            ],
            defaultEffort: 'ultra',
            supportedOutputs: ['none', 'full'],
            defaultOutput: 'full'
          },
          speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' }
        }
      ]
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState().models).toEqual([
      expect.objectContaining({
        id: 'claude-opus-4-7',
        reasoning: expect.objectContaining({
          supportsDisable: true,
          defaultEffort: 'ultra',
          supportedEfforts: [
            { effort: 'low', label: 'Low', description: 'Fast' },
            { effort: 'extraHigh', label: 'Extra High', description: 'Deep' },
            { effort: 'ultra', label: 'Ultra', description: 'Orchestrated' }
          ]
        }),
        speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' }
      })
    ])
  })

  it('keeps a non-blank displayName from model/list', async () => {
    appServerListModels.mockResolvedValueOnce({
      success: true,
      models: [{ id: 'opus[1m]', displayName: ' Opus ' }, { id: 'haiku', displayName: '  ' }]
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    const models = useModelCatalogStore.getState().models
    expect(models.find((model) => model.id === 'opus[1m]')?.displayName).toBe('Opus')
    expect(models.find((model) => model.id === 'haiku')).not.toHaveProperty('displayName')
  })

  it('reuses catalogs across preference changes, provider switches and the resolved default provider', async () => {
    appServerListModels.mockImplementation(async (providerId) => ({
      success: true, providerId: providerId ?? 'one', models: [{ id: providerId ?? 'one' }]
    }))
    const store = useModelCatalogStore.getState()
    await store.loadIfNeeded()
    await store.loadIfNeeded(false, 'one')
    await store.handleConfigChanged(['ProviderPreferences'])
    await store.loadIfNeeded(false, 'one')
    await store.loadIfNeeded(false, 'two')
    await store.loadIfNeeded(false, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState().modelOptions).toEqual(['one'])
    await store.handleConfigChanged(['ProviderId'])
    await store.loadIfNeeded(false, 'two')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    await store.loadIfNeeded()
    expect(appServerListModels).toHaveBeenCalledTimes(3)
    await store.handleConfigChanged(['ProviderId'])
    await store.loadIfNeeded()
    expect(appServerListModels).toHaveBeenCalledTimes(4)
  })

  it('coalesces refreshes and invalidates catalogs when provider configuration changes', async () => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValue({ success: true, providerId: 'one', models: [{ id: 'old' }] })
    await store.loadIfNeeded(false, 'one')
    const refreshed = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(refreshed.promise)
    const first = store.loadIfNeeded(true, 'one')
    const second = store.loadIfNeeded(true, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    refreshed.resolve({ success: true, providerId: 'one', models: [{ id: 'new' }] })
    await Promise.all([first, second])
    expect(useModelCatalogStore.getState().modelOptions).toEqual(['new'])
    await store.handleConfigChanged(['providers'])
    expect(appServerListModels).toHaveBeenCalledTimes(3)
  })

  it('discards old connection results after reset, including cached provider aliases', async () => {
    const old = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(old.promise)
    const store = useModelCatalogStore.getState()
    const pending = store.loadIfNeeded(false, 'one')
    store.reset()
    appServerListModels.mockResolvedValue({ success: true, providerId: 'one', models: [{ id: 'fresh' }] })
    await store.loadIfNeeded(false, 'one')
    old.resolve({ success: true, providerId: 'one', models: [{ id: 'stale' }] })
    await pending
    await store.loadIfNeeded(false, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState().modelOptions).toEqual(['fresh'])
  })
})
