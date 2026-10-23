import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../shared/mcpAppSandbox'
import { VisualizationFrame } from '../components/conversation/VisualizationFrame'
import { installDesktopApiMock } from './desktopApiMock'

const CHART_PATH = '/workspace/.craft/visualizations/thread-test/chart.html'
const addToast = vi.hoisted(() => vi.fn())
const confirm = vi.hoisted(() => vi.fn())
const intersectionObservers: TestIntersectionObserver[] = []

class TestIntersectionObserver implements IntersectionObserver {
  readonly root: Element | Document | null
  readonly rootMargin: string
  readonly thresholds: readonly number[]
  private readonly callback: IntersectionObserverCallback
  private readonly elements = new Set<Element>()

  constructor(callback: IntersectionObserverCallback, options: IntersectionObserverInit = {}) {
    this.callback = callback
    this.root = options.root ?? null
    this.rootMargin = options.rootMargin ?? '0px'
    this.thresholds = Array.isArray(options.threshold) ? options.threshold : [options.threshold ?? 0]
    intersectionObservers.push(this)
  }

  observe(element: Element): void { this.elements.add(element) }
  unobserve(element: Element): void { this.elements.delete(element) }
  disconnect(): void { this.elements.clear() }
  takeRecords(): IntersectionObserverEntry[] { return [] }

  trigger(isIntersecting: boolean): void {
    const target = [...this.elements][0]
    if (!target) return
    const rect = target.getBoundingClientRect()
    this.callback([{
      boundingClientRect: rect,
      intersectionRatio: isIntersecting ? 1 : 0,
      intersectionRect: isIntersecting ? rect : emptyRect(),
      isIntersecting,
      rootBounds: null,
      target,
      time: 0
    }], this)
  }
}

vi.mock('../stores/toastStore', () => ({ addToast }))
vi.mock('../components/ui/ConfirmDialog', () => ({ useConfirmDialog: () => confirm }))

