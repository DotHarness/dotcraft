import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { disposeSummaryFeeds, useSummaryState } from '../components/conversation/summary/store'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'

function createHost() {
  const request = vi.fn(async (method: string) => {
    if (method === 'thread/items/list') return { data: [], nextCursor: null }
    return {}
  })
  const services = {
    subagents: { list: () => [], onChange: () => () => undefined },
    appServer: { request, onNotification: () => () => undefined }
  } as unknown as SummaryServices
  return { services, request }
}

async function settle(): Promise<void> {
  await act(async () => {
    for (let index = 0; index < 5; index += 1) await Promise.resolve()
  })
}

afterEach(() => disposeSummaryFeeds())

describe('Summary feed cache', () => {
  it('shows the loaded state at once when the Summary reopens, without reading the thread again', async () => {
    const { services, request } = createHost()
    const first = renderHook(() => useSummaryState(services, 'thread-1'))
    await settle()
    expect(first.result.current?.ready).toBe(true)
    const reads = request.mock.calls.length
    first.unmount()

    const second = renderHook(() => useSummaryState(services, 'thread-1'))
    expect(second.result.current?.ready).toBe(true)
    await settle()
    expect(request.mock.calls.length).toBe(reads)
    second.unmount()
  })

  it('stops the least recently used hidden feeds beyond the limit', async () => {
    const { services, request } = createHost()
    for (const threadId of ['a', 'b', 'c', 'd', 'e']) renderHook(() => useSummaryState(services, threadId)).unmount()
    await settle()
    const reads = request.mock.calls.length

    renderHook(() => useSummaryState(services, 'a')).unmount()
    renderHook(() => useSummaryState(services, 'e')).unmount()
    await settle()
    const reread = request.mock.calls.slice(reads)
      .filter(([method]) => method !== 'automation/list')
      .map(([, params]) => (params as { threadId?: string }).threadId)
    expect(new Set(reread)).toEqual(new Set(['a']))
  })
})
