import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TurnNextStep } from '../components/conversation/TurnNextStep'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadTasksStore } from '../stores/threadTasksStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useToastStore } from '../stores/toastStore'
import type { ConversationTurn } from '../types/conversation'
import type { ThreadTasksSnapshot } from '../../shared/threadTasks'
import { installDesktopApiMock } from './desktopApiMock'
import { sendTextHere } from '../utils/sendTextHere'

vi.mock('../utils/sendTextHere', () => ({ sendTextHere: vi.fn(async () => {}) }))

const threadId = 'wrap-thread'
let snapshot: ThreadTasksSnapshot
let publishSnapshot: (snapshot: ThreadTasksSnapshot) => void
const dismissQuestion = vi.fn(async ({ callId }: { threadId: string; callId: string }) => {
  snapshot = { ...snapshot, question: undefined, dismissedQuestionCallIds: [callId] }
  publishSnapshot(snapshot)
  return { ok: true }
})

function turn(callId = 'wrap-call'): ConversationTurn {
  return {
    id: 'wrap-turn', threadId, status: 'completed', startedAt: '2026-11-18T00:00:00Z',
    items: [{
      id: 'wrap-item', toolCallId: callId, type: 'dynamicToolCall', status: 'completed',
      createdAt: '2026-11-18T00:00:00Z', toolName: 'WrapUp', pluginNamespace: 'desktop_conversation',
      success: true, structuredResult: { shown: true },
      arguments: { recap: 'The draft is ready.', ask: 'Publish?', actions: [{ label: 'Publish' }, { label: 'Keep draft' }], blocked: true },
    }],
  }
}

function show() {
  return render(<LocaleProvider loadSettings={false}><TurnNextStep turnId="wrap-turn" /></LocaleProvider>)
}

beforeEach(() => {
  vi.clearAllMocks()
  snapshot = { threadId, tasks: [], question: { turnId: 'wrap-turn', callId: 'wrap-call' } }
  useThreadTasksStore.setState({ byThread: {} })
  useToastStore.setState({ toasts: [] })
  useConversationStore.setState({ turns: [turn()], workspacePath: '/work', turnStatus: 'idle' })
  useComposerDraftStore.getState().saveDraft(threadId, { text: 'Unsent draft', segments: [], images: [], files: [] })
  installDesktopApiMock({
    threadTasks: {
      list: async () => snapshot,
      dismissQuestion,
      onChanged: (listener) => { publishSnapshot = listener; return () => {} },
    },
  })
})

afterEach(() => useComposerDraftStore.getState().clearDraft(threadId))

describe('Wrap Up card', () => {
  it('closes without sending or changing the draft and stays hidden after remount', async () => {
    const view = show()
    fireEvent.click(await screen.findByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Publish?' })).toBeNull())
    expect(dismissQuestion).toHaveBeenCalledWith({ threadId, callId: 'wrap-call' })
    expect(sendTextHere).not.toHaveBeenCalled()
    expect(useComposerDraftStore.getState().getDraft(threadId)?.text).toBe('Unsent draft')
    view.unmount()
    show()
    expect(screen.queryByRole('group', { name: 'Publish?' })).toBeNull()
  })

  it('keeps the card actionable when closing fails and allows retry', async () => {
    dismissQuestion.mockRejectedValueOnce(new Error('Connection unavailable'))
    show()
    fireEvent.click(await screen.findByRole('button', { name: 'Close' }))
    await waitFor(() => expect(useToastStore.getState().toasts).toMatchObject([{ type: 'error', message: 'Connection unavailable' }]))
    expect(screen.getByRole('group', { name: 'Publish?' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Publish?' })).toBeNull())
  })

  it('applies cross-window dismissal only to the matching box and keeps a later Wrap Up visible', async () => {
    show()
    await waitFor(() => expect(useThreadTasksStore.getState().byThread[threadId]).toBeDefined())
    act(() => publishSnapshot({ ...snapshot, dismissedQuestionCallIds: ['old-call'] }))
    expect(screen.getByRole('group', { name: 'Publish?' })).toBeTruthy()
    act(() => publishSnapshot({ ...snapshot, dismissedQuestionCallIds: ['wrap-call'] }))
    expect(screen.queryByRole('group', { name: 'Publish?' })).toBeNull()
    act(() => useConversationStore.setState({ turns: [turn('new-call')] }))
    expect(screen.getByRole('group', { name: 'Publish?' })).toBeTruthy()
  })

  it('allows closing a historical card without offering replies', async () => {
    useConversationStore.setState({ turns: [turn(), { ...turn('new-call'), id: 'new-turn' }] })
    show()
    expect(screen.queryByRole('button', { name: 'Publish', exact: true })).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: 'Close' }))
    await waitFor(() => expect(dismissQuestion).toHaveBeenCalledWith({ threadId, callId: 'wrap-call' }))
  })
})
