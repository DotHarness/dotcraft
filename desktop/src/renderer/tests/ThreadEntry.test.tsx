import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ThreadEntry } from '../components/sidebar/ThreadEntry'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useThreadStore } from '../stores/threadStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useGitHeadStore } from '../stores/gitHeadStore'
import type { ThreadSummary } from '../types/thread'
import { buildWorkspaceOpenDeepLink } from '../../shared/desktopDeepLink'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const gitInspectHead = vi.fn()
const clipboardWriteText = vi.fn()

function makeThread(overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  const now = Date.now()
  return {
    id: 'thread-1',
    displayName: 'Optimize workspace cleanup',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: new Date(now - 2 * 60 * 60 * 1000).toISOString(),
    lastActiveAt: new Date(now - 61 * 60 * 1000).toISOString(),
    ...overrides
  }
}

function renderThreadEntry(thread: ThreadSummary): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <ThreadEntry thread={thread} />
    </LocaleProvider>
  )
}

describe('ThreadEntry', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue({})
    appServerSendRequest.mockResolvedValue({})
    gitInspectHead.mockResolvedValue({ kind: 'branch', label: 'main' })
    clipboardWriteText.mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: clipboardWriteText }
    })

    useThreadStore.getState().reset()
    useConnectionStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useGitHeadStore.getState().reset()
    useThreadStore.setState({
      threadList: [],
      activeThreadId: null,
      activeThread: null,
      searchQuery: '',
      loading: false,
      runningTurnThreadIds: new Set<string>(),
      parkedApprovals: new Map(),
      parkedUserInputs: new Map(),
      runtimeSnapshots: new Map(),
      pendingApprovalThreadIds: new Set<string>(),
      pendingUserInputThreadIds: new Set<string>(),
      pendingPlanConfirmationThreadIds: new Set<string>(),
      unreadCompletedThreadIds: new Set<string>(),
      goalSnapshots: new Map(),
      pinnedThreadIds: [],
      pinnedThreadWorkspacePath: 'C:\\fixtures\\sample-project'
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        appServer: { sendRequest: appServerSendRequest },
        git: { inspectHead: gitInspectHead }
      }
    })
  })

  it('shows project and Git branch in the focused thread details card', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'C:\\fixtures\\sample-project',
      secondaryLimit: 8,
      projects: [{
        path: 'C:\\fixtures\\sample-project',
        name: 'sample-project',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [],
        pinnedThreadIds: []
      }]
    })
    renderThreadEntry(makeThread({ workspacePath: 'C:\\fixtures\\sample-project' }))

    fireEvent.focus(screen.getByTestId('thread-entry-thread-1').parentElement!)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Optimize workspace cleanup')
    expect(screen.getByRole('tooltip')).toHaveTextContent('sample-project')
    await waitFor(() => {
      expect(screen.getByRole('tooltip')).toHaveTextContent('main')
    })
    expect(gitInspectHead).toHaveBeenCalledWith('C:\\fixtures\\sample-project')
  })

  it('uses a worktree branch without probing Git again', async () => {
    renderThreadEntry(makeThread({
      workspacePath: 'C:\\fixtures\\sample-project',
      worktree: {
        path: 'C:\\fixtures\\sample-project\\.craft\\worktrees\\details',
        sourceWorkspacePath: 'C:\\fixtures\\sample-project',
        branchName: 'feature/details-card'
      }
    }))

    fireEvent.focus(screen.getByTestId('thread-entry-thread-1').parentElement!)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('feature/details-card')
    expect(gitInspectHead).not.toHaveBeenCalled()
  })

  it('shows relative time by default and swaps to compact archive slot on hover', async () => {
    renderThreadEntry(makeThread())

    const timeLabel = screen.getByText('1h')
    const archiveButton = screen.getByRole('button', { name: 'Archive' })

    expect(timeLabel).toBeVisible()
    expect(archiveButton).not.toBeVisible()

    fireEvent.mouseEnter(screen.getByTestId('thread-entry-thread-1'))

    await waitFor(() => {
      expect(timeLabel).not.toBeVisible()
      expect(archiveButton).toBeVisible()
    })
  })

  it('places a frameless 12px origin icon before the running spinner', () => {
    useThreadStore.setState({ runningTurnThreadIds: new Set(['thread-1']) })
    renderThreadEntry(makeThread({ originChannel: 'heartbeat' }))

    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const originSlot = screen.getByTestId('thread-origin-slot-thread-1')
    const originIcon = within(originSlot).getByLabelText('Origin channel: heartbeat')
    const spinner = screen.getByTestId('thread-running-indicator-thread-1')

    expect(statusSlot).toContainElement(originSlot)
    expect(statusSlot).toContainElement(spinner)
    expect(originSlot.compareDocumentPosition(spinner) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
    expect(originIcon).toHaveStyle({
      width: '12px',
      height: '12px',
      background: 'transparent'
    })
    expect(originIcon).toHaveStyle({ borderWidth: '0px' })
  })

  it('lets a pending pill replace both the origin icon and running spinner', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set(['thread-1']),
      pendingApprovalThreadIds: new Set(['thread-1'])
    })
    renderThreadEntry(makeThread({ originChannel: 'automations' }))

    expect(screen.getByTestId('thread-pending-approval-thread-1')).toHaveTextContent('Awaiting approval')
    expect(screen.queryByTestId('thread-origin-slot-thread-1')).not.toBeInTheDocument()
    expect(screen.queryByTestId('thread-running-indicator-thread-1')).not.toBeInTheDocument()
  })

  it('hides origin and spinner together when Archive takes the trailing slot', async () => {
    useThreadStore.setState({ runningTurnThreadIds: new Set(['thread-1']) })
    renderThreadEntry(makeThread({ originChannel: 'cron' }))

    const row = screen.getByTestId('thread-entry-thread-1')
    const spinner = screen.getByTestId('thread-running-indicator-thread-1')
    expect(screen.getByTestId('thread-origin-slot-thread-1')).toBeVisible()
    expect(spinner).toBeVisible()

    fireEvent.mouseEnter(row)

    await waitFor(() => {
      expect(screen.queryByTestId('thread-origin-slot-thread-1')).not.toBeInTheDocument()
      expect(spinner).not.toBeVisible()
      expect(screen.getByRole('button', { name: 'Archive' })).toBeVisible()
    })
  })

  it('keeps archive action hidden for active row until hover', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderThreadEntry(makeThread())

    const row = screen.getByTestId('thread-entry-thread-1')
    const timeLabel = screen.getByText('1h')
    const archiveButton = screen.getByRole('button', { name: 'Archive' })

    expect(timeLabel).toBeVisible()
    expect(archiveButton).not.toBeVisible()

    fireEvent.mouseEnter(row)

    await waitFor(() => {
      expect(timeLabel).not.toBeVisible()
      expect(archiveButton).toBeVisible()
    })
  })

  it('reveals archive action on focus for keyboard access', async () => {
    renderThreadEntry(makeThread())

    const archiveButton = screen.getByRole('button', { name: 'Archive' })
    expect(archiveButton).not.toBeVisible()

    fireEvent.focus(archiveButton)

    await waitFor(() => {
      expect(archiveButton).toBeVisible()
    })
  })

  it('keeps the pin action hidden until the row is hovered', async () => {
    renderThreadEntry(makeThread())

    const row = screen.getByTestId('thread-entry-thread-1')
    const pinButton = screen.getByRole('button', { name: 'Pin conversation' })

    expect(pinButton).not.toBeVisible()

    fireEvent.mouseEnter(row)

    await waitFor(() => {
      expect(pinButton).toBeVisible()
    })
  })

  it('reveals the pin action on focus for keyboard access', async () => {
    renderThreadEntry(makeThread())

    const pinButton = screen.getByRole('button', { name: 'Pin conversation' })
    expect(pinButton).not.toBeVisible()

    fireEvent.focus(pinButton)

    await waitFor(() => {
      expect(pinButton).toBeVisible()
    })
  })

  it('keeps the pin action visible for pinned threads', () => {
    const thread = makeThread()
    useThreadStore.getState().setThreadList([thread])
    useThreadStore.getState().hydratePinnedThreadIds('C:\\fixtures\\sample-project', ['thread-1'])

    renderThreadEntry(thread)

    const pinButton = screen.getByRole('button', { name: 'Unpin conversation' })
    expect(pinButton).toBeVisible()
    expect(pinButton).toHaveAttribute('aria-pressed', 'true')
  })

  it('toggles pinned state from the row pin action', async () => {
    const thread = makeThread()
    useThreadStore.getState().setThreadList([thread])
    renderThreadEntry(thread)

    const row = screen.getByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)
    fireEvent.click(screen.getByRole('button', { name: 'Pin conversation' }))

    await waitFor(() => {
      expect(useThreadStore.getState().pinnedThreadIds).toEqual(['thread-1'])
      expect(settingsSet).toHaveBeenCalledWith({
        pinnedThreadIdsByWorkspace: {
          'c:/fixtures/sample-project': ['thread-1']
        }
      })
    })

    fireEvent.click(screen.getByRole('button', { name: 'Unpin conversation' }))

    await waitFor(() => {
      expect(useThreadStore.getState().pinnedThreadIds).toEqual([])
      expect(settingsSet).toHaveBeenLastCalledWith({
        pinnedThreadIdsByWorkspace: {
          'c:/fixtures/sample-project': []
        }
      })
    })
  })

  it('archives immediately on a single archive click (no confirm step)', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.mouseEnter(await screen.findByTestId('thread-entry-thread-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    // There is no longer a two-step "Confirm" pill.
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().activeThreadId).toBeNull()
    })
  })

  it('supports keyboard focus to reveal and trigger the archive action', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    const archiveButton = screen.getByRole('button', { name: 'Archive' })
    fireEvent.focus(archiveButton)
    expect(archiveButton).toBeVisible()

    fireEvent.click(archiveButton)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
    })
  })

  it('archives directly from the context menu without a confirm dialog', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
    })
  })

  it('copies the session ID from the context menu', async () => {
    renderThreadEntry(makeThread())

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copy session ID' }))

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith('thread-1')
    })
  })

  it('copies a workspace-aware deep link from the context menu', async () => {
    const workspacePath = 'C:\\fixtures\\sample project'
    renderThreadEntry(makeThread({ workspacePath }))

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copy deep link' }))

    await waitFor(() => {
      expect(clipboardWriteText).toHaveBeenCalledWith(
        buildWorkspaceOpenDeepLink(workspacePath, 'thread-1')
      )
    })
  })

  it('does not offer a deep link for a remote project', async () => {
    const thread = makeThread({ workspacePath: '/remote/workspace' })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/remote/workspace',
      foregroundProjectId: 'remote:manual:ws://example.test',
      secondaryLimit: 8,
      projects: [{
        projectId: 'remote:manual:ws://example.test',
        kind: 'remote',
        path: '/remote/workspace',
        name: 'remote',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [thread],
        pinned: false,
        remote: { source: 'manual', endpoint: 'ws://example.test' }
      }]
    })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    expect(await screen.findByRole('menuitem', { name: 'Copy session ID' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Copy deep link' })).not.toBeInTheDocument()
  })

  it('forks a thread into local from the context menu and selects the result', async () => {
    const thread = makeThread()
    const forked = makeThread({ id: 'thread-fork', displayName: 'Forked thread' })
    useConnectionStore.setState({ capabilities: { threadFork: true, gitWorktrees: true } })
    useThreadStore.setState({ threadList: [thread] })
    appServerSendRequest.mockResolvedValueOnce({ thread: forked })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Fork into local' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/fork', { threadId: 'thread-1' }, undefined)
      expect(useThreadStore.getState().activeThreadId).toBe('thread-fork')
      expect(useThreadStore.getState().threadList[0].id).toBe('thread-fork')
    })
  })

  it('forks a thread into a new worktree from the context menu', async () => {
    const thread = makeThread()
    const workspacePath = 'C:\\Workspaces\\sample-app'
    const worktreePath = `${workspacePath}\\.craft\\worktrees\\dotcraft-thread-worktree`
    const forked = makeThread({
      id: 'thread-worktree',
      displayName: 'Worktree fork',
      effectiveWorkspacePath: worktreePath,
      worktree: {
        id: 'wt-1',
        sourceThreadId: 'thread-1',
        workspacePath,
        sourceWorkspacePath: workspacePath,
        path: worktreePath,
        branchName: 'dotcraft/thread-worktree',
        baseRef: 'HEAD',
        head: 'abc123',
        createdAt: '2026-06-03T00:00:00.000Z'
      }
    })
    useConnectionStore.setState({ capabilities: { threadFork: true, gitWorktrees: true } })
    useThreadStore.setState({ threadList: [thread] })
    appServerSendRequest.mockResolvedValueOnce({ thread: forked })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Fork into new worktree' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'worktree/createAndFork',
        { sourceThreadId: 'thread-1', copyDirtyChanges: true },
        180000
      )
      expect(useThreadStore.getState().activeThreadId).toBe('thread-worktree')
    })
  })

  it('shows the custom confirm dialog before deleting from the context menu', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))

    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByText('Delete conversation?')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/delete', { threadId: 'thread-1' })
  })

  it('does not expose archive or delete actions for subagent children', async () => {
    const thread = makeThread({
      id: 'child-1',
      workspacePath: 'C:\\fixtures\\sample-project',
      originChannel: 'subagent',
      source: {
        kind: 'subagent',
        subAgent: {
          parentThreadId: 'parent-1',
          depth: 1
        }
      }
    })
    renderThreadEntry(thread)

    fireEvent.mouseEnter(await screen.findByTestId('thread-entry-child-1'))
    expect(screen.queryByRole('button', { name: 'Pin conversation' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument()

    fireEvent.contextMenu(screen.getByTestId('thread-entry-child-1'), {
      clientX: 20,
      clientY: 20
    })

    expect(await screen.findByRole('menuitem', { name: 'Rename' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Copy session ID' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Copy deep link' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Archive' })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it('keeps the thread in local state when backend delete fails', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread], activeThreadId: 'thread-1' })
    appServerSendRequest.mockRejectedValueOnce(new Error('delete failed'))
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))

    const dialog = screen.getByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'thread-1' })
    })
    expect(useThreadStore.getState().threadList).toEqual([thread])
    expect(useThreadStore.getState().activeThreadId).toBe('thread-1')
  })

  it('hides time and archive action while renaming', async () => {
    renderThreadEntry(makeThread())

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 24,
      clientY: 24
    })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Rename' }))

    await waitFor(() => {
      expect(screen.getByDisplayValue('Optimize workspace cleanup')).toBeInTheDocument()
    })
    expect(screen.queryByText('1h')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Archive' })).toBeNull()
  })

  it('shows a running spinner for a background thread with an active turn', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
    expect(screen.getByLabelText('Turn running')).toBeInTheDocument()
  })

  it('shows running spinner in default state and swaps to archive on hover', async () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    const spinner = screen.getByTestId('thread-running-indicator-thread-1')
    const archiveButton = screen.getByRole('button', { name: 'Archive' })

    expect(spinner).toBeVisible()
    expect(archiveButton).not.toBeVisible()

    fireEvent.mouseEnter(screen.getByTestId('thread-entry-thread-1'))

    await waitFor(() => {
      expect(spinner).not.toBeVisible()
      expect(archiveButton).toBeVisible()
    })
  })

  it('shows a running spinner after thread list runtime hydration', () => {
    const thread = makeThread({
      runtime: {
        running: true,
        waitingOnApproval: false,
        waitingOnPlanConfirmation: false
      }
    })
    useThreadStore.getState().setThreadList([thread])

    renderThreadEntry(thread)

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
    expect(screen.getByLabelText('Turn running')).toBeInTheDocument()
  })

  it('shows a running spinner for the active thread with an active turn', () => {
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
  })

  it('shows paused status when not running', () => {
    renderThreadEntry(makeThread({ status: 'paused' }))

    expect(screen.queryByTestId('thread-running-indicator-thread-1')).not.toBeInTheDocument()
    expect(screen.getByLabelText('paused')).toBeInTheDocument()
  })

  it('prefers the running spinner over paused status when both states are present', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({ status: 'paused' }))

    expect(screen.getByTestId('thread-running-indicator-thread-1')).toBeInTheDocument()
    expect(screen.queryByLabelText('paused')).not.toBeInTheDocument()
  })

  it('renders origin channel as an icon badge with tooltip text', () => {
    renderThreadEntry(makeThread({ originChannel: 'qq' }))

    expect(screen.getByLabelText('Origin channel: qq')).toBeInTheDocument()
    expect(screen.queryByText('qq')).not.toBeInTheDocument()
  })

  it('shows pending approval badge over pending confirmation badge for inactive thread', () => {
    useThreadStore.setState({
      pendingApprovalThreadIds: new Set<string>(['thread-1']),
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByText('Awaiting approval')).toBeInTheDocument()
    expect(screen.queryByText('Awaiting confirmation')).not.toBeInTheDocument()
  })

  it('shows pending confirmation badge when approval is not pending', () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByText('Awaiting confirmation')).toBeInTheDocument()
  })

  it('moves the pending pill into the trailing status slot and hides the running spinner', () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1']),
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'A very long thread title that should give space to the pill without stealing the status slot'
    }))

    const content = screen.getByTestId('thread-layout-thread-1')
    const title = screen.getByTestId('thread-title-thread-1')
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const badge = screen.getByTestId('thread-pending-confirmation-thread-1')

    expect(title.parentElement).toBe(content)
    expect(statusSlot.parentElement).toBe(content)
    // The pending pill now lives in the trailing status slot
    // (pill span -> status span -> status slot).
    expect(statusSlot).toContainElement(badge)
    // The running spinner is suppressed while the pending pill occupies the slot.
    expect(screen.queryByTestId('thread-running-indicator-thread-1')).not.toBeInTheDocument()
    // The middle badge slot is no longer rendered for the pending pill.
    expect(screen.queryByTestId('thread-badge-slot-thread-1')).not.toBeInTheDocument()
  })

  it('keeps a pending badge row interactive when relative time swaps to archive', async () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'A pending confirmation row with relative time should keep archive close to the pill'
    }))

    fireEvent.mouseEnter(screen.getByTestId('thread-entry-thread-1'))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Archive' })).toBeVisible()
    })
  })

  it('keeps no-badge rows without a badge slot while showing relative time', () => {
    renderThreadEntry(makeThread({
      lastActiveAt: new Date(Date.now() - 20 * 1000).toISOString()
    }))

    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const timeLabel = screen.getByText('just now')

    expect(screen.queryByTestId('thread-badge-slot-thread-1')).not.toBeInTheDocument()
    expect(timeLabel).toHaveTextContent('just now')
    expect(statusSlot).toContainElement(timeLabel)
  })

  it('lets no-badge running rows span title text while the spinner stays in the status slot', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'Currently implementing a no badge row that should show more title text'
    }))

    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const spinner = screen.getByTestId('thread-running-indicator-thread-1')

    expect(screen.queryByTestId('thread-badge-slot-thread-1')).not.toBeInTheDocument()
    expect(statusSlot).toContainElement(spinner)
  })

  it('shows pending user input badge when an inactive thread needs an answer', () => {
    useThreadStore.setState({
      pendingUserInputThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByText('Needs answer')).toBeInTheDocument()
  })

  it('hides pending badges for active thread', () => {
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      pendingApprovalThreadIds: new Set<string>(['thread-1']),
      pendingUserInputThreadIds: new Set<string>(['thread-1']),
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.queryByText('Awaiting approval')).not.toBeInTheDocument()
    expect(screen.queryByText('Needs answer')).not.toBeInTheDocument()
    expect(screen.queryByText('Awaiting confirmation')).not.toBeInTheDocument()
  })

  it('shows unread completed dot when thread finished in background', () => {
    useThreadStore.setState({
      unreadCompletedThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread())

    expect(screen.getByLabelText('New result')).toBeInTheDocument()
  })

  it('hides the origin channel icon while the archive action is revealed on hover', async () => {
    renderThreadEntry(makeThread({ originChannel: 'qq' }))

    const row = await screen.findByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)

    expect(screen.getByRole('button', { name: 'Archive' })).toBeVisible()
    expect(screen.queryByLabelText('Origin channel: qq')).not.toBeInTheDocument()
  })

  it('renders the app-origin badge (icon + name) when originApp is set', async () => {
    const icon = 'data:image/svg+xml;base64,PHN2Zz48L3N2Zz4='
    renderThreadEntry(
      makeThread({
        originChannel: 'workflow',
        originApp: { appId: 'com.example.workflow', displayName: 'Workflow App', icon }
      })
    )

    const badge = await screen.findByLabelText('Origin app: Workflow App')
    expect(badge).toBeInTheDocument()
    const img = badge.querySelector('img')
    expect(img?.getAttribute('src')).toBe(icon)
  })

  it('prefers source-neutral origin presentation over originApp', async () => {
    const presentationIcon = 'data:image/svg+xml;base64,PHN2ZyBpZD0icHJlc2VudGF0aW9uIi8+'
    renderThreadEntry(
      makeThread({
        originChannel: 'teams',
        originPresentation: {
          sourceId: 'agent-teams',
          displayName: 'Builder',
          icon: presentationIcon,
          subjectId: 'builder',
          subjectKind: 'member'
        },
        originApp: {
          appId: 'com.example.secondary',
          displayName: 'Secondary origin',
          icon: null
        }
      })
    )

    const badge = await screen.findByLabelText('Origin: Builder')
    expect(badge.querySelector('img')?.getAttribute('src')).toBe(presentationIcon)
    expect(screen.queryByLabelText('Origin app: Secondary origin')).not.toBeInTheDocument()
  })

  it('renders origin presentation even for the desktop origin channel', async () => {
    renderThreadEntry(
      makeThread({
        originPresentation: {
          sourceId: 'native-source',
          displayName: 'Native source'
        }
      })
    )

    expect(await screen.findByLabelText('Origin: Native source')).toBeInTheDocument()
  })

  it('falls back to the channel badge when originApp is absent', () => {
    renderThreadEntry(makeThread({ originChannel: 'workflow' }))

    expect(screen.getByLabelText('Origin channel: workflow')).toBeInTheDocument()
    expect(screen.queryByLabelText('Origin app: Workflow App')).not.toBeInTheDocument()
  })

  it('renders the app-origin badge by name even when its icon is missing', async () => {
    renderThreadEntry(
      makeThread({
        originChannel: 'workflow',
        originApp: { appId: 'com.example.workflow', displayName: 'Workflow App', icon: null }
      })
    )

    expect(await screen.findByLabelText('Origin app: Workflow App')).toBeInTheDocument()
  })

  it('uses the per-member tooltip when app origin metadata carries a member id', async () => {
    const icon = 'data:image/svg+xml;base64,PHN2Zz48L3N2Zz4='
    renderThreadEntry(
      makeThread({
        originChannel: 'workflow',
        originApp: { appId: 'com.example.workflow', displayName: 'Worker', icon, memberId: 'worker' }
      })
    )

    expect(await screen.findByLabelText('Origin: Worker')).toBeInTheDocument()
    expect(screen.queryByLabelText('Origin app: Worker')).not.toBeInTheDocument()
  })
})
