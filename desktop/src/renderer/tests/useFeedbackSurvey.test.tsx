import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { useFeedbackSurvey } from '../components/conversation/useFeedbackSurvey'
import { feedbackFollowUpUrl } from '../components/conversation/FeedbackSurvey'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { ConversationTurn } from '../types/conversation'

const THREAD_ID = 'thread-feedback'
const TEN_MINUTES = 10 * 60 * 1000

function turn(id: string, status: ConversationTurn['status']): ConversationTurn {
  return { id, threadId: THREAD_ID, status, startedAt: new Date().toISOString(), items: [] }
}

function runTurn(id: string, ending: ConversationTurn['status'] = 'completed'): void {
  act(() => useConversationStore.setState({ turnStatus: 'running', activeTurnId: id, turns: [turn(id, 'running')] }))
  act(() => useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null, turns: [turn(id, ending)] }))
}

function runTurns(count: number): void {
  for (let index = 0; index < count; index++) runTurn(`turn-${index}`)
}

function setThread(overrides: Record<string, unknown> = {}): void {
  useThreadStore.setState({
    activeThreadId: THREAD_ID,
    activeThread: { id: THREAD_ID, originChannel: 'dotcraft-desktop', metadata: {}, ...overrides } as never,
  })
}

describe('useFeedbackSurvey', () => {
  let shown: boolean
  let setSettings: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    shown = false
    setSettings = vi.fn(async (partial: { feedbackSurveyShown?: boolean }) => {
      shown = partial.feedbackSurveyShown ?? shown
    })
    installDesktopApiMock({
      settings: { get: async () => ({ feedbackSurveyShown: shown }), set: setSettings },
    } as never)
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null, turns: [] })
    setThread()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('asks after five turns and ten minutes, and records that it appeared', async () => {
    const { result } = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(5)
    await waitFor(() => expect(result.current.state).toEqual({ phase: 'ask' }))
    expect(setSettings).toHaveBeenCalledWith({ feedbackSurveyShown: true })
  })

  it('stays quiet before either threshold is met', async () => {
    const early = renderHook(() => useFeedbackSurvey(THREAD_ID))
    runTurns(5)
    await act(async () => {})
    expect(early.result.current.state).toBeNull()
    early.unmount()

    const fewTurns = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(4)
    await act(async () => {})
    expect(fewTurns.result.current.state).toBeNull()
  })

  it('never asks again once it has appeared', async () => {
    shown = true
    const { result } = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(5)
    await act(async () => {})
    expect(result.current.state).toBeNull()
    expect(setSettings).not.toHaveBeenCalled()
  })

  it.each([
    ['a sub-agent thread', { metadata: { spawnedFromThreadId: 'parent' } }],
    ['a channel thread', { originChannel: 'telegram' }],
  ])('does not ask in %s', async (_name, overrides) => {
    setThread(overrides)
    const { result } = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(5)
    await act(async () => {})
    expect(result.current.state).toBeNull()
  })

  it('does not ask right after a failed turn', async () => {
    const { result } = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(4)
    runTurn('turn-failed', 'failed')
    await act(async () => {})
    expect(result.current.state).toBeNull()
  })

  it('keeps an unanswered question through a running turn but drops the thanks line', async () => {
    const { result } = renderHook(() => useFeedbackSurvey(THREAD_ID))
    vi.advanceTimersByTime(TEN_MINUTES)
    runTurns(5)
    await waitFor(() => expect(result.current.state).toEqual({ phase: 'ask' }))

    act(() => useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-next' }))
    expect(result.current.state).toBeNull()
    act(() => useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null }))
    expect(result.current.state).toEqual({ phase: 'ask' })

    act(() => result.current.rate('good'))
    expect(result.current.state).toEqual({ phase: 'thanks', rating: 'good' })
    act(() => useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-later' }))
    act(() => useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null }))
    expect(result.current.state).toBeNull()
  })

  it('sends good ratings to the repository and others to a prefilled issue', () => {
    expect(feedbackFollowUpUrl('good', 'win32')).toBe('https://github.com/DotHarness/dotcraft')
    const issue = new URL(feedbackFollowUpUrl('bad', 'win32'))
    expect(issue.pathname).toBe('/DotHarness/dotcraft/issues/new')
    expect(issue.searchParams.get('body')).toContain('win32')
  })
})