describe('VisualizationFrame actions', () => {
  const sendRequest = vi.fn()
  const openExternal = vi.fn()
  const saveFileAs = vi.fn()
  const notificationListeners = new Set<(payload: unknown) => void>()
  const onNotification = vi.fn((listener: (payload: unknown) => void) => {
    notificationListeners.add(listener)
    return () => { notificationListeners.delete(listener) }
  })

  beforeEach(() => {
    vi.clearAllMocks()
    notificationListeners.clear()
    intersectionObservers.length = 0
    vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue('view-test')
    vi.stubGlobal('IntersectionObserver', TestIntersectionObserver)
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 1
    })
    vi.stubGlobal('ResizeObserver', class {
      observe(): void {}
      disconnect(): void {}
    })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'visualization/view/open') {
        return { viewHandle: 'view-handle', fragment: '<div class="viz-root">Chart</div>', mimeType: 'text/html' }
      }
      return { closed: true }
    })
    installDesktopApiMock({
      initialLocale: 'en',
      settings: { get: vi.fn(async () => ({ locale: 'en' })) },
      appServer: { sendRequest, onNotification },
      shell: { openExternal, saveFileAs }
    })
    openExternal.mockResolvedValue(undefined)
    saveFileAs.mockResolvedValue({ saved: true })
  })

  it('retries the complete load after view open fails', async () => {
    sendRequest
      .mockRejectedValueOnce(new Error('runtime binding unavailable'))
      .mockResolvedValueOnce({ viewHandle: 'view-handle', fragment: '<div class="viz-root">Chart</div>', mimeType: 'text/html' })
    renderFrame()

    act(() => intersectionObservers[0].trigger(true))
    expect(await screen.findByText('This visualization is unavailable.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('Loading interactive visualization.')).toBeInTheDocument()
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(2))
    expect(sendRequest).toHaveBeenNthCalledWith(2,
      'visualization/view/open',
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', path: CHART_PATH },
      15_000)
  })

  it('saves widget state through the host and pushes it to sibling views of the same path', async () => {
    let sequence = 0
    vi.mocked(globalThis.crypto.randomUUID).mockImplementation(() => `view-${++sequence}` as ReturnType<typeof crypto.randomUUID>)
    render(
      <LocaleProvider>
        <div data-testid="message-stream">
          <VisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-a" path={CHART_PATH} title="First" />
          <VisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-b" path={CHART_PATH} title="Second" />
        </div>
      </LocaleProvider>
    )
    act(() => intersectionObservers.forEach(observer => observer.trigger(true)))
    await waitFor(() => expect(sendRequest.mock.calls.filter(call => call[0] === 'visualization/view/open')).toHaveLength(2))
    await act(async () => {})
    const [first, second] = [screen.getByTitle('First'), screen.getByTitle('Second')] as HTMLIFrameElement[]
    const firstPost = vi.spyOn(first.contentWindow!, 'postMessage')
    const secondPost = vi.spyOn(second.contentWindow!, 'postMessage')
    act(() => {
      dispatchFrameMessage(first, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(second, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
    })
    const firstViewId = bridgeViewId(firstPost)
    const secondViewId = bridgeViewId(secondPost)
    const state = { modelContent: { region: 'north' }, privateContent: null }

    act(() => dispatchFrameMessage(first, { method: 'visualization/setWidgetState', params: { id: 'request-1', state, viewId: firstViewId } }))

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/widgetState/update',
      { viewHandle: 'view-handle', modelContent: { region: 'north' }, privateContent: null }
    ))
    await waitFor(() => expect(firstPost).toHaveBeenCalledWith(
      { method: 'visualization/result', params: { id: 'request-1', ok: true, viewId: firstViewId } }, '*'))
    expect(secondPost).toHaveBeenCalledWith(
      { method: 'visualization/globals', params: { globals: { widgetState: state }, viewId: secondViewId } }, '*')
    expect(firstPost).not.toHaveBeenCalledWith(expect.objectContaining({ method: 'visualization/globals' }), '*')
  })

  it('pushes widget state saved by another client to mounted views of the same path', async () => {
    renderFrame()
    act(() => intersectionObservers[0].trigger(true))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))
    await act(async () => {})
    const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement
    const post = vi.spyOn(frame.contentWindow!, 'postMessage')
    const notify = (params: unknown): void => act(() => notificationListeners.forEach(listener =>
      listener({ method: 'visualization/widgetState/updated', params })))

    notify({ threadId: 'thread-other', path: CHART_PATH, widgetState: { modelContent: 1, privateContent: null } })
    notify({ threadId: 'thread-test', path: CHART_PATH.toUpperCase(), widgetState: { modelContent: { region: 'south' } } })
    notify({ threadId: 'thread-test', path: CHART_PATH, widgetState: null })

    const pushed = post.mock.calls
      .map(([message]) => message as { method?: string; params?: { globals?: unknown } })
      .filter(message => message.method === 'visualization/globals')
      .map(message => message.params?.globals)
    expect(pushed).toEqual([{ widgetState: null }])
  })

  it('passes follow-up context to the view message after confirmation', async () => {
    confirm.mockResolvedValue(true)
    renderFrame()
    act(() => intersectionObservers[0].trigger(true))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))
    await act(async () => {})
    const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement

    act(() => dispatchFrameMessage(frame, {
      method: 'visualization/followUp',
      params: { id: 'request-1', prompt: 'Explain north.', context: { region: 'north' }, viewId: 'view-test' }
    }))

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/message',
      { viewHandle: 'view-handle', prompt: 'Explain north.', context: { region: 'north' } }
    ))
  })

  it('opens web links externally only after the user confirms', async () => {
    const frame = await renderReadyFrame()
    confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true)

    act(() => dispatchFrameMessage(frame, { method: 'visualization/openLink', params: { url: 'https://example.com/a', viewId: 'view-test' } }))
    await waitFor(() => expect(confirm).toHaveBeenCalledTimes(1))
    await act(async () => {})
    act(() => dispatchFrameMessage(frame, { method: 'visualization/openLink', params: { url: 'file:///etc/passwd', viewId: 'view-test' } }))
    act(() => dispatchFrameMessage(frame, { method: 'visualization/openLink', params: { url: 'https://example.com/b', viewId: 'view-test' } }))

    await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://example.com/b'))
    expect(confirm).toHaveBeenCalledTimes(2)
    expect(openExternal).toHaveBeenCalledTimes(1)
  })

  it('saves relayed downloads through the native dialog and rejects oversized ones', async () => {
    const frame = await renderReadyFrame()

    act(() => dispatchFrameMessage(frame, { method: 'visualization/download', params: { name: 'table.csv', mimeType: 'text/csv', data: btoa('a,b'), viewId: 'view-test' } }))
    await waitFor(() => expect(saveFileAs).toHaveBeenCalledTimes(1))
    const request = saveFileAs.mock.calls[0][0] as { data: Uint8Array; suggestedName: string }
    expect({ text: new TextDecoder().decode(request.data), name: request.suggestedName }).toEqual({ text: 'a,b', name: 'table.csv' })

    act(() => dispatchFrameMessage(frame, { method: 'visualization/download', params: { name: 'huge.bin', size: 3 * 1024 * 1024, viewId: 'view-test' } }))
    expect(addToast).toHaveBeenCalledWith('This file is too large to save. A visualization can save files up to 2 MB.', 'error', 3000)
    expect(saveFileAs).toHaveBeenCalledTimes(1)
  })

  it('closes a view handle that arrives after the frame unmounts', async () => {
    let resolveOpen!: (value: unknown) => void
    sendRequest.mockImplementation((method: string) => {
      if (method === 'visualization/view/open') {
        return new Promise(resolve => { resolveOpen = resolve })
      }
      return Promise.resolve({ closed: true })
    })
    const { unmount } = renderFrame()

    act(() => intersectionObservers[0].trigger(true))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))
    unmount()
    resolveOpen({ viewHandle: 'late-view', fragment: '<div />', mimeType: 'text/html' })

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/close',
      { viewHandle: 'late-view' }
    ))
  })
})

function renderFrame(): ReturnType<typeof render> {
  return render(
    <LocaleProvider>
      <div data-testid="message-stream">
        <VisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-test" path={CHART_PATH} />
      </div>
    </LocaleProvider>
  )
}

async function renderReadyFrame(): Promise<HTMLIFrameElement> {
  renderFrame()
  act(() => intersectionObservers[0].trigger(true))
  await waitFor(() => expect(screen.getByTitle('Interactive visualization: chart.html')).toBeInTheDocument())
  await act(async () => {})
  const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement
  act(() => {
    dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
    dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
  })
  return frame
}

function emptyRect(): DOMRectReadOnly {
  return { x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) }
}

function bridgeViewId(post: { mock: { calls: unknown[][] } }): string {
  const call = post.mock.calls.find(([message]) =>
    (message as { method?: string }).method === VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD)
  const html = (call?.[0] as { params: { html: string } }).params.html
  return /const viewId = "([^"]+)"/.exec(html)![1]
}

function dispatchFrameMessage(frame: HTMLIFrameElement, data: unknown): void {
  const event = new MessageEvent('message', { data })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
}
