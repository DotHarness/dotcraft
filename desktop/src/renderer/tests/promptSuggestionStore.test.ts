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
  it('cancels only the affected pending prediction on a new turn or queued input without retrying the old turn', () => {
    const store = usePromptSuggestionStore.getState()
    store.request('first', 'first-turn')
    store.request('second', 'second-turn')
    const firstSignal = generate.mock.calls[0][2]
    const secondSignal = generate.mock.calls[1][2]

    notify({ method: 'turn/started', params: { turn: { threadId: 'first', id: 'next-turn' } } })
    expect(firstSignal.aborted).toBe(true)
    expect(secondSignal.aborted).toBe(false)
    notify({ method: 'thread/queue/updated', params: { threadId: 'second', queuedInputs: [{ id: 'queued' }] } })
    expect(secondSignal.aborted).toBe(true)

    store.request('first', 'first-turn')
    store.request('second', 'second-turn')
    expect(generate).toHaveBeenCalledTimes(2)
    store.request('first', 'next-turn')
    expect(generate).toHaveBeenCalledTimes(3)
  })
})
