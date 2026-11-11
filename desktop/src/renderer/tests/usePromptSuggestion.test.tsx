import { Activity, type PropsWithChildren } from 'react'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usePromptSuggestion } from '../components/conversation/usePromptSuggestion'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import {
  activateThreadConversation,
  createThreadConversation,
  getThreadConversation,
  releaseThreadConversation
} from '../stores/conversationStore'
import { initPromptSuggestionStore, usePromptSuggestionStore } from '../stores/promptSuggestionStore'
import { useThreadStore } from '../stores/threadStore'
import { beginThreadRestoration, finishThreadRestoration } from '../stores/threadRestoration'
import { generatePromptSuggestion } from '../utils/promptSuggestion'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { activateTestWorkspace } from './workspaceResourceFixtures'

vi.mock('../utils/promptSuggestion', () => ({ generatePromptSuggestion: vi.fn(), logPromptSuggestion: vi.fn() }))

const generate = vi.mocked(generatePromptSuggestion)
const NOW = Date.parse('2026-11-10T12:01:00Z')
const completedTurn: ConversationTurn = {
  id: 'turn-1', threadId: 'thread-1', status: 'completed', startedAt: '2026-11-10T12:00:00Z',
  completedAt: '2026-11-10T12:00:01Z',
  items: [{ id: 'reply', type: 'agentMessage', status: 'completed', text: 'Done.', createdAt: '2026-11-10T12:00:01Z' }]
}
let stopSuggestions: () => void

function selectThread(threadId: string): void {
  activateThreadConversation(threadId)
  useThreadStore.setState({ activeThreadId: threadId })
}

function completeThread(threadId = 'thread-1', turn = completedTurn): void {
  getThreadConversation(threadId)!.setState({ turnStatus: 'idle', turns: [turn] })
}

function useActiveSuggestion({ canSuggest = true, visible = true } = {}) {
  const threadId = useThreadStore((state) => state.activeThreadId)!
  return usePromptSuggestion({ threadId, canSuggest, visible })
}

beforeEach(() => {
  vi.spyOn(Date, 'now').mockReturnValue(NOW)
  finishThreadRestoration()
  generate.mockReset().mockResolvedValue('Add a regression test')
  installDesktopApiMock({ appServer: {
    onNotification: () => () => undefined,
    sendRequest: vi.fn(async () => ({}))
  } })
  usePromptSuggestionStore.getState().reset()
  useThreadStore.getState().reset()
  createThreadConversation('thread-1').setState({
    turnStatus: 'running',
    turns: [{ ...completedTurn, status: 'running', items: [] }]
  })
  createThreadConversation('thread-2')
  selectThread('thread-1')
  useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true } })
  activateTestWorkspace()
  useConfigStore.setState({ config: { PromptSuggestions: { Enabled: true } }, status: 'ready', confirmed: true })
  stopSuggestions = initPromptSuggestionStore()
})

afterEach(() => {
  cleanup()
  stopSuggestions()
  finishThreadRestoration()
  activateThreadConversation(null)
  releaseThreadConversation('thread-1')
  releaseThreadConversation('thread-2')
  vi.restoreAllMocks()
})

