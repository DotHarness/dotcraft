// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { installDesktopApiMock } from './desktopApiMock'

const appServerListModels = vi.fn()

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

describe('modelCatalogStore', () => {
  beforeEach(() => {
    vi.resetAllMocks()
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
    expect(useModelCatalogStore.getState().getCatalog()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: true,
      errorCode: 'EndpointNotSupported',
      errorMessage: 'Endpoint does not support model listing.'
    })

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(useModelCatalogStore.getState().getCatalog()).toMatchObject({
      status: 'ready',
      modelOptions: ['gpt-5'],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
  })

  it('clears refresh metadata after recovery, provider changes and reset', async () => {
    appServerListModels.mockResolvedValue({
      success: false, errorCode: 'Network', errorMessage: 'offline', refreshNotice: 'usingCachedCatalog',
      source: 'cache', refreshedAt: '2026-01-01T00:00:00Z',
      models: [{ id: 'cached' }]
    })
    const store = useModelCatalogStore.getState()
    await store.loadIfNeeded(false, 'one')
    expect(store.getCatalog('one').refreshNotice).toBe('usingCachedCatalog')
    appServerListModels.mockResolvedValue({ success: true, source: 'user', models: [{ id: 'fresh' }] })
    await store.loadIfNeeded(true, 'one')
    expect(store.getCatalog('one')).toMatchObject({ source: 'user', refreshedAt: undefined, refreshNotice: undefined })

    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const loading = store.loadIfNeeded(false, 'two')
    expect(store.getCatalog('two')).toMatchObject({ status: 'loading', source: undefined, refreshedAt: undefined, refreshNotice: undefined })
    pending.resolve({ success: true, models: [] })
    await loading
    expect(store.getCatalog('two').source).toBeUndefined()
    await store.loadIfNeeded(false, 'one')
    expect(store.getCatalog('one').source).toBe('user')
    store.reset()
    expect(store.getCatalog('one')).toMatchObject({ source: undefined, refreshedAt: undefined, refreshNotice: undefined })
  })

  it('stores thrown model/list errors and keeps loading after a synchronous failure', async () => {
    appServerListModels.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState().getCatalog()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
    })

    appServerListModels.mockImplementation(() => {
      throw new Error('bridge unavailable')
    })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-a')
    expect(useModelCatalogStore.getState().getCatalog('provider-a')).toMatchObject({ status: 'error', errorMessage: 'bridge unavailable' })

    await useModelCatalogStore.getState().loadIfNeeded(false, 'provider-b')

    expect(appServerListModels).toHaveBeenCalledTimes(3)
    expect(useModelCatalogStore.getState().getCatalog('provider-b')).toMatchObject({ status: 'error', errorMessage: 'bridge unavailable' })
  })

  it('keeps a pre-connection model list retryable without surfacing an error', async () => {
    appServerListModels.mockResolvedValueOnce(null)

    expect(await useModelCatalogStore.getState().loadIfNeeded()).toBeNull()

    expect(useModelCatalogStore.getState().getCatalog()).toMatchObject({
      status: 'idle',
      models: [],
      modelOptions: [],
      errorCode: null,
      errorMessage: null
    })
  })

  it.each(['explicit', 'default'] as const)('isolates catalogs when the default request starts last and %s completes first', async (completesFirst) => {
    const pending = { explicit: createDeferred<unknown>(), default: createDeferred<unknown>() }
    const payloads = {
      explicit: {
        success: true, providerId: 'anthropic',
        models: [{ id: 'claude-opus-5-5', reasoning: {
          supportsDisable: false, supportedEfforts: [{ effort: 'high' }], defaultEffort: 'high',
        } }],
      },
      default: {
        success: true, providerId: 'deepseek',
        models: [{ id: 'deepseek-v4-pro', reasoning: {
          supportsDisable: true, supportedEfforts: [{ effort: 'max' }], defaultEffort: 'max',
        } }],
      },
    }
    appServerListModels.mockImplementation((providerId) =>
      providerId === 'anthropic' ? pending.explicit.promise : pending.default.promise)
    const store = useModelCatalogStore.getState()
    const loads = {
      explicit: store.loadIfNeeded(false, 'anthropic'),
      default: store.loadIfNeeded(),
    }
    const completesSecond = completesFirst === 'explicit' ? 'default' : 'explicit'

    pending[completesFirst].resolve(payloads[completesFirst])
    expect(await loads[completesFirst]).toMatchObject({ providerId: payloads[completesFirst].providerId })
    expect(store.getCatalog(completesSecond === 'explicit' ? 'anthropic' : null).status).toBe('loading')
    pending[completesSecond].resolve(payloads[completesSecond])
    expect(await loads[completesSecond]).toMatchObject({ providerId: payloads[completesSecond].providerId })

    expect(store.getCatalog('anthropic')).toMatchObject({
      providerId: 'anthropic', modelOptions: ['claude-opus-5-5'],
      models: [{ id: 'claude-opus-5-5', reasoning: { defaultEffort: 'high', supportsDisable: false } }],
    })
    expect(store.getCatalog()).toMatchObject({
      providerId: 'deepseek', modelOptions: ['deepseek-v4-pro'],
      models: [{ id: 'deepseek-v4-pro', reasoning: { defaultEffort: 'max', supportsDisable: true } }],
    })
  })

  it('coalesces and caches provider ids without regard to case or surrounding whitespace', async () => {
    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const store = useModelCatalogStore.getState()
    const first = store.loadIfNeeded(false, ' Anthropic ')
    const second = store.loadIfNeeded(false, 'ANTHROPIC')
    expect(appServerListModels).toHaveBeenCalledExactlyOnceWith('anthropic')
    pending.resolve({ success: true, providerId: 'anthropic', models: [{ id: 'claude' }] })
    await Promise.all([first, second])
    await store.loadIfNeeded(false, 'Anthropic')
    expect(appServerListModels).toHaveBeenCalledTimes(1)
    expect(store.getCatalog(' ANTHROPIC ').modelOptions).toEqual(['claude'])
  })

  it.each(['pending', 'ready'])('does not seed over a %s concrete provider from a default result', async (concreteStatus) => {
    const concrete = createDeferred<unknown>()
    const defaultCatalog = createDeferred<unknown>()
    appServerListModels.mockImplementation((providerId) => providerId ? concrete.promise : defaultCatalog.promise)
    const store = useModelCatalogStore.getState()
    const concreteLoad = store.loadIfNeeded(false, 'one')
    const defaultLoad = store.loadIfNeeded()
    if (concreteStatus === 'ready') {
      concrete.resolve({ success: true, providerId: 'one', models: [{ id: 'concrete' }] })
      await concreteLoad
    }
    defaultCatalog.resolve({ success: true, providerId: 'one', models: [{ id: 'default' }] })
    await defaultLoad

    expect(store.getCatalog('one')).toMatchObject(concreteStatus === 'pending'
      ? { status: 'loading', modelOptions: [] }
      : { status: 'ready', modelOptions: ['concrete'] })
    concrete.resolve({ success: true, providerId: 'one', models: [{ id: 'concrete' }] })
    await concreteLoad
    expect(store.getCatalog('one').modelOptions).toEqual(['concrete'])
    expect(store.getCatalog().modelOptions).toEqual(['default'])
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

    expect(useModelCatalogStore.getState().getCatalog().models).toEqual([
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

    const models = useModelCatalogStore.getState().getCatalog().models
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
    store.handleConfigChanged(['ProviderPreferences'])
    await store.loadIfNeeded(false, 'one')
    await store.loadIfNeeded(false, 'two')
    await store.loadIfNeeded(false, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    expect(store.getCatalog('one').modelOptions).toEqual(['one'])
    store.handleConfigChanged(['ProviderId'])
    await store.loadIfNeeded(false, 'two')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    await store.loadIfNeeded()
    expect(appServerListModels).toHaveBeenCalledTimes(3)
    store.handleConfigChanged(['ProviderId'])
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
    expect(store.getCatalog('one').modelOptions).toEqual(['new'])
    store.handleConfigChanged(['providers'])
    expect(store.getCatalog('one').status).toBe('idle')
    await store.loadIfNeeded(false, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(3)
  })

  it('keeps a new same-provider request coalesced when an invalidated request finishes first', async () => {
    const old = createDeferred<unknown>()
    const current = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    const store = useModelCatalogStore.getState()
    const oldLoad = store.loadIfNeeded(false, 'one')
    store.reset()
    const currentLoad = store.loadIfNeeded(false, 'one')

    old.resolve({ success: true, providerId: 'one', models: [{ id: 'stale' }] })
    expect(await oldLoad).toBeNull()
    expect(store.getCatalog('one')).toMatchObject({ status: 'loading', modelOptions: [] })
    const coalesced = store.loadIfNeeded(false, 'ONE')
    expect(appServerListModels).toHaveBeenCalledTimes(2)
    current.resolve({ success: true, providerId: 'one', models: [{ id: 'fresh' }] })
    await Promise.all([currentLoad, coalesced])
    expect(store.getCatalog('one').modelOptions).toEqual(['fresh'])
  })

  it('does not replace a fresh default catalog or its provider alias with a pre-reset result', async () => {
    const old = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(old.promise)
    const store = useModelCatalogStore.getState()
    const oldLoad = store.loadIfNeeded()
    store.reset()
    appServerListModels.mockResolvedValue({ success: true, providerId: 'one', models: [{ id: 'fresh' }] })
    await store.loadIfNeeded()
    old.resolve({ success: true, providerId: 'one', models: [{ id: 'stale' }] })

    expect(await oldLoad).toBeNull()
    expect(store.getCatalog().modelOptions).toEqual(['fresh'])
    expect(store.getCatalog('one').modelOptions).toEqual(['fresh'])
  })
})
