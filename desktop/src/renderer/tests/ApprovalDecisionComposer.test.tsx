import './setupPluginRuntime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { ApprovalDecisionComposer } from '../components/conversation/ApprovalDecisionComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

const sendServerResponse = vi.fn()
const TEST_WORKSPACE = '<workspace>'

function pendingApproval(overrides: Partial<PendingApproval> = {}): PendingApproval {
  return {
    bridgeId: 'bridge-approval',
    threadId: 'thread-approval',
    turnId: 'turn-approval',
    requestId: 'request-approval',
    locallySubmittedDecision: null,
    itemId: 'approval-bridge-approval',
    approvalType: 'shell',
    operation: 'npm test',
    target: TEST_WORKSPACE,
    reason: 'DotCraft wants to run the test suite.',
    ...overrides
  }
}

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function setPendingApproval(request = pendingApproval()): void {
  useConversationStore.setState({
    turnStatus: 'waitingApproval',
    pendingApproval: request,
    pendingApprovals: [request]
  })
}

describe('ApprovalDecisionComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearDesktopPluginRegistry()
    sendServerResponse.mockResolvedValue({})
    installDesktopApiMock({
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({}),
          sendServerResponse
        },
        file: { readFile: vi.fn().mockResolvedValue('{}') },
        shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
        workspace: { saveImageToTemp: vi.fn() }
      })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      planApprovalDismissed: {}
    })
  })

  afterEach(() => {
    clearDesktopPluginRegistry()
  })

  it('keeps subscription status independent when workspace status is replaced', () => {
    const host = {
      plugin: { id: 'fixture.status', version: '1.0.0', displayName: 'Fixture status' }
    } as DesktopPluginHost
    registerDesktopPluginSurface(
      'fixture.status',
      host,
      'composer.status.workspace',
      'replace',
      () => <span>Custom workspace status</span>
    )
    registerDesktopPluginSurface(
      'fixture.status',
      host,
      'composer.status.subscription',
      'add',
      () => <span>Custom subscription status</span>
    )

    renderWithLocale(<ApprovalDecisionComposer request={pendingApproval()} />)

    expect(screen.getByText('Custom workspace status')).toBeInTheDocument()
    expect(screen.getByText('Custom subscription status')).toBeInTheDocument()
  })

  it('does not re-enter waitingApproval when a stale snapshot arrives after submission', async () => {
    const pending = pendingApproval()
    useConversationStore.setState({
      turns: [{
        id: 'turn-approval',
        threadId: 'thread-approval',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }],
      activeTurnId: 'turn-approval'
    })
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    fireEvent.keyDown(window, { key: 'Enter' })
    await waitFor(() => {
      expect(useConversationStore.getState().pendingApproval).toBeNull()
    })

    act(() => {
      useConversationStore.getState().setTurns([{
        id: 'turn-approval',
        threadId: 'thread-approval',
        status: 'waitingApproval',
        items: [],
        startedAt: new Date().toISOString()
      }], {
        preserveExistingRealtime: true,
        realtimeScopeThreadId: 'thread-approval'
      })
    })

    expect(useConversationStore.getState().turnStatus).toBe('running')
    expect(useConversationStore.getState().pendingApproval).toBeNull()
  })

  it('disables duplicate submits while sending and restores controls after failure', async () => {
    let rejectSend!: (err: Error) => void
    sendServerResponse.mockReturnValueOnce(new Promise((_resolve, reject) => {
      rejectSend = reject
    }))

    const pending = pendingApproval()
    const onResponseAccepted = vi.fn()
    setPendingApproval(pending)
    renderWithLocale(
      <ApprovalDecisionComposer request={pending} onResponseAccepted={onResponseAccepted} />
    )

    const primary = screen.getByRole('button', { name: 'Allow once' })
    fireEvent.click(primary)

    await waitFor(() => {
      expect(primary).toBeDisabled()
    })
    expect(useConversationStore.getState().pendingApproval?.locallySubmittedDecision).toBe('accept')
    fireEvent.click(primary)
    expect(sendServerResponse).toHaveBeenCalledTimes(1)

    await act(async () => {
      rejectSend(new Error('network down'))
    })

    await waitFor(() => {
      expect(primary).not.toBeDisabled()
    })
    expect(useConversationStore.getState().pendingApproval?.locallySubmittedDecision).toBeNull()
    expect(sendServerResponse).toHaveBeenCalledTimes(1)
    expect(onResponseAccepted).not.toHaveBeenCalled()

    fireEvent.click(primary)
    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledTimes(2)
    })
    expect(onResponseAccepted).toHaveBeenCalledTimes(1)
    expect(useConversationStore.getState().pendingApproval).toBeNull()
    expect(useConversationStore.getState().pendingApprovals).toHaveLength(0)
  })

  it('keeps the next approval enabled when the previous response resolves late', async () => {
    let resolveFirst!: () => void
    sendServerResponse.mockReturnValueOnce(new Promise<void>((resolve) => {
      resolveFirst = resolve
    }))

    const firstPending = pendingApproval({
      bridgeId: 'bridge-approval-1',
      requestId: 'request-approval-1',
      itemId: 'approval-bridge-approval-1',
      target: '<workspace>/first.md'
    })
    const secondPending = pendingApproval({
      bridgeId: 'bridge-approval-2',
      requestId: 'request-approval-2',
      itemId: 'approval-bridge-approval-2',
      target: '<workspace>/second.md'
    })

    setPendingApproval(firstPending)
    const { rerender } = render(
      <LocaleProvider><ApprovalDecisionComposer request={firstPending} /></LocaleProvider>
    )

    const firstPrimary = screen.getByRole('button', { name: 'Allow once' })
    fireEvent.click(firstPrimary)
    await waitFor(() => {
      expect(firstPrimary).toBeDisabled()
    })

    setPendingApproval(secondPending)
    rerender(<LocaleProvider><ApprovalDecisionComposer request={secondPending} /></LocaleProvider>)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Allow once' })).not.toBeDisabled()
    })
    expect(screen.getByRole('button', { name: 'More allow options' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reject approval' })).not.toBeDisabled()

    await act(async () => {
      resolveFirst()
    })

    expect(screen.getByRole('button', { name: 'Allow once' })).not.toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Allow once' }))

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval-2', { decision: 'accept' })
    })
  })

  it('advances to the next queued approval after a local decision without waiting for resolved notification', async () => {
    const firstPending = pendingApproval({
      bridgeId: 'bridge-approval-1',
      requestId: 'request-approval-1',
      itemId: 'approval-bridge-approval-1',
      target: '<workspace>/first.md'
    })
    const secondPending = pendingApproval({
      bridgeId: 'bridge-approval-2',
      requestId: 'request-approval-2',
      itemId: 'approval-bridge-approval-2',
      target: '<workspace>/second.md'
    })
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true }
    })
    useThreadStore.setState({
      activeThreadId: 'thread-approval',
      activeThread: {
        id: 'thread-approval',
        userId: 'local',
        workspacePath: TEST_WORKSPACE,
        displayName: 'Approval thread',
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
      activeTurnId: 'turn-approval',
      turnStatus: 'waitingApproval',
      pendingApproval: firstPending,
      pendingApprovals: [firstPending, secondPending]
    })

    renderWithLocale(<ConversationPanel workspacePath={TEST_WORKSPACE} />)
    expect(screen.getByText('<workspace>/first.md')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Allow once' }))

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval-1', { decision: 'accept' })
    })
    await waitFor(() => {
      expect(screen.getByText('<workspace>/second.md')).toBeInTheDocument()
    })

    expect(useConversationStore.getState().pendingApproval?.requestId).toBe('request-approval-2')
    expect(screen.getByRole('button', { name: 'Allow once' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'More allow options' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reject approval' })).not.toBeDisabled()
  })

  it('answers Enter with the narrowest grant when the request has no single-use option', async () => {
    const pending = pendingApproval({ approvalType: 'computerUse', operation: '', target: 'Figma', targetLabel: 'Figma' })
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval', { decision: 'acceptForSession' })
    })
  })

  it('does not resubmit an approval that was already submitted locally', () => {
    const pending = pendingApproval({ locallySubmittedDecision: 'accept' })
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    const primary = screen.getByRole('button', { name: 'Allow once' })
    expect(primary).toBeDisabled()

    fireEvent.keyDown(window, { key: 'Escape' })
    fireEvent.click(primary)

    expect(sendServerResponse).not.toHaveBeenCalled()
  })
})
