import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentDock } from '../components/conversation/SubAgentDock'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { getSubAgentAccent } from '../utils/subAgentPresentation'

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
                agentNickname: child.nickname,
                agentRole: child.agentRole,
                profileName: child.profileName,
                runtimeType: child.runtimeType,
                supportsSendInput: child.supportsSendInput,
                supportsResume: child.supportsResume,
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
        nickname: 'Lovelace',
        agentRole: 'explorer',
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
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
    const nickname = screen.getByText('Lovelace')
    expect(nickname).toBeInTheDocument()
    expect(nickname.getAttribute('style')).toContain('color:')
    expect(nickname).toHaveStyle({ color: getSubAgentAccent('child-1') })
    expect(nickname.parentElement?.querySelector('span[aria-hidden]')).toBeNull()
    expect(screen.getByText('(explorer)')).toBeInTheDocument()
    expect(screen.queryByText(/codex-cli/)).toBeNull()
    const dockStyle = screen.getByTestId('subagent-dock').getAttribute('style') ?? ''
    expect(dockStyle).toContain('width: calc(100% - 40px)')
    expect(dockStyle).toContain('max-width: none')
    expect(dockStyle).toContain('margin: 0px auto -1px')
    expect(dockStyle).toContain('backdrop-filter: var(--glass-blur-soft)')
    expect(dockStyle).toContain('background: linear-gradient')
    expect(dockStyle).toContain('transparent')
    expect(dockStyle).toContain('box-shadow: var(--background-activity-dock-shadow)')
    expect(screen.getByTestId('subagent-dock-rows').getAttribute('style')).toContain('transition:')
    expect(screen.getByTestId('subagent-dock-rows').getAttribute('style')).toContain('max-height:')
    const description = screen.getByText('Reading sprite atlas')
    expect(description).toHaveClass('tool-running-gradient-text')

    fireEvent.click(screen.getByRole('button', { name: 'Open' }))

    expect(useThreadStore.getState().activeThreadId).toBe('child-1')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('fetches current children when mounted for the active parent thread', async () => {
    renderDock()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: true,
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
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    expect(screen.getByText('Reading sprite atlas')).toHaveClass('tool-running-gradient-text')
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

    await waitFor(() => {
      expect(screen.getByText('Alias child')).toBeInTheDocument()
      expect(screen.getByText('(explorer)')).toBeInTheDocument()
    })
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

    fireEvent.click(screen.getByText('1 background agents'))

    await waitFor(() => {
      expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
      expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBe(true)
    })

    expect(screen.getByText('1 running')).toBeInTheDocument()
    expect(screen.getByTestId('subagent-dock-rows').getAttribute('style')).toContain('max-height: 0px')
    expect(screen.getByTestId('subagent-dock-rows').getAttribute('style')).toContain('opacity: 0')
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Expand background agents' }))

    await waitFor(() => {
      expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(false)
      expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBeUndefined()
    })
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
  })

  it('stops closeable running children through subagent/close', async () => {
    renderDock()

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).not.toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Stop all background agents' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/close', {
        parentThreadId: 'parent-1',
        childThreadId: 'child-1'
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: true,
        includeThreads: true
      })
    })
    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).not.toBe(true)
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
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    const description = screen.getByText('Completed')
    expect(description).not.toHaveClass('tool-running-gradient-text')
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
    expect(screen.getByText('Lovelace')).toBeInTheDocument()
    expect(screen.getByText('Completed')).not.toHaveClass('tool-running-gradient-text')
    expect(screen.queryByTestId('subagent-dock-running-child-1')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop all background agents' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
  })

  it('updates a running child to completed without hiding the dock', async () => {
    renderDock()

    expect(screen.getByText('Reading sprite atlas')).toHaveClass('tool-running-gradient-text')
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'parent-1',
        includeClosed: true,
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
    expect(screen.getByTestId('subagent-dock-rows').getAttribute('style')).toContain('max-height: 0px')
  })
})
