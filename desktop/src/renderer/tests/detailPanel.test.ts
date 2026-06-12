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
  // Reset UI store state manually
  useUIStore.setState({
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false,
    detailPanelPreferredVisible: true,
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
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
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
    }
  })
})

// ---------------------------------------------------------------------------
// Commit file filter
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Plan todo status mapping
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Auto-show: uiStore.showChangesForFile
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Auto-show: markAutoShowForTurn prevents re-trigger
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// plan/updated notification — store test
// ---------------------------------------------------------------------------

describe('onPlanUpdated via store', () => {
  it('setActiveDetailTab("plan") auto-shows the panel', () => {
    useUIStore.setState({ detailPanelVisible: false })

    ui().setActiveDetailTab('plan')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })
})

// ---------------------------------------------------------------------------
// Viewer tab: setActiveViewerTab / closeViewerTab / lastActiveSystemTab
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Optional system tabs (Diff / Progress) + launcher empty state
// ---------------------------------------------------------------------------

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

  it('resetDetailTabs clears open tabs and shows the launcher', () => {
    ui().setActiveDetailTab('changes')
    ui().resetDetailTabs()
    expect(ui().openSystemTabs).toEqual([])
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
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
    expect(screen.getByRole('menuitem', { name: /Open File/ })).not.toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Browser/ })).not.toHaveProperty('disabled', true)
    expect(screen.getByRole('menuitem', { name: /Terminal/ })).not.toHaveProperty('disabled', true)
  })

  it('disables browser and terminal menu items without an active workspace thread', async () => {
    render(createElement(Harness, { workspacePath: '' }))

    fireEvent.click(screen.getByLabelText('Add tab'))

    expect(await screen.findByRole('menuitem', { name: /Open File/ })).not.toHaveProperty('disabled', true)
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

  it('opens Quick Open when the menu returns openFile', async () => {
    render(createElement(Harness, {}))

    fireEvent.click(screen.getByLabelText('Add tab'))
    fireEvent.click(await screen.findByRole('menuitem', { name: /Open File/ }))

    await waitFor(() => {
      expect(useUIStore.getState().quickOpenVisible).toBe(true)
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
