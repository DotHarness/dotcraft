import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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
    fireEvent.click(screen.getByRole('button', { name: 'Back to local' }))

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
      expect(useThreadStore.getState().activeThread?.worktree).toBeUndefined()
    })
  })
})
