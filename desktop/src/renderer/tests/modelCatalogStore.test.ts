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

  it('keeps loading after model/list fails before returning a promise', async () => {
    appServerListModels.mockImplementation(() => {
      throw new Error('bridge unavailable')
    })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-a')
    expect(useModelCatalogStore.getState()).toMatchObject({ status: 'error', errorMessage: 'bridge unavailable' })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-b')

    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState()).toMatchObject({ status: 'error', requestedProviderId: 'provider-b' })
  })

  it('stores thrown model/list errors', async () => {
    appServerListModels.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
    })
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

  it('passes provider id to model/list and reloads when it changes', async () => {
    appServerListModels
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'claude-sonnet' }]
      })
      .mockResolvedValueOnce({
        success: true,
        models: [{ id: 'gpt-5' }]
      })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'anthropic-main')
    await useModelCatalogStore.getState().loadIfNeeded(false, 'openrouter')

    expect(appServerListModels).toHaveBeenNthCalledWith(1, 'anthropic-main')
    expect(appServerListModels).toHaveBeenNthCalledWith(2, 'openrouter')
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openrouter',
      modelOptions: ['gpt-5']
    })
  })

  it('runs a provider-specific reload after another model list request is already in flight', async () => {
    const first = createDeferred<unknown>()
    const second = createDeferred<unknown>()
    appServerListModels
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const defaultLoad = useModelCatalogStore.getState().loadIfNeeded()
    const providerLoad = useModelCatalogStore.getState().loadIfNeeded(false, 'anthropic-main')

    expect(appServerListModels).toHaveBeenCalledTimes(1)
    first.resolve({
      success: true,
      providerId: 'openai',
      models: [{ id: 'gpt-5' }]
    })
    await defaultLoad
    await Promise.resolve()

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

  it('stores the effective provider id returned for the workspace default provider', async () => {
    appServerListModels.mockResolvedValueOnce({
      success: true,
      providerId: 'openai',
      models: [{ id: 'gpt-5.5' }]
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledWith(null)
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openai',
      requestedProviderId: null,
      modelOptions: ['gpt-5.5']
    })
  })

  it('keeps reasoning metadata from model/list', async () => {
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
          }
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
        })
      })
    ])
  })

  it('keeps Fast capability from model/list', async () => {
    appServerListModels.mockResolvedValueOnce({
      success: true,
      models: [{
        id: 'gpt-5.5',
        speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' }
      }]
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState().models[0]).toMatchObject({
      id: 'gpt-5.5',
      speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' }
    })
  })
})
