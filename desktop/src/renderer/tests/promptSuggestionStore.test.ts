import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initPromptSuggestionStore, usePromptSuggestionStore } from '../stores/promptSuggestionStore'
import { useConfigStore } from '../stores/configStore'
import { generatePromptSuggestion } from '../utils/promptSuggestion'

vi.mock('../utils/promptSuggestion', () => ({ generatePromptSuggestion: vi.fn(), logPromptSuggestion: vi.fn() }))

const generate = vi.mocked(generatePromptSuggestion)
const NOW = Date.parse('2026-11-10T12:01:00Z')
type Notification = { method: string; params: Record<string, unknown>; foreground?: boolean }
let notify: (notification: Notification) => void
let stopSuggestions: () => void

beforeEach(() => {
  vi.spyOn(Date, 'now').mockReturnValue(NOW)
  generate.mockReset().mockImplementation(() => new Promise(() => {}))
  vi.stubGlobal('window', { api: { appServer: { onNotification: (callback: typeof notify) => {
    notify = callback
    return () => undefined
  } } } })
  usePromptSuggestionStore.getState().reset()
  useConfigStore.setState({ config: { PromptSuggestions: { Enabled: true } } })
  stopSuggestions = initPromptSuggestionStore()
})

afterEach(() => {
  stopSuggestions()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('prompt prediction lifecycle outside the composer', () => {
  it('requests a prediction just before the generation deadline', () => {
    usePromptSuggestionStore.getState().request('thread', {
      id: 'turn', completedAt: new Date(NOW - 5 * 60_000 + 1).toISOString()
    })

    expect(generate).toHaveBeenCalledOnce()
    expect(generate).toHaveBeenCalledWith('thread', 'turn', expect.any(AbortSignal), expect.any(Function))
  })

  it.each([undefined, new Date(NOW - 5 * 60_000).toISOString()])('does not request or cache a prediction for completion time %s', (completedAt) => {
    usePromptSuggestionStore.getState().request('thread', { id: 'turn', completedAt })

    expect(generate).not.toHaveBeenCalled()
    expect(usePromptSuggestionStore.getState().entries.has('thread')).toBe(false)
  })

  it('cancels only the affected pending prediction on a later turn or queued input', () => {
    const store = usePromptSuggestionStore.getState()
    const firstTurn = { id: 'first-turn', completedAt: new Date(NOW).toISOString() }
    const nextTurn = { ...firstTurn, id: 'next-turn' }
    store.request('first', firstTurn)
    const secondTurn = { ...firstTurn, id: 'second-turn' }
    store.request('second', secondTurn)
    const firstSignal = generate.mock.calls[0][2]
    const secondSignal = generate.mock.calls[1][2]

    notify({ method: 'turn/started', params: { turn: { ...nextTurn, threadId: 'first' } } })
    expect(firstSignal.aborted).toBe(true)
    expect(secondSignal.aborted).toBe(false)
    notify({ method: 'thread/queue/updated', params: { threadId: 'second', queuedInputs: [{ id: 'queued' }] } })
    expect(secondSignal.aborted).toBe(true)

    notify({ method: 'thread/queue/updated', params: { threadId: 'second', queuedInputs: [] } })
    store.request('second', secondTurn)
    store.request('first', nextTurn)
    expect(generate).toHaveBeenCalledTimes(4)
  })
})
