import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initPromptSuggestionStore, usePromptSuggestionStore } from '../stores/promptSuggestionStore'
import { useConfigStore } from '../stores/configStore'
import { generatePromptSuggestion } from '../utils/promptSuggestion'

vi.mock('../utils/promptSuggestion', () => ({ generatePromptSuggestion: vi.fn(), logPromptSuggestion: vi.fn() }))

const generate = vi.mocked(generatePromptSuggestion)
type Notification = { method: string; params: Record<string, unknown>; foreground?: boolean }
let notify: (notification: Notification) => void
let stopSuggestions: () => void

beforeEach(() => {
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
})

describe('prompt prediction lifecycle outside the composer', () => {
  it('cancels only the affected pending prediction on a later turn or queued input', () => {
    const store = usePromptSuggestionStore.getState()
    const firstTurn = { id: 'first-turn', startedAt: '2026-09-27T00:00:00Z' }
    const nextTurn = { id: 'next-turn', startedAt: '2026-09-27T00:00:02Z' }
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
