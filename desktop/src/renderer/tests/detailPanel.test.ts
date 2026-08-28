// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createElement } from 'react'
import { useConversationStore } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DetailPanel } from '../components/layout/DetailPanel'
import {
  PLAN_TODO_STATUS_ICON_NAMES,
  PlanTodoStatusIcon,
  type PlanTodoStatusIconStatus
} from '../components/plan/PlanTodoStatusIcon'
import type { FileDiff } from '../types/toolCall'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/detail/ViewerTab', () => ({
  ViewerTab: () => null
}))

const cs = () => useConversationStore.getState()
const ui = () => useUIStore.getState()

function makeDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/test.ts',
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 10,
    deletions: 2,
    diffHunks: [],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

beforeEach(() => {
  cs().reset()
  useUIStore.setState({
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false,
    detailPanelPreferredVisible: true,
    detailPanelPreferredVisibleByThread: {},
    selectedChangedFile: null,
    autoShowTriggeredForTurn: null,
    autoShowPlanForItem: null,
    activeDetailTab: { kind: 'system', id: 'changes' },
    openSystemTabs: ['changes'],
    lastActiveSystemTab: 'changes',
    detailPanelVisible: true,
    responsiveLayout: 'full'
  })
  useThreadStore.setState({
    threadList: [],
    activeThreadId: null,
    activeThread: null,
    searchQuery: '',
    loading: false,
    runningTurnThreadIds: new Set(),
    parkedApprovals: new Map(),
    runtimeSnapshots: new Map(),
    pendingApprovalThreadIds: new Set(),
    pendingPlanConfirmationThreadIds: new Set(),
    unreadCompletedThreadIds: new Set()
  })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    currentWorkspacePath: null
  })
  installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      platform: 'win32',
      workspace: {
        viewer: {
          browser: {
            create: vi.fn(async () => ({
              tabId: 'browser-created',
              currentUrl: 'about:blank',
              title: 'New Tab',
              canGoBack: false,
              canGoForward: false,
              loading: false
            }))
          }
        }
      }
    })
})

