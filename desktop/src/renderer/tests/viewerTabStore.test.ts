import { beforeEach, describe, expect, it } from 'vitest'
import type { FileViewerTab } from '../../shared/viewer/types'
import { useViewerTabStore } from '../stores/viewerTabStore'

const store = () => useViewerTabStore.getState()

const THREAD_A = 'thread-a'
const THREAD_B = 'thread-b'
const WS_PATH = '/home/user/project'

function openFile(
  threadId: string,
  relativePath: string,
  extra: Partial<Parameters<ReturnType<typeof store>['openFile']>[0]> = {}
): string {
  return store().openFile({
    threadId,
    absolutePath: `${WS_PATH}/${relativePath}`,
    relativePath,
    contentClass: 'text',
    ...extra
  })
}

beforeEach(() => {
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    currentWorkspacePath: null
  })
})

describe('openWorkflow', () => {
  it('deduplicates workflow tabs by run within each parent thread', () => {
    const first = store().openWorkflow({ threadId: THREAD_A, runId: 'run_001', initialLabel: 'Workflow' })
    const second = store().openWorkflow({ threadId: THREAD_A, runId: 'run_001', initialLabel: 'Workflow' })
    const state = store().getThreadState(THREAD_A)

    expect(second).toBe(first)
    expect(state.tabs).toEqual([
      expect.objectContaining({ id: first, kind: 'workflow', threadId: THREAD_A, runId: 'run_001' })
    ])
    expect(state.activeTabId).toBe(first)
  })
})

describe('openFiles', () => {
  it('reuses one empty viewer per thread and replaces it in place when a file is selected', () => {
    const first = store().openFiles({ threadId: THREAD_A, initialLabel: 'Files' })
    expect(store().openFiles({ threadId: THREAD_A, initialLabel: 'Files' })).toBe(first)

    const fileId = openFile(THREAD_A, 'src/index.ts')
    const state = store().getThreadState(THREAD_A)
    expect(fileId).toBe(first)
    expect(state.tabs).toHaveLength(1)
    expect(state.tabs[0]).toMatchObject({ id: first, kind: 'file', relativePath: 'src/index.ts' })
  })

  it('closes the active placeholder and focuses an already open file', () => {
    const existingId = openFile(THREAD_A, 'src/index.ts')
    store().openFiles({ threadId: THREAD_A, initialLabel: 'Files' })
    const focusedId = openFile(THREAD_A, 'src/index.ts')
    const state = store().getThreadState(THREAD_A)
    expect(focusedId).toBe(existingId)
    expect(state.tabs).toHaveLength(1)
    expect(state.activeTabId).toBe(existingId)
  })
})

describe('openFile', () => {
  it('deduplicates by absolute path, activating new tabs and opening forceNew duplicates', () => {
    const a = openFile(THREAD_A, 'src/a.ts')
    expect(openFile(THREAD_A, 'src/a.ts')).toBe(a)
    const b = openFile(THREAD_A, 'src/b.ts')
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(b)

    const forced = openFile(THREAD_A, 'src/a.ts', { forceNew: true })
    expect(forced).not.toBe(a)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(3)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(forced)
  })

  it('treats separator spellings of one path as the same tab', () => {
    const forward = openFile(THREAD_A, 'decks/q4.pptx', { absolutePath: 'D:/work/decks/q4.pptx' })
    expect(openFile(THREAD_A, 'decks/q4.pptx', { absolutePath: 'D:\\work\\decks\\q4.pptx' })).toBe(forward)
  })

  it('keeps case-distinct POSIX paths in separate tabs', () => {
    const upper = openFile(THREAD_A, 'Report.csv', { absolutePath: '/work/Report.csv' })
    expect(openFile(THREAD_A, 'report.csv', { absolutePath: '/work/report.csv' })).not.toBe(upper)
  })

  it('refreshes, bumps, and clears navigation hints when focusing an existing tab', () => {
    const id = openFile(THREAD_A, 'src/foo.ts', { navigationHint: { line: 5 } })
    expect(store().getThreadState(THREAD_A).tabs[0]).toMatchObject({ navigationRevision: 1 })

    openFile(THREAD_A, 'src/foo.ts', { navigationHint: { line: 17, column: 3 } })
    expect(store().getThreadState(THREAD_A).tabs[0]).toMatchObject({
      id,
      navigationHint: { line: 17, column: 3 },
      navigationRevision: 2
    })

    openFile(THREAD_A, 'src/foo.ts', { navigationHint: { line: 17, column: 3 } })
    expect(store().getThreadState(THREAD_A).tabs[0]).toMatchObject({ navigationRevision: 3 })

    openFile(THREAD_A, 'src/foo.ts')
    const tab = store().getThreadState(THREAD_A).tabs[0]
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(tab).not.toHaveProperty('navigationHint')
  })

  it('disambiguates colliding basenames only', () => {
    openFile(THREAD_A, 'src/components/Button.tsx')
    openFile(THREAD_A, 'src/elements/Button.tsx')
    openFile(THREAD_A, 'lib/utils.ts')

    const labels = store().getThreadState(THREAD_A).tabs.map((t) => t.label)
    expect(new Set(labels.slice(0, 2)).size).toBe(2)
    expect(labels.slice(0, 2)).not.toContain('Button.tsx')
    expect(labels[2]).toBe('utils.ts')
  })
})

