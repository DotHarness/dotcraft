import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubagentsTab } from '../components/detail/SubagentsTab'
import { useConnectionStore } from '../stores/connectionStore'
import { useSubAgentStore, type SubAgentChild } from '../stores/subAgentStore'
import { useSubAgentTranscriptStore } from '../stores/subAgentTranscriptStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()
let notify: (payload: { method: string; params: Record<string, unknown> }) => void = () => {}

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
    currentTool: null,
    model: null,
    contextTokens: 0,
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
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest: appServerSendRequest,
        onNotification: vi.fn((callback) => {
          notify = callback as typeof notify
          return () => { notify = () => {} }
        })
      }
    })
    useConnectionStore.getState().reset()
    useSubAgentStore.getState().reset()
    useSubAgentTranscriptStore.getState().reset()
    useThreadStore.getState().reset()
    // Leave subAgentSessions capability unset so the mount fetch is a no-op and
    // the seeded children remain the source of truth for the test.
    useThreadStore.setState({ activeThreadId: 'thread-1' })
  })

  it('shows an empty state when the thread has no subagents', () => {
    renderTab()
    expect(screen.getByText('No subagents yet')).toBeInTheDocument()
  })

  it('does not expose a destructive clear/close action', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-done',
        nickname: 'Babbage',
        status: 'completed',
        isCompleted: true,
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

    await act(async () => { renderTab() })

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
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()
    fireEvent.click(screen.getByRole('button', { name: /^Closed 1/ }))

    expect(screen.getByRole('button', { name: 'Open subagent Retired' })).toBeEnabled()
  })

  it('stops a running subagent by interrupting its active turn', async () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({
        childThreadId: 'child-running',
        nickname: 'Lovelace',
        runtime: { running: true, activeTurnId: 'turn-7', waitingOnApproval: false, waitingOnPlanConfirmation: false }
      })
    ])

    renderTab()
    appServerSendRequest.mockClear()
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Stop Lovelace' }))
    })

    expect(appServerSendRequest).toHaveBeenCalledWith('turn/interrupt', { threadId: 'child-running', turnId: 'turn-7' })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('subagent/close', expect.anything())
  })

  it('shows and advances the current turn elapsed time instead of last activity recency', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-24T01:02:03.000Z'))
    try {
      useSubAgentStore.getState().setChildren('thread-1', [
        makeChild({
          childThreadId: 'child-running',
          nickname: 'Lovelace',
          runtime: {
            running: true,
            activeTurnId: 'turn-1',
            activeTurnStartedAt: '2026-08-24T00:00:00.000Z',
            waitingOnApproval: false,
            waitingOnPlanConfirmation: false
          },
          threadSummary: {
            id: 'child-running',
            displayName: 'Lovelace',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-08-23T00:00:00.000Z',
            lastActiveAt: '2026-08-24T01:01:53.000Z'
          }
        })
      ])

      renderTab()

      expect(screen.getByText('1h 2m 3s')).toBeInTheDocument()
      expect(screen.queryByText('just now')).not.toBeInTheDocument()

      act(() => vi.advanceTimersByTime(1_000))
      expect(screen.getByText('1h 2m 4s')).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })

  it('does not fall back to last activity when a running turn start is unavailable', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-24T01:00:00.000Z'))
    try {
      useSubAgentStore.getState().setChildren('thread-1', [
        makeChild({
          childThreadId: 'child-running',
          nickname: 'Lovelace',
          runtime: {
            running: true,
            activeTurnId: null,
            activeTurnStartedAt: null,
            waitingOnApproval: false,
            waitingOnPlanConfirmation: false
          },
          threadSummary: {
            id: 'child-running',
            displayName: 'Lovelace',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-08-24T00:00:00.000Z',
            lastActiveAt: '2026-08-24T00:30:00.000Z'
          }
        })
      ])

      renderTab()

      expect(screen.queryByText('30m')).not.toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })

  it('opens the subagent transcript in the tab without leaving the parent thread', async () => {
    useConnectionStore.setState({ status: 'connected' })
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({ childThreadId: 'child-running', nickname: 'Lovelace' })
    ])

    renderTab()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Open subagent Lovelace' }))
    })

    expect(useThreadStore.getState().activeThreadId).toBe('thread-1')
    expect(useSubAgentStore.getState().selectedChildByParent.get('thread-1')).toBe('child-running')
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/subscribe', { threadId: 'child-running', replayRecent: true })
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', { threadId: 'child-running' })

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Back to subagents' }))
    })

    expect(useSubAgentStore.getState().selectedChildByParent.has('thread-1')).toBe(false)
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/unsubscribe', { threadId: 'child-running' })
  })

  it('streams the opened subagent\'s events into its transcript', async () => {
    useConnectionStore.setState({ status: 'connected' })
    useSubAgentStore.getState().setChildren('thread-1', [
      makeChild({ childThreadId: 'child-running', nickname: 'Lovelace' })
    ])
    useSubAgentStore.getState().selectChild('thread-1', 'child-running')

    await act(async () => { renderTab() })
    await act(async () => {
      notify({ method: 'turn/started', params: { threadId: 'child-running', turn: { id: 'turn-9', threadId: 'child-running', status: 'running' } } })
      notify({ method: 'item/started', params: { threadId: 'child-running', turnId: 'turn-9', item: { id: 'msg-1', type: 'agentMessage', status: 'started', createdAt: '2026-10-01T00:00:00.000Z' } } })
      notify({ method: 'item/agentMessage/delta', params: { threadId: 'child-running', turnId: 'turn-9', itemId: 'msg-1', delta: 'Reading ' } })
      notify({ method: 'item/agentMessage/delta', params: { threadId: 'child-running', turnId: 'turn-9', itemId: 'msg-1', delta: 'the atlas' } })
      notify({ method: 'item/agentMessage/delta', params: { threadId: 'other-thread', turnId: 'turn-1', itemId: 'msg-1', delta: 'ignored' } })
      await new Promise((resolve) => setTimeout(resolve, 80))
    })

    const transcript = useSubAgentTranscriptStore.getState().transcripts.get('child-running')
    expect(transcript?.live).toEqual({ itemId: 'msg-1', kind: 'agentMessage', text: 'Reading the atlas' })
    expect(transcript?.turns.find((turn) => turn.id === 'turn-9')?.items.map((item) => item.id)).toEqual(['msg-1'])
  })

  it('falls back to polling a running subagent when the thread cannot be subscribed', async () => {
    vi.useFakeTimers()
    try {
      useConnectionStore.setState({ status: 'connected' })
      appServerSendRequest.mockImplementation(async (method: string) => {
        if (method === 'thread/subscribe') throw new Error('unsupported')
        return {}
      })
      useSubAgentStore.getState().setChildren('thread-1', [
        makeChild({ childThreadId: 'child-running', nickname: 'Lovelace' })
      ])
      useSubAgentStore.getState().selectChild('thread-1', 'child-running')

      renderTab()
      await act(async () => { await vi.advanceTimersByTimeAsync(0) })
      appServerSendRequest.mockClear()

      await act(async () => { await vi.advanceTimersByTimeAsync(2000) })

      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', { threadId: 'child-running' })
    } finally {
      vi.useRealTimers()
    }
  })
})
