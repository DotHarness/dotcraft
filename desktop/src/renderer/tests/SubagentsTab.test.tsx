import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubagentsTab } from '../components/detail/SubagentsTab'
import { useConnectionStore } from '../stores/connectionStore'
import { useSubAgentStore, type SubAgentChild } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()

function makeChild(overrides: Partial<SubAgentChild> & Pick<SubAgentChild, 'childThreadId' | 'nickname'>): SubAgentChild {
  return {
    parentThreadId: 'thread-1',
    agentPath: `/root/${overrides.childThreadId}`,
    taskName: null,
    agentRole: null,
    profileName: 'native',
    runtimeType: 'native',
    supportsSendInput: true,
    supportsResume: true,
    supportsClose: true,
    status: 'open',
    lastToolDisplay: null,
    lastMessagePreview: null,
    currentTool: null,
    inputTokens: 0,
    outputTokens: 0,
    isCompleted: false,
    runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false },
    ...overrides
  }
}

function renderTab(): void {
  render(
    <LocaleProvider>
      <SubagentsTab />
    </LocaleProvider>
  )
}

describe('SubagentsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockResolvedValue({})
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        appServer: { sendRequest: appServerSendRequest }
      }
    })
    useConnectionStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    // Leave subAgentSessions capability unset so the mount fetch is a no-op and
    // the seeded children remain the source of truth for the test.
    useThreadStore.setState({ activeThreadId: 'thread-1' })
  })

  it('shows an empty state when the thread has no subagents', () => {
    renderTab()
    expect(screen.getByText('No subagents yet')).toBeInTheDocument()
  })

  it('splits subagents into Active and Done sections', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-running',
        nickname: 'Lovelace',
        lastToolDisplay: 'Reading atlas'
      }),
      makeChild({
        childThreadId: 'child-done',
        nickname: 'Babbage',
        status: 'completed',
        isCompleted: true,
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()

    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByText('Done')).toBeInTheDocument()
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    expect(screen.getByText('Babbage')).toBeInTheDocument()
  })

  it('does not expose a destructive clear/close action', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-done',
        nickname: 'Babbage',
        status: 'completed',
        isCompleted: true,
        lastMessagePreview: 'Done researching the topic.',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()

    expect(screen.queryByRole('button', { name: 'Clear done' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop' })).not.toBeInTheDocument()
    // No subagent/close request should be reachable from this panel.
    expect(appServerSendRequest).not.toHaveBeenCalledWith('subagent/close', expect.anything())
  })

  it('requests closed edges so the panel can show closed subagents', async () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'subagent/children/list') return { data: [] }
      return {}
    })

    renderTab()

    await vi.waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'thread-1',
        includeClosed: true,
        includeThreads: true
      })
    })
  })

  it('keeps closed subagents visible in a Closed section for read-only review', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-closed',
        nickname: 'Retired',
        status: 'closed',
        isCompleted: true,
        lastMessagePreview: 'Final summary before it was closed.',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()

    expect(screen.getByText('Closed')).toBeInTheDocument()
    expect(screen.getByText('Retired')).toBeInTheDocument()
    // Still openable for review — the row is a live button, not disabled.
    expect(screen.getByRole('button', { name: 'Open subagent Retired' })).toBeEnabled()
  })

  it('shows the last message preview for a finished subagent', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-done',
        nickname: 'Babbage',
        status: 'completed',
        isCompleted: true,
        lastMessagePreview: 'Budget research complete and reported back.',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()

    expect(screen.getByText(/Budget research complete and reported back\./)).toBeInTheDocument()
  })

  it('shows the live agent message for a running subagent instead of "Running"', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-running',
        nickname: 'Lovelace',
        lastMessagePreview: 'Currently analyzing the deployment scripts',
        lastToolDisplay: 'Reading atlas'
      })
    ])

    renderTab()

    expect(screen.getByText('Currently analyzing the deployment scripts')).toBeInTheDocument()
    expect(screen.queryByText('Running')).not.toBeInTheDocument()
  })

  it('falls back to tool progress, then Running, when a running subagent has no message', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({ childThreadId: 'child-tool', nickname: 'ToolOnly', lastToolDisplay: 'Reading atlas' }),
      makeChild({ childThreadId: 'child-bare', nickname: 'Bare' })
    ])

    renderTab()

    expect(screen.getByText('Reading atlas')).toBeInTheDocument()
    expect(screen.getByText('Running')).toBeInTheDocument()
  })

  it('polls thread/read for running subagents while the tab is open', () => {
    vi.useFakeTimers()
    try {
      useSubAgentStore.getState().setChildren('thread-1', [
        makeChild({ childThreadId: 'child-running', nickname: 'Lovelace', lastMessagePreview: 'Working' })
      ])

      renderTab()
      appServerSendRequest.mockClear()

      vi.advanceTimersByTime(3000)

      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId: 'child-running',
        includeTurns: true
      })
    } finally {
      vi.useRealTimers()
    }
  })

  it('does not poll when no subagent is running', () => {
    vi.useFakeTimers()
    try {
      useSubAgentStore.getState().setChildren('thread-1', [
        makeChild({
          childThreadId: 'child-done',
          nickname: 'Babbage',
          status: 'completed',
          isCompleted: true,
          lastMessagePreview: 'All done.',
          runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
        })
      ])

      renderTab()
      appServerSendRequest.mockClear()

      vi.advanceTimersByTime(9000)

      expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/read', expect.anything())
    } finally {
      vi.useRealTimers()
    }
  })

  it('opens the child thread when the whole row is clicked', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({ childThreadId: 'child-running', nickname: 'Lovelace' })
    ])

    renderTab()

    fireEvent.click(screen.getByRole('button', { name: 'Open subagent Lovelace' }))

    expect(useThreadStore.getState().activeThreadId).toBe('child-running')
  })
})
