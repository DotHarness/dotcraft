import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { disposeSummaryFeeds, useRunningTaskCount, useSummaryState } from '../components/conversation/summary/store'
import { desktopSummaryServices, type SummaryServices } from '../components/conversation/summary/summaryServices'
import { useConversationStore } from '../stores/conversationStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'

function createHost() {
  const notifications = new Map<string, (params: unknown) => void>()
  const request = vi.fn(async (method: string) => {
    if (method === 'thread/items/list') return { data: [], nextCursor: null }
    return {}
  })
  const services = {
    subagents: { list: () => [], onChange: () => () => undefined },
    appServer: {
      request, connectionKey: () => 'connection', onConnectionChange: () => () => undefined,
      onNotification(method: string, listener: (params: unknown) => void) {
        notifications.set(method, listener)
        return () => notifications.delete(method)
      }
    }
  } as unknown as SummaryServices
  return { services, request, notify: (method: string, params: unknown) => notifications.get(method)?.(params) }
}

async function settle(): Promise<void> {
  await act(async () => {
    for (let index = 0; index < 5; index += 1) await Promise.resolve()
  })
}

afterEach(() => {
  disposeSummaryFeeds()
  useConversationStore.getState().setWorkspacePath('')
  useWorkspaceProjectsStore.getState().reset()
})

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
    expect(request.mock.calls.slice(reads).map(([method]) => method)).toEqual(['terminal/list'])
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
      .filter(([method]) => method === 'thread/read')
      .map(([, params]) => (params as { threadId?: string }).threadId)
    expect(new Set(reread)).toEqual(new Set(['a']))
  })

  it('reconciles cached terminals on reopen even while a badge keeps the feed subscribed', async () => {
    const { services, request } = createHost()
    let terminals = [{ sessionId: 'npx', threadId: 'thread-1', turnId: 't1', status: 'running' }]
    request.mockImplementation(async (method) => method === 'terminal/list' ? { terminals } : {})
    const badge = renderHook(() => useRunningTaskCount(services, 'thread-1'))
    const first = renderHook(() => useSummaryState(services, 'thread-1'))
    await settle()
    expect(badge.result.current).toBe(1)
    first.unmount()
    terminals = []
    const second = renderHook(() => useSummaryState(services, 'thread-1'))
    await settle()
    expect(second.result.current!.terminals).toEqual([])
    expect(badge.result.current).toBe(0)
  })

  it('keeps worktree updates live and isolates cached threads by the Main-owned connection key', async () => {
    const { services, request, notify } = createHost()
    useWorkspaceProjectsStore.setState({ foregroundProjectId: 'project-a' })
    useConversationStore.getState().setWorkspacePath('/checkout')
    services.appServer.connectionKey = desktopSummaryServices.appServer.connectionKey
    services.appServer.onConnectionChange = desktopSummaryServices.appServer.onConnectionChange
    request.mockImplementation(async (method) => method === 'terminal/list'
      ? { terminals: [{ sessionId: services.appServer.connectionKey(), threadId: 'thread-1', status: 'running' }] } : {})
    const first = renderHook(() => useSummaryState(services, 'thread-1'))
    await settle()
    expect(first.result.current!.terminals[0].sessionId).toBe('project-a')

    await act(async () => {
      useConversationStore.getState().setWorkspacePath('/checkout-worktree')
      notify('terminal/completed', { terminal: { sessionId: 'project-a', threadId: 'thread-1', status: 'completed' } })
    })
    expect(first.result.current!.terminals).toEqual([])
    first.unmount()
    const cached = renderHook(() => useSummaryState(services, 'thread-1'))
    expect(cached.result.current!.ready).toBe(true)
    await settle()
    expect(cached.result.current!.terminals).toEqual([])
    expect(request.mock.calls.filter(([method]) => method === 'thread/items/list')).toHaveLength(1)
    cached.unmount()

    useWorkspaceProjectsStore.setState({ foregroundProjectId: 'project-b' })
    const second = renderHook(() => useSummaryState(services, 'thread-1'))
    await settle()
    expect(second.result.current!.terminals.map((entry) => entry.sessionId)).toEqual(['project-b'])
  })
})
