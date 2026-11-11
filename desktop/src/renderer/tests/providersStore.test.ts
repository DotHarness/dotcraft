// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import type { KnownNotificationPayload } from '../../shared/appServerBoundary'
import { useProvidersStore } from '../stores/providersStore'
import { workspaceResourceKey, type WorkspaceResourceScope } from '../stores/workspaceResourceScope'
import { installDesktopApiMock } from './desktopApiMock'

const a: WorkspaceResourceScope = { projectKey: '/a', connectionKey: '/a', connectionId: 'a-1' }
const b: WorkspaceResourceScope = { projectKey: '/b', connectionKey: '/b', connectionId: 'b-1' }

afterEach(() => useProvidersStore.getState().reset())

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function activate(scope = a) {
  const store = useProvidersStore.getState()
  store.activate(scope)
  return store
}

function installRequest(sendConnectionRequest = vi.fn().mockResolvedValue({})) {
  installDesktopApiMock({ appServer: { sendConnectionRequest, onNotification: () => () => undefined } })
  return sendConnectionRequest
}

it('restores each live workspace list without restoring stale usage', async () => {
  const request = installRequest(vi.fn().mockImplementation(async (key, method) => method === 'provider/list'
    ? { managedBy: key, providers: [{ id: 'shared', displayName: key, hasApiKey: true, backendSettings: { region: key } }] }
    : { available: true, values: { usage: 1 } }))
  const store = activate()
  await store.reload()
  await store.refreshUsage('shared')
  store.activate(b)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'idle', providers: [], usage: {} })
  await store.reload()
  expect(useProvidersStore.getState().providers[0].displayName).toBe('/b')

  store.activate(a)
  expect(useProvidersStore.getState()).toMatchObject({
    status: 'ready', providers: [{ id: 'shared', displayName: '/a' }], usage: {}
  })
  expect(useProvidersStore.getState().listResult).toEqual({
    managedBy: '/a', providers: [{ id: 'shared', displayName: '/a', hasApiKey: true, backendSettings: { region: '/a' } }]
  })
  expect(request).toHaveBeenCalledTimes(3)
  expect(request).toHaveBeenCalledWith('/a', 'provider/list', {}, 15_000, { connectionId: 'a-1' })
  expect(request).toHaveBeenCalledWith('/a', 'provider/auth/usage', { providerId: 'shared' }, 30_000, { connectionId: 'a-1' })
})

it('keeps a successful list ready during refresh and failure, then accepts retry', async () => {
  const pending = deferred<unknown>()
  const request = installRequest(vi.fn()
    .mockResolvedValueOnce({ providers: [{ id: 'cached' }] })
    .mockReturnValueOnce(pending.promise)
    .mockResolvedValueOnce({ providers: [{ id: 'updated' }] }))
  const store = activate()
  await store.reload()
  const refresh = store.reload()
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', providers: [{ id: 'cached' }] })
  pending.reject(new Error('network unavailable'))
  await refresh
  expect(useProvidersStore.getState()).toMatchObject({
    status: 'ready', error: 'network unavailable', providers: [{ id: 'cached' }],
    listResult: { providers: [{ id: 'cached' }] }
  })

  await store.reload()
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', error: null, providers: [{ id: 'updated' }] })
  expect(request).toHaveBeenCalledTimes(3)
})

it('exposes a failed cold list as retryable instead of stuck loading', async () => {
  installRequest(vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({ providers: [{ id: 'retried' }] }))
  const store = activate()
  await store.reload()
  expect(useProvidersStore.getState()).toMatchObject({ status: 'error', error: 'offline' })
  await store.reload()
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', error: null, providers: [{ id: 'retried' }] })
})

it('shares an in-flight list across detachment and reactivation without affecting another scope', async () => {
  const pending = deferred<unknown>()
  const request = installRequest(vi.fn().mockReturnValueOnce(pending.promise).mockResolvedValueOnce({ providers: [{ id: 'b' }] }))
  const store = activate()
  const original = store.reload()
  store.activate(null)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'idle', providers: [] })
  store.activate(b)
  await store.reload()
  pending.resolve({ providers: [{ id: 'a' }] })
  await original
  expect(useProvidersStore.getState().providers).toMatchObject([{ id: 'b' }])
  store.activate(a)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', providers: [{ id: 'a' }] })
  expect(request).toHaveBeenCalledTimes(2)
})

it('deduplicates list reads and preserves them for repeated activation of the same scope', async () => {
  const pending = deferred<unknown>()
  const request = installRequest(vi.fn().mockReturnValue(pending.promise))
  const store = activate()
  const first = store.reload()
  store.activate({ ...a })
  const second = store.reload()
  expect(second).toBe(first)
  expect(request).toHaveBeenCalledOnce()
  pending.resolve({ providers: [{ id: 'a' }] })
  await Promise.all([first, second])
  expect(useProvidersStore.getState().status).toBe('ready')
})

