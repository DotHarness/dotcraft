import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../shared/mcpAppSandbox'
import { VisualizationFrame } from '../components/conversation/VisualizationFrame'
import { ConversationVisualizationsContext, type VisualizationEntry } from '../components/conversation/visualizationGallery'
import type { VisualizationFullscreenHost } from '../components/conversation/VisualizationViewer'
import { installDesktopApiMock } from './desktopApiMock'

const DIRECTORY = '/workspace/.craft/visualizations/thread-test'
const ENTRIES: VisualizationEntry[] = ['revenue', 'latency', 'schedule'].map((name, index) => ({
  key: `item-${index}:0`,
  threadId: 'thread-test',
  turnId: 'turn-test',
  itemId: `item-${index}`,
  path: `${DIRECTORY}/${name}.html`,
  title: `${name} view`
}))

vi.mock('../stores/toastStore', () => ({ addToast: vi.fn() }))
vi.mock('../components/ui/ConfirmDialog', () => ({ useConfirmDialog: () => vi.fn() }))

describe('VisualizationFrame display modes', () => {
  const sendRequest = vi.fn()
  let opened = 0

  beforeEach(() => {
    vi.clearAllMocks()
    opened = 0
    vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue('view-test')
    vi.stubGlobal('IntersectionObserver', undefined)
    vi.stubGlobal('ResizeObserver', class {
      observe(): void {}
      disconnect(): void {}
    })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
    })
    sendRequest.mockImplementation(async (method: string, params: { path?: string }) => {
      if (method === 'visualization/view/open') {
        return { viewHandle: `handle-${++opened}:${params.path}`, fragment: '<div>View</div>', mimeType: 'text/html' }
      }
      return { closed: true }
    })
    installDesktopApiMock({
      initialLocale: 'en',
      settings: { get: vi.fn(async () => ({ locale: 'en' })) },
      appServer: { sendRequest, onNotification: () => () => undefined },
      visualization: { copyImage: vi.fn() }
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('opens the conversation viewer at the view, pages in order, and closes on Escape', async () => {
    const inline = await renderReadyInlineFrame()
    const button = screen.getByRole('button', { name: 'Open fullscreen' })
    act(() => button.focus())
    fireEvent.click(button)

    const dialog = await screen.findByRole('dialog', { name: 'latency view' })
    expect(within(dialog).getByText('2 of 3')).toBeInTheDocument()
    await waitFor(() => expect(openedPaths()).toEqual([ENTRIES[1].path, ENTRIES[1].path]))

    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    expect(within(dialog).getByText('3 of 3')).toBeInTheDocument()
    await waitFor(() => expect(openedPaths()).toEqual([ENTRIES[1].path, ENTRIES[1].path, ENTRIES[2].path]))
    expect(sendRequest).toHaveBeenCalledWith('visualization/view/close', { viewHandle: `handle-2:${ENTRIES[1].path}` })

    const fullscreen = within(dialog).getByTitle('schedule view') as HTMLIFrameElement
    act(() => dispatchFrameMessage(fullscreen, { method: 'visualization/key', params: { key: 'ArrowLeft', viewId: 'view-test' } }))
    expect(within(dialog).getByText('2 of 3')).toBeInTheDocument()

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(inline).toBeInTheDocument()
    expect(document.activeElement).toBe(button)
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('visualization/view/close', { viewHandle: `handle-4:${ENTRIES[1].path}` }))
  })

  it('grants requestDisplayMode fullscreen and returns inline from the fullscreen view', async () => {
    const inline = await renderReadyInlineFrame()
    const inlinePost = vi.spyOn(inline.contentWindow!, 'postMessage')

    act(() => dispatchFrameMessage(inline, { method: 'visualization/requestDisplayMode', params: { id: 'request-1', mode: 'fullscreen', viewId: 'view-test' } }))

    const dialog = await screen.findByRole('dialog')
    expect(inlinePost).toHaveBeenCalledWith(
      { method: 'visualization/result', params: { id: 'request-1', ok: true, result: { mode: 'fullscreen' }, viewId: 'view-test' } }, '*')
    const fullscreen = within(dialog).getByTitle('latency view') as HTMLIFrameElement
    const fullscreenPost = vi.spyOn(fullscreen.contentWindow!, 'postMessage')

    act(() => dispatchFrameMessage(fullscreen, { method: 'visualization/requestDisplayMode', params: { id: 'request-2', mode: 'inline', viewId: 'view-test' } }))

    expect(fullscreenPost).toHaveBeenCalledWith(
      { method: 'visualization/result', params: { id: 'request-2', ok: true, result: { mode: 'inline' }, viewId: 'view-test' } }, '*')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('builds a fullscreen view with its display mode and pushes height cap changes as globals', async () => {
    const host: VisualizationFullscreenHost = { maxHeight: 500, onExit: vi.fn(), onKey: vi.fn(), onReadyChange: vi.fn(), designControlsOpen: false, capturing: false, onDesignControlsAvailableChange: vi.fn(), onDesignControlsOpenChange: vi.fn() }
    const view = (maxHeight: number): JSX.Element => (
      <LocaleProvider>
        <VisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-0" path={ENTRIES[0].path} title="Full" fullscreen={{ ...host, maxHeight }} />
      </LocaleProvider>
    )
    const { rerender } = render(view(500))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('visualization/view/open', expect.objectContaining({ path: ENTRIES[0].path }), 15_000))
    await act(async () => {})
    const frame = screen.getByTitle('Full') as HTMLIFrameElement
    const post = vi.spyOn(frame.contentWindow!, 'postMessage')

    act(() => {
      dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
    })
    const html = (post.mock.calls.find(([message]) => (message as { method?: string }).method === VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD)![0] as { params: { html: string } }).params.html
    expect(html).toContain('let displayMode = "fullscreen"')
    expect(html).toContain('let maxHeight = 500')
    expect(host.onReadyChange).toHaveBeenLastCalledWith(true)
    expect(post).not.toHaveBeenCalledWith(expect.objectContaining({ method: 'visualization/globals' }), '*')

    rerender(view(640))

    expect(post).toHaveBeenCalledWith(
      { method: 'visualization/globals', params: { globals: { displayMode: 'fullscreen', maxHeight: 640 }, viewId: 'view-test' } }, '*')
  })

  function openedPaths(): string[] {
    return sendRequest.mock.calls.filter(([method]) => method === 'visualization/view/open').map(([, params]) => (params as { path: string }).path)
  }

  async function renderReadyInlineFrame(): Promise<HTMLIFrameElement> {
    render(
      <LocaleProvider>
        <ConversationVisualizationsContext.Provider value={() => ENTRIES}>
          <div data-testid="message-stream">
            <VisualizationFrame threadId="thread-test" turnId="turn-test" itemId="item-1" path={ENTRIES[1].path} title="latency view" />
          </div>
        </ConversationVisualizationsContext.Provider>
      </LocaleProvider>
    )
    await waitFor(() => expect(openedPaths()).toEqual([ENTRIES[1].path]))
    await act(async () => {})
    const frame = screen.getByTitle('latency view') as HTMLIFrameElement
    act(() => {
      dispatchFrameMessage(frame, { method: MCP_APP_SANDBOX_PROXY_READY_METHOD, params: {} })
      dispatchFrameMessage(frame, { method: 'visualization/ready', params: { viewId: 'view-test' } })
    })
    return frame
  }
})

function dispatchFrameMessage(frame: HTMLIFrameElement, data: unknown): void {
  const event = new MessageEvent('message', { data })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
}
