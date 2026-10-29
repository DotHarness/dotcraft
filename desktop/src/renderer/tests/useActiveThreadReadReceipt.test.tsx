import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useActiveThreadReadReceipt } from '../hooks/useActiveThreadReadReceipt'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ThreadReadState, ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()

function summary(id: string, readState: ThreadReadState): ThreadSummary {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-10-29T00:00:00Z',
    lastActiveAt: '2026-10-29T00:00:00Z',
    readState
  }
}

const unreadTurn: ThreadReadState = { firstUnread: { type: 'turn', turnId: 'turn_001' }, revision: 'r1' }
const read: ThreadReadState = { firstUnread: null, revision: 'r2' }

function readRequests(): unknown[] {
  return sendRequest.mock.calls.filter(([method]) => method === 'thread/readState/update').map(([, params]) => params)
}

async function settle(): Promise<void> {
  await act(async () => {
    for (let index = 0; index < 5; index += 1) await Promise.resolve()
  })
}

describe('active thread read receipt', () => {
  beforeEach(() => {
    sendRequest.mockReset()
    sendRequest.mockImplementation(async () => ({ readState: read }))
    installDesktopApiMock({ appServer: { sendRequest } })
    vi.spyOn(document, 'hasFocus').mockReturnValue(true)
    useThreadStore.getState().reset()
    useUIStore.getState().setActiveMainView('conversation')
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('marks an unread thread read with the revision the user saw when it is opened', async () => {
    useThreadStore.getState().setThreadList([summary('a', unreadTurn)])
    renderHook(() => useActiveThreadReadReceipt())

    act(() => useThreadStore.getState().setActiveThreadId('a'))
    await settle()

    expect(readRequests()).toEqual([{ threadId: 'a', expectedRevision: 'r1', operation: { type: 'read' } }])
    expect(useThreadStore.getState().threadList[0].readState).toEqual(read)
  })

  it('keeps an explicit unread mark on the open thread until it is opened again', async () => {
    useThreadStore.getState().setThreadList([summary('a', read), summary('b', read)])
    renderHook(() => useActiveThreadReadReceipt())
    act(() => useThreadStore.getState().setActiveThreadId('a'))
    await settle()

    const marked: ThreadReadState = { firstUnread: { type: 'threadStart' }, revision: 'r3' }
    act(() => useThreadStore.getState().setThreadReadState('a', marked))
    await settle()
    expect(readRequests()).toEqual([])

    act(() => useThreadStore.getState().setActiveThreadId('b'))
    act(() => useThreadStore.getState().setActiveThreadId('a'))
    await settle()
    expect(readRequests()).toEqual([{ threadId: 'a', expectedRevision: 'r3', operation: { type: 'read' } }])
  })

  it('leaves new results unread while the window is unfocused and reads them on focus', async () => {
    useThreadStore.getState().setThreadList([summary('a', read)])
    renderHook(() => useActiveThreadReadReceipt())
    act(() => useThreadStore.getState().setActiveThreadId('a'))
    act(() => { window.dispatchEvent(new Event('blur')) })

    act(() => useThreadStore.getState().setThreadReadState('a', unreadTurn))
    await settle()
    expect(readRequests()).toEqual([])

    act(() => { window.dispatchEvent(new Event('focus')) })
    await settle()
    expect(readRequests()).toEqual([{ threadId: 'a', expectedRevision: 'r1', operation: { type: 'read' } }])
  })

  it('adopts the current receipt when the server reports a conflict', async () => {
    const current: ThreadReadState = { firstUnread: { type: 'threadStart' }, revision: 'r9' }
    sendRequest.mockImplementation(async () => {
      throw { rpcCode: -32600, data: { code: 'readStateConflict', readState: current } }
    })
    useThreadStore.getState().setThreadList([summary('a', unreadTurn)])
    renderHook(() => useActiveThreadReadReceipt())

    act(() => useThreadStore.getState().setActiveThreadId('a'))
    await settle()

    expect(useThreadStore.getState().threadList[0].readState).toEqual(current)
    expect(readRequests()).toHaveLength(1)
  })

  it('retries a read mark that failed when the thread is seen again', async () => {
    sendRequest.mockImplementationOnce(async () => { throw new Error('connection lost') })
    useThreadStore.getState().setThreadList([summary('a', unreadTurn)])
    renderHook(() => useActiveThreadReadReceipt())
    act(() => useThreadStore.getState().setActiveThreadId('a'))
    await settle()
    expect(readRequests()).toHaveLength(1)

    act(() => { window.dispatchEvent(new Event('blur')) })
    act(() => { window.dispatchEvent(new Event('focus')) })
    await settle()

    expect(readRequests()).toEqual([
      { threadId: 'a', expectedRevision: 'r1', operation: { type: 'read' } },
      { threadId: 'a', expectedRevision: 'r1', operation: { type: 'read' } }
    ])
  })
})
