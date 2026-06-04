import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ComposerWorkspaceFooter } from '../components/conversation/ComposerWorkspaceFooter'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import type { Thread } from '../types/thread'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const gitListBranches = vi.fn()
const gitCheckoutBranch = vi.fn()
const gitCreateAndCheckoutBranch = vi.fn()

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
    useThreadStore.getState().reset()
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

  it('shows hover feedback on workspace and branch controls', async () => {
    const localThread = makeThread()
    renderFooter(localThread, 'local')

    const workspaceButton = await screen.findByRole('button', { name: 'Local' })
    fireEvent.pointerEnter(workspaceButton)
    expect(workspaceButton).toHaveStyle({
      background: 'var(--bg-tertiary)',
      boxShadow: 'none'
    })

    fireEvent.click(workspaceButton)
    const handoffButton = screen.getByRole('button', { name: 'Handoff to worktree' })
    fireEvent.pointerEnter(handoffButton)
    expect(handoffButton).toHaveStyle({
      background: 'var(--bg-tertiary)',
      boxShadow: 'none'
    })

    const branchButton = screen.getByRole('button', { name: 'main' })
    fireEvent.pointerEnter(branchButton)
    expect(branchButton).toHaveStyle({
      background: 'var(--bg-tertiary)',
      boxShadow: 'none'
    })
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
