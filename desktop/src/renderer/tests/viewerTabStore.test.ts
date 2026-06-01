import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerTabStore } from '../stores/viewerTabStore'

const store = () => useViewerTabStore.getState()

const THREAD_A = 'thread-a'
const THREAD_B = 'thread-b'
const WS_PATH = '/home/user/project'

function openFile(
  threadId: string,
  relativePath: string,
  absolutePath = `${WS_PATH}/${relativePath}`
): string {
  return store().openFile({
    threadId,
    absolutePath,
    relativePath,
    contentClass: 'text'
  })
}

beforeEach(() => {
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    currentWorkspacePath: null
  })
})

// ---------------------------------------------------------------------------
// openFile — basic behaviour
// ---------------------------------------------------------------------------

describe('openFile', () => {
  it('adds a new tab and sets it as active', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'src/index.ts')

    const state = store().getThreadState(THREAD_A)
    expect(state.tabs).toHaveLength(1)
    expect(state.activeTabId).toBe(id)
    const tab = state.tabs[0]
    expect(tab?.kind).toBe('file')
    if (tab?.kind === 'file') {
      expect(tab.relativePath).toBe('src/index.ts')
    }
  })

  it('returns existing tab id on duplicate absolutePath (deduplication)', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'src/foo.ts')
    const id2 = openFile(THREAD_A, 'src/foo.ts') // same file

    expect(id1).toBe(id2)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
  })

  it('refreshes navigation hint when focusing an existing file tab', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text',
      navigationHint: { line: 5 }
    })
    const id2 = store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text',
      navigationHint: { line: 17, column: 3 }
    })

    const state = store().getThreadState(THREAD_A)
    expect(id2).toBe(id1)
    expect(state.tabs).toHaveLength(1)
    expect(state.activeTabId).toBe(id1)
    expect(state.tabs[0]).toMatchObject({
      kind: 'file',
      navigationHint: { line: 17, column: 3 }
    })
  })

  it('clears stale navigation hint when focusing an existing file without one', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text',
      navigationHint: { line: 5 }
    })
    store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text'
    })

    const tab = store().getThreadState(THREAD_A).tabs.find((item) => item.id === id)
    expect(tab).toBeTruthy()
    expect(tab).not.toHaveProperty('navigationHint')
  })

  it('creates separate tabs for different files', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_A, 'src/b.ts')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(2)
  })

  it('creates a new tab when forceNew=true even if absolutePath matches', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text'
    })
    const id2 = store().openFile({
      threadId: THREAD_A,
      absolutePath: `${WS_PATH}/src/foo.ts`,
      relativePath: 'src/foo.ts',
      contentClass: 'text',
      forceNew: true
    })
    expect(id2).not.toBe(id1)
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(2)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id2)
  })
})

describe('openBrowser / updateBrowserTab', () => {
  it('opens a browser tab with default state and makes it active', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({
      threadId: THREAD_A,
      initialUrl: 'https://example.com'
    })

    const threadState = store().getThreadState(THREAD_A)
    const tab = threadState.tabs.find((item) => item.id === id)
    expect(tab).toBeTruthy()
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.currentUrl).toBe('https://example.com')
      expect(tab.label).toBe('example.com')
      expect(tab.canGoBack).toBe(false)
      expect(tab.canGoForward).toBe(false)
    }
    expect(threadState.activeTabId).toBe(id)
  })

  it('opens browser tab with caller-provided id for browser integration', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      target: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      initialLabel: 'Browser'
    })

    expect(id).toBe('browser-thread-a-1')
    const threadState = store().getThreadState(THREAD_A)
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.activeTabId).toBe(id)
  })

  it('deduplicates caller-provided browser tab ids', () => {
    store().onThreadSwitched(THREAD_A)
    const first = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/'
    })
    const second = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3001/',
      initialLabel: 'Updated'
    })

    const threadState = store().getThreadState(THREAD_A)
    expect(second).toBe(first)
    expect(threadState.tabs).toHaveLength(1)
    const tab = threadState.tabs[0]
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.currentUrl).toBe('http://localhost:3001/')
      expect(tab.title).toBe('Updated')
    }
  })

  it('can register browser tab without activating it', () => {
    store().onThreadSwitched(THREAD_A)
    const fileId = openFile(THREAD_A, 'src/index.ts')
    const browserId = store().openBrowser({
      threadId: THREAD_A,
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      activate: false
    })

    expect(browserId).toBe('browser-thread-a-1')
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(fileId)
  })

  it('updates browser tabs via patch and keeps browser kind stable', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })

    store().updateBrowserTab(THREAD_A, id, {
      title: 'Example Domain',
      canGoBack: true,
      faviconDataUrl: 'data:image/png;base64,test'
    })

    const tab = store().getThreadState(THREAD_A).tabs.find((item) => item.id === id)
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.title).toBe('Example Domain')
      expect(tab.label).toBe('Example Domain')
      expect(tab.canGoBack).toBe(true)
      expect(tab.faviconDataUrl).toContain('data:image/png;base64')
    }
  })

  it('updates browser automation state via patch', () => {
    store().onThreadSwitched(THREAD_A)
    const id = store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })

    store().updateBrowserTab(THREAD_A, id, {
      automationActive: true,
      automationSessionName: 'DotCraft',
      lastAutomationAction: 'click',
      virtualCursor: { x: 12, y: 34 }
    })

    const tab = store().getThreadState(THREAD_A).tabs.find((item) => item.id === id)
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.automationActive).toBe(true)
      expect(tab.automationSessionName).toBe('DotCraft')
      expect(tab.lastAutomationAction).toBe('click')
      expect(tab.virtualCursor).toEqual({ x: 12, y: 34 })
    }
  })

  it('focuses matching browser tab by normalized URL', () => {
    store().onThreadSwitched(THREAD_A)
    const browserA = store().openBrowser({
      threadId: THREAD_A,
      initialUrl: 'https://example.com/'
    })
    store().openBrowser({
      threadId: THREAD_A,
      initialUrl: 'https://example.com/path/?q=1'
    })
    const focused = store().focusBrowserTabByUrl({
      threadId: THREAD_A,
      url: 'HTTPS://Example.com#section'
    })
    expect(focused).toBe(browserA)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(browserA)
  })

  it('does not leak browser URL focus across threads', () => {
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com/' })
    const browserB = store().openBrowser({ threadId: THREAD_B, initialUrl: 'https://example.com/' })
    const focused = store().focusBrowserTabByUrl({
      threadId: THREAD_A,
      url: 'https://example.com'
    })
    expect(focused).not.toBe(browserB)
  })
})

