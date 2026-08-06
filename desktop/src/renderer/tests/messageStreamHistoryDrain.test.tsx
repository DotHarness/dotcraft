import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()

const TURN_PAGES: Record<string, { data: Array<{ id: string }>; nextCursor: string | null }> = {
  head: { data: [{ id: 'turn-3' }], nextCursor: 'cursor-2' },
  'cursor-2': { data: [{ id: 'turn-2' }], nextCursor: 'cursor-1' },
  'cursor-1': { data: [{ id: 'turn-1' }], nextCursor: null }
}

const ITEMS: Record<string, string> = {
  'turn-1': 'oldest message',
  'turn-2': 'middle message',
  'turn-3': 'newest message'
}

function headTurn(id: string): ReturnType<typeof useConversationStore.getState>['turns'][number] {
  return {
    id,
    threadId: 'thread-1',
    status: 'completed',
    startedAt: '2026-08-06T00:00:00Z',
    completedAt: '2026-08-06T00:00:01Z',
    items: [{
      id: `${id}-agent`,
      type: 'agentMessage',
      status: 'completed',
      text: ITEMS[id],
      createdAt: '2026-08-06T00:00:00Z'
    }]
  }
}

describe('MessageStream history drain', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    appServerSendRequest.mockImplementation(async (method: string, params: Record<string, unknown>) => {
      if (method === 'thread/turns/list') {
        return TURN_PAGES[(params.cursor as string | null) ?? 'head']
      }
      if (method === 'thread/items/list') {
        const turnId = params.turnId as string
        return {
          data: [{
            turnId,
            item: {
              id: `${turnId}-agent`,
              type: 'agentMessage',
              status: 'completed',
              text: ITEMS[turnId],
              createdAt: '2026-08-06T00:00:00Z'
            }
          }],
          nextCursor: null
        }
      }
      return {}
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: async () => ({ locale: 'en' }) },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
      }
    })
  })

  it('drains older turns without any scroll event once the head lands', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useConversationStore.setState({ turns: [headTurn('turn-3')] })
    useThreadStore.getState().setActiveHistoryCursors('thread-1', 'cursor-2')

    render(<LocaleProvider><MessageStream /></LocaleProvider>)

    // No scroll is dispatched: jsdom has no layout, so the container is never scrollable.
    await waitFor(() => {
      expect(screen.getByText('oldest message')).toBeInTheDocument()
    })
    expect(useConversationStore.getState().turns.map((turn) => turn.id))
      .toEqual(['turn-1', 'turn-2', 'turn-3'])
    expect(useThreadStore.getState().activeHistoryCursors).toEqual({
      threadId: 'thread-1',
      turnCursor: null
    })
  })

  it('stops paging when the thread is switched away', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useConversationStore.setState({ turns: [headTurn('turn-3')] })
    useThreadStore.getState().setActiveHistoryCursors('thread-1', 'cursor-2')

    const { unmount } = render(<LocaleProvider><MessageStream /></LocaleProvider>)
    await act(async () => {
      useThreadStore.getState().setActiveThreadId('thread-2')
      await Promise.resolve()
    })
    unmount()

    expect(appServerSendRequest).toHaveBeenCalled()
    const turnListCalls = appServerSendRequest.mock.calls
      .filter(([method]) => method === 'thread/turns/list').length
    expect(turnListCalls).toBeLessThanOrEqual(1)
  })
})
