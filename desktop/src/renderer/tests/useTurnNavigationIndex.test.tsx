import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { beginThreadHistory, restartThreadHistory, useThreadHistoryStore } from '../stores/threadHistoryStore'
import { useTurnNavigationIndex } from '../components/conversation/turnNavigation/useTurnNavigationIndex'
import { installDesktopApiMock } from './desktopApiMock'
import {
  createFakeHistoryServer,
  loadedTurnIds,
  openThreadHistory,
  turnRange,
  userMessageAndReply,
  type FakeHistoryServer,
  type TurnItemsFactory
} from './threadHistoryFakeServer'

const THREAD = 'thread-1'

type Request = (method: string, params: Record<string, unknown>) => Promise<unknown>

function serve(
  turnCount: number,
  itemsFor: TurnItemsFactory = userMessageAndReply,
  intercept: (request: Request) => Request = (request) => request
): FakeHistoryServer {
  const server = createFakeHistoryServer(THREAD, turnCount, itemsFor)
  const sendRequest: ReturnType<typeof vi.fn> = vi.fn(intercept(server.request))
  installDesktopApiMock({ appServer: { sendRequest } })
  return server
}

function navigationListings(server: FakeHistoryServer): Array<Record<string, unknown>> {
  return server.turnsListCalls().filter((params) => params.limit === 100)
}

function itemReads(server: FakeHistoryServer, turnId?: string): Array<Record<string, unknown>> {
  return server.request.mock.calls
    .filter(([method, params]) => method === 'thread/items/list' && (!turnId || params.turnId === turnId))
    .map(([, params]) => params as Record<string, unknown>)
}

async function openIndex(server: FakeHistoryServer) {
  await openThreadHistory(server, THREAD)
  const hook = renderHook(() => useTurnNavigationIndex())
  await waitFor(() => expect(hook.result.current.railAllowed).toBe(true))
  return hook
}

function entriesOf(entries: ReturnType<typeof useTurnNavigationIndex>['entries'], turnId: string) {
  return entries.filter((entry) => entry.turnId === turnId)
}

