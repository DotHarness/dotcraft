// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { handleBrowserUseOpen } from '../utils/browserUseOpenHandler'
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
  it('opens and focuses first browser tab for the active thread', () => {
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

  it('does not duplicate repeated browser tab ids', () => {
    const payload = {
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      title: 'Browser',
      focusMode: 'first-open' as const
    }

    handleBrowserUseOpen(payload)
    handleBrowserUseOpen({ ...payload, initialUrl: 'http://localhost:3001/' })

    const viewerState = useViewerTabStore.getState().getThreadState('thread-a')
    expect(viewerState.tabs).toHaveLength(1)
    const tab = viewerState.tabs[0]
    expect(tab?.kind).toBe('browser')
    if (tab?.kind === 'browser') {
      expect(tab.currentUrl).toBe('http://localhost:3001/')
    }
  })
})
