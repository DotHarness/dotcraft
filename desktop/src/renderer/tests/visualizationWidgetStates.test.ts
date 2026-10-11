import { afterEach, describe, expect, it, vi } from 'vitest'
import { saveVisualizationWidgetState, subscribeVisualizationWidgetState } from '../components/conversation/visualizationWidgetStates'
import type { VisualizationWidgetState } from '../components/conversation/visualizationSecurity'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('visualization widget state saves', () => {
  it('sends and publishes consecutive saves of one visualization in order', async () => {
    const pending: Array<() => void> = []
    const sendRequest = vi.fn(() => new Promise<void>(resolve => pending.push(resolve)))
    vi.stubGlobal('window', { api: { appServer: { sendRequest, onNotification: () => () => {} } } })
    const seen: Array<VisualizationWidgetState | null> = []
    const unsubscribe = subscribeVisualizationWidgetState('t1', '/w/chart.html', 'sibling', state => seen.push(state))
    const first = { modelContent: { rate: 1 }, privateContent: null }
    const second = { modelContent: { rate: 2 }, privateContent: null }

    const saves = [
      saveVisualizationWidgetState('t1', '/w/chart.html', 'source', 'view-1', first),
      saveVisualizationWidgetState('t1', '/w/chart.html', 'source', 'view-1', second)
    ]
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(sendRequest).toHaveBeenCalledTimes(1)

    pending[0]()
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(2))
    pending[1]()
    await Promise.all(saves)

    expect(sendRequest.mock.calls.map(call => (call as unknown[])[1])).toEqual([{ viewHandle: 'view-1', ...first }, { viewHandle: 'view-1', ...second }])
    expect(seen).toEqual([first, second])
    unsubscribe()
  })
})