describe('openTerminal / updateTerminalTab', () => {
  it('opens terminal tabs with incremental default labels', () => {
    store().onThreadSwitched(THREAD_A)
    const terminal1 = store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    const terminal2 = store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })

    const tabs = store().getThreadState(THREAD_A).tabs
    expect(tabs.find((item) => item.id === terminal1)?.label).toBe('Terminal 1')
    expect(tabs.find((item) => item.id === terminal2)?.label).toBe('Terminal 2')
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(terminal2)
  })

  it('updates terminal runtime state via patch', () => {
    store().onThreadSwitched(THREAD_A)
    const terminal = store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    store().updateTerminalTab(THREAD_A, terminal, {
      hasStarted: true,
      pid: 1234,
      shell: 'powershell.exe',
      exited: { code: 0, signal: null }
    })

    const tab = store().getThreadState(THREAD_A).tabs.find((item) => item.id === terminal)
    expect(tab?.kind).toBe('terminal')
    if (tab?.kind === 'terminal') {
      expect(tab.hasStarted).toBe(true)
      expect(tab.pid).toBe(1234)
      expect(tab.shell).toBe('powershell.exe')
      expect(tab.exited?.code).toBe(0)
    }
  })
})

// ---------------------------------------------------------------------------
// closeTab — nearest-neighbor fallback
// ---------------------------------------------------------------------------

describe('closeTab', () => {
  it('selects left neighbor when closing the rightmost active tab', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'a.ts')
    const id2 = openFile(THREAD_A, 'b.ts') // active

    store().closeTab(THREAD_A, id2)

    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id1)
  })

  it('selects right neighbor when closing the leftmost tab', () => {
    store().onThreadSwitched(THREAD_A)
    const id1 = openFile(THREAD_A, 'a.ts') // active first, then overridden
    const id2 = openFile(THREAD_A, 'b.ts')
    store().setActiveTab(THREAD_A, id1) // make id1 active

    store().closeTab(THREAD_A, id1)

    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id2)
  })

  it('sets activeTabId to null when last tab is closed', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'only.ts')

    store().closeTab(THREAD_A, id)

    expect(store().getThreadState(THREAD_A).activeTabId).toBeNull()
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
  })

  it('is a no-op when tabId does not exist', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'a.ts')

    store().closeTab(THREAD_A, 'nonexistent')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(store().getThreadState(THREAD_A).activeTabId).toBe(id)
  })
})

// ---------------------------------------------------------------------------
// Label collision deduplication
// ---------------------------------------------------------------------------

describe('label collision', () => {
  it('deduplicates tabs with the same basename by prepending parent dir', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/components/Button.tsx', `${WS_PATH}/src/components/Button.tsx`)
    openFile(THREAD_A, 'src/elements/Button.tsx', `${WS_PATH}/src/elements/Button.tsx`)

    const tabs = store().getThreadState(THREAD_A).tabs
    const labels = tabs.map((t) => t.label)

    // Labels must be unique
    expect(new Set(labels).size).toBe(2)
    // Each label should contain extra path info, not just 'Button.tsx'
    for (const l of labels) {
      expect(l).not.toBe('Button.tsx')
    }
  })

  it('uses just the basename when there is no collision', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/index.ts', `${WS_PATH}/src/index.ts`)
    openFile(THREAD_A, 'lib/utils.ts', `${WS_PATH}/lib/utils.ts`)

    const tabs = store().getThreadState(THREAD_A).tabs
    expect(tabs[0]!.label).toBe('index.ts')
    expect(tabs[1]!.label).toBe('utils.ts')
  })
})

