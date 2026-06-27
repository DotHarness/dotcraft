import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ComposerWorkspaceFooter } from '../components/conversation/ComposerWorkspaceFooter'
import { useConnectionStore } from '../stores/connectionStore'
import { normalizeGitPathKey, useGitStore, type GitBranchListSnapshot } from '../stores/gitStore'
import { usePerforceChangelistStore } from '../stores/perforceChangelistStore'
import { useSourceControlStore } from '../stores/sourceControlStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { Thread } from '../types/thread'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const gitListBranches = vi.fn()
const gitCheckoutBranch = vi.fn()
const gitCreateAndCheckoutBranch = vi.fn()

function branchSnapshot(current: string): GitBranchListSnapshot {
  return {
    current,
    detachedHead: null,
    branches: [
      { name: 'main', current: current === 'main' },
      { name: 'feat/example', current: current === 'feat/example' }
    ]
  }
}

function makeThread(overrides: Partial<Thread> = {}): Thread {
  return {
    id: 'thread-1',
    userId: 'local',
    workspacePath: 'fixtures\\sample-app',
    effectiveWorkspacePath: 'fixtures\\sample-app',
    displayName: 'Thread',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActiveAt: '2026-01-01T00:00:00.000Z',
    metadata: {},
    turns: [],
    ...overrides
  }
}

function makeWorktreeThread(): Thread {
  return makeThread({
    effectiveWorkspacePath: 'fixtures\\sample-app\\.craft\\worktrees\\dotcraft-handoff',
    worktree: {
      id: 'worktree-1',
      sourceThreadId: 'thread-1',
      workspacePath: 'fixtures\\sample-app',
      sourceWorkspacePath: 'fixtures\\sample-app',
      path: 'fixtures\\sample-app\\.craft\\worktrees\\dotcraft-handoff',
      branchName: 'dotcraft/handoff',
      baseRef: 'main',
      head: 'abc123',
      createdAt: '2026-01-01T00:00:00.000Z'
    }
  })
}

function makeRuntime(overrides: Partial<NonNullable<Thread['runtime']>> = {}): NonNullable<Thread['runtime']> {
  return {
    running: true,
    busy: false,
    waitingOnApproval: false,
    waitingOnInput: false,
    waitingOnPlanConfirmation: false,
    maintenanceKind: null,
    ...overrides
  }
}

function renderFooter(thread: Thread, mode: 'local' | 'worktree') {
  useThreadStore.setState({
    activeThreadId: thread.id,
    activeThread: thread,
    threadList: [thread]
  })

  return render(
    <LocaleProvider>
      <ComposerWorkspaceFooter
        workspacePath={thread.effectiveWorkspacePath || thread.workspacePath}
        mode={mode}
        variant="thread"
        thread={thread}
      />
    </LocaleProvider>
  )
}