it.each(['success', 'failure'] as const)('isolates pruned list %s and cleanup from a replacement connection', async outcome => {
  const old = deferred<unknown>()
  const current = deferred<unknown>()
  const request = installRequest(vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise))
  const store = activate()
  const previousLoad = store.reload()
  const replacement = { ...a, connectionId: 'a-2' }
  store.activate(replacement)
  store.prune(new Set([workspaceResourceKey(replacement)]))
  const currentLoad = store.reload()

  if (outcome === 'success') old.resolve({ providers: [{ id: 'previous' }] })
  else old.reject(new Error('old connection closed'))
  await previousLoad
  expect(useProvidersStore.getState()).toMatchObject({ status: 'loading', providers: [] })
  const duplicateLoad = store.reload()
  expect(request).toHaveBeenCalledTimes(2)
  current.resolve({ providers: [{ id: 'current' }] })
  await Promise.all([currentLoad, duplicateLoad])
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', providers: [{ id: 'current' }] })
  store.activate(a)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'idle', providers: [] })
})

it('evicts a previous connection lifetime immediately without waiting for a project snapshot', async () => {
  const pending = deferred<unknown>()
  installRequest(vi.fn().mockResolvedValueOnce({ providers: [{ id: 'old' }] }).mockReturnValueOnce(pending.promise))
  const store = activate()
  await store.reload()
  const refresh = store.reload()
  store.activate({ ...a, connectionId: 'a-2' })
  expect(useProvidersStore.getState()).toMatchObject({ status: 'idle', providers: [] })
  pending.resolve({ providers: [{ id: 'late-old' }] })
  await refresh

  store.activate(a)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'idle', providers: [] })
})

it('does not discard an active list because a project snapshot lags behind activation', async () => {
  installRequest(vi.fn().mockResolvedValue({ providers: [{ id: 'current' }] }))
  const store = activate()
  await store.reload()
  store.prune(new Set())
  store.activate(null)
  store.activate(a)
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', providers: [{ id: 'current' }] })
})

it('refreshes current-key spending and balances through the shared turn subscription', async () => {
  let notify: (payload: KnownNotificationPayload) => void = () => undefined
  const unsubscribe = vi.fn()
  const request = vi.fn().mockResolvedValue({
    available: true, labels: { scope: 'apiKey' }, values: { usage: 1, last_request_cost: 0.1 }
  })
  installDesktopApiMock({ appServer: {
    sendConnectionRequest: request,
    onNotification: callback => { notify = callback; return unsubscribe }
  } })
  const store = activate()
  await store.refreshUsage('router')
  store.applyUsage('balance', { ...useProvidersStore.getState().usage.router, labels: undefined,
    balances: [{ currency: 'USD', total: '10', granted: '0', toppedUp: '10' }] })
  store.applyUsage('subscription', { ...useProvidersStore.getState().usage.router, labels: undefined, balances: null })
  request.mockClear()
  request.mockResolvedValue({
    available: true, labels: { scope: 'apiKey' }, values: { usage: 2, last_request_cost: 0.25 }
  })
  notify({ method: 'turn/completed', params: { turn: {
    id: 'turn', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T00:00:00Z'
  } } })
  await Promise.all(request.mock.results.map(result => result.value))
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(2)
  expect(useProvidersStore.getState().usage.router.values?.last_request_cost).toBe(0.25)
  expect(request.mock.calls.map(([, , params]) => params.providerId).sort()).toEqual(['balance', 'router'])
  store.reset()
  expect(unsubscribe).toHaveBeenCalledOnce()
})

it('isolates old usage requests even when returning to the same cached list', async () => {
  const old = deferred<unknown>()
  const current = deferred<unknown>()
  const request = installRequest(vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise))
  const store = activate()
  const previousLoad = store.refreshUsage('router')
  store.activate(b)
  store.activate(a)
  const currentLoad = store.refreshUsage('router')
  old.resolve({ available: true, values: { usage: 99 } })
  await previousLoad
  expect(useProvidersStore.getState().usage).toEqual({})
  const duplicateLoad = store.refreshUsage(' ROUTER ')
  expect(request).toHaveBeenCalledTimes(2)
  current.resolve({ available: true, values: { usage: 2 } })
  await Promise.all([currentLoad, duplicateLoad])
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(2)
})

it('ignores replaced, background and mismatched-connection usage notifications', async () => {
  const listeners: Array<(payload: KnownNotificationPayload) => void> = []
  installDesktopApiMock({ appServer: {
    sendConnectionRequest: vi.fn().mockResolvedValue({ available: true, values: { usage: 1 } }),
    onNotification: callback => { listeners.push(callback); return () => undefined }
  } })
  const store = activate()
  await store.refreshUsage('router')
  store.activate(b)
  await store.refreshUsage('router')
  const usageUpdate = { method: 'provider/auth/usage/updated', params: {
    providerId: 'router', available: true, values: { usage: 99 }
  } } as KnownNotificationPayload

  listeners[0](usageUpdate)
  listeners[1]({ ...usageUpdate, foreground: false })
  listeners[1]({ ...usageUpdate, connectionKey: '/a', connectionId: 'a-1' })
  listeners[1]({ ...usageUpdate, connectionKey: '/b', connectionId: 'old-b' })
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(1)
  listeners[1]({ ...usageUpdate, connectionKey: '/b', connectionId: 'b-1' })
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(99)
})

it('does not issue requests or accept usage without an active scope', async () => {
  const request = installRequest()
  const store = useProvidersStore.getState()
  await Promise.all([store.reload(), store.refreshUsage('router')])
  store.applyUsage('router', { available: false, planType: null, primary: null, secondary: null,
    credits: null, balances: null, limitReachedKind: null, fetchedAt: null, pluginId: null })
  expect(request).not.toHaveBeenCalled()
  expect(useProvidersStore.getState().usage).toEqual({})
})
