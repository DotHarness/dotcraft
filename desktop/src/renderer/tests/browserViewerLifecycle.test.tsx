import { act, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ViewerTab } from '../components/detail/ViewerTab'
import { BrowserViewerTab } from '../components/detail/viewers/BrowserViewerTab'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'
import { useUIStore } from '../stores/uiStore'
import { handleBrowserEvent } from '../utils/browserEventHandler'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/detail/viewers/BrowserToolbar', () => ({
  BrowserToolbar: ({ onAddressDismiss }: { onAddressDismiss?: () => void }) => (
    <button type="button" onClick={onAddressDismiss}>dismiss-address</button>
  )
}))
vi.mock('../components/detail/viewers/BrowserFeedbackControls', () => ({ BrowserFeedbackControls: () => null, BrowserFindBar: () => null }))
vi.mock('../components/detail/viewers/BrowserPageFeedback', () => ({ BrowserPageFeedback: () => null }))

function rectOf(left: number, top: number, width: number, height: number): DOMRect {
  return {
    x: left, y: top, left, top, width, height, right: left + width, bottom: top + height,
    toJSON: () => ({})
  } as DOMRect
}

function mockBrowserApi(tabId: string, snapshot: Partial<{
  browserStorageId: string
  currentUrl: string
  title: string
  canGoBack: boolean
}> = {}) {
  const create = vi.fn(async () => ({
    tabId,
    browserStorageId: snapshot.browserStorageId ?? 'page-1',
    projectKey: 'project:a',
    suspended: false,
    currentUrl: snapshot.currentUrl ?? 'https://example.test/',
    title: snapshot.title ?? 'Browser',
    canGoBack: snapshot.canGoBack ?? false,
    canGoForward: false,
    loading: false
  }))
  const destroy = vi.fn(async () => {})
  const setVisible = vi.fn(async () => {})
  const setActive = vi.fn(async () => {})
  const setBounds = vi.fn(async () => {})
  installDesktopApiMock({
    workspace: {
      viewer: {
        browser: {
          create,
          destroy,
          setVisible,
          setActive,
          setBounds,
          onFeedback: () => () => {},
          downloads: async () => [],
          enableFeedback: async () => ({ find: { open: false, query: '', current: 0, total: 0 }, zoomPercent: 100 })
        }
      }
    }
  })
  return { create, destroy, setVisible, setActive, setBounds }
}

beforeEach(() => {
  useDetailPanelStore.getState().reset()
  useDetailPanelStore.getState().onWorkspaceSwitched('/workspace/a', 'project:a')
  useDetailPanelStore.getState().onThreadSwitched('thread-a')
  useUIStore.getState().setResponsiveLayout('full')
  useUIStore.getState().setActiveMainView('conversation')
  useUIStore.getState().setQuickOpenVisible(false)
  useTransientOverlayStore.setState({ openDepths: [], topDepth: 0, nativeViewBlockerCount: 0 })
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('passes durable browser identity and its project to the host and only hides on unmount', async () => {
  const store = useDetailPanelStore.getState()
  const tabId = store.openBrowser({ threadId: 'thread-a', browserStorageId: 'saved-page', restore: 'required', initialUrl: 'https://example.test/' })
  store.setActiveViewerTab(tabId)
  const { create, destroy, setVisible } = mockBrowserApi(tabId, { browserStorageId: 'saved-page', title: 'Saved', canGoBack: true })
  const view = render(<LocaleProvider loadSettings={false}><BrowserViewerTab tabId={tabId} /></LocaleProvider>)
  await waitFor(() => expect(create).toHaveBeenCalledWith(expect.objectContaining({
    tabId, threadId: 'thread-a', projectKey: 'project:a', workspacePath: '/workspace/a', browserStorageId: 'saved-page', restore: 'required'
  })))
  await waitFor(() => expect(useDetailPanelStore.getState().getCurrentTabs()[0]).toMatchObject({ browserStorageId: 'saved-page', canGoBack: true }))
  view.unmount()
  expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: false })
  expect(destroy).not.toHaveBeenCalled()
})

