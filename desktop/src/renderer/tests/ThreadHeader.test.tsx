import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadHeader } from '../components/conversation/ThreadHeader'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { usePerforceChangelistStore } from '../stores/perforceChangelistStore'
import { useSourceControlStore } from '../stores/sourceControlStore'
import { useThreadStore } from '../stores/threadStore'
import type { Thread } from '../types/thread'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const gitCommit = vi.fn()

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

function renderHeader(remoteWorkspace = false, workspacePath = 'fixtures\\sample-app'): void {
  render(
    <LocaleProvider>
      <ThreadHeader
        threadName="Thread"
        threadId="thread-1"
        workspacePath={workspacePath}
        remoteWorkspace={remoteWorkspace}
      />
    </LocaleProvider>
  )
}

function setupOnlinePerforceThread(workspacePath = 'C:\\workspace\\sample-app'): void {
  useConnectionStore.setState({
    status: 'connected',
    capabilities: { sourceControlManagement: true }
  })
  const thread = makeThread({
    workspacePath,
    effectiveWorkspacePath: workspacePath,
    metadata: {
      'sourceControl.provider': 'perforce',
      'sourceControl.perforce.changelist': '123'
    }
  })
  useThreadStore.setState({
    activeThreadId: thread.id,
    activeThread: thread,
    threadList: [thread]
  })
  useSourceControlStore.setState({
    workspacePath,
    effectiveProvider: 'perforce',
    status: 'connected',
    perforceChangelist: true
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
          target: { provider: 'perforce', changelist: '123' }
        },
        errorMessage: null,
        requestId: 1
      }
    }
  })
  useConversationStore.getState().upsertChangedFile({
    filePath: `${workspacePath}\\src\\a.ts`,
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 2,
    deletions: 1,
    diffHunks: [],
    status: 'written',
    isNewFile: false
  })
}

