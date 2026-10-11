import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../shared/mcpAppSandbox'
import { VisualizationFrame } from '../components/conversation/VisualizationFrame'
import { ColorPickerDialogHost } from '../components/ui/ColorPickerDialog'
import { SelectMenuHost } from '../components/ui/SelectMenu'
import { reloadVisualizationsReferencedBy } from '../components/conversation/visualizationReloads'
import { useUIStore } from '../stores/uiStore'
import { useComposerContextStore } from '../stores/composerContextStore'
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
  const copyImage = vi.fn()
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
    copyImage.mockResolvedValue({ width: 736, height: 362 })
    installDesktopApiMock({
      initialLocale: 'en',
      settings: { get: vi.fn(async () => ({ locale: 'en' })) },
      appServer: { sendRequest, onNotification },
      visualization: { copyImage },
      shell: { openExternal, saveFileAs }
    })
    openExternal.mockResolvedValue(undefined)
    saveFileAs.mockResolvedValue({ saved: true })
    useUIStore.setState({ composerPrefill: null })
    useComposerContextStore.setState({ byThread: {} })
  })

  it('copies the iframe bounds as an image', async () => {
    renderFrame()

    act(() => intersectionObservers[0].trigger(true))

    const frame = screen.getByTitle('Interactive visualization: chart.html') as HTMLIFrameElement
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/open',
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', path: CHART_PATH },
      15_000
    ))
    act(() => {
      dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
    })

    const copyButton = await screen.findByRole('button', { name: 'Copy as image' })
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

    expect(screen.getByTestId('visualization-idle')).toBeInTheDocument()
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
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', path: CHART_PATH },
      15_000)
  })

  it('loads immediately when IntersectionObserver is unavailable', async () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    renderFrame()

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'visualization/view/open',
      { threadId: 'thread-test', turnId: 'turn-test', itemId: 'item-test', path: CHART_PATH },
      15_000
    ))
  })

  it('reloads a mounted view when a later message references the same path', async () => {
    renderFrame()
    act(() => intersectionObservers[0].trigger(true))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(1))
    await act(async () => {})
    const reference = `visualize${JSON.stringify({ path: CHART_PATH })}`

    act(() => reloadVisualizationsReferencedBy('thread-test', 'item-test', reference))
    act(() => reloadVisualizationsReferencedBy('thread-other', 'item-later', reference))
    expect(sendRequest).toHaveBeenCalledTimes(1)

    act(() => reloadVisualizationsReferencedBy('thread-test', 'item-later', reference))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('visualization/view/close', { viewHandle: 'view-handle' }))
    await waitFor(() => expect(sendRequest.mock.calls.filter(call => call[0] === 'visualization/view/open')).toHaveLength(2))
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

  it('reports a runtime error with an action that prefills the composer without sending', async () => {
    const frame = await renderReadyFrame()

    act(() => {
      dispatchFrameMessage(frame, { method: 'visualization/error', params: { message: 'TypeError: x is undefined', viewId: 'view-test' } })
      dispatchFrameMessage(frame, { method: 'visualization/error', params: { message: 'Second error', viewId: 'view-test' } })
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Ask to fix' }))

    expect(useUIStore.getState().composerPrefill).toBe('Fix the visualization in chart.html. It failed with: TypeError: x is undefined')
    expect(sendRequest).not.toHaveBeenCalledWith('visualization/view/message', expect.anything())
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

  it('shows only the visible design controls and sends edits, the original, and reset back to the view', async () => {
    const frame = await renderReadyFrame()
    const post = vi.spyOn(frame.contentWindow!, 'postMessage')
    const sent = () => post.mock.calls.map(([message]) => message as { method: string; params: { values: Record<string, unknown> } })
      .filter(message => message.method === 'visualization/designControls/set').map(message => message.params.values)
    expect(screen.queryByRole('button', { name: 'Design controls' })).not.toBeInTheDocument()

    act(() => dispatchFrameMessage(frame, { method: 'visualization/designControls', params: { viewId: 'view-test', variant: 'Minimal', groups: [
      { id: 'tweak-1', label: 'Minimal player', variant: 'Minimal', visible: true, controls: [{ id: 'tweak-1.0', label: 'Playing', kind: 'toggle', initial: true }] },
      { id: 'tweak-2', label: 'Editorial player', variant: 'Editorial', visible: false, controls: [{ id: 'tweak-2.0', label: 'Serif', kind: 'toggle', initial: false }] }
    ] } }))
    expect(screen.queryByRole('region', { name: 'Design controls' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Design controls' }))

    const panel = screen.getByRole('region', { name: 'Design controls' })
    expect(within(panel).queryByRole('switch', { name: 'Serif' })).not.toBeInTheDocument()
    fireEvent.click(within(panel).getByRole('switch', { name: 'Playing' }))
    expect(sent()).toEqual([{ 'tweak-1.0': false }])

    fireEvent.click(within(panel).getByRole('button', { name: 'Show original' }))
    expect(within(panel).getByRole('switch', { name: 'Playing' })).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(within(panel).getByRole('button', { name: 'Show original' }))
    expect(sent().slice(1)).toEqual([{ 'tweak-1.0': true, 'tweak-2.0': false }, { 'tweak-1.0': false, 'tweak-2.0': false }])

    fireEvent.click(within(panel).getByRole('button', { name: 'Add to message' }))
    fireEvent.click(within(panel).getByRole('button', { name: 'Add to message' }))
    expect(useComposerContextStore.getState().getContexts('thread-test')).toEqual([{
      id: `visualization-changes:${CHART_PATH}`,
      kind: 'visualizationChanges',
      path: CHART_PATH,
      title: 'chart.html',
      text: 'Visible design: Minimal\nMinimal player · Playing: true → false'
    }])

    fireEvent.click(within(panel).getByRole('button', { name: 'Reset' }))
    expect(sent().at(-1)).toEqual({ 'tweak-1.0': true, 'tweak-2.0': false })
    expect(within(panel).getByRole('button', { name: 'Reset' })).toBeDisabled()
    expect(within(panel).getByRole('button', { name: 'Add to message' })).toBeDisabled()
  })

  it('opens the Desktop color picker for a color input and relays previews and the commit to the view', async () => {
    const frame = await renderReadyFrame()
    render(<LocaleProvider><ColorPickerDialogHost /></LocaleProvider>)
    const post = vi.spyOn(frame.contentWindow!, 'postMessage')
    const posted = (method: string) => post.mock.calls.map(([message]) => message as { method: string; params: Record<string, unknown> })
      .filter(message => message.method === method).map(message => message.params)

    act(() => dispatchFrameMessage(frame, { method: 'visualization/pickColor', params: { id: 'request-1', color: 'oops', rect: { x: 0, y: 0, width: 24, height: 24 }, viewId: 'view-test' } }))
    expect(posted('visualization/result')).toEqual([{ id: 'request-1', ok: false, error: 'Cancelled', viewId: 'view-test' }])

    act(() => dispatchFrameMessage(frame, { method: 'visualization/pickColor', params: { id: 'request-2', color: '#4f6cce', label: 'Label color', rect: { x: 12, y: 40, width: 24, height: 24 }, viewId: 'view-test' } }))
    const dialog = await screen.findByRole('dialog', { name: 'Label color' })
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Hex color' }), { target: { value: 'abc' } })
    expect(posted('visualization/colorPreview')).toEqual([{ color: '#aabbcc', viewId: 'view-test' }])

    fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }))
    await waitFor(() => expect(posted('visualization/result').at(-1)).toEqual({ id: 'request-2', ok: true, result: { color: '#aabbcc' }, viewId: 'view-test' }))
  })

  it('opens the Desktop menu for a select and returns the chosen option, or cancels on Escape and scroll', async () => {
    const frame = await renderReadyFrame()
    render(<LocaleProvider><SelectMenuHost /></LocaleProvider>)
    const post = vi.spyOn(frame.contentWindow!, 'postMessage')
    const results = () => post.mock.calls.map(([message]) => message as { method: string; params: Record<string, unknown> })
      .filter(message => message.method === 'visualization/result').map(message => message.params)
    const open = (id: string): void => act(() => dispatchFrameMessage(frame, { method: 'visualization/selectMenu', params: {
      id,
      label: 'Channel',
      options: [{ label: 'Stable' }, { label: 'Preview', group: 'Early' }, { label: 'Nightly', group: 'Early', disabled: true }],
      selectedIndex: 0,
      rect: { x: 0, y: 0, width: 200, height: 32 },
      viewId: 'view-test'
    } }))

    open('request-1')
    const menu = await screen.findByRole('listbox', { name: 'Channel' })
    expect(within(menu).getByText('Early')).toBeInTheDocument()
    expect(within(menu).getByRole('option', { name: 'Stable' })).toHaveAttribute('aria-selected', 'true')
    fireEvent.click(within(menu).getByRole('option', { name: 'Nightly' }))
    fireEvent.click(within(menu).getByRole('option', { name: 'Preview' }))
    await waitFor(() => expect(results().at(-1)).toEqual({ id: 'request-1', ok: true, result: { index: 1 }, viewId: 'view-test' }))

    open('request-2')
    fireEvent.keyDown(await screen.findByRole('listbox', { name: 'Channel' }), { key: 'Escape' })
    await waitFor(() => expect(results().at(-1)).toEqual({ id: 'request-2', ok: false, error: 'Cancelled', viewId: 'view-test' }))

    open('request-3')
    await screen.findByRole('listbox', { name: 'Channel' })
    fireEvent.scroll(window)
    await waitFor(() => expect(results().at(-1)).toEqual({ id: 'request-3', ok: false, error: 'Cancelled', viewId: 'view-test' }))
    expect(screen.queryByRole('listbox', { name: 'Channel' })).not.toBeInTheDocument()
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
