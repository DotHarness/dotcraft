import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../shared/mcpAppSandbox'
import { InlineVisualizationFrame } from '../components/conversation/InlineVisualizationFrame'

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

describe('InlineVisualizationFrame actions', () => {
  const sendRequest = vi.fn()
  const copyImage = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
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
    copyImage.mockResolvedValue({ width: 736, height: 362 })
    window.api = {
      initialLocale: 'en',
      settings: { get: vi.fn(async () => ({ locale: 'en' })) },
      appServer: { sendRequest },
      visualization: { copyImage }
    } as never
  })

  it('copies iframe bounds from the direct borderless action outside the visualization', async () => {
    renderFrame()

    act(() => intersectionObservers[0].trigger(true))

    const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/open',
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', file: 'chart.html' },
      15_000
    ))
    act(() => {
      dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
    })

    const copyButton = await screen.findByRole('button', { name: 'Copy as image' })
    fireEvent.mouseEnter(copyButton.parentElement!)
    expect(copyButton).toHaveClass('inline-visualization-copy-button')
    expect(copyButton.parentElement).toHaveStyle({ paddingRight: '32px' })
    expect(copyButton).not.toHaveAttribute('aria-haspopup')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    Object.defineProperty(frame, 'getBoundingClientRect', {
      configurable: true,
      value: () => ({ x: 12, y: 18, width: 736, height: 362, top: 18, right: 748, bottom: 380, left: 12, toJSON: () => ({}) })
    })
    fireEvent.click(copyButton)

    await waitFor(() => expect(copyImage).toHaveBeenCalledWith({ x: 12, y: 18, width: 736, height: 362 }))
    expect(addToast).toHaveBeenCalledWith('Copied to clipboard', 'success', 2000)
  })

  it('waits until the frame approaches the message stream before loading', async () => {
    const { unmount } = renderFrame()

    expect(screen.getByTestId('inline-visualization-idle')).toBeInTheDocument()
    expect(sendRequest).not.toHaveBeenCalled()
    expect(intersectionObservers).toHaveLength(1)
    expect(intersectionObservers[0].root).toBe(screen.getByTestId('message-stream'))
    expect(intersectionObservers[0].rootMargin).toBe('320px 0px')

    act(() => intersectionObservers[0].trigger(false))
    expect(sendRequest).not.toHaveBeenCalled()

    act(() => intersectionObservers[0].trigger(true))
    expect(await screen.findByText('Loading interactive visualization.')).toBeInTheDocument()
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))

    const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement
    act(() => {
      dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
    })
    expect(await screen.findByRole('button', { name: 'Copy as image' })).toBeInTheDocument()

    act(() => intersectionObservers[0]?.trigger(false))
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(sendRequest).not.toHaveBeenCalledWith('visualization/view/close', expect.anything())

    unmount()
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('visualization/view/close', { viewHandle: 'view-handle' }))
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
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', file: 'chart.html' },
      15_000)
  })

  it('loads immediately when IntersectionObserver is unavailable', async () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    renderFrame()

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/open',
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', file: 'chart.html' },
      15_000
    ))
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
        <InlineVisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-test" file="chart.html" />
      </div>
    </LocaleProvider>
  )
}

function emptyRect(): DOMRectReadOnly {
  return { x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) }
}

function dispatchFrameMessage(frame: HTMLIFrameElement, data: unknown): void {
  const event = new MessageEvent('message', { data })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
}
