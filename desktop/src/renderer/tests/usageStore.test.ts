// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest'
import { useUsageStore } from '../stores/usageStore'
import { installDesktopApiMock } from './desktopApiMock'

afterEach(() => useUsageStore.getState().reset())

it('switches history to cost without a token-type breakdown, keeping the token chart until cost arrives', async () => {
  const sendRequest = vi.fn(async (method: string) => {
    if (method === 'usage/summary') return { totalCostUsd: 1.25, unpricedTurns: 3 }
    return { unit: 'usd', groupBy: 'model', days: [], series: [] }
  })
  installDesktopApiMock({ appServer: { sendRequest } })
  const tokens = { unit: 'tokens' as const, groupBy: 'tokenType', days: [], series: [] }
  useUsageStore.setState({ historyGroup: 'tokenType', history: { data: tokens, loading: false, error: null } })

  useUsageStore.getState().setHistoryMetric('cost')
  expect(useUsageStore.getState().history).toMatchObject({ data: tokens, loading: true })

  await vi.waitFor(() => expect(useUsageStore.getState().costSummary.data).toEqual({ totalCostUsd: 1.25, unpricedTurns: 3 }))
  const state = useUsageStore.getState()
  expect(state.historyMetric).toBe('cost')
  expect(state.historyGroup).toBe('model')
  expect(sendRequest).toHaveBeenCalledWith('usage/history', expect.objectContaining({ metric: 'cost', groupBy: 'model' }))
})
