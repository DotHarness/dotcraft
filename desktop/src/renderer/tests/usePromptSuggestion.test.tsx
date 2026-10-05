import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePromptSuggestion } from '../components/conversation/usePromptSuggestion'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { generatePromptSuggestion } from '../utils/promptSuggestion'

vi.mock('../utils/promptSuggestion', () => ({ generatePromptSuggestion: vi.fn(), logPromptSuggestion: vi.fn() }))

const generate = vi.mocked(generatePromptSuggestion)

beforeEach(() => {
  generate.mockReset().mockResolvedValue('Add a regression test')
  useConversationStore.getState().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useConversationStore.setState({
    turnStatus: 'running',
    turns: [{ id: 'turn-1', threadId: 'thread-1', status: 'running', startedAt: '2026-09-27T00:00:00Z', items: [] }]
  })
  useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true } })
  useConfigStore.setState({ config: { PromptSuggestions: { Enabled: true } } })
})

describe('conversation prompt suggestion lifecycle', () => {
  it('generates once after a successful turn and offers editable text', async () => {
    const { result } = renderHook(() => usePromptSuggestion({
      threadId: 'thread-1', canSuggest: true
    }))
    await act(async () => { await Promise.resolve() })

    act(() => useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{ id: 'turn-1', threadId: 'thread-1', status: 'completed', startedAt: '2026-09-27T00:00:00Z', items: [] }]
    }))

    await waitFor(() => expect(result.current.suggestion).toBe('Add a regression test'))
    expect(generate).toHaveBeenCalledOnce()
    expect(generate).toHaveBeenCalledWith('thread-1', 'turn-1', expect.any(AbortSignal), expect.any(Function))
    act(() => expect(result.current.accept()).toBe('Add a regression test'))
    expect(result.current.suggestion).toBeNull()
  })

  it('cancels a pending suggestion when the composer gains content', async () => {
    let requestSignal: AbortSignal | null = null
    generate.mockImplementation(async (_threadId, _turnId, signal) => {
      requestSignal = signal
      return await new Promise<string | null>(() => {})
    })
    const { rerender } = renderHook(({ canSuggest }) => usePromptSuggestion({
      threadId: 'thread-1', canSuggest
    }), { initialProps: { canSuggest: true } })
    await act(async () => { await Promise.resolve() })

    act(() => useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{ id: 'turn-1', threadId: 'thread-1', status: 'completed', startedAt: '2026-09-27T00:00:00Z', items: [] }]
    }))
    await waitFor(() => expect(requestSignal).not.toBeNull())
    rerender({ canSuggest: false })
    expect(requestSignal?.aborted).toBe(true)
  })
})
