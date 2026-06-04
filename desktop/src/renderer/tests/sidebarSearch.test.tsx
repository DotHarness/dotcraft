import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadSearch } from '../components/sidebar/ThreadSearch'
import { SidebarFooter } from '../components/sidebar/SidebarFooter'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()

function makeThread(overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  const now = Date.now()
  return {
    id: 'thread-1',
    displayName: 'Optimize shortcut button Tooltip',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: new Date(now - 30 * 60 * 1000).toISOString(),
    lastActiveAt: new Date(now - 30 * 60 * 1000).toISOString(),
    ...overrides
  }
}

function resetStores(): void {
  useThreadStore.setState({
    threadList: [],
    activeThreadId: null,
    activeThread: null,
    searchQuery: '',
    loading: false,
    runningTurnThreadIds: new Set<string>(),
    parkedApprovals: new Map(),
    runtimeSnapshots: new Map(),
    pendingApprovalThreadIds: new Set<string>(),
    pendingPlanConfirmationThreadIds: new Set<string>(),
    unreadCompletedThreadIds: new Set<string>()
  })

  useUIStore.setState({
    activeMainView: 'conversation',
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false,
    whatsNewOpenRequestSeq: 0
  })
}

function renderWithLocale(ui: JSX.Element): void {
  render(<LocaleProvider>{ui}</LocaleProvider>)
}

describe('sidebar search redesign', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet }
      }
    })
    resetStores()
  })

  it('renders search as a sidebar action, not a persistent input', () => {
    renderWithLocale(<ThreadSearch workspaceName="dotcraft" />)

    expect(screen.getByRole('button', { name: 'Search' })).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('opens the centered search dialog from the sidebar action', async () => {
    useThreadStore.getState().setThreadList([
      makeThread(),
      makeThread({
        id: 'thread-2',
        displayName: 'Analyze Mac OAuth auth issue',
        lastActiveAt: new Date(Date.now() - 60 * 60 * 1000).toISOString()
      })
    ])
    renderWithLocale(<ThreadSearch workspaceName="dotcraft" />)

    fireEvent.click(screen.getByRole('button', { name: 'Search' }))

    const dialog = await screen.findByRole('dialog', { name: 'Search conversations' })
    expect(within(dialog).getByRole('textbox', { name: 'Search conversations' })).toHaveFocus()
    expect(within(dialog).getByText('Recent conversations')).toBeInTheDocument()
    expect(within(dialog).getByText('Optimize shortcut button Tooltip')).toBeInTheDocument()
    expect(within(dialog).getByText('Analyze Mac OAuth auth issue')).toBeInTheDocument()
  })

  it('opens through the Ctrl+K bridge and selects a result with Ctrl+1', async () => {
    useThreadStore.getState().setThreadList([
      makeThread({ id: 'target-thread' }),
      makeThread({
        id: 'thread-2',
        displayName: 'Other thread',
        lastActiveAt: new Date(Date.now() - 90 * 60 * 1000).toISOString()
      })
    ])
    renderWithLocale(<ThreadSearch workspaceName="dotcraft" />)

    act(() => {
      ;(window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus?.()
    })

    const dialog = await screen.findByRole('dialog', { name: 'Search conversations' })
    expect(dialog).toBeInTheDocument()

    fireEvent.keyDown(window, { key: '1', ctrlKey: true })

    expect(useThreadStore.getState().activeThreadId).toBe('target-thread')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(screen.queryByRole('dialog', { name: 'Search conversations' })).not.toBeInTheDocument()
  })
})

describe('SidebarFooter settings row', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet }
      }
    })
    resetStores()
  })

  it('shows the Settings shortcut inline on hover', () => {
    renderWithLocale(<SidebarFooter />)

    const settingsButton = screen.getByRole('button', { name: 'Open settings' })
    expect(settingsButton).toHaveTextContent('Settings')
    expect(settingsButton).not.toHaveTextContent('Ctrl')

    fireEvent.mouseEnter(settingsButton)

    expect(settingsButton).toHaveTextContent('Ctrl')
    expect(settingsButton).toHaveTextContent(',')
  })

  it("opens What's New from the sidebar version label", () => {
    renderWithLocale(<SidebarFooter />)

    fireEvent.click(screen.getByRole('button', { name: "What's New" }))

    expect(useUIStore.getState().whatsNewOpenRequestSeq).toBe(1)
  })
})
