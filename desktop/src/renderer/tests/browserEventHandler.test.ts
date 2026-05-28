// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { handleBrowserEvent } from '../utils/browserEventHandler'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import type { BrowserEventPayload } from '../../shared/viewer/types'

const THREAD_A = 'thread-a'
const THREAD_B = 'thread-b'
const WORKSPACE_PATH = 'F:/workspace'

const createBrowser = vi.fn()

function store() {
  return useViewerTabStore.getState()
}

function installWindowApi(): void {
  Object.defineProperty(window, 'api', {
    value: {
      workspace: {
        viewer: {
          browser: {
            create: createBrowser
          }
        }
      }
    },
    configurable: true
  })
}

function resetStores(): void {
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: THREAD_A,
    currentWorkspacePath: WORKSPACE_PATH
  })
  useUIStore.setState({
    activeMainView: 'settings',
    activeDetailTab: { kind: 'system', id: 'changes' },
    detailPanelPreferredVisible: false,
    detailPanelVisible: false,
    responsiveLayout: 'full',
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false
  })
}

function openBrowserTab(threadId: string, tabId: string, initialUrl = 'about:blank'): string {
  return store().openBrowser({
    threadId,
    tabId,
    target: tabId,
    initialUrl,
    initialLabel: 'Browser',
    activate: true
  })
}

function handle(event: BrowserEventPayload, workspacePath = WORKSPACE_PATH): void {
  handleBrowserEvent(event, {
    locale: 'en',
    workspacePath
  })
}

beforeEach(() => {
  createBrowser.mockReset()
  createBrowser.mockResolvedValue(null)
  installWindowApi()
  resetStores()
})

describe('handleBrowserEvent', () => {
  it('applies normal browser events only to the target tab', () => {
    openBrowserTab(THREAD_A, 'tab-a-1')
    openBrowserTab(THREAD_A, 'tab-a-2')

    handle({ type: 'did-start-loading', tabId: 'tab-a-1' })
    handle({ type: 'did-navigate', tabId: 'tab-a-1', url: 'https://example.com/' })

    const threadState = store().getThreadState(THREAD_A)
    const target = threadState.tabs.find((tab) => tab.id === 'tab-a-1')
    const other = threadState.tabs.find((tab) => tab.id === 'tab-a-2')
    expect(target?.kind).toBe('browser')
    expect(other?.kind).toBe('browser')
    if (target?.kind === 'browser') {
      expect(target.currentUrl).toBe('https://example.com/')
      expect(target.loading).toBe(false)
    }
    if (other?.kind === 'browser') {
      expect(other.currentUrl).toBe('about:blank')
      expect(other.loading).toBe(false)
    }
  })

  it('updates background thread tabs when the event includes a threadId', () => {
    openBrowserTab(THREAD_A, 'tab-a')
    openBrowserTab(THREAD_B, 'tab-b')

    handle({
      type: 'automation-updated',
      tabId: 'tab-b',
      threadId: THREAD_B,
      sessionName: 'smoke',
      action: 'click'
    })

    const backgroundTab = store().getThreadState(THREAD_B).tabs.find((tab) => tab.id === 'tab-b')
    const currentTab = store().getThreadState(THREAD_A).tabs.find((tab) => tab.id === 'tab-a')
    expect(backgroundTab?.kind).toBe('browser')
    expect(currentTab?.kind).toBe('browser')
    if (backgroundTab?.kind === 'browser') {
      expect(backgroundTab.automationActive).toBe(true)
      expect(backgroundTab.automationSessionName).toBe('smoke')
      expect(backgroundTab.lastAutomationAction).toBe('click')
    }
    if (currentTab?.kind === 'browser') {
      expect(currentTab.automationActive).toBeUndefined()
    }
  })

  it('creates and activates a requested popup tab for the current thread', () => {
    openBrowserTab(THREAD_A, 'source-tab')

    handle({
      type: 'request-new-tab',
      tabId: 'source-tab',
      threadId: THREAD_A,
      url: 'https://example.com/popup'
    })

    const threadState = store().getThreadState(THREAD_A)
    const newTab = threadState.tabs.find((tab) => tab.id !== 'source-tab')
    expect(newTab?.kind).toBe('browser')
    expect(threadState.activeTabId).toBe(newTab?.id)
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: newTab?.id })
    expect(createBrowser).toHaveBeenCalledWith({
      tabId: newTab?.id,
      threadId: THREAD_A,
      workspacePath: WORKSPACE_PATH,
      initialUrl: 'https://example.com/popup'
    })
  })

  it('creates requested popup tabs for background threads without stealing UI focus', () => {
    openBrowserTab(THREAD_A, 'tab-a')
    openBrowserTab(THREAD_B, 'source-tab')
    useViewerTabStore.setState({ currentThreadId: THREAD_A })
    useUIStore.setState({ activeDetailTab: { kind: 'system', id: 'changes' } })
    const previousBackgroundActive = store().getThreadState(THREAD_B).activeTabId

    handle({
      type: 'request-new-tab',
      tabId: 'source-tab',
      threadId: THREAD_B,
      url: 'https://example.com/background-popup'
    })

    const backgroundState = store().getThreadState(THREAD_B)
    const newTab = backgroundState.tabs.find((tab) => tab.id !== 'source-tab')
    expect(newTab?.kind).toBe('browser')
    expect(backgroundState.activeTabId).toBe(previousBackgroundActive)
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(createBrowser).toHaveBeenCalledWith({
      tabId: newTab?.id,
      threadId: THREAD_B,
      workspacePath: WORKSPACE_PATH,
      initialUrl: 'https://example.com/background-popup'
    })
  })

  it('ignores request-new-tab when required context is missing', () => {
    openBrowserTab(THREAD_A, 'source-tab')

    handle({ type: 'request-new-tab', tabId: 'source-tab', threadId: THREAD_A })
    handle({ type: 'request-new-tab', tabId: 'source-tab', threadId: THREAD_A, url: 'https://example.com/' }, '')
    handle({ type: 'request-new-tab', tabId: 'missing-tab', threadId: THREAD_A, url: 'https://example.com/' })

    expect(store().getThreadState(THREAD_A).tabs).toHaveLength(1)
    expect(createBrowser).not.toHaveBeenCalled()
  })
})