describe('ComposerWorkspaceFooter', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    usePerforceChangelistStore.getState().reset()
    useSourceControlStore.setState({ workspacePath: null, effectiveProvider: null, status: null })
    useThreadStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    settingsGet.mockResolvedValue({ locale: 'en' })
    gitListBranches.mockResolvedValue({
      current: 'main',
      detachedHead: null,
      branches: [
        { name: 'main', current: true },
        { name: 'feat/example', current: false }
      ]
    })
    gitCheckoutBranch.mockResolvedValue(undefined)
    gitCreateAndCheckoutBranch.mockResolvedValue(undefined)
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        gitWorktrees: true
      }
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest
        },
        git: {
          listBranches: gitListBranches,
          checkoutBranch: gitCheckoutBranch,
          createAndCheckoutBranch: gitCreateAndCheckoutBranch
        }
      }
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders an available cached branch snapshot on the first frame', () => {
    useGitStore.setState({
      branchesByPath: {
        [normalizeGitPathKey('fixtures\\sample-app')]: {
          path: 'fixtures\\sample-app',
          status: 'available',
          snapshot: branchSnapshot('feat/example'),
          refreshing: false,
          errorMessage: null,
          updatedAt: Date.now(),
          requestId: 1
        }
      }
    })

    const localThread = makeThread()
    renderFooter(localThread, 'local')

    expect(screen.getByRole('button', { name: 'Local' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'feat/example' })).toBeInTheDocument()
    expect(gitListBranches).not.toHaveBeenCalled()
  })

  it('hides project and Git controls for the default chat workspace', async () => {
    const chatPath = 'C:\\Users\\me\\.craft\\workspaces\\chats'
    const chatThread = makeThread({
      workspacePath: chatPath,
      effectiveWorkspacePath: chatPath
    })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: chatPath,
      foregroundProjectId: chatPath,
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: chatPath,
        kind: 'chat',
        path: chatPath,
        name: chatPath,
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [chatThread]
      }
    })

    renderFooter(chatThread, 'local')

    await act(async () => {
      await Promise.resolve()
    })

    expect(screen.queryByRole('button', { name: 'Local' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'main' })).not.toBeInTheDocument()
    expect(gitListBranches).not.toHaveBeenCalled()
  })

  it('lets the welcome composer choose another project from the footer', async () => {
    const onWelcomeWorkspaceChange = vi.fn().mockResolvedValue(undefined)
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        },
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    render(
      <LocaleProvider>
        <ComposerWorkspaceFooter
          workspacePath="/workspace/a"
          mode="local"
          variant="welcome"
          onWelcomeWorkspaceChange={onWelcomeWorkspaceChange}
        />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'a' }))
    fireEvent.click(screen.getByRole('button', { name: 'b' }))

    await waitFor(() => {
      expect(onWelcomeWorkspaceChange).toHaveBeenCalledWith('/workspace/b')
    })
  })

  it('keeps footer controls mounted but disabled while branch probing is pending', () => {
    gitListBranches.mockReturnValue(new Promise(() => {}))

    const worktreeThread = makeWorktreeThread()
    renderFooter(worktreeThread, 'worktree')

    expect(screen.getByRole('button', { name: 'Worktree' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'dotcraft/handoff' })).toBeDisabled()
  })

  it('refreshes the current branch while the footer is mounted', async () => {
    vi.useFakeTimers()
    const localThread = makeThread()
    gitListBranches
      .mockResolvedValueOnce({
        current: 'main',
        detachedHead: null,
        branches: [
          { name: 'main', current: true },
          { name: 'master', current: false }
        ]
      })
      .mockResolvedValue({
        current: 'master',
        detachedHead: null,
        branches: [
          { name: 'main', current: false },
          { name: 'master', current: true }
        ]
      })

    renderFooter(localThread, 'local')

    await act(async () => {
      await Promise.resolve()
    })
    expect(screen.getByRole('button', { name: 'main' })).toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
      await Promise.resolve()
    })

    expect(screen.getByRole('button', { name: 'master' })).toBeInTheDocument()
  })

  it('refreshes the shared branch snapshot after checking out another branch', async () => {
    const localThread = makeThread()
    gitListBranches
      .mockResolvedValueOnce(branchSnapshot('main'))
      .mockResolvedValueOnce(branchSnapshot('feat/example'))

    renderFooter(localThread, 'local')

    fireEvent.click(await screen.findByRole('button', { name: 'main' }))
    fireEvent.click(screen.getByText('feat/example'))

    await waitFor(() => {
      expect(gitCheckoutBranch).toHaveBeenCalledWith('fixtures\\sample-app', 'feat/example')
    })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'feat/example' })).toBeInTheDocument()
    })
  })

  it('shows a Perforce changelist selector and updates the thread target', async () => {
    const thread = makeThread()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        gitWorktrees: true,
        sourceControlManagement: true
      }
    })
    useSourceControlStore.setState({
      workspacePath: 'fixtures\\sample-app',
      effectiveProvider: 'perforce',
      status: 'connected'
    })
    usePerforceChangelistStore.setState({
      byThreadId: {
        'thread-1': {
          threadId: 'thread-1',
          status: 'available',
          snapshot: {
            changelists: [
              { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
              { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' }
            ],
            target: { provider: 'perforce', changelist: 'default' }
          },
          errorMessage: null,
          requestId: 1
        }
      }
    })
    appServerSendRequest.mockResolvedValue({
      target: { provider: 'perforce', changelist: '123' }
    })

    renderFooter(thread, 'local')

    expect(screen.queryByRole('button', { name: 'Local' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'CL default' }))
    fireEvent.click(screen.getByRole('button', { name: /CL 123.*Task CL/ }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/threadTarget/update',
        { threadId: 'thread-1', target: { provider: 'perforce', changelist: '123' } },
        20_000
      )
    })
    expect(screen.getByRole('button', { name: 'CL 123' })).toBeInTheDocument()
  })

  it('creates a Perforce changelist from the selector and sets it as the thread target', async () => {
    const thread = makeThread()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        gitWorktrees: true,
        sourceControlManagement: true
      }
    })
    useSourceControlStore.setState({
      workspacePath: 'fixtures\\sample-app',
      effectiveProvider: 'perforce',
      status: 'connected'
    })
    usePerforceChangelistStore.setState({
      byThreadId: {
        'thread-1': {
          threadId: 'thread-1',
          status: 'available',
          snapshot: {
            changelists: [
              { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' }
            ],
            target: { provider: 'perforce', changelist: 'default' }
          },
          errorMessage: null,
          requestId: 1
        }
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'sourceControl/changelist/create') {
        return {
          changelist: { id: '777', isDefault: false, description: 'New work', user: 'me', client: 'ws', status: 'pending' },
          target: { provider: 'perforce', changelist: '777' }
        }
      }
      if (method === 'sourceControl/changelist/list') {
        return {
          changelists: [
            { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
            { id: '777', isDefault: false, description: 'New work', user: 'me', client: 'ws', status: 'pending' }
          ],
          target: { provider: 'perforce', changelist: '777' }
        }
      }
      return {}
    })

    renderFooter(thread, 'local')

    fireEvent.click(screen.getByRole('button', { name: 'CL default' }))
    fireEvent.click(screen.getByRole('button', { name: 'Create changelist...' }))
    fireEvent.change(screen.getByLabelText('Changelist description'), {
      target: { value: 'New work' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/changelist/create',
        { threadId: 'thread-1', description: 'New work', setAsTarget: true },
        30_000
      )
    })
    expect(await screen.findByRole('button', { name: 'CL 777' })).toBeInTheDocument()
  })

  it('hides welcome Git worktree controls when the workspace provider is Perforce', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        gitWorktrees: true,
        sourceControlManagement: true
      }
    })
    useSourceControlStore.setState({
      workspacePath: 'fixtures\\sample-app',
      effectiveProvider: 'perforce',
      status: 'notTested'
    })
    useGitStore.setState({
      branchesByPath: {
        [normalizeGitPathKey('fixtures\\sample-app')]: {
          path: 'fixtures\\sample-app',
          status: 'available',
          snapshot: branchSnapshot('main'),
          refreshing: false,
          errorMessage: null,
          updatedAt: Date.now(),
          requestId: 1
        }
      }
    })
    const onWelcomeModeChange = vi.fn()
    const onBaseRefChange = vi.fn()
    const onWorktreeBranchNameChange = vi.fn()

    render(
      <LocaleProvider>
        <ComposerWorkspaceFooter
          workspacePath="fixtures\\sample-app"
          mode="worktree"
          variant="welcome"
          baseRef="main"
          worktreeBranchName="dotcraft/sample-app"
          onWelcomeModeChange={onWelcomeModeChange}
          onBaseRefChange={onBaseRefChange}
          onWorktreeBranchNameChange={onWorktreeBranchNameChange}
        />
      </LocaleProvider>
    )

    expect(screen.queryByRole('button', { name: 'New worktree' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'main' })).not.toBeInTheDocument()
    expect(gitListBranches).not.toHaveBeenCalled()
    await waitFor(() => {
      expect(onWelcomeModeChange).toHaveBeenCalledWith('local')
    })
    expect(onBaseRefChange).toHaveBeenCalledWith(null)
    expect(onWorktreeBranchNameChange).toHaveBeenCalledWith(null)
  })

  it('hides and resets welcome worktree choices after Git is confirmed unavailable', async () => {
    gitListBranches.mockRejectedValue(new Error('not a git repository'))
    const onWelcomeModeChange = vi.fn()
    const onBaseRefChange = vi.fn()
    const onWorktreeBranchNameChange = vi.fn()

    render(
      <LocaleProvider>
        <ComposerWorkspaceFooter
          workspacePath="fixtures\\sample-app"
          mode="worktree"
          variant="welcome"
          baseRef="main"
          worktreeBranchName="dotcraft/sample-app"
          onWelcomeModeChange={onWelcomeModeChange}
          onBaseRefChange={onBaseRefChange}
          onWorktreeBranchNameChange={onWorktreeBranchNameChange}
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('button', { name: 'New worktree' })).toBeInTheDocument()

    await waitFor(() => {
      expect(onWelcomeModeChange).toHaveBeenCalledWith('local')
    })
    expect(onBaseRefChange).toHaveBeenCalledWith(null)
    expect(onWorktreeBranchNameChange).toHaveBeenCalledWith(null)
    expect(screen.queryByRole('button', { name: 'New worktree' })).not.toBeInTheDocument()
  })

  it('opens the local to worktree handoff dialog and sends the default branch request', async () => {
    const localThread = makeThread()
    const worktreeThread = makeWorktreeThread()
    appServerSendRequest.mockResolvedValue({ thread: worktreeThread })

    renderFooter(localThread, 'local')

    fireEvent.click(await screen.findByRole('button', { name: 'Local' }))
    fireEvent.click(screen.getByRole('button', { name: 'Handoff to worktree' }))

    expect(screen.getByRole('dialog', { name: 'Hand off chat to worktree' })).toBeInTheDocument()
    expect(screen.getByLabelText('Branch name')).toHaveValue('dotcraft/sample-app')

    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/worktree/handoff',
        {
          threadId: 'thread-1',
          mode: 'worktree',
          branchName: 'dotcraft/sample-app',
          baseRef: 'main',
          copyDirtyChanges: true
        },
        180_000
      )
    })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.worktree?.branchName).toBe('dotcraft/handoff')
    })
  })

  it('opens local handoff choices for running threads but disables handoff in the dialog', async () => {
    const runningThread = makeThread({ runtime: makeRuntime() })
    const idleThread = makeThread({ runtime: makeRuntime({ running: false }) })
    const worktreeThread = makeWorktreeThread()
    appServerSendRequest.mockResolvedValue({ thread: worktreeThread })

    const view = renderFooter(runningThread, 'local')

    const locationButton = await screen.findByRole('button', { name: 'Local' })
    expect(locationButton).not.toBeDisabled()
    fireEvent.click(locationButton)

    const handoffItem = screen.getByRole('button', { name: 'Handoff to worktree' })
    expect(handoffItem).not.toBeDisabled()
    fireEvent.click(handoffItem)

    expect(screen.getByRole('dialog', { name: 'Hand off chat to worktree' })).toBeInTheDocument()
    expect(screen.getByText('Workspace switching is unavailable while the conversation is in progress.')).toBeInTheDocument()

    const handoffButton = screen.getByRole('button', { name: 'Hand off' })
    expect(handoffButton).toBeDisabled()
    fireEvent.click(handoffButton)
    fireEvent.keyDown(document, { key: 'Enter' })
    expect(appServerSendRequest).not.toHaveBeenCalled()

    useThreadStore.getState().setActiveThread(idleThread)
    view.rerender(
      <LocaleProvider>
        <ComposerWorkspaceFooter
          workspacePath={idleThread.effectiveWorkspacePath || idleThread.workspacePath}
          mode="local"
          variant="thread"
          thread={idleThread}
        />
      </LocaleProvider>
    )

    expect(screen.queryByText('Workspace switching is unavailable while the conversation is in progress.')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Hand off' })).not.toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/worktree/handoff',
        {
          threadId: 'thread-1',
          mode: 'worktree',
          branchName: 'dotcraft/sample-app',
          baseRef: 'main',
          copyDirtyChanges: true
        },
        180_000
      )
    })
  })

  it('opens worktree handoff choices for running threads but disables local handoff in the dialog', async () => {
    const runningWorktreeThread = makeWorktreeThread()
    runningWorktreeThread.runtime = makeRuntime()

    renderFooter(runningWorktreeThread, 'worktree')

    const locationButton = await screen.findByRole('button', { name: 'Worktree' })
    expect(locationButton).not.toBeDisabled()
    fireEvent.click(locationButton)

    const handoffItem = screen.getByRole('button', { name: 'Handoff to branch' })
    expect(handoffItem).not.toBeDisabled()
    fireEvent.click(handoffItem)

    expect(screen.getByRole('dialog', { name: 'Hand off chat to local' })).toBeInTheDocument()
    expect(screen.getByText('Workspace switching is unavailable while the conversation is in progress.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Hand off' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))
    fireEvent.keyDown(document, { key: 'Enter' })
    expect(appServerSendRequest).not.toHaveBeenCalled()
  })

  it('shows worktree to local progress and sends a local handoff request', async () => {
    const worktreeThread = makeWorktreeThread()
    const localThread = makeThread()
    let resolveRequest: (value: { thread: Thread }) => void = () => {}
    appServerSendRequest.mockReturnValue(new Promise((resolve) => {
      resolveRequest = resolve
    }))

    renderFooter(worktreeThread, 'worktree')

    fireEvent.click(await screen.findByRole('button', { name: 'Worktree' }))
    expect(screen.queryByRole('button', { name: 'Back to local' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Handoff to worktree' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Handoff to branch' }))

    expect(screen.getByRole('dialog', { name: 'Hand off chat to local' })).toBeInTheDocument()
    expect(screen.getByText('dotcraft/handoff')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))

    expect(await screen.findByText('Handing off to local')).toBeInTheDocument()
    expect(screen.getByText('Checking out dotcraft/handoff locally')).toBeInTheDocument()
    expect(appServerSendRequest).toHaveBeenCalledWith(
      'thread/worktree/handoff',
      {
        threadId: 'thread-1',
        mode: 'local'
      },
      180_000
    )

    resolveRequest({ thread: localThread })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.worktree).toBeNull()
    })
    expect(useThreadStore.getState().activeThread?.effectiveWorkspacePath).toBe('fixtures\\sample-app')
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
      threadId: 'thread-1',
      includeTurns: false
    })
  })

  it('keeps the dialog open as a success view after fast handoff stages', async () => {
    const localThread = makeThread()
    const worktreeThread = makeWorktreeThread()
    appServerSendRequest.mockResolvedValue({ thread: worktreeThread })

    const view = renderFooter(localThread, 'local')

    fireEvent.click(await screen.findByRole('button', { name: 'Local' }))
    fireEvent.click(screen.getByRole('button', { name: 'Handoff to worktree' }))
    expect(screen.getByRole('dialog', { name: 'Hand off chat to worktree' })).toBeInTheDocument()

    vi.useFakeTimers()
    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))

    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(screen.getByText('Handing off to worktree')).toBeInTheDocument()
    expect(screen.queryByLabelText('Branch name')).not.toBeInTheDocument()
    expect(screen.getByText('Creating a new worktree')).toBeInTheDocument()
    expect(screen.getByText('Moving chat to worktree')).toBeInTheDocument()
    expect(useThreadStore.getState().activeThread?.worktree?.branchName).toBe('dotcraft/handoff')
    expect(screen.getByRole('dialog', { name: 'Handing off to worktree' })).toBeInTheDocument()

    view.rerender(
      <LocaleProvider>
        <ComposerWorkspaceFooter
          workspacePath={worktreeThread.effectiveWorkspacePath || worktreeThread.workspacePath}
          mode="worktree"
          variant="thread"
          thread={worktreeThread}
        />
      </LocaleProvider>
    )
    expect(screen.getByRole('dialog', { name: 'Handing off to worktree' })).toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(520 * 3)
    })
    expect(screen.queryByRole('dialog', { name: 'Handing off to worktree' })).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Handed-off to worktree' })).toBeInTheDocument()
    expect(screen.getByText('You are now working on dotcraft/handoff in a new worktree. Branch main was checked out locally.')).toBeInTheDocument()
    expect(useToastStore.getState().toasts).toEqual([])
  })

  it('uses a toast instead of the success view when handoff progress is dismissed', async () => {
    const localThread = makeThread()
    const worktreeThread = makeWorktreeThread()
    let resolveRequest: (value: { thread: Thread }) => void = () => {}
    appServerSendRequest.mockReturnValue(new Promise((resolve) => {
      resolveRequest = resolve
    }))

    renderFooter(localThread, 'local')

    fireEvent.click(await screen.findByRole('button', { name: 'Local' }))
    fireEvent.click(screen.getByRole('button', { name: 'Handoff to worktree' }))
    fireEvent.click(screen.getByRole('button', { name: 'Hand off' }))

    expect(await screen.findByText('Handing off to worktree')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog', { name: 'Handing off to worktree' })).not.toBeInTheDocument()

    resolveRequest({ thread: worktreeThread })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'Thread moved to a worktree')).toBe(true)
    })
    expect(screen.queryByRole('dialog', { name: 'Handed-off to worktree' })).not.toBeInTheDocument()
  })
})
