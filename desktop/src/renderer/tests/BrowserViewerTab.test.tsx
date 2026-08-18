// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useUIStore } from '../stores/uiStore'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'
import { BrowserViewerTab } from '../components/detail/viewers/BrowserViewerTab'
import { installDesktopApiMock } from './desktopApiMock'

const THREAD_ID = 'thread-a'
const TAB_ID = 'browser-tab-a'
const WORKSPACE_PATH = 'F:/workspace'

const browserApi = {
  back: vi.fn(),
  create: vi.fn(),
  forward: vi.fn(),
  navigate: vi.fn(),
  onEvent: vi.fn(),
  openExternal: vi.fn(),
  reload: vi.fn(),
  setActive: vi.fn(),
  setBounds: vi.fn(),
  setVisible: vi.fn(),
  stop: vi.fn()
}

class ResizeObserverMock {
  observe(): void {}
  disconnect(): void {}
}

function installWindowApi(): void {
  installDesktopApiMock({
    settings: {
      get: vi.fn().mockResolvedValue({ locale: 'en' })
    },
    workspace: {
      viewer: {
        browser: browserApi
      }
    }
  })
}

beforeEach(() => {
  for (const mock of Object.values(browserApi)) {
    mock.mockReset()
  }
  browserApi.create.mockResolvedValue({
    tabId: TAB_ID,
    currentUrl: 'https://example.com/',
    title: 'Example',
    canGoBack: false,
    canGoForward: false,
    loading: false
  })
  installWindowApi()
  Object.defineProperty(window, 'ResizeObserver', {
    value: ResizeObserverMock,
    configurable: true
  })
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: THREAD_ID,
    currentWorkspacePath: WORKSPACE_PATH
  })
  useConversationStore.setState({
    workspacePath: WORKSPACE_PATH
  })
  useUIStore.setState({
    activeMainView: 'conversation',
    activeDetailTab: { kind: 'launcher' },
    detailPanelVisible: true,
    quickOpenVisible: false
  })
  useTransientOverlayStore.setState({
    openDepths: [],
    topDepth: 0,
    nativeViewBlockerCount: 0
  })
  useViewerTabStore.getState().openBrowser({
    threadId: THREAD_ID,
    tabId: TAB_ID,
    target: TAB_ID,
    initialUrl: 'https://example.com/',
    initialLabel: 'Example'
  })
})

describe('BrowserViewerTab', () => {
  it('does not subscribe to browser events when mounted', async () => {
    render(
      <LocaleProvider>
        <BrowserViewerTab tabId={TAB_ID} />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(browserApi.create).toHaveBeenCalledWith({
        tabId: TAB_ID,
        threadId: THREAD_ID,
        workspacePath: WORKSPACE_PATH,
        initialUrl: 'https://example.com/'
      })
    })
    expect(browserApi.onEvent).not.toHaveBeenCalled()
  })

  it('pushes native browser bounds only for the active visible viewer tab', async () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'viewer', id: TAB_ID }
    })
    const rect = {
      x: 420,
      y: 80,
      left: 420,
      top: 80,
      right: 1020,
      bottom: 680,
      width: 600,
      height: 600,
      toJSON: () => ({})
    } as DOMRect
    const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(rect)
    Object.defineProperty(window, 'requestAnimationFrame', {
      value: (callback: FrameRequestCallback) => {
        callback(0)
        return 1
      },
      configurable: true
    })
    Object.defineProperty(window, 'cancelAnimationFrame', {
      value: vi.fn(),
      configurable: true
    })

    render(
      <LocaleProvider>
        <BrowserViewerTab tabId={TAB_ID} />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(browserApi.setBounds).toHaveBeenCalledWith({
        tabId: TAB_ID,
        x: 420,
        y: 80,
        width: 600,
        height: 600
      })
    })
    expect(browserApi.setActive).toHaveBeenCalledWith({ tabId: TAB_ID })

    rectSpy.mockRestore()
  })

  it('hides the native view and skips bounds while quick open covers the active tab', async () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'viewer', id: TAB_ID },
      quickOpenVisible: true
    })
    const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')

    render(
      <LocaleProvider>
        <BrowserViewerTab tabId={TAB_ID} />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(browserApi.setVisible).toHaveBeenCalledWith({ tabId: TAB_ID, visible: false })
    })
    expect(browserApi.setBounds).not.toHaveBeenCalled()
    expect(rectSpy).not.toHaveBeenCalled()

    rectSpy.mockRestore()
  })

  it('omits the redundant page and automation status row', () => {
    useViewerTabStore.getState().updateBrowserTab(THREAD_ID, TAB_ID, {
      title: 'Composer subscription placement review',
      automationActive: true,
      automationSessionName: 'Design review',
      lastAutomationAction: 'screenshot'
    })

    render(
      <LocaleProvider>
        <BrowserViewerTab tabId={TAB_ID} />
      </LocaleProvider>
    )

    expect(screen.queryByText('Composer subscription placement review')).not.toBeInTheDocument()
    expect(screen.queryByText('Design review')).not.toBeInTheDocument()
    expect(screen.queryByText('screenshot')).not.toBeInTheDocument()
  })

  it('keeps the native view hidden until every fullscreen renderer blocker closes', async () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'viewer', id: TAB_ID }
    })
    act(() => {
      useTransientOverlayStore.getState().pushNativeViewBlocker()
      useTransientOverlayStore.getState().pushNativeViewBlocker()
    })

    render(
      <LocaleProvider>
        <BrowserViewerTab tabId={TAB_ID} />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(browserApi.setVisible).toHaveBeenCalledWith({ tabId: TAB_ID, visible: false })
    })
    browserApi.setVisible.mockClear()

    act(() => {
      useTransientOverlayStore.getState().popNativeViewBlocker()
    })
    expect(browserApi.setVisible).not.toHaveBeenCalledWith({ tabId: TAB_ID, visible: true })

    act(() => {
      useTransientOverlayStore.getState().popNativeViewBlocker()
    })

    await waitFor(() => {
      expect(browserApi.setVisible).toHaveBeenCalledWith({ tabId: TAB_ID, visible: true })
    })
  })
})
