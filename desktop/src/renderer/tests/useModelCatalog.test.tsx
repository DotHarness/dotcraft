import { act, renderHook, waitFor } from '@testing-library/react'
import { useLayoutEffect } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useModelCatalog } from '../hooks/useModelCatalog'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { installDesktopApiMock } from './desktopApiMock'

const listModels = vi.fn()
const scopeA = { projectKey: '/a', connectionKey: '/a', connectionId: 'a-1' }
const scopeB = { projectKey: '/b', connectionKey: '/b', connectionId: 'b-1' }

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

function response(providerId: string, modelId: string, effort: 'high' | 'max') {
  return {
    success: true, providerId,
    models: [{
      id: modelId,
      reasoning: {
        supportsDisable: true,
        supportedEfforts: [{ effort }],
        defaultEffort: effort,
      },
    }],
  }
}

describe('provider-scoped model catalogs', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useModelCatalogStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      connectionKey: scopeA.connectionKey,
      connectionId: scopeA.connectionId,
      capabilities: { modelCatalogManagement: true },
    })
    installDesktopApiMock({ appServer: {
      sendConnectionRequest: vi.fn((_key, _method, params) => listModels((params as { providerId?: string }).providerId ?? null))
    } })
    useModelCatalogStore.getState().activate(scopeA)
  })

  it.each([
    { connectionKey: '/b', connectionId: 'b-1' },
    { connectionKey: '/a', connectionId: 'a-2' }
  ])('waits for the activated scope to match connection $connectionKey/$connectionId', async connection => {
    useConnectionStore.setState(connection)
    listModels.mockResolvedValue(response('one', 'matched', 'high'))
    const hook = renderHook(() => useModelCatalog('one'))
    expect(window.api.appServer.sendConnectionRequest).not.toHaveBeenCalled()
    act(() => useModelCatalogStore.getState().activate({
      projectKey: connection.connectionKey, ...connection
    }))
    await waitFor(() => expect(hook.result.current.modelOptions).toEqual(['matched']))
    expect(window.api.appServer.sendConnectionRequest).toHaveBeenCalledExactlyOnceWith(
      connection.connectionKey, 'model/list', { providerId: 'one' }, 20_000, { connectionId: connection.connectionId }
    )
  })

  it('does not run an old scope effect after activation changes before passive effects', async () => {
    listModels.mockResolvedValue(response('one', 'b-model', 'high'))
    const hook = renderHook(() => {
      const catalog = useModelCatalog('one')
      useLayoutEffect(() => { useModelCatalogStore.getState().activate(scopeB) }, [])
      return catalog
    })
    expect(window.api.appServer.sendConnectionRequest).not.toHaveBeenCalled()
    act(() => useConnectionStore.setState({ connectionKey: scopeB.connectionKey, connectionId: scopeB.connectionId }))
    await waitFor(() => expect(hook.result.current.modelOptions).toEqual(['b-model']))
    expect(window.api.appServer.sendConnectionRequest).toHaveBeenCalledExactlyOnceWith(
      '/b', 'model/list', { providerId: 'one' }, 20_000, { connectionId: 'b-1' }
    )
  })

  it('keeps simultaneous provider consumers independent when responses arrive out of order', async () => {
    const pending = { anthropic: deferred<unknown>(), deepseek: deferred<unknown>() }
    listModels.mockImplementation((providerId: 'anthropic' | 'deepseek') => pending[providerId].promise)
    const anthropic = renderHook(() => useModelCatalog('anthropic'))
    const deepseek = renderHook(() => useModelCatalog('deepseek'))
    const payloads = {
      anthropic: response('anthropic', 'claude-opus-5-5', 'high'),
      deepseek: response('deepseek', 'deepseek-v4-pro', 'max'),
    }
    await act(async () => { pending.deepseek.resolve(payloads.deepseek) })
    expect(deepseek.result.current.status).toBe('ready')
    expect(anthropic.result.current.status).toBe('loading')
    await act(async () => { pending.anthropic.resolve(payloads.anthropic) })

    expect(anthropic.result.current).toMatchObject({
      providerId: 'anthropic', modelOptions: ['claude-opus-5-5'],
      models: [{ id: 'claude-opus-5-5', reasoning: { defaultEffort: 'high' } }],
    })
    expect(deepseek.result.current).toMatchObject({
      providerId: 'deepseek', modelOptions: ['deepseek-v4-pro'],
      models: [{ id: 'deepseek-v4-pro', reasoning: { defaultEffort: 'max' } }],
    })
  })

  it('reloads mounted consumers on provider invalidation without applying pre-invalidation results', async () => {
    const old = deferred<unknown>()
    const current = deferred<unknown>()
    listModels.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    const hook = renderHook(() => useModelCatalog('anthropic'))
    expect(listModels).toHaveBeenCalledTimes(1)

    act(() => useModelCatalogStore.getState().handleConfigChanged(['providers']))
    await waitFor(() => expect(listModels).toHaveBeenCalledTimes(2))
    await act(async () => { old.resolve(response('anthropic', 'stale', 'max')) })
    expect(hook.result.current).toMatchObject({ status: 'loading', modelOptions: [] })

    await act(async () => { current.resolve(response('anthropic', 'fresh', 'high')) })
    expect(hook.result.current).toMatchObject({
      status: 'ready', modelOptions: ['fresh'],
      models: [{ id: 'fresh', reasoning: { defaultEffort: 'high' } }],
    })
  })

  it('moves a consumer to its new provider without awaiting the previous request', async () => {
    const old = deferred<unknown>()
    listModels.mockImplementation((providerId) => providerId === 'anthropic'
      ? old.promise : Promise.resolve(response('deepseek', 'deepseek-v4-pro', 'max')))
    const hook = renderHook(({ providerId }) => useModelCatalog(providerId), {
      initialProps: { providerId: 'anthropic' },
    })
    hook.rerender({ providerId: 'deepseek' })
    await waitFor(() => expect(hook.result.current.providerId).toBe('deepseek'))
    await act(async () => { old.resolve(response('anthropic', 'claude', 'high')) })
    expect(hook.result.current).toMatchObject({
      providerId: 'deepseek', modelOptions: ['deepseek-v4-pro'],
      models: [{ reasoning: { defaultEffort: 'max' } }],
    })
  })

})
