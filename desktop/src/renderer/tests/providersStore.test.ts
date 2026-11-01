// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import type { KnownNotificationPayload } from '../../shared/appServerBoundary'
import { useProvidersStore } from '../stores/providersStore'
import { installDesktopApiMock } from './desktopApiMock'

afterEach(() => useProvidersStore.getState().reset())

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