describe('openBrowser / updateBrowserTab', () => {
  it('opens a browser tab with default state and makes it active', () => {
    const id = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })

    const threadState = store().getThreadState(THREAD_A)
    expect(threadState.tabs.find((item) => item.id === id)).toMatchObject({
      kind: 'browser',
      currentUrl: 'https://example.com',
      label: 'example.com',
      canGoBack: false,
      canGoForward: false
    })
    expect(threadState.activeTabId).toBe(id)
  })

  it('deduplicates caller-provided browser tab ids and can register without activating', () => {
    const fileId = openFile(THREAD_A, 'src/index.ts')
    const first = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      activate: false
    })
    expect(first).toBe('browser-thread-a-1')
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(fileId)

    const second = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3001/',
      initialLabel: 'Updated'
    })

    const threadState = store().getThreadState(THREAD_A)
    expect(second).toBe(first)
    expect(threadState.tabs).toHaveLength(2)
    expect(threadState.tabs[1]).toMatchObject({ kind: 'browser', currentUrl: 'http://localhost:3001/', title: 'Updated' })
  })

  it('patches browser tab page and automation state', () => {
    const id = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })

    store().updateBrowserTab(THREAD_A, id, {
      title: 'Example Domain',
      canGoBack: true,
      automationActive: true,
      automationSessionName: 'DotCraft',
      lastAutomationAction: 'click'
    })

    expect(store().getThreadState(THREAD_A).tabs.find((item) => item.id === id)).toMatchObject({
      kind: 'browser',
      title: 'Example Domain',
      label: 'Example Domain',
      canGoBack: true,
      automationActive: true,
      automationSessionName: 'DotCraft',
      lastAutomationAction: 'click'
    })
  })

  it('focuses matching browser tab by normalized URL within the thread only', () => {
    const browserA = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com/' })
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com/path/?q=1' })
    const browserB = store().openBrowser({ threadId: THREAD_B, initialUrl: 'https://example.com/' })

    const focused = store().focusBrowserTabByUrl({ threadId: THREAD_A, url: 'HTTPS://Example.com#section' })
    expect(focused).toBe(browserA)
    expect(focused).not.toBe(browserB)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(browserA)
  })
})

describe('openTerminal / updateTerminalTab', () => {
  it('opens terminal tabs with incremental labels and patches runtime state', () => {
    const terminal1 = store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    const terminal2 = store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    store().updateTerminalTab(THREAD_A, terminal2, {
      hasStarted: true,
      pid: 1234,
      shell: 'powershell.exe',
      exited: { code: 0, signal: null }
    })

    const state = store().getThreadState(THREAD_A)
    expect(state.tabs.find((item) => item.id === terminal1)?.label).toBe('Terminal 1')
    expect(state.tabs.find((item) => item.id === terminal2)).toMatchObject({
      label: 'Terminal 2',
      hasStarted: true,
      pid: 1234,
      shell: 'powershell.exe',
      exited: { code: 0 }
    })
    expect(state.activeTabId).toBe(terminal2)
  })
})

