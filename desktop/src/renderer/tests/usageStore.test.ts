// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import { useUsageStore } from '../stores/usageStore'
import { useUsageConnectionStore } from '../stores/usageConnectionStore'
import { useConnectionStore } from '../stores/connectionStore'
import { installDesktopApiMock } from './desktopApiMock'

afterEach(() => {
  useUsageStore.getState().reset()
  useUsageConnectionStore.setState({ capable: null })
  useConnectionStore.getState().reset()
})

it('switches history to cost without a token-type breakdown, keeping the token chart until cost arrives', async () => {
  const sendConnectionRequest = vi.fn(async (_key: string, method: string) => {
    if (method === 'usage/summary') return { totalCostUsd: 1.25, unpricedTurns: 3 }
    return { unit: 'usd', groupBy: 'model', days: [], series: [] }
  })
  const sendRequest = vi.fn()
  installDesktopApiMock({ appServer: {
    sendRequest, sendConnectionRequest,
    getUsageConnection: async () => ({ connectionKey: 'local-chats', usageTelemetry: true })
  } })
  useConnectionStore.setState({ capabilities: { usageTelemetry: false } })
  const tokens = { unit: 'tokens' as const, groupBy: 'tokenType', days: [], series: [] }
  useUsageStore.setState({ historyGroup: 'tokenType', history: { data: tokens, loading: false, error: null } })

  useUsageStore.getState().setHistoryMetric('cost')
  expect(useUsageStore.getState().history).toMatchObject({ data: tokens, loading: true })

  await vi.waitFor(() => expect(useUsageStore.getState().costSummary.data).toEqual({ totalCostUsd: 1.25, unpricedTurns: 3 }))
  const state = useUsageStore.getState()
  expect(state.historyMetric).toBe('cost')
  expect(state.historyGroup).toBe('model')
  expect(sendConnectionRequest).toHaveBeenCalledWith('local-chats', 'usage/history', expect.objectContaining({ metric: 'cost', groupBy: 'model' }))
  expect(sendRequest).not.toHaveBeenCalled()
  expect(useUsageConnectionStore.getState().capable).toBe(true)
})