describe('commit file filter', () => {
  it('excludes reverted files from the commit list', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts', status: 'written' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/b.ts', status: 'written' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/c.ts', status: 'reverted' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/d.ts', status: 'reverted' }))

    const allFiles = Array.from(cs().changedFiles.values())
    const writtenFiles = allFiles.filter((f) => f.status === 'written')

    expect(writtenFiles).toHaveLength(2)
    expect(writtenFiles.map((f) => f.filePath)).toEqual(
      expect.arrayContaining(['src/a.ts', 'src/b.ts'])
    )
    expect(writtenFiles.map((f) => f.filePath)).not.toContain('src/c.ts')
    expect(writtenFiles.map((f) => f.filePath)).not.toContain('src/d.ts')
  })

  it('shows 0 files when all are reverted', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts', status: 'reverted' }))
    const written = Array.from(cs().changedFiles.values()).filter((f) => f.status === 'written')
    expect(written).toHaveLength(0)
  })

  it('shows all files when none are reverted', () => {
    cs().upsertChangedFile(makeDiff({ filePath: 'src/a.ts' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/b.ts' }))
    cs().upsertChangedFile(makeDiff({ filePath: 'src/c.ts' }))
    const written = Array.from(cs().changedFiles.values()).filter((f) => f.status === 'written')
    expect(written).toHaveLength(3)
  })
})

describe('terminal command badge data', () => {
  it('counts commandExecution items instead of completed Exec tool calls', () => {
    cs().onTurnStarted({
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    })
    cs().onItemStarted({
      turnId: 'turn-1',
      item: {
        id: 'cmd-1',
        type: 'commandExecution',
        payload: {
          callId: 'exec-1',
          command: 'npm test',
          status: 'inProgress',
          aggregatedOutput: ''
        }
      }
    })

    const terminalCount = cs().turns.reduce(
      (acc, turn) => acc + turn.items.filter((i) => i.type === 'commandExecution').length,
      0
    )

    expect(terminalCount).toBe(1)
  })
})

describe('plan todo status icons', () => {
  const statuses: PlanTodoStatusIconStatus[] = ['pending', 'in_progress', 'completed', 'cancelled']

  it('maps all four statuses to distinct lucide icon names', () => {
    const icons = statuses.map((status) => PLAN_TODO_STATUS_ICON_NAMES[status])
    expect(new Set(icons).size).toBe(statuses.length)
  })

  it('renders status icons without Unicode glyph text', () => {
    for (const status of statuses) {
      const { container, unmount } = render(createElement(PlanTodoStatusIcon, { status }))
      const icon = container.querySelector(`[data-plan-todo-status="${status}"]`)
      expect(icon).not.toBeNull()
      expect(icon?.getAttribute('data-plan-todo-icon')).toBe(PLAN_TODO_STATUS_ICON_NAMES[status])
      expect(container.textContent).toBe('')
      unmount()
    }
  })
})

describe('showChangesForFile', () => {
  it('sets detail panel visible, switches to changes tab, selects file', () => {
    useUIStore.setState({
      detailPanelVisible: false,
      activeDetailTab: { kind: 'system', id: 'plan' },
      selectedChangedFile: null
    })

    ui().showChangesForFile('src/foo.ts')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(ui().selectedChangedFile).toBe('src/foo.ts')
  })

  it('works when panel is already visible', () => {
    useUIStore.setState({
      detailPanelVisible: true,
      activeDetailTab: { kind: 'system', id: 'plan' }
    })

    ui().showChangesForFile('src/bar.ts')

    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(ui().selectedChangedFile).toBe('src/bar.ts')
  })
})

describe('markAutoShowForTurn', () => {
  it('stores the turn id that triggered auto-show', () => {
    expect(ui().autoShowTriggeredForTurn).toBeNull()

    ui().markAutoShowForTurn('turn-abc')

    expect(ui().autoShowTriggeredForTurn).toBe('turn-abc')
  })

  it('allows override for a different turn', () => {
    ui().markAutoShowForTurn('turn-1')
    ui().markAutoShowForTurn('turn-2')
    expect(ui().autoShowTriggeredForTurn).toBe('turn-2')
  })
})

describe('markAutoShowPlanForItem', () => {
  it('stores the CreatePlan item id that triggered plan auto-switch', () => {
    expect(ui().autoShowPlanForItem).toBeNull()

    ui().markAutoShowPlanForItem('item-plan-1')

    expect(ui().autoShowPlanForItem).toBe('item-plan-1')
  })

  it('allows override for the next CreatePlan item', () => {
    ui().markAutoShowPlanForItem('item-plan-1')
    ui().markAutoShowPlanForItem('item-plan-2')
    expect(ui().autoShowPlanForItem).toBe('item-plan-2')
  })
})

describe('onPlanUpdated via store', () => {
  it('setActiveDetailTab("plan") auto-shows the panel', () => {
    useUIStore.setState({ detailPanelVisible: false })

    ui().setActiveDetailTab('plan')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })
})

describe('viewer tab in uiStore', () => {
  it('setActiveViewerTab switches to viewer kind and shows the panel', () => {
    ui().setActiveDetailTab('plan')

    ui().setActiveViewerTab('vtab-123')

    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-123' })
    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().lastActiveSystemTab).toBe('plan')
  })

  it('closeViewerTab falls back to an open system tab', () => {
    ui().setActiveDetailTab('plan')
    ui().setActiveViewerTab('vtab-abc')

    ui().closeViewerTab()

    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })

  it('lastActiveSystemTab is remembered when switching between system tabs', () => {
    ui().setActiveDetailTab('changes')
    ui().setActiveDetailTab('plan')

    expect(ui().lastActiveSystemTab).toBe('plan')
  })

  it('setQuickOpenVisible toggles the flag', () => {
    expect(ui().quickOpenVisible).toBe(false)
    ui().setQuickOpenVisible(true)
    expect(ui().quickOpenVisible).toBe(true)
    ui().setQuickOpenVisible(false)
    expect(ui().quickOpenVisible).toBe(false)
  })
})

