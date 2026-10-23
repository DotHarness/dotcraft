// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { handleBrowserUseClose, handleBrowserUseOpen } from '../utils/browserUseOpenHandler'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

beforeEach(() => {
  useThreadStore.setState({
    activeThreadId: 'thread-a'
  })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: 'thread-a',
    currentWorkspacePath: 'F:/workspace'
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
})

describe('handleBrowserUseOpen', () => {
  it('opens and focuses first browser tab for the active thread without duplicating repeated ids', () => {
    handleBrowserUseOpen({
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      title: 'Browser',
      focusMode: 'first-open'
    })

    const viewerState = useViewerTabStore.getState().getThreadState('thread-a')
    expect(viewerState.tabs).toHaveLength(1)
    expect(viewerState.activeTabId).toBe('browser-thread-a-1')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(useUIStore.getState().activeDetailTab).toEqual({
      kind: 'viewer',
      id: 'browser-thread-a-1'
    })

    handleBrowserUseOpen({
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3001/',
      focusMode: 'first-open'
    })
    const reopened = useViewerTabStore.getState().getThreadState('thread-a').tabs
    expect(reopened).toHaveLength(1)
    expect(reopened[0]).toMatchObject({ kind: 'browser', currentUrl: 'http://localhost:3001/' })
  })

  it('registers non-active thread tabs without switching the active thread', () => {
    handleBrowserUseOpen({
      threadId: 'thread-b',
      tabId: 'browser-thread-b-1',
      initialUrl: 'http://localhost:3000/',
      focusMode: 'first-open'
    })

    expect(useThreadStore.getState().activeThreadId).toBe('thread-a')
    expect(useUIStore.getState().activeMainView).toBe('settings')
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useViewerTabStore.getState().getThreadState('thread-b').tabs).toHaveLength(1)
  })

  it('removes browser-use tabs on close events and ignores unknown tabs', () => {
    handleBrowserUseOpen({
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      title: 'First',
      focusMode: 'first-open'
    })
    handleBrowserUseOpen({
      threadId: 'thread-a',
      tabId: 'browser-thread-a-2',
      initialUrl: 'http://localhost:3001/',
      title: 'Second',
      focusMode: 'none'
    })

    handleBrowserUseClose({
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1'
    })

    const viewerState = useViewerTabStore.getState().getThreadState('thread-a')
    expect(viewerState.tabs.map((tab) => tab.id)).toEqual(['browser-thread-a-2'])
    expect(viewerState.activeTabId).toBe('browser-thread-a-2')

    handleBrowserUseClose({ threadId: 'thread-a', tabId: 'missing-tab' })
    expect(useViewerTabStore.getState().getThreadState('thread-a').tabs).toHaveLength(1)
  })
})
