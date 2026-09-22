import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { interruptTurn } from '../utils/interruptTurn'
import { turnStopKey, useTurnStopStore } from '../stores/turnStopStore'

const state = vi.hoisted(() => ({
  activeTurnId: 'turn-1',
  interruptingTurnId: null as string | null,
  setInterruptingTurnId: vi.fn()
}))
vi.mock('../stores/conversationStore', () => ({ useConversationStore: { getState: () => state } }))

describe('stop request attribution', () => {
  afterEach(() => vi.unstubAllGlobals())
  beforeEach(() => {
    useTurnStopStore.setState({ requested: new Set() })
    state.interruptingTurnId = null
    state.setInterruptingTurnId.mockImplementation(value => { state.interruptingTurnId = value })
  })

  it('records the requesting thread before a terminal event can arrive and keeps it after acknowledgement', async () => {
    const sendRequest = vi.fn(async () => {
      expect(useTurnStopStore.getState().requested.has(turnStopKey('thread-1', 'turn-1'))).toBe(true)
      state.interruptingTurnId = null
    })
    vi.stubGlobal('window', { api: { appServer: { sendRequest } } })
    expect(await interruptTurn({ threadId: 'thread-1', turnId: 'turn-1', onError: vi.fn() })).toBe(true)
    expect(useTurnStopStore.getState().requested.has(turnStopKey('thread-1', 'turn-1'))).toBe(true)
    expect(useTurnStopStore.getState().requested.has(turnStopKey('thread-2', 'turn-1'))).toBe(false)
  })

  it('removes attribution after failure and allows retry', async () => {
    const onError = vi.fn()
    const sendRequest = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({})
    vi.stubGlobal('window', { api: { appServer: { sendRequest } } })
    expect(await interruptTurn({ threadId: 'thread-1', turnId: 'turn-1', onError })).toBe(false)
    expect(useTurnStopStore.getState().requested.size).toBe(0)
    expect(state.interruptingTurnId).toBeNull()
    expect(onError).toHaveBeenCalledOnce()
    expect(await interruptTurn({ threadId: 'thread-1', turnId: 'turn-1', onError })).toBe(true)
  })

  it('does not attribute an inactive turn or send a duplicate request', async () => {
    const sendRequest = vi.fn()
    vi.stubGlobal('window', { api: { appServer: { sendRequest } } })
    await interruptTurn({ threadId: 'thread-1', turnId: 'other', onError: vi.fn() })
    state.interruptingTurnId = 'turn-1'
    await interruptTurn({ threadId: 'thread-1', turnId: 'turn-1', onError: vi.fn() })
    expect(sendRequest).not.toHaveBeenCalled()
    expect(useTurnStopStore.getState().requested.size).toBe(0)
  })
})
