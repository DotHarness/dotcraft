import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadList } from '../components/sidebar/ThreadList'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()
const settingsSet = vi.fn()

function makeThread(overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  const now = new Date().toISOString()
  return {
    id: 'parent-1',
    displayName: 'Create hatch pet',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: now,
    lastActiveAt: now,
    ...overrides
  }
}

function renderList(): void {
  render(
    <LocaleProvider>
      <ThreadList />
    </LocaleProvider>
  )
}

describe('ThreadList subagent entries', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue({})
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        appServer: { sendRequest: vi.fn() }
      }
    })
    useThreadStore.getState().reset()
  })

  it('places subagent children directly after their parent and marks them as background agents', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'other-1', displayName: 'Other conversation' }),
      makeThread({ id: 'parent-1', displayName: 'Create hatch pet' }),
      makeThread({
        id: 'child-1',
        displayName: 'Create hatch pet Lovelace',
        originChannel: 'subagent',
        source: {
          kind: 'subagent',
          subAgent: {
            parentThreadId: 'parent-1',
            depth: 1
          }
        }
      })
    ])

    renderList()

    const rows = screen.getAllByTestId(/thread-entry-/)
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual([
      'thread-entry-other-1',
      'thread-entry-parent-1',
      'thread-entry-child-1'
    ])
    expect(screen.getByLabelText('Background agent')).toBeInTheDocument()
    expect(screen.queryByLabelText('Origin channel: subagent')).not.toBeInTheDocument()
  })

  it('does not show a pinned section when no threads are pinned', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'thread-1', displayName: 'Unpinned conversation' })
    ])

    renderList()

    expect(screen.queryByText('Pinned')).not.toBeInTheDocument()
  })

  it('renders pinned threads at the top without duplicating them in time groups', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'other-1', displayName: 'Other conversation' }),
      makeThread({ id: 'parent-1', displayName: 'Pinned parent' }),
      makeThread({
        id: 'child-1',
        displayName: 'Pinned child',
        originChannel: 'subagent',
        source: {
          kind: 'subagent',
          subAgent: {
            parentThreadId: 'parent-1',
            depth: 1
          }
        }
      })
    ])
    useThreadStore.getState().hydratePinnedThreadIds('E:\\Git\\dotcraft', ['parent-1'])

    renderList()

    const pinnedHeading = screen.getByText('Pinned')
    const todayHeading = screen.getByText('Today')
    expect(pinnedHeading.compareDocumentPosition(todayHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    const rows = screen.getAllByTestId(/thread-entry-/)
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual([
      'thread-entry-parent-1',
      'thread-entry-child-1',
      'thread-entry-other-1'
    ])
    expect(screen.getAllByTestId('thread-entry-parent-1')).toHaveLength(1)
    expect(screen.getAllByTestId('thread-entry-child-1')).toHaveLength(1)
  })
})
