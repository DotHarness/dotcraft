import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { ThreadHeader } from '../components/conversation/ThreadHeader'
import { ThreadStatusIndicator } from '../components/sidebar/ThreadStatusIndicator'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginThreadMenuItems
} from '../plugins/desktopPluginRegistry'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'

const OWNED_RECORD: OwnedThreadRecord = {
  pluginId: 'sample-plugin',
  key: 'assistant',
  space: 'main',
  title: 'Assistant',
  threadId: 'thread-owned-1',
  workspacePath: '/chats',
  fingerprint: '{}',
  paused: true
}

describe('Owned thread host UI', () => {
  beforeEach(() => {
    clearDesktopPluginRegistry()
    useOwnedThreadsStore.getState().setRecords([OWNED_RECORD])
    useThreadStore.setState({
      activeThreadId: OWNED_RECORD.threadId,
      activeThread: null,
      pendingApprovalThreadIds: new Set([OWNED_RECORD.threadId])
    })
  })

  afterEach(() => {
    cleanup()
    clearDesktopPluginRegistry()
    useOwnedThreadsStore.getState().setRecords([])
    useThreadStore.setState({ activeThreadId: null, activeThread: null, pendingApprovalThreadIds: new Set() })
  })

  it('toggles cleanly between active waiting and paused icon without hook order errors', () => {
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) as never } })
    useOwnedThreadsStore.getState().setRecords([{ ...OWNED_RECORD, paused: undefined }])
    const { rerender } = render(
      <LocaleProvider>
        <ThreadStatusIndicator threadId={OWNED_RECORD.threadId} active={false} unreadVariant="count" />
      </LocaleProvider>
    )
    expect(screen.queryByTestId(`thread-paused-${OWNED_RECORD.threadId}`)).not.toBeInTheDocument()

    useOwnedThreadsStore.getState().setRecords([OWNED_RECORD])
    rerender(
      <LocaleProvider>
        <ThreadStatusIndicator threadId={OWNED_RECORD.threadId} active={false} unreadVariant="count" />
      </LocaleProvider>
    )
    expect(screen.getByTestId(`thread-paused-${OWNED_RECORD.threadId}`)).toBeInTheDocument()
  })

  it('shows only plugin-contributed overflow menu actions for owned threads', () => {
    const onResume = vi.fn()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) as never },
      system: { listOpenTargets: async () => ({ targets: [], preferredId: null }) as never }
    })
    const disposeMenu = registerDesktopPluginThreadMenuItems('sample-plugin', (context) =>
      context.threadId === OWNED_RECORD.threadId
        ? [{ label: 'Resume', onSelect: onResume }, { label: 'Archive', onSelect: vi.fn(), danger: true }]
        : []
    )

    render(
      <LocaleProvider>
        <ThreadHeader
          threadName="Assistant"
          threadId={OWNED_RECORD.threadId}
          workspacePath="/chats"
        />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByRole('button', { name: 'More chat actions' }))
    const resumeItem = screen.getByRole('menuitem', { name: 'Resume' })
    expect(resumeItem).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /Pin/i })).not.toBeInTheDocument()
    fireEvent.click(resumeItem)
    expect(onResume).toHaveBeenCalledTimes(1)
    disposeMenu()
  })
})
