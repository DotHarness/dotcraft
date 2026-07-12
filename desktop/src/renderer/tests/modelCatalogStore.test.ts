// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalogStore } from '../stores/modelCatalogStore'

const appServerSendRequest = vi.fn()

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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { sendRequest: appServerSendRequest }
      }
    })
  })

  it('treats model/list failures as retryable errors', async () => {
    appServerSendRequest
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

    expect(appServerSendRequest).toHaveBeenCalledWith('model/list', {}, 20_000)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: true,
      errorCode: 'EndpointNotSupported',
      errorMessage: 'Endpoint does not support model listing.'
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerSendRequest).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'ready',
      modelOptions: ['gpt-5'],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
  })

  it('stores thrown model/list errors', async () => {
    appServerSendRequest.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
    })
  })

  it('passes provider id to model/list and reloads when it changes', async () => {
    appServerSendRequest
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

    expect(appServerSendRequest).toHaveBeenNthCalledWith(1, 'model/list', { providerId: 'anthropic-main' }, 20_000)
    expect(appServerSendRequest).toHaveBeenNthCalledWith(2, 'model/list', { providerId: 'openrouter' }, 20_000)
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openrouter',
      modelOptions: ['gpt-5']
    })
  })

  it('runs a provider-specific reload after another model list request is already in flight', async () => {
    const first = createDeferred<unknown>()
    const second = createDeferred<unknown>()
    appServerSendRequest
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const defaultLoad = useModelCatalogStore.getState().loadIfNeeded()
    const providerLoad = useModelCatalogStore.getState().loadIfNeeded(false, 'anthropic-main')

    expect(appServerSendRequest).toHaveBeenCalledTimes(1)
    first.resolve({
      success: true,
      providerId: 'openai',
      models: [{ id: 'gpt-5' }]
    })
    await defaultLoad
    await Promise.resolve()

    expect(appServerSendRequest).toHaveBeenNthCalledWith(2, 'model/list', { providerId: 'anthropic-main' }, 20_000)
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
    appServerSendRequest.mockResolvedValueOnce({
      success: true,
      providerId: 'openai',
      models: [{ id: 'gpt-5.5' }]
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerSendRequest).toHaveBeenCalledWith('model/list', {}, 20_000)
    expect(useModelCatalogStore.getState()).toMatchObject({
      providerId: 'openai',
      requestedProviderId: null,
      modelOptions: ['gpt-5.5']
    })
  })

  it('keeps reasoning metadata from model/list', async () => {
    appServerSendRequest.mockResolvedValueOnce({
      success: true,
      models: [
        {
          id: 'claude-opus-4-7',
          reasoning: {
            supportsDisable: true,
            supportedEfforts: [
              { effort: 'low', label: 'Low', description: 'Fast' },
              { effort: 'extraHigh', label: 'Extra High', description: 'Deep' }
            ],
            defaultEffort: 'extraHigh',
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
          defaultEffort: 'extraHigh',
          supportedEfforts: [
            { effort: 'low', label: 'Low', description: 'Fast' },
            { effort: 'extraHigh', label: 'Extra High', description: 'Deep' }
          ]
        })
      })
    ])
  })

  it('keeps Fast capability from model/list', async () => {
    appServerSendRequest.mockResolvedValueOnce({
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