describe('useTurnNavigationIndex', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    beginThreadHistory(null)
  })

  it('lists unloaded turns as placeholders without loading history pages', async () => {
    const server = serve(30)
    await openThreadHistory(server, THREAD)
    const headItemReads = itemReads(server).length

    const { result } = renderHook(() => useTurnNavigationIndex())
    expect(result.current.railAllowed).toBe(false)
    await waitFor(() => expect(result.current.railAllowed).toBe(true))

    const { entries } = result.current
    expect(entries).toHaveLength(30)
    expect(entries.slice(0, 25).map((entry) => entry.turnId)).toEqual(turnRange(1, 25))
    expect(entries.slice(0, 25).every((entry) => entry.content === null && entry.position !== null)).toBe(true)
    expect(entries.slice(25).map((entry) => entry.content?.label)).toEqual(turnRange(26, 30).map((id) => `${id} ask`))
    expect(loadedTurnIds()).toEqual(turnRange(26, 30))
    expect(itemReads(server)).toHaveLength(headItemReads)
  })

  it('replaces a previewed placeholder with its entries and reads it once per generation', async () => {
    const server = serve(30, (turn) => turn.id === 'turn-12'
      ? [
          { id: 'turn-12-first', type: 'userMessage', status: 'completed', text: 'first ask', createdAt: turn.startedAt },
          { id: 'turn-12-reply', type: 'agentMessage', status: 'completed', text: 'first reply', createdAt: turn.startedAt },
          { id: 'turn-12-second', type: 'userMessage', status: 'completed', text: 'second ask', createdAt: turn.startedAt }
        ]
      : userMessageAndReply(turn))
    const { result } = await openIndex(server)
    const placeholder = entriesOf(result.current.entries, 'turn-12')[0]

    act(() => result.current.requestPreview(placeholder))
    await waitFor(() => expect(entriesOf(result.current.entries, 'turn-12')).toHaveLength(2))
    act(() => result.current.requestPreview(placeholder))

    const previewed = entriesOf(result.current.entries, 'turn-12')
    expect(previewed.map((entry) => [entry.content?.label, entry.content?.response])).toEqual([
      ['first ask', 'first reply'],
      ['second ask', '']
    ])
    expect(previewed.every((entry) => entry.position !== null)).toBe(true)
    expect(result.current.entries).toHaveLength(31)
    expect(itemReads(server, 'turn-12')).toHaveLength(1)
    expect(loadedTurnIds()).toEqual(turnRange(26, 30))
  })

  it('drops a turn with no visible user message and marks a failed preview without retrying', async () => {
    let failedReads = 0
    const server = serve(
      30,
      (turn) => turn.id === 'turn-12' ? [] : userMessageAndReply(turn),
      (request) => (method, params) => {
        if (method !== 'thread/items/list' || params.turnId !== 'turn-13') return request(method, params)
        failedReads++
        return Promise.reject(new Error('unavailable'))
      }
    )
    const { result } = await openIndex(server)
    const empty = entriesOf(result.current.entries, 'turn-12')[0]
    const failing = entriesOf(result.current.entries, 'turn-13')[0]

    act(() => {
      result.current.requestPreview(empty)
      result.current.requestPreview(failing)
    })
    await waitFor(() => expect(entriesOf(result.current.entries, 'turn-12')).toHaveLength(0))
    await waitFor(() => expect(entriesOf(result.current.entries, 'turn-13')[0].previewFailed).toBe(true))
    act(() => result.current.requestPreview(entriesOf(result.current.entries, 'turn-13')[0]))

    expect(entriesOf(result.current.entries, 'turn-13')[0].content).toBeNull()
    expect(result.current.entries).toHaveLength(29)
    expect(failedReads).toBe(1)
  })

  it('discards the listing and previews when the history generation changes', async () => {
    const server = serve(30)
    const { result } = await openIndex(server)
    act(() => result.current.requestPreview(entriesOf(result.current.entries, 'turn-12')[0]))
    await waitFor(() => expect(entriesOf(result.current.entries, 'turn-12')[0].content).not.toBeNull())
    const olderCursor = useThreadHistoryStore.getState().gaps[0].olderCursor

    act(() => { restartThreadHistory(THREAD, useConversationStore.getState().turns, olderCursor) })

    expect(result.current.railAllowed).toBe(false)
    await waitFor(() => expect(navigationListings(server)).toHaveLength(2))
    await waitFor(() => expect(result.current.railAllowed).toBe(true))
    expect(entriesOf(result.current.entries, 'turn-12')[0].content).toBeNull()

    act(() => result.current.requestPreview(entriesOf(result.current.entries, 'turn-12')[0]))
    await waitFor(() => expect(itemReads(server, 'turn-12')).toHaveLength(2))
  })

  it('shows the rail only once all history is loaded when the thread has more than 1000 turns', async () => {
    const server = serve(1001)
    await openThreadHistory(server, THREAD)
    const { result } = renderHook(() => useTurnNavigationIndex())

    await waitFor(() => expect(navigationListings(server)).toHaveLength(10))
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
    expect(navigationListings(server)).toHaveLength(10)
    expect(result.current.railAllowed).toBe(false)
    expect(result.current.entries.every((entry) => entry.position === null)).toBe(true)

    act(() => { restartThreadHistory(THREAD, useConversationStore.getState().turns, null) })

    expect(result.current.railAllowed).toBe(true)
    expect(result.current.entries.map((entry) => entry.turnId)).toEqual(turnRange(997, 1001))
  })

  it('covers loaded turns only when turn pages carry no backwards cursor', async () => {
    const server = serve(30, userMessageAndReply, (request) => async (method, params) => {
      const result = await request(method, params) as Record<string, unknown>
      return method === 'thread/turns/list' ? { ...result, backwardsCursor: null } : result
    })

    const { result } = await openIndex(server)

    expect(result.current.entries.map((entry) => entry.turnId)).toEqual(turnRange(26, 30))
    expect(result.current.entries.every((entry) => entry.position === null)).toBe(true)
  })

  it('reads nothing for a thread whose history is already complete', async () => {
    const server = serve(4)
    await openThreadHistory(server, THREAD)
    const callsBefore = server.request.mock.calls.length

    const { result } = renderHook(() => useTurnNavigationIndex())

    expect(result.current.railAllowed).toBe(true)
    expect(result.current.entries.map((entry) => entry.turnId)).toEqual(turnRange(1, 4))
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
    expect(server.request.mock.calls).toHaveLength(callsBefore)
  })
})