describe('conversation prompt suggestion lifecycle', () => {
  it.each([
    ['recent', 60_000, true],
    ['old', 24 * 60 * 60_000, false]
  ] as const)('only generates for %s history after restoration finishes', async (_label, elapsed, shouldGenerate) => {
    const gate = beginThreadRestoration('thread-1')
    const hook = renderHook(() => useActiveSuggestion())
    act(() => completeThread('thread-1', {
      ...completedTurn,
      startedAt: new Date(NOW - elapsed - 60_000).toISOString(),
      completedAt: new Date(NOW - elapsed).toISOString()
    }))
    expect(generate).not.toHaveBeenCalled()

    act(() => finishThreadRestoration(gate))
    if (shouldGenerate) {
      await waitFor(() => expect(hook.result.current.suggestion).toBe('Add a regression test'))
      expect(generate).toHaveBeenCalledOnce()
    } else {
      expect(generate).not.toHaveBeenCalled()
      expect(hook.result.current.suggestion).toBeNull()
    }
  })

  it('generates once, hides while typing, and remembers acceptance after remounting', async () => {
    const hook = renderHook(({ visible }) => useActiveSuggestion({ visible }), { initialProps: { visible: true } })
    act(() => completeThread())

    await waitFor(() => expect(hook.result.current.suggestion).toBe('Add a regression test'))
    expect(generate).toHaveBeenCalledWith('thread-1', 'turn-1', expect.any(AbortSignal), expect.any(Function))
    hook.rerender({ visible: false })
    expect(hook.result.current.suggestion).toBeNull()
    hook.rerender({ visible: true })
    expect(hook.result.current.suggestion).toBe('Add a regression test')
    act(() => expect(hook.result.current.accept()).toBe('Add a regression test'))
    expect(hook.result.current.suggestion).toBeNull()
    hook.unmount()

    const restored = renderHook(() => useActiveSuggestion())
    expect(restored.result.current.suggestion).toBeNull()
    expect(generate).toHaveBeenCalledOnce()
  })

  it('retains a prediction across Activity hiding, temporary composer unavailability, and unmounting', async () => {
    completeThread()
    let mode: 'visible' | 'hidden' = 'visible'
    const wrapper = ({ children }: PropsWithChildren) => <Activity mode={mode}>{children}</Activity>
    const hook = renderHook(({ canSuggest }) => useActiveSuggestion({ canSuggest }), {
      initialProps: { canSuggest: true }, wrapper
    })
    await waitFor(() => expect(hook.result.current.suggestion).toBe('Add a regression test'))

    mode = 'hidden'
    hook.rerender({ canSuggest: true })
    mode = 'visible'
    hook.rerender({ canSuggest: false })
    expect(hook.result.current.suggestion).toBeNull()
    hook.rerender({ canSuggest: true })
    expect(hook.result.current.suggestion).toBe('Add a regression test')
    hook.unmount()

    const restored = renderHook(() => useActiveSuggestion())
    expect(restored.result.current.suggestion).toBe('Add a regression test')
    expect(generate).toHaveBeenCalledOnce()
  })

  it('restores each thread prediction even when it arrives in the background without mixing results', async () => {
    let resolveFirst!: (value: string | null) => void
    generate.mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockResolvedValue('Run the formatter')
    completeThread()
    const hook = renderHook(() => useActiveSuggestion())

    act(() => {
      selectThread('thread-2')
      completeThread('thread-2', { ...completedTurn, id: 'turn-2', threadId: 'thread-2' })
    })
    await waitFor(() => expect(hook.result.current.suggestion).toBe('Run the formatter'))
    await act(async () => resolveFirst('Check the parser'))
    expect(hook.result.current.suggestion).toBe('Run the formatter')
    act(() => selectThread('thread-1'))
    expect(hook.result.current.suggestion).toBe('Check the parser')
    act(() => selectThread('thread-2'))
    expect(hook.result.current.suggestion).toBe('Run the formatter')
    expect(generate).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['within', 60_000, true],
    ['outside', 5 * 60_000, false]
  ] as const)('only catches up a background completion %s the generation window', async (_label, delay, shouldGenerate) => {
    const hook = renderHook(({ canSuggest }) => useActiveSuggestion({ canSuggest }), {
      initialProps: { canSuggest: true }
    })
    act(() => selectThread('thread-2'))
    act(() => completeThread())
    expect(generate).not.toHaveBeenCalled()

    vi.mocked(Date.now).mockReturnValue(NOW + delay)
    hook.rerender({ canSuggest: false })
    act(() => selectThread('thread-1'))
    expect(generate).not.toHaveBeenCalled()
    hook.rerender({ canSuggest: true })
    if (shouldGenerate) {
      await waitFor(() => expect(hook.result.current.suggestion).toBe('Add a regression test'))
      expect(generate).toHaveBeenCalledOnce()
    } else {
      expect(generate).not.toHaveBeenCalled()
      expect(hook.result.current.suggestion).toBeNull()
    }
  })

  it('reuses a pending prediction across navigation and ignores its late result after a newer turn', async () => {
    let resolveOld!: (value: string | null) => void
    generate.mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve }))
    completeThread()
    const hook = renderHook(() => useActiveSuggestion())
    const oldSignal = generate.mock.calls[0][2]

    act(() => selectThread('thread-2'))
    expect(hook.result.current.suggestion).toBeNull()
    act(() => selectThread('thread-1'))
    hook.unmount()
    const restored = renderHook(() => useActiveSuggestion())
    expect(oldSignal.aborted).toBe(false)
    expect(generate).toHaveBeenCalledOnce()

    const nextTurn = {
      ...completedTurn, id: 'turn-next', startedAt: '2026-11-10T12:00:02Z', completedAt: '2026-11-10T12:00:03Z'
    }
    act(() => getThreadConversation('thread-1')!.setState({
      turnStatus: 'running', turns: [completedTurn, { ...nextTurn, status: 'running', items: [] }]
    }))
    expect(oldSignal.aborted).toBe(true)
    act(() => completeThread('thread-1', nextTurn))
    await waitFor(() => expect(restored.result.current.suggestion).toBe('Add a regression test'))
    await act(async () => resolveOld('Outdated suggestion'))
    expect(restored.result.current.suggestion).toBe('Add a regression test')
    expect(generate).toHaveBeenCalledTimes(2)
  })

  it.each(['cached', 'pending'] as const)('preserves a %s prediction across restoration after the generation window', async (state) => {
    let resolvePrediction!: (value: string | null) => void
    if (state === 'pending') generate.mockImplementationOnce(() => new Promise((resolve) => { resolvePrediction = resolve }))
    completeThread()
    const hook = renderHook(() => useActiveSuggestion())
    if (state === 'cached') await waitFor(() => expect(hook.result.current.suggestion).toBe('Add a regression test'))
    const signal = generate.mock.calls[0][2]
    hook.unmount()
    selectThread('thread-2')
    releaseThreadConversation('thread-1')
    vi.mocked(Date.now).mockReturnValue(NOW + 60 * 60_000)
    const gate = beginThreadRestoration('thread-1')
    const conversation = createThreadConversation('thread-1')
    selectThread('thread-1')
    const restored = renderHook(() => useActiveSuggestion())
    act(() => conversation.getState().setTurns([{ ...completedTurn, id: 'preview-turn' }]))
    expect(signal.aborted).toBe(false)
    expect(restored.result.current.suggestion).toBeNull()
    act(() => {
      conversation.getState().setTurns([completedTurn])
      finishThreadRestoration(gate)
    })
    expect(signal.aborted).toBe(false)
    if (state === 'pending') await act(async () => resolvePrediction('Add a regression test'))
    await waitFor(() => expect(restored.result.current.suggestion).toBe('Add a regression test'))
    expect(generate).toHaveBeenCalledOnce()
  })

  it('does not regenerate an empty prediction after returning', async () => {
    generate.mockResolvedValue(null)
    completeThread()
    const hook = renderHook(() => useActiveSuggestion())
    await act(async () => { await Promise.resolve() })
    hook.unmount()

    const restored = renderHook(() => useActiveSuggestion())
    expect(restored.result.current.suggestion).toBeNull()
    expect(generate).toHaveBeenCalledOnce()
  })

  it('does not request another suggestion when a trailing notification completes without output', () => {
    const notification: ConversationTurn = {
      id: 'turn-notice', threadId: 'thread-1', status: 'running', startedAt: '2026-11-10T12:00:02Z',
      completedAt: '2026-11-10T12:00:03Z',
      items: [{ id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerStatus: 'completed', text: 'Background command finished', createdAt: '2026-11-10T12:00:02Z' }]
    }
    getThreadConversation('thread-1')!.setState({ turns: [completedTurn, notification] })
    const hook = renderHook(() => useActiveSuggestion())
    act(() => getThreadConversation('thread-1')!.setState({
      turnStatus: 'idle', turns: [completedTurn, { ...notification, status: 'completed' }]
    }))

    expect(generate).not.toHaveBeenCalled()
    expect(hook.result.current.suggestion).toBeNull()
  })

  it.each(['disabled', 'workspace reset'] as const)('cancels a pending prediction when settings are %s without a mounted composer', (change) => {
    generate.mockImplementation(() => new Promise(() => {}))
    completeThread()
    const hook = renderHook(() => useActiveSuggestion())
    const signal = generate.mock.calls[0][2]
    hook.unmount()
    expect(signal.aborted).toBe(false)

    act(() => {
      if (change === 'disabled') useConfigStore.setState({ config: { PromptSuggestions: { Enabled: false } } })
      else useConfigStore.getState().reset()
    })
    expect(signal.aborted).toBe(true)
  })
})
