import './setupPluginRuntime'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PlanApprovalComposer } from '../components/conversation/PlanApprovalComposer'
import { TurnStatusSeat } from '../components/conversation/TurnStatusSeat'
import { TodoCard } from '../../bundled-plugins/todo-list/src/TodoCard'
import { clearDesktopPluginRegistry, registerDesktopPluginSurface } from '../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../stores/connectionStore'
import { getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { applyThreadNotification } from '../stores/threadNotifications'
import { installDesktopApiMock } from './desktopApiMock'
import { streamingToolCallStarted } from './testToolPresentation'

const appServerSendRequest = vi.fn()

function LatestTurnStatus(): JSX.Element | null {
  const turn = useConversationStore((state) => state.turns.at(-1))
  return turn ? <TurnStatusSeat turn={turn} running={turn.status === 'running'} /> : null
}

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}<LatestTurnStatus /></LocaleProvider>)
}

function notifyMode(mode: 'agent' | 'plan'): void {
  applyThreadNotification({ method: 'thread/settings/updated', params: { threadId: 'thread-1', mode } }, {
    locale: 'en', foreground: true,
    updateWarmThread() {}, updateWarmThreadMode() {}, releaseWarmThread() {},
    reloadThreadList: async () => {}
  })
}

function deferred() {
  let resolve!: (value: unknown) => void
  let reject!: (error: Error) => void
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

describe('PlanApprovalComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    releaseThreadState('thread-1')
    releaseThreadState('thread-2')
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    openThreadState('thread-1')
    useUIStore.setState({ planApprovalDismissed: {} })
    useConversationStore.setState({
      threadMode: 'plan', turnStatus: 'idle',
      plan: {
        title: 'Plan', content: 'Ship it', overview: '',
        todos: [{ id: 'todo-1', content: 'Ship it', status: 'pending' }]
      }
    })
    registerDesktopPluginSurface(
      'todo-list', { environment: { locale: 'en' } } as DesktopPluginHost, 'conversation.turn.status', 'add', TodoCard
    )
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      threadList: [{
        id: 'thread-1', displayName: 'Thread', status: 'active', originChannel: 'dotcraft',
        createdAt: new Date().toISOString(), lastActiveAt: new Date().toISOString()
      }]
    })
    appServerSendRequest.mockReset().mockImplementation((method: string) => {
      if (method === 'turn/start') return Promise.resolve({ turn: { id: 'turn-server-1' } })
      return Promise.resolve({})
    })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: appServerSendRequest },
      file: { readFile: vi.fn().mockResolvedValue('{}') },
      shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
      workspace: { saveImageToTemp: vi.fn() }
    })
  })

  afterEach(() => act(clearDesktopPluginRegistry))

  it('shows the approved tasks before the execution turn is confirmed and keeps them on confirmation', async () => {
    const response = deferred()
    appServerSendRequest.mockReturnValueOnce(response.promise)
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )

    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))

    expect(appServerSendRequest).toHaveBeenCalledExactlyOnceWith('turn/start', expect.objectContaining({
      threadId: 'thread-1', mode: 'agent', input: [{ type: 'text', text: 'Implement the plan.' }]
    }))
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
    expect(screen.getByRole('region', { name: 'To-dos, 0 of 1 done' })).toBeInTheDocument()

    const clientUserMessageId = appServerSendRequest.mock.calls[0][1].clientUserMessageId
    act(() => useConversationStore.getState().onTurnStarted({
      id: 'turn-server-1', threadId: 'thread-1', status: 'running',
      items: [{ id: 'message-server-1', type: 'userMessage', text: 'Implement the plan.', clientUserMessageId }]
    }))
    expect(screen.getByRole('region', { name: 'To-dos, 0 of 1 done' })).toBeInTheDocument()

    await act(async () => {
      notifyMode('agent')
      response.resolve({ turn: { id: 'turn-server-1' } })
    })

    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).toBe(true)
    expect(useConversationStore.getState().turns).toEqual([
      expect.objectContaining({
        id: 'turn-server-1',
        items: [expect.objectContaining({ type: 'userMessage', text: 'Implement the plan.' })]
      })
    ])
  })

  it('rolls back the execution checklist after rejected admission and allows retry', async () => {
    const response = deferred()
    appServerSendRequest.mockReturnValueOnce(response.promise)
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    expect(screen.getByRole('region', { name: 'To-dos, 0 of 1 done' })).toBeInTheDocument()

    await act(async () => { response.reject(new Error('turn rejected')) })
    expect(screen.queryByRole('region', { name: /To-dos/ })).not.toBeInTheDocument()
    expect(useConversationStore.getState().turns).toEqual([])
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)

    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    await waitFor(() => expect(useUIStore.getState().planApprovalDismissed['turn-1']).toBe(true))
    expect(appServerSendRequest.mock.calls.map(([method]) => method)).toEqual(['turn/start', 'turn/start'])
  })

  it('keeps the completed checklist on the updating turn but not on an ordinary follow-up', () => {
    const turn = { id: 'turn-todos', threadId: 'thread-1', status: 'running', items: [] }
    const conversation = useConversationStore.getState()
    conversation.onTurnStarted(turn)
    renderWithLocale(<></>)

    act(() => {
      conversation.onItemStarted(streamingToolCallStarted(turn.id, 'todo-write', 'TodoWrite', 'todo-call'))
      conversation.onPlanUpdated({ title: '', todos: [{ id: 'todo-1', content: 'Ship it', status: 'completed' }] })
      conversation.onItemCompleted({
        turnId: turn.id,
        item: { id: 'todo-result', type: 'toolResult', payload: { callId: 'todo-call', success: true, result: 'Plan updated' } }
      })
    })
    expect(screen.getByRole('region', { name: 'To-dos, 1 of 1 done' })).toBeInTheDocument()

    act(() => conversation.onTurnCompleted({ ...turn, status: 'completed' }))
    expect(screen.queryByRole('region', { name: /To-dos/ })).not.toBeInTheDocument()

    act(() => conversation.onTurnStarted({ ...turn, id: 'turn-follow-up' }))
    expect(screen.queryByRole('region', { name: /To-dos/ })).not.toBeInTheDocument()
  })

  it('does not submit a stale plan after the selected thread changes', () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )
    act(() => useThreadStore.getState().setActiveThreadId('thread-2'))
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    expect(appServerSendRequest).not.toHaveBeenCalled()
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
  })

  it('does not modify the next selected conversation when an accepted request completes', async () => {
    const response = deferred()
    appServerSendRequest.mockReturnValueOnce(response.promise)
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="/fixture" turnId="turn-1" />
    )
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    act(() => {
      openThreadState('thread-2')
      useThreadStore.getState().setActiveThreadId('thread-2')
      useConversationStore.setState({ threadMode: 'plan', turnStatus: 'idle' })
    })
    await act(async () => { response.resolve({ turn: { id: 'turn-server-1' } }) })

    expect(useConversationStore.getState().turns).toEqual([])
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
    expect(getThreadConversation('thread-1')?.getState().turns[0]?.id).toBe('turn-server-1')
  })

  it('keeps plan mode when submitting an adjustment', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-2" />
    )
    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Please split this into two milestones.'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(useUIStore.getState().planApprovalDismissed['turn-2']).toBe(true))
    expect(appServerSendRequest).toHaveBeenCalledExactlyOnceWith('turn/start', expect.objectContaining({
      threadId: 'thread-1', input: [{ type: 'text', text: 'Please split this into two milestones.' }]
    }))
    expect(appServerSendRequest.mock.calls[0][1]).not.toHaveProperty('mode')
    expect(useConversationStore.getState().threadMode).toBe('plan')
  })

  it('restores an adjustment after rejected admission without dismissing the plan', async () => {
    appServerSendRequest.mockRejectedValueOnce(new Error('turn rejected'))
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="/fixture" turnId="turn-2" />
    )
    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Keep this adjustment.'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(textbox.textContent).toBe('Keep this adjustment.'))
    expect(useUIStore.getState().planApprovalDismissed['turn-2']).not.toBe(true)
    expect(useConversationStore.getState().turns).toEqual([])
  })

  it('uses the latest thread and turn after rerender when accepting via keyboard', async () => {
    const { rerender } = render(
      <LocaleProvider>
        <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
      </LocaleProvider>
    )
    act(() => useThreadStore.getState().setActiveThreadId('thread-2'))
    rerender(
      <LocaleProvider>
        <PlanApprovalComposer threadId="thread-2" workspacePath="F:\\another-workspace" turnId="turn-2" />
      </LocaleProvider>
    )
    fireEvent.keyDown(window, { key: '1' })

    await waitFor(() => expect(useUIStore.getState().planApprovalDismissed['turn-2']).toBe(true))
    expect(appServerSendRequest).toHaveBeenCalledExactlyOnceWith('turn/start', expect.objectContaining({
      threadId: 'thread-2', mode: 'agent'
    }))
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
  })
})
