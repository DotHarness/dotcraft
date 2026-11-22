import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState, type JSX } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ComposerWorkspaceFooter,
  type ComposerWorkspaceMode
} from '../components/conversation/ComposerWorkspaceFooter'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useGitStore } from '../stores/gitStore'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

const WORKSPACE = 'X:/workspace'

function makeThread(overrides: Partial<Thread> = {}): Thread {
  return {
    id: 'thread-1',
    workspacePath: WORKSPACE,
    effectiveWorkspacePath: WORKSPACE,
    displayName: 'Test thread',
    status: 'active',
    runtime: {
      busy: false,
      running: false,
      waitingOnApproval: false,
      waitingOnInput: false,
      maintenanceKind: null
    },
    worktree: null,
    ...overrides
  } as unknown as Thread
}

function setupFooterEnvironment({
  gitWorktrees = true,
  appServerSendRequest = vi.fn(async () => ({ data: [] }))
}: {
  gitWorktrees?: boolean
  appServerSendRequest?: ReturnType<typeof vi.fn>
} = {}) {
  installDesktopApiMock({
    initialLocale: 'en',
    git: {
      listBranches: async () => ({
        current: 'main',
        detachedHead: null,
        branches: [{ name: 'main', current: true }]
      })
    },
    appServer: { sendRequest: appServerSendRequest },
    projectActions: {
      listWorktreeEnvironments: async () => ({
        environments: [],
        selectedEnvironmentId: null,
        unavailable: false
      }),
      watch: () => () => {}
    }
  })
  useConnectionStore.setState({ capabilities: { gitWorktrees } })
}

function WelcomeFooterHarness({ onModeChange }: { onModeChange?: (mode: ComposerWorkspaceMode) => void }): JSX.Element {
  const [mode, setMode] = useState<ComposerWorkspaceMode>('local')
  return (
    <LocaleProvider loadSettings={false}>
      <ComposerWorkspaceFooter
        workspacePath={WORKSPACE}
        mode={mode}
        variant="welcome"
        onWelcomeModeChange={(next) => {
          setMode(next)
          onModeChange?.(next)
        }}
      />
    </LocaleProvider>
  )
}

describe('WorkLocationToggle in ComposerWorkspaceFooter', () => {
  beforeEach(() => {
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
  })

  it('toggles directly between local and worktree on Welcome without opening a menu', async () => {
    const onModeChange = vi.fn()
    setupFooterEnvironment()

    render(<WelcomeFooterHarness onModeChange={onModeChange} />)

    const localButton = await screen.findByRole('button', { name: 'Work locally' })
    await waitFor(() => expect(localButton).toBeEnabled())

    fireEvent.click(localButton)
    expect(onModeChange).toHaveBeenCalledWith('worktree')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const worktreeButton = await screen.findByRole('button', { name: 'New worktree' })

    fireEvent.click(worktreeButton)
    expect(onModeChange).toHaveBeenCalledWith('local')
  })

  it('opens the worktree handoff dialog directly on one click from an idle local thread', async () => {
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'worktree/list') return { data: [] }
      return {}
    })
    setupFooterEnvironment({ appServerSendRequest: sendRequest })

    render(
      <LocaleProvider loadSettings={false}>
        <ComposerWorkspaceFooter
          workspacePath={WORKSPACE}
          mode="local"
          variant="thread"
          thread={makeThread()}
        />
      </LocaleProvider>
    )

    const toggle = await screen.findByRole('button', { name: 'Local' })
    await waitFor(() => expect(toggle).toBeEnabled())
    fireEvent.click(toggle)

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(sendRequest).toHaveBeenCalledWith('worktree/list', {})
  })

  it('opens the local handoff dialog directly on one click from an idle worktree thread', async () => {
    const sendRequest = vi.fn(async () => ({}))
    setupFooterEnvironment({ appServerSendRequest: sendRequest })

    render(
      <LocaleProvider loadSettings={false}>
        <ComposerWorkspaceFooter
          workspacePath={`${WORKSPACE}/.craft/worktrees/thread-1`}
          mode="worktree"
          variant="thread"
          thread={makeThread({
            effectiveWorkspacePath: `${WORKSPACE}/.craft/worktrees/thread-1`,
            worktree: {
              id: 'wt-1',
              sourceThreadId: 'thread-1',
              workspacePath: WORKSPACE,
              sourceWorkspacePath: WORKSPACE,
              path: `${WORKSPACE}/.craft/worktrees/thread-1`,
              branchName: 'dotcraft/feature',
              baseRef: 'main',
              head: 'dotcraft/feature'
            }
          } as Partial<Thread>)}
        />
      </LocaleProvider>
    )

    const toggle = await screen.findByRole('button', { name: 'Worktree' })
    await waitFor(() => expect(toggle).toBeEnabled())
    fireEvent.click(toggle)

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(sendRequest).not.toHaveBeenCalledWith('worktree/list', {})
  })

  it('blocks switching when the thread is busy or has queued inputs', async () => {
    setupFooterEnvironment()
    const runningThread = makeThread({
      runtime: {
        busy: true,
        running: true,
        waitingOnApproval: false,
        waitingOnInput: false,
        maintenanceKind: null
      }
    } as Partial<Thread>)

    const { rerender } = render(
      <LocaleProvider loadSettings={false}>
        <ComposerWorkspaceFooter
          workspacePath={WORKSPACE}
          mode="local"
          variant="thread"
          thread={runningThread}
        />
      </LocaleProvider>
    )

    const toggle = await screen.findByRole('button', { name: 'Local' })
    await waitFor(() => expect(toggle).toBeEnabled())
    expect(toggle).toHaveAttribute('aria-disabled', 'true')

    fireEvent.click(toggle)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    const queuedThread = makeThread({ queuedInputs: [{ id: 'q1' }] } as unknown as Partial<Thread>)
    rerender(
      <LocaleProvider loadSettings={false}>
        <ComposerWorkspaceFooter
          workspacePath={WORKSPACE}
          mode="local"
          variant="thread"
          thread={queuedThread}
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('button', { name: 'Local' })).toHaveAttribute('aria-disabled', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Local' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('blocks switching when gitWorktrees capability is unavailable', async () => {
    const onModeChange = vi.fn()
    setupFooterEnvironment({ gitWorktrees: false })

    render(<WelcomeFooterHarness onModeChange={onModeChange} />)

    const localButton = await screen.findByRole('button', { name: 'Work locally' })
    await waitFor(() => expect(localButton).toHaveAttribute('aria-disabled', 'true'))

    fireEvent.click(localButton)
    expect(onModeChange).not.toHaveBeenCalled()
  })
})
