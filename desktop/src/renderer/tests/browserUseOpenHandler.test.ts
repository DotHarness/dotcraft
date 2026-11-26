// @vitest-environment jsdom
import { getRendererTestState, setRendererTestState } from './panelTestState'
import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useDetailPanelLifecycle } from '../hooks/useDetailPanelLifecycle'
import { handleBrowserEvent } from '../utils/browserEventHandler'
import { handleBrowserUseClose, handleBrowserUseOpen, presentPendingBrowserUseTab } from '../utils/browserUseOpenHandler'
import { installDesktopApiMock } from './desktopApiMock'
import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

beforeEach(() => {
  useThreadStore.setState({
    activeThreadId: 'thread-a'
  })
  useDetailPanelStore.setState({
    byThread: new Map(),
    currentThreadId: 'thread-a',
    currentProjectKey: '',
    currentWorkspacePath: '/workspace'
  })
  setRendererTestState({
    activeMainView: 'agents',
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
    setRendererTestState({ activeMainView: 'conversation' })
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
    expect(getRendererTestState().activeMainView).toBe('agents')
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useDetailPanelStore.getState().getThreadState('thread-b').tabs).toHaveLength(1)
  })

  it('presents a retained page only after its thread returns to the foreground', async () => {
    const projectKey = 'project:a'
    const workspacePath = '/workspace'
    const tabId = 'browser-thread-a-1'
    const setVisible = vi.fn(async () => {})
    installDesktopApiMock({ workspace: { viewer: { browser: {
      setVisible, setActive: vi.fn(async () => {})
    } } } })
    setRendererTestState({ activeMainView: 'conversation' })
    const { rerender } = renderHook(({ threadId }) => useDetailPanelLifecycle({
      projectKey, selectionProjectKey: projectKey, threadId,
      workspacePath, browserWorkspacePath: workspacePath, welcomeScopeId: 'welcome'
    }), { initialProps: { threadId: 'thread-a' } })

    await act(async () => handleBrowserUseOpen({
      projectKey, threadId: 'thread-a', tabId, initialUrl: 'http://localhost:3000/', focusMode: 'first-open'
    }))
    expect(getRendererTestState().detailPanelVisible).toBe(true)
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'viewer', id: tabId })
    expect(setVisible).not.toHaveBeenCalledWith({ tabId, visible: true })
    act(() => useDetailPanelStore.getState().setDetailPanelVisible(false))
    rerender({ threadId: 'thread-b' })
    expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: false })
    setVisible.mockClear()

    act(() => handleBrowserEvent({
      projectKey, threadId: 'thread-a', tabId, type: 'request-show'
    }, { workspacePath, locale: 'en' }))
    expect(setVisible).not.toHaveBeenCalled()
    expect(getRendererTestState().detailPanelVisible).toBe(false)

    rerender({ threadId: 'thread-a' })
    expect(getRendererTestState().detailPanelVisible).toBe(true)
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'viewer', id: tabId })

    act(() => handleBrowserEvent({
      projectKey, threadId: 'thread-a', tabId, type: 'request-hide'
    }, { workspacePath, locale: 'en' }))
    expect(getRendererTestState().detailPanelVisible).toBe(false)
    expect(getRendererTestState().tabs.map(tab => tab.id)).toEqual([tabId])
  })

  it.each(['hide', 'close'])('cancels a pending reveal when its tab receives %s', (action) => {
    const tabId = 'browser-thread-b-1'
    handleBrowserUseOpen({
      projectKey: '', threadId: 'thread-b', tabId, initialUrl: 'http://localhost:3000/', focusMode: 'first-open'
    })

    if (action === 'hide') handleBrowserEvent({
      projectKey: '', threadId: 'thread-b', tabId, type: 'request-hide'
    }, { workspacePath: '/workspace', locale: 'en' })
    else handleBrowserUseClose({ threadId: 'thread-b', tabId })

    useThreadStore.setState({ activeThreadId: 'thread-b' })
    setRendererTestState({ activeMainView: 'conversation' })
    presentPendingBrowserUseTab()
    expect(getRendererTestState().detailPanelVisible).toBe(false)
  })

  it('removes browser-use tabs on close events and ignores unknown tabs', () => {
    setRendererTestState({ activeMainView: 'conversation' })
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