// ---------------------------------------------------------------------------
// Thread isolation
// ---------------------------------------------------------------------------

describe('thread isolation', () => {
  it('tabs in different threads do not interfere', () => {
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_B, 'src/b.ts')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(1)
    const tabA = store().getThreadState(THREAD_A).tabs[0]
    const tabB = store().getThreadState(THREAD_B).tabs[0]
    expect(tabA?.kind).toBe('file')
    expect(tabB?.kind).toBe('file')
    if (tabA?.kind === 'file') {
      expect(tabA.relativePath).toBe('src/a.ts')
    }
    if (tabB?.kind === 'file') {
      expect(tabB.relativePath).toBe('src/b.ts')
    }
  })
})

// ---------------------------------------------------------------------------
// onThreadSwitched
// ---------------------------------------------------------------------------

describe('onThreadSwitched', () => {
  it('updates currentThreadId without clearing tab state', () => {
    openFile(THREAD_A, 'a.ts')
    store().onThreadSwitched(THREAD_B)

    expect(store().currentThreadId).toBe(THREAD_B)
    // Thread A state preserved
    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
  })
})

// ---------------------------------------------------------------------------
// onThreadDeleted
// ---------------------------------------------------------------------------

describe('onThreadDeleted', () => {
  it('removes all tabs for the deleted thread', () => {
    openFile(THREAD_A, 'a.ts')
    openFile(THREAD_B, 'b.ts')

    store().onThreadDeleted(THREAD_A)

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(1)
  })

  it('enumerates browser tabs for cleanup callback', () => {
    openFile(THREAD_A, 'a.ts')
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://dotcraft.ai' })
    const removed: string[] = []

    store().onThreadDeleted(THREAD_A, {
      onBrowserTabRemoved: (tab) => {
        removed.push(tab.id)
      }
    })

    expect(removed).toHaveLength(2)
  })

  it('enumerates terminal tabs for cleanup callback', () => {
    store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    const removed: string[] = []

    store().onThreadDeleted(THREAD_A, {
      onTerminalTabRemoved: (tab) => {
        removed.push(tab.id)
      }
    })

    expect(removed).toHaveLength(2)
  })
})

// ---------------------------------------------------------------------------
// onWorkspaceSwitched
// ---------------------------------------------------------------------------

describe('onWorkspaceSwitched', () => {
  it('clears all tabs across all threads', () => {
    openFile(THREAD_A, 'a.ts')
    openFile(THREAD_B, 'b.ts')

    store().onWorkspaceSwitched('/new/workspace')

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(0)
    expect(store().getThreadState(THREAD_B).tabs).toHaveLength(0)
    expect(store().currentWorkspacePath).toBe('/new/workspace')
  })

  it('invokes cleanup callbacks for browser and terminal tabs', () => {
    store().openBrowser({ threadId: THREAD_A, initialUrl: 'https://example.com' })
    store().openTerminal({ threadId: THREAD_A, cwd: WS_PATH })
    let browserRemoved = 0
    let terminalRemoved = 0

    store().onWorkspaceSwitched('/new/workspace', {
      onBrowserTabRemoved: () => { browserRemoved++ },
      onTerminalTabRemoved: () => { terminalRemoved++ }
    })

    expect(browserRemoved).toBe(1)
    expect(terminalRemoved).toBe(1)
  })
})

// ---------------------------------------------------------------------------
// getCurrentTabs / getCurrentActiveTabId
// ---------------------------------------------------------------------------

describe('getCurrentTabs / getCurrentActiveTabId', () => {
  it('returns empty array when no thread is active', () => {
    expect(store().getCurrentTabs()).toHaveLength(0)
    expect(store().getCurrentActiveTabId()).toBeNull()
  })

  it('returns stable references for missing thread and no active thread', () => {
    const threadStateA = store().getThreadState('unknown-thread')
    const threadStateB = store().getThreadState('unknown-thread')
    expect(threadStateA).toBe(threadStateB)
    expect(threadStateA.tabs).toBe(threadStateB.tabs)

    const currentTabsA = store().getCurrentTabs()
    const currentTabsB = store().getCurrentTabs()
    expect(currentTabsA).toBe(currentTabsB)
  })

  it('returns the current thread tabs', () => {
    store().onThreadSwitched(THREAD_A)
    openFile(THREAD_A, 'src/a.ts')
    openFile(THREAD_A, 'src/b.ts')

    expect(store().getCurrentTabs()).toHaveLength(2)
  })

  it('returns the active tab id for the current thread', () => {
    store().onThreadSwitched(THREAD_A)
    const id = openFile(THREAD_A, 'src/a.ts')

    expect(store().getCurrentActiveTabId()).toBe(id)
  })
})
