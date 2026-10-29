import { act } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { reloadThreadAfterRollback } from '../utils/threadRollback'
import { installDesktopApiMock } from './desktopApiMock'
import { createFakeHistoryServer, loadedTurnIds } from './threadHistoryFakeServer'

const THREAD_ID = 'thread-a'

function shownTurn(id: string, minute: number) {
  return {
    id,
    threadId: THREAD_ID,
    status: 'completed',
    startedAt: new Date(Date.UTC(2026, 0, 1, 0, minute)).toISOString(),
    items: []
  }
}

afterEach(() => {
  releaseThreadState(THREAD_ID)
  useThreadStore.getState().reset()
})

describe('reloading a thread after rollback', () => {
  it('drops rolled back turns but keeps a turn that started while the reload was pending', async () => {
    const server = createFakeHistoryServer(THREAD_ID, 1)
    installDesktopApiMock({ appServer: { sendRequest: server.request } })
    useThreadStore.setState({ activeThreadId: THREAD_ID })
    openThreadState(THREAD_ID)
    useConversationStore.getState().setTurns([shownTurn('turn-1', 0), shownTurn('turn-2', 1)])

    const release = server.holdNextTurnsList()
    const reload = reloadThreadAfterRollback(THREAD_ID)
    act(() => {
      useConversationStore.getState().onTurnStarted({
        id: 'turn-3',
        threadId: THREAD_ID,
        status: 'running',
        startedAt: new Date(Date.UTC(2026, 0, 1, 0, 2)).toISOString(),
        items: []
      })
    })
    release()
    await act(async () => { await reload })

    expect(loadedTurnIds()).toEqual(['turn-1', 'turn-3'])
  })
})