describe('closeTab', () => {
  it('selects the neighbor of the closed active tab and ignores unknown ids', () => {
    const a = openFile(THREAD_A, 'a.ts')
    const b = openFile(THREAD_A, 'b.ts')
    const c = openFile(THREAD_A, 'c.ts')

    store().closeTab(THREAD_A, 'nonexistent')
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(3)

    store().closeTab(THREAD_A, c)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(b)

    store().setActiveTab(THREAD_A, a)
    store().closeTab(THREAD_A, a)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(b)

    store().closeTab(THREAD_A, b)
    expect(store().getThreadState(THREAD_A)).toMatchObject({ tabs: [], activeTabId: null })
  })
})

describe('thread lifecycle', () => {
  it('keeps tabs per thread and exposes the current thread tabs after switching', () => {
    expect(store().getCurrentTabs()).toHaveLength(0)
    expect(store().getCurrentActiveTabId()).toBeNull()
    expect(store().getCurrentTabs()).toBe(store().getCurrentTabs())
    expect(store().getThreadState('unknown')).toBe(store().getThreadState('unknown'))

    const a = openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_B, 'src/b.ts')
    store().onThreadSwitched(THREAD_A)
    expect(store().getCurrentTabs()).toEqual([expect.objectContaining({ relativePath: 'src/a.ts' })])
    expect(store().getCurrentActiveTabId()).toBe(a)

    store().onThreadSwitched(THREAD_B)
    expect(store().currentThreadId).toBe(THREAD_B)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
  })

  it('onThreadDeleted removes only that thread and reports browser and terminal tabs for cleanup', () => {
    openFile(THREAD_A, 'a.ts')
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://dotcraft.ai' })
    store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    openFile(THREAD_B, 'b.ts')
    const browsers: string[] = []
    const terminals: string[] = []

    store().onThreadDeleted(THREAD_A, {
      onBrowserTabRemoved: (tab) => { browsers.push(tab.id) },
      onTerminalTabRemoved: (tab) => { terminals.push(tab.id) }
    })

    expect(browsers).toHaveLength(2)
    expect(terminals).toHaveLength(1)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(1)
  })

  it('onWorkspaceSwitched clears every thread and reports browser and terminal tabs for cleanup', () => {
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })
    store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    openFile(THREAD_B, 'b.ts')
    let browserRemoved = 0
    let terminalRemoved = 0

    store().onWorkspaceSwitched('/new/workspace', {
      onBrowserTabRemoved: () => { browserRemoved++ },
      onTerminalTabRemoved: () => { terminalRemoved++ }
    })

    expect(browserRemoved).toBe(1)
    expect(terminalRemoved).toBe(1)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(0)
    expect(store().currentWorkspacePath).toBe('/new/workspace')
  })
})

describe('pending reveal', () => {
  const fileTab = (threadId: string, tabId: string) =>
    store().getThreadState(threadId).tabs.find((tab): tab is FileViewerTab => tab.id === tabId && tab.kind === 'file')

  it('hands each reveal to the file tab once and keeps a newer request when an older one is cleared', () => {
    const tabId = openFile(THREAD_A, 'model.xlsx', {
      contentClass: 'spreadsheet',
      reveal: { targetKind: 'range', location: { sheet: 'Revenue', range: 'B6:D8' } }
    })
    const first = fileTab(THREAD_A, tabId)?.pendingReveal
    expect(first).toMatchObject({ targetKind: 'range', location: { sheet: 'Revenue', range: 'B6:D8' } })

    expect(openFile(THREAD_A, 'model.xlsx', {
      contentClass: 'spreadsheet',
      reveal: { targetKind: 'object', location: { sheet: 'Revenue', elementId: 'chart-1' } }
    })).toBe(tabId)
    const second = fileTab(THREAD_A, tabId)?.pendingReveal
    expect(second?.revision).toBeGreaterThan(first!.revision)

    store().clearPendingReveal(THREAD_A, tabId, first!.revision)
    expect(fileTab(THREAD_A, tabId)?.pendingReveal).toEqual(second)
    store().clearPendingReveal(THREAD_A, tabId, second!.revision)
    expect(fileTab(THREAD_A, tabId)?.pendingReveal).toBeUndefined()

    openFile(THREAD_A, 'model.xlsx', { contentClass: 'spreadsheet' })
    expect(fileTab(THREAD_A, tabId)?.pendingReveal).toBeUndefined()
  })
})
