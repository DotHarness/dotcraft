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

function makeSubAgent(id: string, parentThreadId: string, displayName: string): ThreadSummary {
  return makeThread({
    id,
    displayName,
    originChannel: 'subagent',
    source: {
      kind: 'subagent',
      subAgent: {
        parentThreadId,
        depth: 1
      }
    }
  })
}

function renderList(): void {
  render(
    <LocaleProvider>
      <ThreadList />
    </LocaleProvider>
  )
}

describe('ThreadList subagent handling', () => {
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

  it('hides subagent children from the sidebar thread list', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'other-1', displayName: 'Other conversation' }),
      makeThread({ id: 'parent-1', displayName: 'Create hatch pet' }),
      makeSubAgent('child-1', 'parent-1', 'Create hatch pet Lovelace')
    ])

    renderList()

    const rows = screen.getAllByTestId(/thread-entry-/)
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual([
      'thread-entry-other-1',
      'thread-entry-parent-1'
    ])
    expect(screen.queryByTestId('thread-entry-child-1')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Background agent')).not.toBeInTheDocument()
  })

  it('does not show a pinned section when no threads are pinned', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'thread-1', displayName: 'Unpinned conversation' })
    ])

    renderList()

    expect(screen.queryByText('Pinned')).not.toBeInTheDocument()
  })

  it('renders pinned parents at the top and never lists their subagent children', () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'other-1', displayName: 'Other conversation' }),
      makeThread({ id: 'parent-1', displayName: 'Pinned parent' }),
      makeSubAgent('child-1', 'parent-1', 'Pinned child')
    ])
    useThreadStore.getState().hydratePinnedThreadIds('E:\\Git\\dotcraft', ['parent-1'])

    renderList()

    expect(screen.getByText('Pinned')).toBeInTheDocument()

    const rows = screen.getAllByTestId(/thread-entry-/)
    expect(rows.map((row) => row.getAttribute('data-testid'))).toEqual([
      'thread-entry-parent-1',
      'thread-entry-other-1'
    ])
    expect(screen.queryByTestId('thread-entry-child-1')).not.toBeInTheDocument()
  })
})
