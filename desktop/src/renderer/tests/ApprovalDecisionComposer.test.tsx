import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { ApprovalDecisionComposer } from '../components/conversation/ApprovalDecisionComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const sendServerResponse = vi.fn()

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
    target: 'F:\\dotcraft',
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
    pendingApproval: request
  })
}

describe('ApprovalDecisionComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sendServerResponse.mockResolvedValue({})
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({}),
          sendServerResponse
        },
        file: { readFile: vi.fn().mockResolvedValue('{}') },
        shell: { listEditors: vi.fn().mockResolvedValue([]) },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      planApprovalDismissed: {}
    })
  })

  it('renders in ConversationPanel instead of the normal composer', async () => {
    const pending = pendingApproval()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true }
    })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: {
        id: 'thread-1',
        userId: 'local',
        workspacePath: 'F:\\dotcraft',
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
    setPendingApproval(pending)

    renderWithLocale(<ConversationPanel workspacePath="F:\\dotcraft" />)
    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.getByText('Allow this command?')).toBeInTheDocument()
    expect(screen.getByText('npm test')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send message' })).not.toBeInTheDocument()
  })

  it('submits the default accept decision with Enter', async () => {
    const pending = pendingApproval()
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval', { decision: 'accept' })
    })
    expect(useConversationStore.getState().pendingApproval?.locallySubmittedDecision).toBe('accept')
  })

  it('uses number keys and Arrow keys to submit the selected decision', async () => {
    const pending = pendingApproval()
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    fireEvent.keyDown(window, { key: '3' })
    expect(screen.getByRole('button', { name: 'Always allow' })).toBeInTheDocument()
    fireEvent.keyDown(window, { key: 'ArrowDown' })
    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval', { decision: 'decline' })
    })
  })

  it('clicking an unselected option selects it, then clicking it again submits', async () => {
    const pending = pendingApproval()
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    fireEvent.click(screen.getByRole('button', { name: '5. Cancel turn' }))
    expect(sendServerResponse).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '5. Cancel turn' }))

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval', { decision: 'cancel' })
    })
  })

  it('sends decline with Escape and the footer reject action', async () => {
    const pending = pendingApproval()
    setPendingApproval(pending)
    const { unmount } = render(<LocaleProvider><ApprovalDecisionComposer request={pending} /></LocaleProvider>)

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval', { decision: 'decline' })
    })

    unmount()
    vi.clearAllMocks()
    sendServerResponse.mockResolvedValue({})

    const secondPending = pendingApproval({ bridgeId: 'bridge-approval-2', itemId: 'approval-bridge-approval-2' })
    setPendingApproval(secondPending)
    renderWithLocale(<ApprovalDecisionComposer request={secondPending} />)
    fireEvent.click(screen.getByRole('button', { name: 'Reject approval' }))

    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledWith('bridge-approval-2', { decision: 'decline' })
    })
  })

  it('renders a browser-use (generic) approval and routes the decision to its submit handler', async () => {
    const submit = vi.fn().mockResolvedValue(undefined)
    const request: PendingApproval = {
      bridgeId: '',
      threadId: null,
      turnId: null,
      requestId: 'browser-req-1',
      locallySubmittedDecision: null,
      itemId: '',
      approvalType: 'remoteResource',
      operation: '',
      target: '',
      reason: '',
      source: 'browserUse',
      question: 'The agent wants to open example.com.',
      detailRows: [{ label: 'Address', value: 'https://example.com', mono: true }],
      declineValue: 'deny',
      options: [
        { value: 'allowDomain', label: 'Always allow', description: '' },
        { value: 'allowOnce', label: 'Allow once', description: '' },
        { value: 'blockDomain', label: 'Block domain', description: '' },
        { value: 'deny', label: 'Cancel', description: '' }
      ],
      submit
    }
    renderWithLocale(<ApprovalDecisionComposer request={request} />)

    expect(screen.getByText('The agent wants to open example.com.')).toBeInTheDocument()
    expect(screen.getByText('https://example.com')).toBeInTheDocument()

    // Select the third option (Block domain) and submit it.
    fireEvent.keyDown(window, { key: '3' })
    fireEvent.keyDown(window, { key: 'Enter' })

    await waitFor(() => {
      expect(submit).toHaveBeenCalledWith('blockDomain')
    })
    expect(sendServerResponse).not.toHaveBeenCalled()
  })

  it('updates the primary button label with the current option', () => {
    const pending = pendingApproval()
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

    expect(screen.getByRole('button', { name: 'Allow once' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reject approval' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '4. Reject' }))
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject approval' })).not.toBeInTheDocument()
  })

  it('disables duplicate submits while sending and restores controls after failure', async () => {
    let rejectSend!: (err: Error) => void
    sendServerResponse.mockReturnValueOnce(new Promise((_resolve, reject) => {
      rejectSend = reject
    }))

    const pending = pendingApproval()
    setPendingApproval(pending)
    renderWithLocale(<ApprovalDecisionComposer request={pending} />)

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

    fireEvent.click(primary)
    await waitFor(() => {
      expect(sendServerResponse).toHaveBeenCalledTimes(2)
    })
    expect(useConversationStore.getState().pendingApproval?.locallySubmittedDecision).toBe('accept')
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
