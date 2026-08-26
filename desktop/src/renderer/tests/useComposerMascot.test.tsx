import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerMascot } from '../components/conversation/useComposerMascot'
import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import { installDesktopApiMock } from './desktopApiMock'

const THREAD_ID = 'thread-mascot'

function approval(overrides: Partial<PendingApproval> = {}): PendingApproval {
  return {
    bridgeId: 'bridge-mascot-1',
    threadId: THREAD_ID,
    turnId: 'turn-mascot',
    requestId: 'request-mascot-1',
    locallySubmittedDecision: null,
    itemId: 'approval-mascot-1',
    approvalType: 'shell',
    operation: 'npm test',
    target: '<workspace>',
    reason: 'Run the test suite.',
    ...overrides
  }
}

function MascotHarness(): JSX.Element {
  const interaction = useComposerMascot({ threadId: THREAD_ID, workspacePath: '<workspace>' })
  const bubble = interaction?.bubble
  return (
    <div>
      {bubble && (
        <section aria-label="mascot bubble">
          <h1>{bubble.title}</h1>
          {bubble.body && <p>{bubble.body}</p>}
          {bubble.actions?.map((action) => (
            <button key={action.label} type="button" onClick={action.onClick}>
              {action.label}
            </button>
          ))}
        </section>
      )}
    </div>
  )
}

function renderHarness(): void {
  render(
    <LocaleProvider>
      <MascotHarness />
    </LocaleProvider>
  )
}

describe('useComposerMascot approval nudge', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    })
    useConversationStore.getState().reset()
  })

  it('does not show an approval bubble for a waiting status without an actionable request', () => {
    useConversationStore.setState({
      turnStatus: 'waitingApproval',
      pendingApproval: null,
      pendingApprovals: []
    })

    renderHarness()

    expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()
  })

  it('keeps the same approval dismissed across rerenders and replay, but shows a new request', async () => {
    const first = approval()
    useConversationStore.setState({
      turnStatus: 'waitingApproval',
      pendingApproval: first,
      pendingApprovals: [first]
    })

    renderHarness()

    expect(await screen.findByRole('heading', { name: 'Your approval is needed' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Got it' }))
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()
    })

    const replayed = { ...first, reason: 'Same request replayed with updated details.' }
    act(() => {
      useConversationStore.setState({
        pendingApproval: replayed,
        pendingApprovals: [replayed]
      })
    })
    expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()

    const second = approval({
      bridgeId: 'bridge-mascot-2',
      requestId: 'request-mascot-2',
      itemId: 'approval-mascot-2',
      reason: 'Run the next command.'
    })
    act(() => {
      useConversationStore.setState({
        pendingApproval: second,
        pendingApprovals: [second]
      })
    })

    expect(await screen.findByRole('heading', { name: 'Your approval is needed' })).toBeInTheDocument()
  })
})
