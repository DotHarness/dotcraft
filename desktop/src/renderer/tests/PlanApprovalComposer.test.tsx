import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PlanApprovalComposer } from '../components/conversation/PlanApprovalComposer'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { ACCEPT_PLAN_SENTINEL_EN } from '../utils/planAcceptSentinel'

const appServerSendRequest = vi.fn()

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('PlanApprovalComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({ planApprovalDismissed: {} })
    useConversationStore.setState({ threadMode: 'plan', turnStatus: 'idle' })
    useThreadStore.setState({
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'turn/start') {
        return Promise.resolve({ turn: { id: 'turn-server-1' } })
      }
      return Promise.resolve({})
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: async () => ({ locale: 'en' }) },
        appServer: { sendRequest: appServerSendRequest },
        file: { readFile: vi.fn().mockResolvedValue('{}') },
        shell: { listEditors: vi.fn().mockResolvedValue([]) },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })
  })

  it('renders in ConversationPanel when CloseAgent follows a successful CreatePlan', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true }
    })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: {
        id: 'thread-1',
        userId: 'local',
        workspacePath: '<workspace>',
        displayName: 'Plan thread',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        metadata: {},
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString(),
        turns: []
      },
      loading: false
    })
    useConversationStore.setState({
      threadMode: 'plan',
      turnStatus: 'idle',
      turns: [{
        id: 'turn-plan',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-07-31T13:55:00.000Z',
        completedAt: '2026-07-31T13:56:00.000Z',
        items: [
          {
            id: 'item-plan',
            type: 'toolCall',
            status: 'completed',
            toolName: 'CreatePlan',
            toolCallId: 'call-plan',
            arguments: { plan: '# Repair plan' },
            success: true
          },
          {
            id: 'item-close',
            type: 'toolCall',
            status: 'completed',
            toolName: 'CloseAgent',
            toolCallId: 'call-close',
            success: true
          }
        ]
      }]
    })

    renderWithLocale(<ConversationPanel workspacePath="<workspace>" />)
    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.getByText('Implement this plan?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '1. Yes, implement this plan' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send message' })).not.toBeInTheDocument()
  })

  it('keeps decision pose while inheriting composer mascot effects', () => {
    const { container } = render(
      <LocaleProvider>
        <PlanApprovalComposer
          threadId="thread-1"
          workspacePath="<workspace>"
          turnId="turn-effects"
          mascotEffectState={{ reasoningEffort: 'high', speed: 'fast', contextMax: true }}
        />
      </LocaleProvider>
    )

    const mascot = container.querySelector('[data-mascot-effort]')
    expect(mascot).toHaveAttribute('data-mascot-effort', 'high')
    expect(mascot).toHaveAttribute('data-mascot-speed', 'fast')
    expect(mascot).toHaveAttribute('data-mascot-context', 'max')
    expect(mascot).toHaveClass('composer-mascot-hold-sign')
  })

  it('accept path switches to agent and sends hidden sentinel', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )

    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'turn/start',
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: ACCEPT_PLAN_SENTINEL_EN }]
        })
      )
    })
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).toBe(true)
  })

  it('empty submit accepts the plan', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-empty-submit" />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'turn/start',
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: ACCEPT_PLAN_SENTINEL_EN }]
        })
      )
    })
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useUIStore.getState().planApprovalDismissed['turn-empty-submit']).toBe(true)
  })

  it('reject path keeps plan mode and sends typed text', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-2" />
    )

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Please split this into two milestones.'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'turn/start',
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: 'Please split this into two milestones.' }]
        })
      )
    })
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useUIStore.getState().planApprovalDismissed['turn-2']).toBe(true)
  })

  it('moves selection between plan actions and focuses the adjustment input', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-arrows" />
    )

    expect(screen.getByRole('button', { name: '1. Yes, implement this plan' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByLabelText('Move selection up / Move selection down')).toBeInTheDocument()

    fireEvent.keyDown(window, { key: 'ArrowDown' })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: '2. No, tell DotCraft how to adjust' })).toHaveAttribute('aria-pressed', 'true')
    })
    await waitFor(() => {
      expect(screen.getByRole('textbox')).toHaveFocus()
    })

    fireEvent.keyDown(window, { key: 'ArrowUp' })

    expect(screen.getByRole('button', { name: '1. Yes, implement this plan' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('dismisses current plan approval on Escape', () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-3" />
    )

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(useUIStore.getState().planApprovalDismissed['turn-3']).toBe(true)
  })

  it('uses latest thread, workspace, and turn ids after rerender when accepting via keyboard', async () => {
    const initialThreadId = 'thread-1'
    const initialWorkspacePath = 'X:\\fixtures\\workspace'
    const initialTurnId = 'turn-1'
    const nextThreadId = 'thread-2'
    const nextWorkspacePath = 'F:\\another-workspace'
    const nextTurnId = 'turn-2'

    const { rerender } = render(
      <LocaleProvider>
        <PlanApprovalComposer
          threadId={initialThreadId}
          workspacePath={initialWorkspacePath}
          turnId={initialTurnId}
        />
      </LocaleProvider>
    )

    rerender(
      <LocaleProvider>
        <PlanApprovalComposer
          threadId={nextThreadId}
          workspacePath={nextWorkspacePath}
          turnId={nextTurnId}
        />
      </LocaleProvider>
    )

    fireEvent.keyDown(window, { key: '1' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: nextThreadId,
        mode: 'agent'
      })
    })
    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find(([method]) => method === 'turn/start')
      expect(turnStartCall).toBeDefined()
      const payload = turnStartCall?.[1] as {
        threadId: string
        identity?: { channelContext?: string; workspacePath?: string }
      }
      expect(payload.threadId).toBe(nextThreadId)
      expect(payload.identity?.channelContext).toBe(`workspace:${nextWorkspacePath}`)
      expect(payload.identity?.workspacePath).toBe(nextWorkspacePath)
    })
    expect(useUIStore.getState().planApprovalDismissed[nextTurnId]).toBe(true)
    expect(useUIStore.getState().planApprovalDismissed[initialTurnId]).not.toBe(true)
  })

  it('dismisses via clicking Esc hint button', () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-4" />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(useUIStore.getState().planApprovalDismissed['turn-4']).toBe(true)
  })
})