describe('optional system tabs', () => {
  beforeEach(() => {
    useUIStore.setState({ openSystemTabs: [], activeDetailTab: { kind: 'launcher' } })
  })

  it('setActiveDetailTab opens the tab and focuses it', () => {
    ui().setActiveDetailTab('plan')
    expect(ui().openSystemTabs).toEqual(['plan'])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })

  it('keeps system tabs in canonical order regardless of open order', () => {
    ui().setActiveDetailTab('plan')
    ui().setActiveDetailTab('changes')
    expect(ui().openSystemTabs).toEqual(['changes', 'plan'])
  })

  it('closeSystemTab removes the tab and falls back to the remaining system tab', () => {
    ui().setActiveDetailTab('changes')
    ui().setActiveDetailTab('plan')
    ui().closeSystemTab('plan')
    expect(ui().openSystemTabs).toEqual(['changes'])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
  })

  it('closeSystemTab falls back to the launcher when no tabs remain', () => {
    ui().setActiveDetailTab('changes')
    ui().closeSystemTab('changes')
    expect(ui().openSystemTabs).toEqual([])
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
  })

  it('closeSystemTab falls back to a supplied viewer tab when no system tabs remain', () => {
    ui().setActiveDetailTab('changes')
    ui().closeSystemTab('changes', 'vtab-9')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-9' })
  })

  it('closeSystemTab on an inactive tab keeps the active tab', () => {
    ui().setActiveDetailTab('changes')
    ui().setActiveDetailTab('plan')
    ui().closeSystemTab('changes')
    expect(ui().openSystemTabs).toEqual(['plan'])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })

  it('closeViewerTab falls back to the launcher when no system tabs are open', () => {
    ui().setActiveViewerTab('vtab-1')
    ui().closeViewerTab()
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
  })

  it('closeViewerTab auto-hides the panel and saves false when closing the last tab', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      responsiveLayout: 'full'
    })
    ui().setActiveViewerTab('vtab-1')
    ui().closeViewerTab()
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('closeViewerTab keeps the panel open when a system tab remains', () => {
    useUIStore.setState({ detailPanelPreferredVisible: true, detailPanelVisible: true, responsiveLayout: 'full' })
    ui().setActiveDetailTab('plan')
    ui().setActiveViewerTab('vtab-2')
    ui().closeViewerTab()
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('closeViewerTab({ reveal: false }) still hides the panel when it empties', () => {
    // Internal tab synchronization must not leave an empty launcher visible.
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      responsiveLayout: 'full',
      openSystemTabs: [],
      activeDetailTab: { kind: 'viewer', id: 'vtab-from-prev-thread' }
    })
    ui().closeViewerTab({ reveal: false })
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('closeSystemTab auto-hides the panel when closing the last tab (no launcher)', () => {
    useUIStore.setState({ detailPanelPreferredVisible: true, detailPanelVisible: true, responsiveLayout: 'full' })
    ui().setActiveDetailTab('changes')
    ui().closeSystemTab('changes')
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('closeSystemTab keeps the panel open when a viewer tab remains', () => {
    useUIStore.setState({ detailPanelPreferredVisible: true, detailPanelVisible: true, responsiveLayout: 'full' })
    ui().setActiveDetailTab('changes')
    ui().closeSystemTab('changes', 'vtab-9')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-9' })
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('resetDetailTabs clears open tabs and hides the launcher', () => {
    ui().setActiveDetailTab('changes')
    ui().resetDetailTabs()
    expect(ui().openSystemTabs).toEqual([])
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisibleByThread).toEqual({})
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it.each([
    { kind: 'launcher' as const },
    { kind: 'viewer' as const, id: 'viewer-from-previous-thread' },
    { kind: 'system' as const, id: 'changes' as const }
  ])('hides an empty incoming thread from a previous $kind state', (activeDetailTab) => {
    useUIStore.setState({
      activeDetailTab,
      openSystemTabs: [],
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    ui().syncDetailPanelForThread('thread-empty', null)

    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('keeps an unremembered incoming thread hidden even when it has a viewer tab', () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'launcher' },
      openSystemTabs: [],
      detailPanelPreferredVisibleByThread: {},
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')

    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('opens and restores a viewer tab for a thread saved as visible', () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'launcher' },
      openSystemTabs: [],
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')

    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('keeps a saved-visible thread hidden when it has no tabs', () => {
    useUIStore.setState({
      openSystemTabs: [],
      detailPanelPreferredVisibleByThread: { 'thread-a': true }
    })

    ui().syncDetailPanelForThread('thread-a', null)

    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('keeps A hidden after switching through visible B', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      openSystemTabs: [],
      detailPanelPreferredVisibleByThread: { 'thread-a': true, 'thread-b': true },
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    ui().toggleDetailPanel()
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)

    useThreadStore.setState({ activeThreadId: 'thread-b' })
    ui().syncDetailPanelForThread('thread-b', 'viewer-thread-b')
    expect(ui().detailPanelVisible).toBe(true)

    useThreadStore.setState({ activeThreadId: 'thread-a' })
    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('restores the last active system tab only for a thread saved as visible', () => {
    useUIStore.setState({
      openSystemTabs: ['changes', 'plan'],
      lastActiveSystemTab: 'changes',
      activeDetailTab: { kind: 'viewer', id: 'viewer-from-previous-thread' },
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    ui().syncDetailPanelForThread('thread-a', null)

    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('records automatic and manual visibility changes for the active thread', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      detailPanelPreferredVisibleByThread: {},
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      autoShowReasons: new Set()
    })

    expect(ui().maybeAutoShowForReason('turn:one')).toBe(true)
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)

    ui().toggleDetailPanel()
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)

    ui().setActiveDetailTab('plan')
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)
  })

  it('preserves saved visibility while responsive layout suppresses the panel', () => {
    useUIStore.setState({
      openSystemTabs: [],
      responsiveLayout: 'no-detail',
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')

    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(false)

    ui().setResponsiveLayout('full')
    expect(ui().detailPanelVisible).toBe(true)
  })
})

describe('detail panel add-tab menu', () => {
  function Harness({ workspacePath = '/workspace/path' }: { workspacePath?: string }): JSX.Element {
    return createElement(
      LocaleProvider,
      null,
      createElement(DetailPanel, { workspacePath })
    )
  }

  it('renders add-tab menu items with enabled state based on workspace context', async () => {
    cs().setWorkspacePath('/workspace/path')
    useThreadStore.getState().setActiveThreadId('thread-1')
    useViewerTabStore.getState().onThreadSwitched('thread-1')
    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))

    const menu = await screen.findByRole('menu', { name: 'Add tab' })
    expect(menu).toBeTruthy()
    expect(screen.getByRole('menuitem', { name: /^Files$/ })).not.toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Browser/ })).not.toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Terminal/ })).not.toHaveProperty('disabled', true)
  })

  it('disables browser and terminal menu items without an active workspace thread', async () => {
    render(createElement(Harness, { workspacePath: '' }))

    fireEvent.click(screen.getByLabelText('Add tab'))

    expect(await screen.findByRole('menuitem', { name: /^Files$/ })).toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Browser/ })).toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Terminal/ })).toHaveProperty('disabled', true)
  })

  it('offers Changes and Checks entries that open the system tabs', async () => {
    useUIStore.setState({ openSystemTabs: [], activeDetailTab: { kind: 'launcher' } })
    render(createElement(Harness, { workspacePath: '' }))

    fireEvent.click(screen.getByLabelText('Add tab'))

    fireEvent.click(await screen.findByRole('menuitem', { name: /Checks/ }))
    await waitFor(() => {
      expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    })
    expect(ui().openSystemTabs).toEqual(['plan'])
  })

  it('opens an empty Files viewer when the menu returns openFile', async () => {
    cs().setWorkspacePath('/workspace/path')
    useThreadStore.getState().setActiveThreadId('thread-1')
    useViewerTabStore.getState().onThreadSwitched('thread-1')
    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))
    fireEvent.click(await screen.findByRole('menuitem', { name: /^Files$/ }))

    await waitFor(() => {
      const state = useViewerTabStore.getState().getThreadState('thread-1')
      expect(state.tabs).toHaveLength(1)
      expect(state.tabs[0]).toMatchObject({ kind: 'files', label: 'Files' })
      expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: state.tabs[0]?.id })
      expect(useUIStore.getState().explorerVisible).toBe(true)
      expect(useUIStore.getState().quickOpenVisible).toBe(false)
    })
  })

  it('does nothing when the menu is dismissed', async () => {
    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useViewerTabStore.getState().getThreadState('thread-1').tabs).toHaveLength(0)
  })

  it('opens a browser viewer tab when the menu returns newBrowser', async () => {
    cs().setWorkspacePath('/workspace/path')
    useThreadStore.getState().setActiveThreadId('thread-1')
    useViewerTabStore.getState().onThreadSwitched('thread-1')

    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))
    fireEvent.click(await screen.findByRole('menuitem', { name: /Browser/ }))

    await waitFor(() => {
      expect(useUIStore.getState().activeDetailTab.kind).toBe('viewer')
    })
    const active = useUIStore.getState().activeDetailTab
    expect(active.kind).toBe('viewer')
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs.some((tab) => tab.kind === 'browser')).toBe(true)
    expect(window.api.workspace.viewer.browser.create).toHaveBeenCalledWith(expect.objectContaining({
      threadId: 'thread-1'
    }))
  })

  it('opens a terminal viewer tab when the menu returns newTerminal', async () => {
    cs().setWorkspacePath('/workspace/path')
    useThreadStore.getState().setActiveThreadId('thread-1')
    useViewerTabStore.getState().onThreadSwitched('thread-1')

    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))
    fireEvent.click(await screen.findByRole('menuitem', { name: /Terminal/ }))

    await waitFor(() => {
      const active = useUIStore.getState().activeDetailTab
      expect(active.kind).toBe('viewer')
    })
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs.some((tab) => tab.kind === 'terminal')).toBe(true)
  })
})
