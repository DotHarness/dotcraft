// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import type { KnownNotificationPayload } from '../../shared/appServerBoundary'
import { useProvidersStore } from '../stores/providersStore'
import { installDesktopApiMock } from './desktopApiMock'

afterEach(() => useProvidersStore.getState().reset())

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

it('refreshes current-key spending and balances through the shared turn subscription', async () => {
  let notify: (payload: KnownNotificationPayload) => void = () => undefined
  const unsubscribe = vi.fn()
  const sendRequest = vi.fn().mockResolvedValue({
    available: true, labels: { scope: 'apiKey' }, values: { usage: 1, last_request_cost: 0.1 }
  })
  installDesktopApiMock({ appServer: {
    sendRequest,
    onNotification: (callback) => { notify = callback; return unsubscribe }
  } })
  const store = useProvidersStore.getState()
  await store.refreshUsage('router')
  store.applyUsage('balance', { ...useProvidersStore.getState().usage.router, labels: undefined,
    balances: [{ currency: 'USD', total: '10', granted: '0', toppedUp: '10' }] })
  store.applyUsage('subscription', { ...useProvidersStore.getState().usage.router, labels: undefined, balances: null })
  sendRequest.mockClear()
  sendRequest.mockResolvedValue({
    available: true, labels: { scope: 'apiKey' }, values: { usage: 2, last_request_cost: 0.25 }
  })
  notify({ method: 'turn/completed', params: { turn: {
    id: 'turn', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T00:00:00Z'
  } } })
  await vi.waitFor(() => expect(useProvidersStore.getState().usage.router.values?.usage).toBe(2))
  expect(useProvidersStore.getState().usage.router.values?.last_request_cost).toBe(0.25)
  expect(sendRequest.mock.calls.map(([, params]) => params.providerId).sort()).toEqual(['balance', 'router'])
  store.reset()
  expect(unsubscribe).toHaveBeenCalledOnce()
})

it.each(['success', 'failure'] as const)('isolates old provider list %s and cleanup after reset', async (outcome) => {
  const old = deferred<unknown>()
  const current = deferred<unknown>()
  const sendRequest = vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  installDesktopApiMock({ appServer: { sendRequest } })
  const store = useProvidersStore.getState()
  const previousLoad = store.reload()
  store.reset()
  const currentLoad = store.reload()

  if (outcome === 'success') old.resolve({ providers: [{ id: 'previous' }] })
  else old.reject(new Error('old connection closed'))
  await previousLoad

  expect(useProvidersStore.getState()).toMatchObject({ status: 'loading', providers: [] })
  const duplicateLoad = store.reload()
  expect(sendRequest).toHaveBeenCalledTimes(2)
  current.resolve({ providers: [{ id: 'current', protocol: 'openai' }] })
  await Promise.all([currentLoad, duplicateLoad])
  expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', providers: [{ id: 'current' }] })
})

it('isolates old usage results and cleanup after reset', async () => {
  const old = deferred<unknown>()
  const current = deferred<unknown>()
  const sendRequest = vi.fn().mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  installDesktopApiMock({ appServer: { sendRequest, onNotification: () => () => undefined } })
  const store = useProvidersStore.getState()
  const previousLoad = store.refreshUsage('router')
  store.reset()
  const currentLoad = store.refreshUsage('router')

  old.resolve({ available: true, values: { usage: 99 } })
  await previousLoad

  expect(useProvidersStore.getState().usage).toEqual({})
  const duplicateLoad = store.refreshUsage(' ROUTER ')
  expect(sendRequest).toHaveBeenCalledTimes(2)
  current.resolve({ available: true, values: { usage: 2 } })
  await Promise.all([currentLoad, duplicateLoad])
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(2)
})

it('keeps the new workspace data when old requests finish last', async () => {
  const oldList = deferred<unknown>()
  const oldUsage = deferred<unknown>()
  const sendRequest = vi.fn()
    .mockReturnValueOnce(oldList.promise)
    .mockReturnValueOnce(oldUsage.promise)
    .mockResolvedValueOnce({ providers: [{ id: 'current' }] })
    .mockResolvedValueOnce({ available: true, values: { usage: 2 } })
  installDesktopApiMock({ appServer: { sendRequest, onNotification: () => () => undefined } })
  const store = useProvidersStore.getState()
  const previous = [store.reload(), store.refreshUsage('router')]
  store.reset()
  await Promise.all([store.reload(), store.refreshUsage('router')])

  oldList.resolve({ providers: [{ id: 'previous' }] })
  oldUsage.resolve({ available: true, values: { usage: 99 } })
  await Promise.all(previous)

  expect(useProvidersStore.getState().providers).toMatchObject([{ id: 'current' }])
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(2)
})

it('ignores a replaced usage listener and background workspace notifications', async () => {
  const listeners: Array<(payload: KnownNotificationPayload) => void> = []
  installDesktopApiMock({ appServer: {
    sendRequest: vi.fn().mockResolvedValue({ available: true, values: { usage: 1 } }),
    onNotification: (callback) => { listeners.push(callback); return () => undefined }
  } })
  const store = useProvidersStore.getState()
  await store.refreshUsage('router')
  store.reset()
  await store.refreshUsage('router')
  const usageUpdate = { method: 'provider/auth/usage/updated', params: {
    providerId: 'router', available: true, values: { usage: 99 }
  } } as KnownNotificationPayload

  listeners[0](usageUpdate)
  listeners[1]({ ...usageUpdate, foreground: false })
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(1)
  listeners[1](usageUpdate)
  expect(useProvidersStore.getState().usage.router.values?.usage).toBe(99)
})
