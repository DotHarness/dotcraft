import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { OwnedThreadPausedBar } from '../components/conversation/OwnedThreadPausedBar'
import { ThreadHeader } from '../components/conversation/ThreadHeader'
import { ThreadStatusIndicator } from '../components/sidebar/ThreadStatusIndicator'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { useThreadStore } from '../stores/threadStore'
import { confirmAndArchiveOwnedThread } from '../utils/ownedThreadLifecycle'
import { installDesktopApiMock } from './desktopApiMock'

const CONDUCTOR_RECORD: OwnedThreadRecord = {
  pluginId: 'oratorio',
  key: 'conductor',
  space: 'conductor',
  title: 'Oratorio',
  threadId: 'thread-conductor-1',
  workspacePath: '/chats',
  fingerprint: '{}',
  paused: true
}

function wrap(ui: JSX.Element) {
  return (
    <LocaleProvider>
      {ui}
      <ConfirmDialogHost />
    </LocaleProvider>
  )
}

describe('Conductor lifecycle UI', () => {
  beforeEach(() => {
    useOwnedThreadsStore.getState().setRecords([CONDUCTOR_RECORD])
    useThreadStore.setState({
      activeThreadId: CONDUCTOR_RECORD.threadId,
      activeThread: null,
      pendingApprovalThreadIds: new Set([CONDUCTOR_RECORD.threadId])
    })
  })

  afterEach(() => {
    cleanup()
    useOwnedThreadsStore.getState().setRecords([])
    useThreadStore.setState({ activeThreadId: null, activeThread: null, pendingApprovalThreadIds: new Set() })
  })

  it('toggles cleanly between active waiting and paused icon without hook order errors', () => {
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) as never } })
    useOwnedThreadsStore.getState().setRecords([{ ...CONDUCTOR_RECORD, paused: undefined }])
    const { rerender } = render(
      wrap(<ThreadStatusIndicator threadId={CONDUCTOR_RECORD.threadId} active={false} unreadVariant="count" />)
    )
    expect(screen.queryByTestId(`thread-paused-${CONDUCTOR_RECORD.threadId}`)).not.toBeInTheDocument()

    useOwnedThreadsStore.getState().setRecords([CONDUCTOR_RECORD])
    rerender(wrap(<ThreadStatusIndicator threadId={CONDUCTOR_RECORD.threadId} active={false} unreadVariant="count" />))
    expect(screen.getByTestId(`thread-paused-${CONDUCTOR_RECORD.threadId}`)).toBeInTheDocument()
  })

  it('shows lifecycle-only actions in ThreadHeader More menu without a duplicate header toggle', () => {
    const setOwnedPaused = vi.fn(async () => ({ ...CONDUCTOR_RECORD, paused: undefined }))
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) as never },
      system: { listOpenTargets: async () => ({ targets: [], preferredId: null }) as never },
      threadTasks: { setOwnedPaused }
    })
    render(
      wrap(
        <ThreadHeader
          threadName="Oratorio"
          threadId={CONDUCTOR_RECORD.threadId}
          workspacePath="/chats"
        />
      )
    )
    expect(screen.queryByTestId('thread-header-pause-toggle')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'More chat actions' }))
    const resumeItem = screen.getByRole('menuitem', { name: 'Resume' })
    expect(resumeItem).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /Pin/i })).not.toBeInTheDocument()
    fireEvent.click(resumeItem)
    expect(setOwnedPaused).toHaveBeenCalledWith(CONDUCTOR_RECORD.threadId, false)
  })

  it('replaces the composer with a centered Paused resume button and confirms before archiving', async () => {
    const setOwnedPaused = vi.fn(async () => ({ ...CONDUCTOR_RECORD, paused: undefined }))
    const archiveOwned = vi.fn(async () => true)
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) as never },
      threadTasks: { setOwnedPaused, archiveOwned }
    })

    render(
      wrap(
        <OwnedThreadPausedBar record={CONDUCTOR_RECORD}>
          <div data-testid="underlying-composer">Composer</div>
        </OwnedThreadPausedBar>
      )
    )
    const resumeBtn = screen.getByTestId('conductor-paused-bar-resume')
    fireEvent.click(resumeBtn)
    expect(setOwnedPaused).toHaveBeenCalledWith(CONDUCTOR_RECORD.threadId, false)

    const archivePromise = confirmAndArchiveOwnedThread(CONDUCTOR_RECORD, (key) => key)
    const confirmButton = await screen.findByRole('button', { name: 'conductor.archiveConfirm.confirm' })
    fireEvent.click(confirmButton)
    await expect(archivePromise).resolves.toBe(true)
    expect(archiveOwned).toHaveBeenCalledWith(CONDUCTOR_RECORD.threadId)
    expect(useOwnedThreadsStore.getState().records).toEqual([])
  })
})
