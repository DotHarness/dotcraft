import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { generatePromptSuggestion } from '../utils/promptSuggestion'

beforeEach(() => vi.spyOn(console, 'debug').mockImplementation(() => {}))
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); vi.useRealTimers() })

describe('prompt suggestions', () => {
  it.each([
    ['  run the tests\n', 'run the tests'],
    ['', null],
    [' \n ', null],
    ['x'.repeat(241), 'x'.repeat(241)]
  ])('reads plain output %j and removes the helper thread', async (answer, expected) => {
    let notify: ((value: { method: string; params: unknown }) => void) | null = null
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/fork') return { thread: { id: 'fork-1' } }
      if (method === 'turn/start') {
        queueMicrotask(() => notify?.({
          method: 'turn/completed',
          params: {
            turn: {
              id: 'fork-turn',
              threadId: 'fork-1',
              status: 'completed',
              items: [{ type: 'agentMessage', payload: { text: answer } }]
            }
          }
        }))
        return { turn: { id: 'fork-turn' } }
      }
      return {}
    })
    vi.stubGlobal('window', {
      setTimeout,
      clearTimeout,
      api: { appServer: { sendRequest, onNotification: (callback: typeof notify) => {
        notify = callback
        return () => { notify = null }
      } } }
    })

    const diagnostic = vi.fn()
    const result = await generatePromptSuggestion('parent', 'completed-turn', new AbortController().signal, diagnostic)

    expect(result).toBe(expected)
    expect(diagnostic).toHaveBeenLastCalledWith({
      parentThreadId: 'parent', parentTurnId: 'completed-turn', threadId: 'fork-1', turnId: 'fork-turn',
      outcome: expected ? 'received' : 'empty', textLength: expected?.length ?? 0
    })
    expect(sendRequest).toHaveBeenCalledWith('thread/fork', expect.objectContaining({
      threadId: 'parent',
      forkPoint: { turnId: 'completed-turn', position: 'after' },
      ephemeral: true,
      promptSuggestion: true
    }), 30_000)
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'fork-1' })
  })

  it('interrupts and deletes a helper turn when dismissed', async () => {
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/fork') return { thread: { id: 'fork-2' } }
      if (method === 'turn/start') return { turn: { id: 'fork-turn-2' } }
      return {}
    })
    vi.stubGlobal('window', {
      setTimeout,
      clearTimeout,
      api: { appServer: { sendRequest, onNotification: () => () => {} } }
    })
    const controller = new AbortController()
    const pending = generatePromptSuggestion('parent', 'completed-turn', controller.signal)
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.anything(), 30_000))

    controller.abort()
    expect(await pending).toBeNull()
    expect(sendRequest).toHaveBeenCalledWith('turn/interrupt', { threadId: 'fork-2', turnId: 'fork-turn-2' })
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'fork-2' })
  })
  it('reports a timeout and cleans up the pending turn', async () => {
    vi.useFakeTimers()
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/fork') return { thread: { id: 'fork-timeout' } }
      if (method === 'turn/start') return { turn: { id: 'turn-timeout' } }
      return {}
    })
    vi.stubGlobal('window', {
      setTimeout, clearTimeout,
      api: { appServer: { sendRequest, onNotification: () => () => {} } }
    })
    const diagnostic = vi.fn()
    const pending = generatePromptSuggestion('parent', 'turn-1', new AbortController().signal, diagnostic)
    await vi.advanceTimersByTimeAsync(30_000)
    expect(await pending).toBeNull()
    expect(diagnostic).toHaveBeenLastCalledWith(expect.objectContaining({
      threadId: 'fork-timeout', turnId: 'turn-timeout', outcome: 'timeout'
    }))
    expect(sendRequest).toHaveBeenCalledWith('turn/interrupt', { threadId: 'fork-timeout', turnId: 'turn-timeout' })
    expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'fork-timeout' })
  })

})