describe('ThreadHeader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useConversationStore.getState().reset()
    usePerforceChangelistStore.getState().reset()
    useSourceControlStore.setState({ workspacePath: null, effectiveProvider: null, status: null, perforceChangelist: null })
    useThreadStore.getState().reset()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})
    gitCommit.mockResolvedValue(undefined)

    const thread = makeThread()
    useThreadStore.setState({
      activeThreadId: thread.id,
      activeThread: thread,
      threadList: [thread]
    })

    installDesktopApiMock({
      settings: { get: settingsGet },
      appServer: {
        sendRequest: appServerSendRequest,
        onNotification: vi.fn(() => () => {})
      },
      git: { commit: gitCommit },
      shell: {
        listEditors: vi.fn().mockResolvedValue([])
      }
    })
  })

  it('renders the source-control action as a frameless icon button', async () => {
    renderHeader()

    const commitButton = await screen.findByRole('button', { name: 'Commit file changes to git' })
    expect(commitButton).toHaveTextContent('')
    expect(commitButton).not.toHaveAttribute('data-bordered')
  })

  it('opens the Fork submenu from the header menu when fork is available', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { threadFork: true, gitWorktrees: true }
    })

    renderHeader()

    fireEvent.click(await screen.findByRole('button', { name: 'More chat actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Fork' }))

    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Fork into new worktree' })).toBeInTheDocument()
  })

  it('omits Fork from the header menu when fork is unavailable', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderHeader()

    fireEvent.click(await screen.findByRole('button', { name: 'More chat actions' }))

    expect(screen.queryByRole('menuitem', { name: 'Fork' })).not.toBeInTheDocument()
  })

  it('prepares a Perforce changelist from the header without using local git', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { sourceControlManagement: true }
    })
    const thread = makeThread({
      workspacePath,
      effectiveWorkspacePath: workspacePath
    })
    thread.metadata = {
      'sourceControl.provider': 'perforce',
      'sourceControl.perforce.changelist': '123'
    }
    useThreadStore.setState({
      activeThreadId: thread.id,
      activeThread: thread,
      threadList: [thread]
    })
    useSourceControlStore.setState({
      workspacePath,
      effectiveProvider: 'perforce',
      status: 'connected',
      perforceChangelist: true
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
            target: { provider: 'perforce', changelist: '123' }
          },
          errorMessage: null,
          requestId: 1
        }
      }
    })
    useConversationStore.getState().upsertChangedFile({
      filePath: 'C:\\workspace\\sample-app\\src\\a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 2,
      deletions: 1,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'sourceControl/changelist/prepare') {
        return {
          status: 'prepared',
          changelist: '123',
          movedPaths: ['src/a.ts'],
          skippedPaths: [],
          warnings: []
        }
      }
      if (method === 'sourceControl/changelist/list') {
        return {
          changelists: [
            { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
            { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' }
          ],
          target: { provider: 'perforce', changelist: '123' }
        }
      }
      return {}
    })

    renderHeader(true, workspacePath)
    await act(async () => {
      useSourceControlStore.setState({
        workspacePath,
        effectiveProvider: 'perforce',
        status: 'connected',
        perforceChangelist: true
      })
    })

    const prepareButton = await screen.findByRole('button', { name: 'Prepare Perforce changelist' })
    expect(prepareButton).not.toBeDisabled()
    fireEvent.click(prepareButton)
    expect(screen.getByRole('dialog', { name: 'Prepare changelist' })).toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText('Leave blank to auto-generate changelist description'), {
      target: { value: 'Prepare task CL' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/changelist/prepare',
        {
          threadId: 'thread-1',
          description: 'Prepare task CL',
          paths: ['src/a.ts'],
          target: '123'
        },
        60_000
      )
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith(
      'workspace/commitMessage/suggest',
      expect.anything(),
      expect.anything()
    )
    expect(gitCommit).not.toHaveBeenCalled()
  })

  it('auto-generates a Perforce changelist description when Checkout description is blank', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    setupOnlinePerforceThread(workspacePath)
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'workspace/commitMessage/suggest') {
        return { message: 'Generated CL summary' }
      }
      if (method === 'sourceControl/changelist/prepare') {
        return {
          status: 'ok',
          changelist: '123',
          movedPaths: ['src/a.ts'],
          skippedPaths: [],
          warnings: []
        }
      }
      if (method === 'sourceControl/changelist/list') {
        return {
          changelists: [
            { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
            { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' }
          ],
          target: { provider: 'perforce', changelist: '123' }
        }
      }
      return {}
    })

    renderHeader(true, workspacePath)

    fireEvent.click(await screen.findByRole('button', { name: 'Prepare Perforce changelist' }))
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'workspace/commitMessage/suggest',
        {
          threadId: 'thread-1',
          paths: ['src/a.ts'],
          provider: 'perforce'
        },
        120_000
      )
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/changelist/prepare',
        {
          threadId: 'thread-1',
          description: 'Generated CL summary',
          paths: ['src/a.ts'],
          target: '123'
        },
        60_000
      )
    })
    expect(gitCommit).not.toHaveBeenCalled()
  })

  it('auto-generates a Perforce description and prepares a new changelist', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    setupOnlinePerforceThread(workspacePath)
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'workspace/commitMessage/suggest') {
        return { message: 'Generated new CL summary' }
      }
      if (method === 'sourceControl/changelist/prepare') {
        return {
          status: 'ok',
          changelist: '321',
          movedPaths: ['src/a.ts'],
          skippedPaths: [],
          warnings: []
        }
      }
      if (method === 'sourceControl/changelist/list') {
        return {
          changelists: [
            { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
            { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' },
            { id: '321', isDefault: false, description: 'Generated new CL summary', user: 'me', client: 'ws', status: 'pending' }
          ],
          target: { provider: 'perforce', changelist: '321' }
        }
      }
      return {}
    })

    renderHeader(true, workspacePath)

    fireEvent.click(await screen.findByRole('button', { name: 'Prepare Perforce changelist' }))
    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    fireEvent.click(targetSelect)
    fireEvent.click(screen.getByRole('option', { name: 'New Changelist' }))
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'workspace/commitMessage/suggest',
        {
          threadId: 'thread-1',
          paths: ['src/a.ts'],
          provider: 'perforce'
        },
        120_000
      )
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/changelist/prepare',
        {
          threadId: 'thread-1',
          description: 'Generated new CL summary',
          paths: ['src/a.ts'],
          target: 'default'
        },
        60_000
      )
    })
    expect(gitCommit).not.toHaveBeenCalled()
  })

  it('does not prepare a Perforce changelist when description generation returns empty', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    setupOnlinePerforceThread(workspacePath)
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'workspace/commitMessage/suggest') {
        return { message: '   ' }
      }
      if (method === 'sourceControl/changelist/list') {
        return {
          changelists: [
            { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
            { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' }
          ],
          target: { provider: 'perforce', changelist: '123' }
        }
      }
      return {}
    })

    renderHeader(true, workspacePath)

    fireEvent.click(await screen.findByRole('button', { name: 'Prepare Perforce changelist' }))
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'workspace/commitMessage/suggest',
        expect.objectContaining({ provider: 'perforce' }),
        120_000
      )
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith(
      'sourceControl/changelist/prepare',
      expect.anything(),
      expect.anything()
    )
    expect(gitCommit).not.toHaveBeenCalled()
  })

  it('keeps Checkout unavailable when Perforce is offline', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { sourceControlManagement: true }
    })
    const thread = makeThread({
      workspacePath,
      effectiveWorkspacePath: workspacePath,
      metadata: {
        'sourceControl.provider': 'perforce',
        'sourceControl.perforce.changelist': '123'
      }
    })
    useThreadStore.setState({
      activeThreadId: thread.id,
      activeThread: thread,
      threadList: [thread]
    })
    useSourceControlStore.setState({
      workspacePath,
      effectiveProvider: 'perforce',
      status: 'offline',
      perforceChangelist: false
    })
    useConversationStore.getState().upsertChangedFile({
      filePath: 'C:\\workspace\\sample-app\\src\\a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 2,
      deletions: 1,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })

    renderHeader(true, workspacePath)

    const prepareButton = await screen.findByRole('button', { name: 'Prepare Perforce changelist' })
    expect(prepareButton).toBeDisabled()
    fireEvent.click(prepareButton)

    expect(gitCommit).not.toHaveBeenCalled()
    expect(appServerSendRequest).not.toHaveBeenCalledWith(
      'sourceControl/changelist/prepare',
      expect.anything(),
      expect.anything()
    )
  })

  it('uses thread metadata changelist while the changelist snapshot is loading', async () => {
    const workspacePath = 'C:\\workspace\\sample-app'
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { sourceControlManagement: true }
    })
    const thread = makeThread({
      workspacePath,
      effectiveWorkspacePath: workspacePath,
      metadata: {
        'sourceControl.provider': 'perforce',
        'sourceControl.perforce.changelist': '123'
      }
    })
    useThreadStore.setState({
      activeThreadId: thread.id,
      activeThread: thread,
      threadList: [thread]
    })
    useSourceControlStore.setState({
      workspacePath,
      effectiveProvider: 'perforce',
      status: 'connected',
      perforceChangelist: true
    })
    useConversationStore.getState().upsertChangedFile({
      filePath: 'C:\\workspace\\sample-app\\src\\a.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 2,
      deletions: 1,
      diffHunks: [],
      status: 'written',
      isNewFile: false
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'sourceControl/changelist/list') {
        return new Promise(() => {})
      }
      if (method === 'sourceControl/changelist/prepare') {
        return {
          status: 'error',
          errors: [{ fallbackText: 'stop after request' }]
        }
      }
      return {}
    })

    renderHeader(true, workspacePath)

    fireEvent.click(await screen.findByRole('button', { name: 'Prepare Perforce changelist' }))
    fireEvent.change(screen.getByPlaceholderText('Leave blank to auto-generate changelist description'), {
      target: { value: 'Prepare task CL' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/changelist/prepare',
        {
          threadId: 'thread-1',
          description: 'Prepare task CL',
          paths: ['src/a.ts'],
          target: '123'
        },
        60_000
      )
    })
  })
})
