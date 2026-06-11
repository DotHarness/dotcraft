import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentDock } from '../components/conversation/SubAgentDock'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderDock(): void {
  render(
    <LocaleProvider>
      <SubAgentDock parentThreadId="parent-1" />
    </LocaleProvider>
  )
}

describe('SubAgentDock', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    useConnectionStore.getState().reset()
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'subagent/children/list') {
        const parentThreadId = typeof params?.parentThreadId === 'string' ? params.parentThreadId : 'parent-1'
        const children = useSubAgentStore.getState().childrenByParent.get(parentThreadId) ?? []
        return {
          data: children
            .filter((child) => child.isPlaceholder !== true)
            .map((child) => ({
              edge: {
                parentThreadId,
                childThreadId: child.childThreadId,
                agentPath: child.agentPath,
                taskName: child.taskName,
                agentNickname: child.nickname,
                agentRole: child.agentRole,
                profileName: child.profileName,
                runtimeType: child.runtimeType,
                supportsSendInput: child.supportsSendInput,
                supportsResume: child.supportsResume,
                supportsSendMessage: child.supportsSendMessage,
                supportsFollowupTask: child.supportsFollowupTask,
                supportsClose: child.supportsClose,
                status: child.status
              },
              thread: child.threadSummary ?? {
                id: child.childThreadId,
                displayName: child.nickname,
                status: 'active',
                originChannel: 'subagent',
                createdAt: '2026-05-03T00:00:00.000Z',
                lastActiveAt: '2026-05-03T00:01:00.000Z',
                runtime: child.runtime
              }
            }))
        }
      }
      return {}
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest }
      }
    })

    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({ activeMainView: 'settings' })
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        agentPath: '/root/lovelace',
        taskName: 'lovelace',
        nickname: 'Lovelace',
        agentRole: 'explorer',
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsSendMessage: true,
        supportsFollowupTask: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
  })

  it('renders running child status above the composer and opens the child thread', () => {
    renderDock()

    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.getByText('1 running')).toBeInTheDocument()
    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(false)
    const nickname = screen.getByText('Lovelace')
    expect(nickname).toBeInTheDocument()
    expect(nickname.parentElement?.querySelector('span[aria-hidden]')).toBeNull()
    expect(screen.getByText('(explorer)')).toBeInTheDocument()
    expect(screen.queryByText(/codex-cli/)).toBeNull()
    const description = screen.getByText('Reading sprite atlas')
    expect(description).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Open' }))

    expect(useThreadStore.getState().activeThreadId).toBe('child-1')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('fetches current children when mounted for the active parent thread', async () => {
    renderDock()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: false,
        includeThreads: true
      })
    })
  })

  it('renders placeholder progress without thread actions before hydration', () => {
    useSubAgentStore.getState().reset()
    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Lovelace',
        isCompleted: false,
        inputTokens: 12,
        outputTokens: 34,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])

    renderDock()

    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.getByText('1 running')).toBeInTheDocument()
    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    expect(screen.getByText('Reading sprite atlas')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop Lovelace' })).not.toBeInTheDocument()
  })

  it('hydrates role aliases into the dock role badge', async () => {
    useSubAgentStore.getState().reset()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'subagent/children/list') {
        return {
          data: [
            {
              edge: {
                parentThreadId: 'parent-1',
                childThreadId: 'child-alias',
                agentNickname: 'Alias child',
                agentType: 'explorer',
                status: 'open'
              },
              thread: {
                id: 'child-alias',
                displayName: 'Alias child',
                status: 'active',
                originChannel: 'subagent',
                createdAt: '2026-05-03T00:00:00.000Z',
                lastActiveAt: '2026-05-03T00:01:00.000Z',
                runtime: {
                  running: false,
                  waitingOnApproval: false,
                  waitingOnPlanConfirmation: false
                }
              }
            }
          ]
        }
      }
      return {}
    })

    renderDock()

    await screen.findByText('1 background agents')
    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    expect(screen.getByText('Alias child')).toBeInTheDocument()
    expect(screen.getByText('(explorer)')).toBeInTheDocument()
  })

  it('does not render a role badge for default or empty roles', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-default',
        parentThreadId: 'parent-1',
        nickname: 'Default child',
        agentRole: 'default',
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'completed',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderDock()

    expect(screen.getByText('Default child')).toBeInTheDocument()
    expect(screen.queryByText('(default)')).toBeNull()
    expect(screen.queryByText(/codex-cli/)).toBeNull()
  })

  it('keeps rows collapsed after a user collapse even while a child is running', async () => {
    renderDock()

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
    expect(screen.getByText('1 running')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    await waitFor(() => {
      expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(false)
      expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBeUndefined()
    })
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Collapse background agents' }))

    await waitFor(() => {
      expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
      expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBe(true)
    })
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
  })

  it('stops closeable running children through subagent/close', async () => {
    renderDock()

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Stop all background agents' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/close', {
        parentThreadId: 'parent-1',
        target: '/root/lovelace'
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: false,
        includeThreads: true
      })
    })
    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
  })

  it('does not expose manual mailbox or follow-up controls in the dock', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        agentPath: '/root/lovelace',
        taskName: 'lovelace',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: false,
        supportsResume: false,
        supportsSendMessage: true,
        supportsFollowupTask: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    renderDock()

    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send mailbox message to Lovelace' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start follow-up task for Lovelace' })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'subagent/sendMessage')).toBe(false)
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'subagent/followupTask')).toBe(false)
  })

  it('keeps closed, pathless, and unsupported child rows without manual controls', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'closed-child',
        parentThreadId: 'parent-1',
        agentPath: '/root/closed_child',
        taskName: 'closed_child',
        nickname: 'Closed child',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: false,
        supportsResume: false,
        supportsSendMessage: true,
        supportsFollowupTask: true,
        supportsClose: true,
        status: 'closed',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: true
      },
      {
        childThreadId: 'pathless-child',
        parentThreadId: 'parent-1',
        agentPath: null,
        taskName: null,
        nickname: 'Pathless child',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: false,
        supportsResume: false,
        supportsSendMessage: true,
        supportsFollowupTask: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: true
      },
      {
        childThreadId: 'unsupported-child',
        parentThreadId: 'parent-1',
        agentPath: '/root/unsupported_child',
        taskName: 'unsupported_child',
        nickname: 'Unsupported child',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: false,
        supportsResume: false,
        supportsSendMessage: false,
        supportsFollowupTask: false,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: true
      }
    ])

    renderDock()

    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    expect(screen.queryByRole('button', { name: 'Send mailbox message to Closed child' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start follow-up task for Closed child' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send mailbox message to Pathless child' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start follow-up task for Pathless child' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send mailbox message to Unsupported child' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start follow-up task for Unsupported child' })).not.toBeInTheDocument()
  })

  it('keeps completed child rows visible as openable history entries', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        status: 'completed',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: null,
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderDock()

    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    const description = screen.getByText('Completed')
    expect(description).toBeInTheDocument()
    expect(screen.queryByTestId('subagent-dock-running-child-1')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop all background agents' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop Lovelace' })).not.toBeInTheDocument()
  })

  it('does not show open child edges as running when thread runtime is stopped', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: null,
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderDock()

    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    expect(screen.getByText('Completed')).toBeInTheDocument()
    expect(screen.queryByTestId('subagent-dock-running-child-1')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop all background agents' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
  })

  it('updates a running child to completed without hiding the dock', async () => {
    renderDock()

    expect(screen.getByText('1 running')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))
    expect(screen.getByText('Reading sprite atlas')).toBeInTheDocument()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: false,
        includeThreads: true
      })
    })

    act(() => {
      useSubAgentStore.getState().updateChildRuntime('child-1', {
        running: false,
        waitingOnApproval: false,
        waitingOnPlanConfirmation: false
      })
    })

    await waitFor(() => {
      expect(screen.getByText('Completed')).toBeInTheDocument()
    })
    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.queryByTestId('subagent-dock-running-child-1')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop all background agents' })).not.toBeInTheDocument()
  })

  it('does not show a collapsed running summary when all children are completed', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        status: 'closed',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: null,
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    useSubAgentStore.getState().setParentCollapsed('parent-1', true)

    renderDock()

    expect(screen.getByText('1 background agents')).toBeInTheDocument()
    expect(screen.queryByText('1 running')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
  })
})
