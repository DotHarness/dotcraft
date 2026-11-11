// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { installDesktopApiMock } from './desktopApiMock'
import { workspaceResourceKey } from '../stores/workspaceResourceScope'

const scopeA = { projectKey: '/a', connectionKey: '/a', connectionId: 'a-1' }
const scopeB = { projectKey: '/b', connectionKey: '/b', connectionId: 'b-1' }
const appServerListModels = vi.fn()
const sendConnectionRequest = vi.fn((_key, _method, params) => params.refresh
  ? appServerListModels(params.providerId ?? null, true)
  : appServerListModels(params.providerId ?? null))

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

describe('modelCatalogStore', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useModelCatalogStore.getState().reset()
    sendConnectionRequest.mockImplementation((_key, _method, params) => params.refresh
      ? appServerListModels(params.providerId ?? null, true)
      : appServerListModels(params.providerId ?? null))
    installDesktopApiMock({ appServer: { sendConnectionRequest } })
    useModelCatalogStore.getState().activate(scopeA)
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

  it('reports model/list request errors', async () => {
    appServerListModels.mockRejectedValueOnce(new Error('proxy unavailable'))

    await useModelCatalogStore.getState().loadIfNeeded()

    expect(useModelCatalogStore.getState().getCatalog()).toMatchObject({
      status: 'error',
      modelOptions: [],
      modelListUnsupportedEndpoint: false,
      errorMessage: 'proxy unavailable'
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
    expect(store.getCatalog('one')).toMatchObject({ status: 'ready', modelOptions: ['new'] })
    await store.loadIfNeeded(false, 'one')
    expect(appServerListModels).toHaveBeenCalledTimes(3)
  })

  it('does not request a catalog until a workspace scope is activated', async () => {
    const store = useModelCatalogStore.getState()
    store.activate(null)
    expect(await store.loadIfNeeded(false, 'one')).toBeNull()
    expect(await store.revalidate('one')).toBeNull()
    expect(sendConnectionRequest).not.toHaveBeenCalled()
  })

  it('restores independent same-provider catalogs when returning to a live workspace', async () => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'a-model' }] })
      .mockResolvedValueOnce({ success: true, models: [{ id: 'b-model' }] })
    await store.loadIfNeeded(false, 'one')
    store.activate(null)
    store.activate(scopeB)
    expect(store.getCatalog('one').status).toBe('idle')
    await store.loadIfNeeded(false, 'one')
    expect(store.getCatalog('one').modelOptions).toEqual(['b-model'])
    store.activate(scopeA)
    expect(store.getCatalog('one').modelOptions).toEqual(['a-model'])
    await store.loadIfNeeded(false, 'one')
    expect(sendConnectionRequest).toHaveBeenCalledTimes(2)
    expect(sendConnectionRequest).toHaveBeenNthCalledWith(1, '/a', 'model/list', { providerId: 'one' }, 20_000, { connectionId: 'a-1' })
    expect(sendConnectionRequest).toHaveBeenNthCalledWith(2, '/b', 'model/list', { providerId: 'one' }, 20_000, { connectionId: 'b-1' })
  })

  it('keeps a usable catalog while revalidating and after a failed refresh', async () => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'cached' }] })
    await store.loadIfNeeded(false, 'one')
    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const refresh = store.revalidate('one')
    expect(store.getCatalog('one')).toMatchObject({ status: 'ready', modelOptions: ['cached'] })
    pending.resolve({ success: false, errorCode: 'Network', errorMessage: 'offline' })
    await refresh
    expect(store.getCatalog('one')).toMatchObject({
      status: 'ready', modelOptions: ['cached'], errorCode: 'Network', errorMessage: 'offline'
    })
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'fresh' }] })
    await store.revalidate('one')
    expect(store.getCatalog('one')).toMatchObject({
      status: 'ready', modelOptions: ['fresh'], errorCode: null, errorMessage: null
    })
    expect(sendConnectionRequest.mock.calls.every(([, , params]) => !params.refresh)).toBe(true)
    appServerListModels.mockResolvedValueOnce({ success: true, models: [] })
    await store.loadIfNeeded(true, 'one')
    expect(sendConnectionRequest).toHaveBeenLastCalledWith('/a', 'model/list', { providerId: 'one', refresh: true }, 20_000, { connectionId: 'a-1' })
  })

  it('updates a secondary workspace cache without replacing the foreground catalog', async () => {
    const store = useModelCatalogStore.getState()
    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const loadA = store.loadIfNeeded(false, 'one')
    store.activate(scopeB)
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'b-model' }] })
    await store.loadIfNeeded(false, 'one')
    pending.resolve({ success: true, models: [{ id: 'a-model' }] })
    await loadA
    expect(store.getCatalog('one').modelOptions).toEqual(['b-model'])
    store.activate(scopeA)
    expect(store.getCatalog('one').modelOptions).toEqual(['a-model'])
  })

  it('discards snapshots and pending responses from replaced connection lifetimes', async () => {
    const store = useModelCatalogStore.getState()
    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const load = store.loadIfNeeded(false, 'one')
    store.activate({ ...scopeA, connectionId: 'a-2' })
    expect(store.getCatalog('one').status).toBe('idle')
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'new-lifetime' }] })
    await store.loadIfNeeded(false, 'one')
    pending.resolve({ success: true, models: [{ id: 'old-lifetime' }] })
    expect(await load).toBeNull()
    expect(store.getCatalog('one').modelOptions).toEqual(['new-lifetime'])
  })

  it('prunes inactive caches and rejects their pending results while retaining the active cache', async () => {
    const store = useModelCatalogStore.getState()
    const pending = createDeferred<unknown>()
    appServerListModels.mockReturnValueOnce(pending.promise)
    const load = store.loadIfNeeded(false, 'one')
    store.activate(scopeB)
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'retained' }] })
    await store.loadIfNeeded(false, 'one')
    store.prune(new Set())
    expect(store.getCatalog('one').modelOptions).toEqual(['retained'])
    pending.resolve({ success: true, models: [{ id: 'evicted' }] })
    expect(await load).toBeNull()
    store.activate(scopeA)
    expect(store.getCatalog('one').status).toBe('idle')
    store.prune(new Set([workspaceResourceKey(scopeA)]))
    store.activate(scopeB)
    expect(store.getCatalog('one').status).toBe('idle')
  })

  it.each([null, 'one'])('refreshes a derived default/provider alias when revalidating %s', async key => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValueOnce({ success: true, providerId: 'one', models: [{ id: 'cached' }] })
      .mockResolvedValueOnce({ success: true, providerId: 'one', models: [{ id: 'fresh' }] })
    await store.loadIfNeeded()
    expect(store.getCatalog('one').modelOptions).toEqual(['cached'])
    await store.revalidate(key)
    expect(store.getCatalog().modelOptions).toEqual(['fresh'])
    expect(store.getCatalog('one').modelOptions).toEqual(['fresh'])
  })

  it('keeps the previous provider catalog when the default resolves to a different provider', async () => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValueOnce({ success: true, providerId: 'one', models: [{ id: 'one-model' }] })
      .mockResolvedValueOnce({ success: true, providerId: 'two', models: [{ id: 'two-model' }] })
    await store.loadIfNeeded()
    await store.revalidate()
    expect(store.getCatalog('one').modelOptions).toEqual(['one-model'])
    expect(store.getCatalog('two').modelOptions).toEqual(['two-model'])
    expect(store.getCatalog().modelOptions).toEqual(['two-model'])
  })

  it('revalidates provider changes only in the current workspace', async () => {
    const store = useModelCatalogStore.getState()
    appServerListModels.mockResolvedValueOnce({ success: true, models: [{ id: 'a-model' }] })
      .mockResolvedValueOnce({ success: true, models: [{ id: 'b-model' }] })
      .mockResolvedValueOnce({ success: true, models: [{ id: 'new-b-model' }] })
    await store.loadIfNeeded(false, 'one')
    store.activate(scopeB)
    await store.loadIfNeeded(false, 'one')
    store.handleConfigChanged(['providers'])
    await store.loadIfNeeded(false, 'one')
    expect(store.getCatalog('one').modelOptions).toEqual(['new-b-model'])
    store.activate(scopeA)
    expect(store.getCatalog('one').modelOptions).toEqual(['a-model'])
  })
})
