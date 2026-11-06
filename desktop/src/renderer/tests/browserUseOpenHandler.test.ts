// @vitest-environment jsdom
import { getRendererTestState, setRendererTestState } from './panelTestState'
import { beforeEach, describe, expect, it } from 'vitest'
import { handleBrowserUseClose, handleBrowserUseOpen } from '../utils/browserUseOpenHandler'
import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

beforeEach(() => {
  useThreadStore.setState({
    activeThreadId: 'thread-a'
  })
  useDetailPanelStore.setState({
    byThread: new Map(),
    currentThreadId: 'thread-a',
    currentWorkspacePath: 'F:/workspace'
  })
  setRendererTestState({
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
      projectKey: '',
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      title: 'Browser',
      focusMode: 'first-open'
    })

    const viewerState = useDetailPanelStore.getState().getThreadState('thread-a')
    expect(viewerState.tabs).toHaveLength(1)
    expect(viewerState.activeTabId).toBe('browser-thread-a-1')
    expect(getRendererTestState().activeMainView).toBe('conversation')
    expect(getRendererTestState().activeDetailTab).toEqual({
      kind: 'viewer',
      id: 'browser-thread-a-1'
    })

    handleBrowserUseOpen({
      projectKey: '',
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3001/',
      focusMode: 'first-open'
    })
    const reopened = useDetailPanelStore.getState().getThreadState('thread-a').tabs
    expect(reopened).toHaveLength(1)
    expect(reopened[0]).toMatchObject({ kind: 'browser', currentUrl: 'http://localhost:3001/' })
  })

  it('registers non-active thread tabs without switching the active thread', () => {
    handleBrowserUseOpen({
      projectKey: '',
      threadId: 'thread-b',
      tabId: 'browser-thread-b-1',
      initialUrl: 'http://localhost:3000/',
      focusMode: 'first-open'
    })

    expect(useThreadStore.getState().activeThreadId).toBe('thread-a')
    expect(getRendererTestState().activeMainView).toBe('settings')
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useDetailPanelStore.getState().getThreadState('thread-b').tabs).toHaveLength(1)
  })

  it('removes browser-use tabs on close events and ignores unknown tabs', () => {
    handleBrowserUseOpen({
      projectKey: '',
      threadId: 'thread-a',
      tabId: 'browser-thread-a-1',
      initialUrl: 'http://localhost:3000/',
      title: 'First',
      focusMode: 'first-open'
    })
    handleBrowserUseOpen({
      projectKey: '',
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

    const viewerState = useDetailPanelStore.getState().getThreadState('thread-a')
    expect(viewerState.tabs.map((tab) => tab.id)).toEqual(['browser-thread-a-2'])
    expect(viewerState.activeTabId).toBe('browser-thread-a-2')

    handleBrowserUseClose({ threadId: 'thread-a', tabId: 'missing-tab' })
    expect(useDetailPanelStore.getState().getThreadState('thread-a').tabs).toHaveLength(1)
  })
})
