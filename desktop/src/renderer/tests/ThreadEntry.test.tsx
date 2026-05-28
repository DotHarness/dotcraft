import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { ThreadEntry } from '../components/sidebar/ThreadEntry'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()

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

    useThreadStore.getState().reset()
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
      pinnedThreadWorkspacePath: 'E:\\Git\\dotcraft'
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        appServer: { sendRequest: appServerSendRequest }
      }
    })
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
    expect(timeLabel).toHaveStyle({ display: 'none' })
    expect(screen.getByTestId('thread-status-slot-thread-1')).toHaveStyle({
      width: '24px',
      minWidth: '24px',
      justifySelf: 'center',
      justifyContent: 'center'
    })
    expect(archiveButton.getAttribute('style')).toContain('right: 0px')
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

  it('keeps the pin hover treatment icon-only', async () => {
    renderThreadEntry(makeThread())

    const row = screen.getByTestId('thread-entry-thread-1')
    const pinButton = screen.getByRole('button', { name: 'Pin conversation' })

    fireEvent.mouseEnter(row)
    fireEvent.mouseEnter(pinButton)

    await waitFor(() => {
      expect(pinButton).toBeVisible()
    })
    expect(pinButton.style.backgroundColor).toBe('transparent')
  })

  it('keeps the top-level pin slot visually centered before the title', () => {
    renderThreadEntry(makeThread())

    expect(screen.getByTestId('thread-entry-thread-1')).toHaveStyle({
      paddingLeft: '6px'
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
    useThreadStore.getState().hydratePinnedThreadIds('E:\\Git\\dotcraft', ['thread-1'])

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
          'E:\\Git\\dotcraft': ['thread-1']
        }
      })
    })

    fireEvent.click(screen.getByRole('button', { name: 'Unpin conversation' }))

    await waitFor(() => {
      expect(useThreadStore.getState().pinnedThreadIds).toEqual([])
      expect(settingsSet).toHaveBeenLastCalledWith({
        pinnedThreadIdsByWorkspace: {
          'E:\\Git\\dotcraft': []
        }
      })
    })
  })

  it('enters inline confirm on first archive click and archives on second click', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.mouseEnter(await screen.findByTestId('thread-entry-thread-1'))
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
    const confirmButton = screen.getByRole('button', { name: 'Confirm' })
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    expect(confirmButton).toBeVisible()
    expect(statusSlot.getAttribute('style')).toContain('min-width: 64px')
    expect(statusSlot.getAttribute('style')).toContain('justify-self: stretch')
    expect(confirmButton.getAttribute('style')).toContain('min-width: 64px')
    expect(confirmButton.getAttribute('style')).toContain('right: 0px')
    expect(confirmButton.getAttribute('style')).toContain('transform: translateY(-50%)')

    fireEvent.click(confirmButton)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().activeThreadId).toBeNull()
    })
  })

  it('cancels inline confirm when the pointer leaves the row', async () => {
    renderThreadEntry(makeThread())

    const row = await screen.findByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeVisible()

    fireEvent.mouseLeave(row)

    await waitFor(() => {
      expect(screen.getByText('1h')).toBeVisible()
      expect(screen.getByRole('button', { name: 'Confirm' })).not.toBeVisible()
    })
  })

  it('supports keyboard focus for inline confirm and cancels when focus leaves', async () => {
    renderThreadEntry(makeThread())

    const archiveButton = screen.getByRole('button', { name: 'Archive' })
    fireEvent.focus(archiveButton)
    fireEvent.click(archiveButton)

    const confirmButton = await screen.findByRole('button', { name: 'Confirm' })
    expect(confirmButton).toBeVisible()

    fireEvent.blur(confirmButton, { relatedTarget: null })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Confirm' })).not.toBeVisible()
    })
  })

  it('reuses the same archive flow from the context menu', async () => {
    const thread = makeThread()
    useThreadStore.setState({ threadList: [thread] })
    renderThreadEntry(thread)

    fireEvent.contextMenu(await screen.findByTestId('thread-entry-thread-1'), {
      clientX: 20,
      clientY: 20
    })

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'thread-1' })
      expect(useThreadStore.getState().threadList).toEqual([])
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
    expect(screen.getByTestId('thread-status-slot-thread-1')).toHaveStyle({
      width: '24px',
      minWidth: '24px',
      justifyContent: 'center'
    })
    expect(archiveButton.getAttribute('style')).toContain('right: 0px')
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

  it('places long-title pending badges and running status in the reference grid columns', () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1']),
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'A very long thread title that should give space to the badge without stealing the status slot'
    }))

    const content = screen.getByTestId('thread-layout-thread-1')
    const title = screen.getByTestId('thread-title-thread-1')
    const badgeSlot = screen.getByTestId('thread-badge-slot-thread-1')
    const badge = screen.getByTestId('thread-pending-confirmation-thread-1')
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const spinner = screen.getByTestId('thread-running-indicator-thread-1')

    expect(content).toHaveStyle({
      display: 'grid',
      gridTemplateColumns: 'minmax(0, 1fr) minmax(74px, max-content) 24px',
      columnGap: '7px'
    })
    expect(title.parentElement).toBe(content)
    expect(badgeSlot.parentElement).toBe(content)
    expect(statusSlot.parentElement).toBe(content)
    expect(badge.parentElement).toBe(badgeSlot)
    expect(spinner.parentElement?.parentElement).toBe(statusSlot)
    expect(badgeSlot).toHaveStyle({
      justifyContent: 'flex-end',
      justifySelf: 'stretch'
    })
    expect(badge).toHaveStyle({ maxWidth: '150px' })
    expect(statusSlot).toHaveStyle({
      width: '24px',
      minWidth: '24px',
      justifyContent: 'center'
    })
  })

  it('uses a compact action column when a pending badge row swaps relative time to archive', async () => {
    useThreadStore.setState({
      pendingPlanConfirmationThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'A pending confirmation row with relative time should keep archive close to the pill'
    }))

    const content = screen.getByTestId('thread-layout-thread-1')
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')

    expect(content).toHaveStyle({
      gridTemplateColumns: 'minmax(0, 1fr) minmax(74px, max-content) minmax(24px, max-content)'
    })
    expect(statusSlot).toHaveStyle({
      width: 'max-content',
      minWidth: '24px',
      justifySelf: 'end',
      justifyContent: 'flex-end'
    })

    fireEvent.mouseEnter(screen.getByTestId('thread-entry-thread-1'))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Archive' })).toBeVisible()
    })
    expect(content).toHaveStyle({
      gridTemplateColumns: 'minmax(0, 1fr) minmax(74px, max-content) 24px'
    })
    expect(statusSlot).toHaveStyle({
      width: '24px',
      minWidth: '24px',
      justifySelf: 'center',
      justifyContent: 'center'
    })
  })

  it('uses title and status columns for no-badge rows while time keeps a readable status column', () => {
    renderThreadEntry(makeThread({
      lastActiveAt: new Date(Date.now() - 20 * 1000).toISOString()
    }))

    const content = screen.getByTestId('thread-layout-thread-1')
    const title = screen.getByTestId('thread-title-thread-1')
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const timeLabel = screen.getByText('just now')

    expect(content).toHaveStyle({
      gridTemplateColumns: 'minmax(0, 1fr) minmax(24px, max-content)'
    })
    expect(screen.queryByTestId('thread-badge-slot-thread-1')).not.toBeInTheDocument()
    expect(title.getAttribute('style')).not.toContain('grid-column')
    expect(statusSlot).toHaveStyle({
      gridColumn: '2',
      width: 'max-content',
      minWidth: '24px',
      justifySelf: 'end',
      justifyContent: 'flex-end'
    })
    expect(timeLabel).toHaveTextContent('just now')
    expect(timeLabel.parentElement).toBe(statusSlot)
  })

  it('lets no-badge running rows span title text while the spinner stays in the status slot', () => {
    useThreadStore.setState({
      runningTurnThreadIds: new Set<string>(['thread-1'])
    })

    renderThreadEntry(makeThread({
      displayName: 'Currently implementing a no badge row that should show more title text'
    }))

    const title = screen.getByTestId('thread-title-thread-1')
    const content = screen.getByTestId('thread-layout-thread-1')
    const statusSlot = screen.getByTestId('thread-status-slot-thread-1')
    const spinner = screen.getByTestId('thread-running-indicator-thread-1')

    expect(screen.queryByTestId('thread-badge-slot-thread-1')).not.toBeInTheDocument()
    expect(content).toHaveStyle({
      gridTemplateColumns: 'minmax(0, 1fr) 24px'
    })
    expect(title.getAttribute('style')).not.toContain('grid-column')
    expect(statusSlot).toHaveStyle({
      gridColumn: '2',
      width: '24px',
      minWidth: '24px'
    })
    expect(spinner.parentElement?.parentElement).toBe(statusSlot)
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

  it('keeps origin channel icon visible during archive confirm state', async () => {
    renderThreadEntry(makeThread({ originChannel: 'qq' }))

    const row = await screen.findByTestId('thread-entry-thread-1')
    fireEvent.mouseEnter(row)
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }))

    expect(screen.getByRole('button', { name: 'Confirm' })).toBeVisible()
    expect(screen.getByLabelText('Origin channel: qq')).toBeVisible()
  })
})
