import './setupPluginRuntime'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useConnectionStore } from '../stores/connectionStore'
import { beginThreadRestoration, finishThreadRestoration } from '../stores/threadRestoration'
import { installDesktopApiMock } from './desktopApiMock'
import type { Thread } from '../types/thread'

vi.mock('../components/conversation/ThreadHeader', () => ({ ThreadHeader: () => null }))
vi.mock('../components/conversation/MessageStream', () => ({ MessageStream: () => {
  const turns = useConversationStore((state) => state.turns)
  return <div role="log">{turns.flatMap(turn => turn.items.map(item => item.text))}</div>
} }))
const { submitDraft, submitRejected } = vi.hoisted(() => ({ submitDraft: vi.fn(), submitRejected: vi.fn() }))
vi.mock('../components/conversation/InputComposer', () => ({
  InputComposer: ({ submitOverride }: { submitOverride?: () => void }) => <>
    <textarea aria-label="Draft" />
    <button aria-label="Send" onClick={async () => {
      try { await (submitOverride ?? submitDraft)() }
      catch (error) { submitRejected(error) }
    }} />
  </>
}))
vi.mock('../components/conversation/RequestUserInputComposer', () => ({ RequestUserInputComposer: () => <div>Current question</div> }))
vi.mock('../components/conversation/PlanApprovalComposer', () => ({ PlanApprovalComposer: () => <div>Implement choice</div> }))

beforeEach(() => {
  submitDraft.mockClear()
  submitRejected.mockClear()
  finishThreadRestoration()
  useConversationStore.getState().reset()
  releaseThreadState('thread')
  releaseThreadState('other')
  openThreadState('thread')
  useThreadStore.getState().reset()
  useConnectionStore.getState().reset()
  useUIStore.setState({ planApprovalDismissed: {}, pendingThreadCreation: null, pendingWelcomeTurn: null })
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) }, appServer: { sendRequest: vi.fn().mockResolvedValue({}) },
    file: { readFile: vi.fn().mockResolvedValue('{}') }, shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
    workspace: { saveImageToTemp: vi.fn() }
  })
  useThreadStore.setState({
    activeThreadId: 'thread',
    activeThread: {
      id: 'thread', displayName: 'Thread', status: 'active', originChannel: 'dotcraft-desktop',
      userId: 'local', metadata: {}, turns: [], workspacePath: 'workspace',
      createdAt: '2026-11-07T00:00:00Z', lastActiveAt: '2026-11-07T00:00:00Z'
    } as Thread
  })
})

describe('restored composer selection', () => {
  it('never presents the previous transcript or question under a new selection', async () => {
    const previous = useThreadStore.getState().activeThread!
    const incoming = { ...previous, id: 'other', displayName: 'Other' }
    useThreadStore.getState().setThreadList([previous, incoming])
    useConversationStore.getState().setTurns([{
      id: 'turn-a', threadId: 'thread', status: 'completed', startedAt: previous.createdAt,
      items: [{ id: 'answer-a', type: 'agentMessage', status: 'completed', text: 'Response A', createdAt: previous.createdAt }]
    }])
    useConversationStore.getState().onUserInputRequest('bridge', {
      threadId: 'thread', turnId: 'turn-a', requestId: 'input-a', isBlocking: true, questions: []
    })
    render(<LocaleProvider><ConversationPanel workspacePath="workspace" /></LocaleProvider>)
    expect(screen.getByRole('log')).toHaveTextContent('Response A')
    expect(screen.getByText('Current question')).toBeInTheDocument()

    act(() => {
      useThreadStore.getState().setActiveThreadId('other')
      useThreadStore.getState().setActiveThread(incoming)
    })
    expect(screen.queryByRole('log')).toBeNull()
    expect(screen.queryByText('Current question')).toBeNull()
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Send' })))
    expect(submitDraft).not.toHaveBeenCalled()

    act(() => {
      openThreadState('other')
      useConversationStore.getState().setTurns([{
        id: 'turn-b', threadId: 'other', status: 'completed', startedAt: incoming.createdAt,
        items: [{ id: 'answer-b', type: 'agentMessage', status: 'completed', text: 'Response B', createdAt: incoming.createdAt }]
      }])
    })
    expect(screen.getByRole('log')).toHaveTextContent('Response B')
    expect(screen.queryByText('Response A')).toBeNull()
    expect(screen.queryByText('Current question')).toBeNull()
  })

  it('preserves the loading composer until current waiting flags have their matching question envelope', async () => {
    const gate = beginThreadRestoration('thread')
    render(<LocaleProvider><ConversationPanel workspacePath="workspace" /></LocaleProvider>)
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Send' })))
    expect(submitDraft).not.toHaveBeenCalled()
    expect(submitRejected).toHaveBeenCalledWith(expect.any(Error))
    act(() => {
      useThreadStore.getState().applyRuntimeSnapshot('thread', {
        running: true, waitingOnApproval: false, waitingOnInput: true, waitingOnPlanConfirmation: false
      }, { isDesktopOrigin: true })
      finishThreadRestoration(gate)
    })
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Send' })))
    expect(submitDraft).not.toHaveBeenCalled()
    expect(submitRejected).toHaveBeenCalledWith(expect.any(Error))
    act(() => useConversationStore.getState().onUserInputRequest('bridge', {
      threadId: 'thread', turnId: 'turn', requestId: 'input', isBlocking: true,
      questions: [{ id: 'q', question: 'Continue?', options: [] }]
    }))
    expect(screen.getByText('Current question')).toBeInTheDocument()
    expect(screen.queryByLabelText('Draft')).toBeNull()
  })

  it('requires the current plan source and preserves explicit local consumption', () => {
    useConversationStore.setState({
      threadMode: 'plan', turnStatus: 'idle', restoredPlanTurnId: 'plan-turn',
      plan: { title: 'Plan', overview: '', content: 'Do it', todos: [] }
    })
    render(<LocaleProvider><ConversationPanel workspacePath="workspace" /></LocaleProvider>)
    expect(screen.getByText('Implement choice')).toBeInTheDocument()
    act(() => useUIStore.getState().dismissPlanApproval('plan-turn'))
    expect(screen.queryByText('Implement choice')).toBeNull()
    act(() => {
      useThreadStore.getState().setActiveThreadId('other')
      useThreadStore.getState().setActiveThreadId('thread')
    })
    expect(useUIStore.getState().planApprovalDismissed['plan-turn']).toBe(true)
  })
})