it('keeps BrowserViewerTab mounted and syncing bounds when did-fail-load sets errorMessage', async () => {
  let width = 400
  const resizeCallbacks: ResizeObserverCallback[] = []
  vi.stubGlobal('ResizeObserver', class {
    private readonly callback: ResizeObserverCallback
    constructor(callback: ResizeObserverCallback) { this.callback = callback }
    observe(): void { resizeCallbacks.push(this.callback) }
    disconnect(): void {}
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => rectOf(600, 80, width, 600))

  const store = useDetailPanelStore.getState()
  const tabId = store.openBrowser({ threadId: 'thread-a', initialUrl: 'http://localhost:5173/' })
  store.setActiveViewerTab(tabId)
  const { setBounds } = mockBrowserApi(tabId, { currentUrl: 'http://localhost:5173/', title: 'Preview' })

  const view = render(<LocaleProvider loadSettings={false}><ViewerTab tabId={tabId} /></LocaleProvider>)
  await waitFor(() => expect(setBounds).toHaveBeenCalledWith({ tabId, x: 600, y: 80, width: 400, height: 600 }))

  act(() => {
    handleBrowserEvent({ type: 'did-fail-load', tabId, message: 'ERR_CONNECTION_REFUSED' }, { locale: 'en', workspacePath: '/workspace/a' })
  })
  await waitFor(() => expect(screen.getByRole('status').textContent).toContain('ERR_CONNECTION_REFUSED'))

  setBounds.mockClear()
  width = 680
  act(() => {
    for (const cb of resizeCallbacks) cb([], {} as ResizeObserver)
  })
  await waitFor(() => expect(setBounds).toHaveBeenCalledWith({ tabId, x: 600, y: 80, width: 680, height: 600 }))

  act(() => {
    handleBrowserEvent({ type: 'did-navigate', tabId, url: 'http://localhost:5173/ok' }, { locale: 'en', workspacePath: '/workspace/a' })
  })
  await waitFor(() => expect(screen.queryByRole('status')).toBeNull())

  view.unmount()
})

it('hides the browser guest on overlays and does not activate blank tabs on address dismiss', async () => {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => rectOf(500, 80, 500, 600))
  const store = useDetailPanelStore.getState()
  const tabId = store.openBrowser({ threadId: 'thread-a', initialUrl: 'https://example.test/' })
  store.setActiveViewerTab(tabId)
  const callOrder: string[] = []
  const { setBounds, setVisible, setActive } = mockBrowserApi(tabId, { browserStorageId: 'page-2', title: 'Example' })
  setBounds.mockImplementation(async () => { callOrder.push('setBounds') })
  setVisible.mockImplementation(async ({ visible }: { tabId: string; visible: boolean }) => { callOrder.push(`setVisible:${visible}`) })
  setActive.mockImplementation(async () => { callOrder.push('setActive') })

  const view = render(<LocaleProvider loadSettings={false}><BrowserViewerTab tabId={tabId} /></LocaleProvider>)
  await waitFor(() => expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: true }))

  act(() => useTransientOverlayStore.getState().pushNativeViewBlocker())
  await waitFor(() => expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: false }))

  callOrder.length = 0
  act(() => useTransientOverlayStore.getState().popNativeViewBlocker())
  await waitFor(() => expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: true }))
  expect(callOrder.indexOf('setBounds')).toBeGreaterThanOrEqual(0)
  expect(callOrder.indexOf('setBounds')).toBeLessThan(callOrder.indexOf('setVisible:true'))

  act(() => useDetailPanelStore.getState().updateBrowserTab('thread-a', tabId, { currentUrl: 'about:blank' }))
  await waitFor(() => expect(setVisible).toHaveBeenLastCalledWith({ tabId, visible: false }))
  setActive.mockClear()
  act(() => screen.getByText('dismiss-address').click())
  expect(setActive).not.toHaveBeenCalled()

  view.unmount()
})
