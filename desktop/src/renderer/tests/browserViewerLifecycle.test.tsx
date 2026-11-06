import { render, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { BrowserViewerTab } from '../components/detail/viewers/BrowserViewerTab'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/detail/viewers/BrowserToolbar', () => ({ BrowserToolbar: () => null }))
vi.mock('../components/detail/viewers/BrowserFeedbackControls', () => ({ BrowserFeedbackControls: () => null, BrowserFindBar: () => null }))
vi.mock('../components/detail/viewers/BrowserPageFeedback', () => ({ BrowserPageFeedback: () => null }))

beforeEach(() => {
  useDetailPanelStore.getState().reset()
  useDetailPanelStore.getState().onWorkspaceSwitched('/workspace/a', 'project:a')
  useDetailPanelStore.getState().onThreadSwitched('thread-a')
  useUIStore.getState().setResponsiveLayout('full')
  useUIStore.getState().setActiveMainView('conversation')
})

it('passes durable browser identity and its project to the host and only hides on unmount', async () => {
  const store = useDetailPanelStore.getState()
  const tabId = store.openBrowser({ threadId: 'thread-a', browserStorageId: 'saved-page', restore: 'required', initialUrl: 'https://example.test/' })
  store.setActiveViewerTab(tabId)
  const create = vi.fn(async () => ({ tabId, browserStorageId: 'saved-page', projectKey: 'project:a', suspended: false,
    currentUrl: 'https://example.test/', title: 'Saved', canGoBack: true, canGoForward: false, loading: false }))
  const destroy = vi.fn(async () => {})
  const setVisible = vi.fn(async () => {})
  installDesktopApiMock({ workspace: { viewer: { browser: {
    create, destroy, setVisible, setActive: vi.fn(async () => {}), setBounds: vi.fn(async () => {}),
    onFeedback: () => () => {}, downloads: async () => [],
    enableFeedback: async () => ({ find: { open: false, query: '', current: 0, total: 0 }, zoomPercent: 100 })
  } } } })
  const view = render(<LocaleProvider loadSettings={false}><BrowserViewerTab tabId={tabId} /></LocaleProvider>)
  await waitFor(() => expect(create).toHaveBeenCalledWith(expect.objectContaining({
    tabId, threadId: 'thread-a', projectKey: 'project:a', workspacePath: '/workspace/a', browserStorageId: 'saved-page', restore: 'required'
  })))
  await waitFor(() => expect(useDetailPanelStore.getState().getCurrentTabs()[0]).toMatchObject({ browserStorageId: 'saved-page', canGoBack: true }))
  view.unmount()
  expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: false })
  expect(destroy).not.toHaveBeenCalled()
})
