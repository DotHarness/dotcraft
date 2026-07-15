import { act, fireEvent, render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { McpAppView } from '../components/conversation/McpAppView'
import type { ConversationItem } from '../types/conversation'
import {
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  MCP_APP_SANDBOX_PROXY_URL
} from '../../shared/mcpAppSandbox'

const { bridgeInstances, MockBridge } = vi.hoisted(() => {
  class HoistedMockBridge {
    transport?: {
      start: () => Promise<void>
      send: (message: unknown) => Promise<void>
      close: () => Promise<void>
    }
    onsandboxready?: () => void
    oninitialized?: () => void
    oncalltool?: unknown
    onlisttools?: unknown
    onreadresource?: unknown
    onlistresources?: unknown
    onlistresourcetemplates?: unknown
    onmessage?: unknown
    onupdatemodelcontext?: unknown
    onopenlink?: unknown
    onrequestdisplaymode?: (params: { mode: string }) => Promise<{ mode: string }>
    onrequestteardown?: unknown
    onsizechange?: (params: { width?: number; height?: number }) => void
    onloggingmessage?: unknown
    initialHostContext?: Record<string, unknown>
    connect = vi.fn(async (transport: {
      start: () => Promise<void>
      send: (message: unknown) => Promise<void>
      close: () => Promise<void>
    }) => {
      this.transport = transport
      await transport.start()
    })
    sendSandboxResourceReady = vi.fn(async () => {})
    sendToolInput = vi.fn(async () => {})
    sendToolResult = vi.fn(async () => {})
    setHostContext = vi.fn()
    teardownResource = vi.fn(async () => ({}))
    close = vi.fn(async () => {
      await this.transport?.close()
    })

    constructor(...args: unknown[]) {
      this.initialHostContext = (args[3] as { hostContext?: Record<string, unknown> } | undefined)?.hostContext
      instances.push(this)
    }
  }
  const instances: HoistedMockBridge[] = []
  return { bridgeInstances: instances, MockBridge: HoistedMockBridge }
})

vi.mock('@modelcontextprotocol/ext-apps/app-bridge', () => ({
  AppBridge: MockBridge,
  PostMessageTransport: class {
    onclose?: () => void
    onerror?: (error: Error) => void
    onmessage?: (message: unknown) => void
    start = vi.fn(async () => {})
    send = vi.fn(async () => {})
    close = vi.fn(async () => { this.onclose?.() })
  }
}))

const sendRequest = vi.fn()
const onNotification = vi.fn(() => vi.fn())
let resizeObserverCallbacks: ResizeObserverCallback[] = []

class ResizeObserverMock {
  private readonly callback: ResizeObserverCallback

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback
    resizeObserverCallbacks.push(callback)
  }

  observe = vi.fn()
  disconnect = vi.fn()
  unobserve = vi.fn()
}

Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  value: ResizeObserverMock
})

function item(available = true, id = 'item-1'): ConversationItem {
  return {
    id,
    type: 'mcpToolCall',
    status: 'completed',
    createdAt: new Date(0).toISOString(),
    toolName: 'chart',
    result: 'fallback',
    mcpAppAvailable: available
  }
}

function openResult(viewHandle: string, prefersBorder: boolean | undefined = true): Record<string, unknown> {
  return {
    viewHandle,
    resource: {
      uri: 'ui://charts/view',
      mimeType: 'text/html;profile=mcp-app',
      html: '<html><head></head><body>chart</body></html>',
      ui: { csp: {}, ...(prefersBorder === undefined ? {} : { prefersBorder }) }
    },
    toolInput: { range: 7 },
    toolResult: { content: [{ type: 'text', text: 'ok' }], isError: false }
  }
}

beforeEach(() => {
  bridgeInstances.length = 0
  resizeObserverCallbacks = []
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'mcpApp/view/open') {
      return openResult('view-1')
    }
    return {}
  })
  ;(window as unknown as { api: unknown }).api = {
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    appServer: { sendRequest, onNotification },
    shell: { openExternal: vi.fn().mockResolvedValue(undefined) }
  }
})

describe('McpAppView', () => {
  it('waits for open before creating the iframe and follows the sandbox initialization sequence', async () => {
    let resolveOpen!: (value: unknown) => void
    const opened = new Promise((resolve) => { resolveOpen = resolve })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'mcpApp/view/open') return await opened
      return {}
    })
    const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'mcpApp/view/open',
      { threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1' },
      15_000
    ))
    expect(container.querySelector('iframe')).toBeNull()
    await act(async () => resolveOpen(openResult('view-1')))
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    const iframe = container.querySelector('iframe') as HTMLIFrameElement
    expect(iframe.getAttribute('sandbox')).toBe('allow-scripts')
    expect(iframe.getAttribute('allow')).toBe('')
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    expect(bridgeInstances[0].connect).toHaveBeenCalledTimes(1)
    expect(iframe.getAttribute('src')).toBe(MCP_APP_SANDBOX_PROXY_URL)
    expect(iframe.getAttribute('srcdoc')).toBeNull()
    expect(container.querySelector('[data-mcp-app-frame="bordered"]')).not.toBeNull()
    expect(bridgeInstances[0].sendSandboxResourceReady).not.toHaveBeenCalled()
    act(() => bridgeInstances[0].onsandboxready?.())
    expect(bridgeInstances[0].sendSandboxResourceReady).toHaveBeenCalledWith(expect.objectContaining({
      sandbox: 'allow-scripts',
      permissions: {}
    }))
    expect(bridgeInstances[0].sendToolInput).not.toHaveBeenCalled()
    act(() => bridgeInstances[0].oninitialized?.())
    await waitFor(() => expect(bridgeInstances[0].sendToolInput).toHaveBeenCalledWith({ arguments: { range: 7 } }))
    expect(bridgeInstances[0].sendToolResult).toHaveBeenCalledTimes(1)
  })

  it('keeps the same iframe, bridge, and View while entering and leaving fullscreen', async () => {
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    const iframe = rendered.container.querySelector('iframe') as HTMLIFrameElement

    act(() => bridgeInstances[0].onsandboxready?.())
    act(() => bridgeInstances[0].oninitialized?.())
    await waitFor(() => expect(bridgeInstances[0].sendToolResult).toHaveBeenCalledTimes(1))

    fireEvent.click(rendered.getByRole('button', { name: 'Open fullscreen' }))
    expect(rendered.container.querySelector('[data-mcp-app-display-mode="fullscreen"]')).not.toBeNull()
    expect(rendered.container.querySelector('iframe')).toBe(iframe)
    expect(bridgeInstances).toHaveLength(1)
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(bridgeInstances[0].sendToolInput).toHaveBeenCalledTimes(1)
    expect(bridgeInstances[0].sendToolResult).toHaveBeenCalledTimes(1)
    expect(bridgeInstances[0].teardownResource).not.toHaveBeenCalled()

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(rendered.container.querySelector('[data-mcp-app-display-mode="inline"]')).not.toBeNull()
    expect(rendered.container.querySelector('iframe')).toBe(iframe)
    expect(sendRequest).not.toHaveBeenCalledWith('mcpApp/view/close', expect.anything())

    let granted: { mode: string } | undefined
    await act(async () => {
      granted = await bridgeInstances[0].onrequestdisplaymode?.({ mode: 'fullscreen' })
    })
    expect(granted).toEqual({ mode: 'fullscreen' })
    expect(rendered.container.querySelector('iframe')).toBe(iframe)
    expect(bridgeInstances).toHaveLength(1)
  })

  it('applies bounded inline sizes, ignores resize requests in fullscreen, and restores inline size', async () => {
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    const surface = rendered.container.querySelector('[data-mcp-app-display-mode]') as HTMLDivElement
    Object.defineProperty(surface, 'clientWidth', { configurable: true, value: 600 })

    act(() => bridgeInstances[0].onsizechange?.({ width: 100, height: 900 }))
    const frame = rendered.container.querySelector('[data-mcp-app-frame]') as HTMLDivElement
    const view = rendered.container.querySelector('iframe')?.parentElement as HTMLDivElement
    expect(frame.style.width).toBe('240px')
    expect(view.style.height).toBe('720px')

    act(() => bridgeInstances[0].onsizechange?.({ width: Number.NaN, height: -10 }))
    expect(frame.style.width).toBe('240px')
    expect(view.style.height).toBe('720px')

    fireEvent.click(rendered.getByRole('button', { name: 'Open fullscreen' }))
    act(() => bridgeInstances[0].onsizechange?.({ width: 500, height: 300 }))
    expect(frame.style.width).toBe('100%')
    expect(view.style.height).toBe('auto')

    fireEvent.click(rendered.getByRole('button', { name: 'Exit fullscreen' }))
    expect(frame.style.width).toBe('240px')
    expect(view.style.height).toBe('720px')
  })

  it('discards throttled size notifications when the rendered item changes', async () => {
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))

    act(() => bridgeInstances[0].onsizechange?.({ height: 300 }))
    act(() => bridgeInstances[0].onsizechange?.({ height: 600 }))
    rendered.rerender(<LocaleProvider><McpAppView item={item(true, 'item-2')} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)

    await act(async () => {
      await new Promise((resolve) => window.setTimeout(resolve, 110))
    })
    const view = rendered.container.querySelector('iframe')?.parentElement as HTMLDivElement
    expect(view.style.height).toBe('420px')
  })

  it('publishes flexible inline dimensions and fixed fullscreen dimensions through host context', async () => {
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    const surface = rendered.container.querySelector('[data-mcp-app-display-mode]') as HTMLDivElement
    const view = rendered.container.querySelector('iframe')?.parentElement as HTMLDivElement
    Object.defineProperty(surface, 'clientWidth', { configurable: true, value: 640 })
    Object.defineProperty(view, 'clientWidth', { configurable: true, value: 592 })
    Object.defineProperty(view, 'clientHeight', { configurable: true, value: 672 })

    await act(async () => {
      resizeObserverCallbacks.forEach((callback) => callback([], {} as ResizeObserver))
      await new Promise((resolve) => window.setTimeout(resolve, 110))
    })
    expect(bridgeInstances[0].setHostContext).toHaveBeenLastCalledWith(expect.objectContaining({
      displayMode: 'inline',
      containerDimensions: { maxWidth: 640, maxHeight: 720 }
    }))

    fireEvent.click(rendered.getByRole('button', { name: 'Open fullscreen' }))
    await act(async () => {
      resizeObserverCallbacks.forEach((callback) => callback([], {} as ResizeObserver))
      await new Promise((resolve) => window.setTimeout(resolve, 110))
    })
    expect(bridgeInstances[0].setHostContext).toHaveBeenLastCalledWith(expect.objectContaining({
      displayMode: 'fullscreen',
      containerDimensions: { width: 592, height: 672 }
    }))
  })

  it('removes the host border and background when the App requests a borderless frame', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'mcpApp/view/open') return openResult('view-1', false)
      return {}
    })
    const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())

    const frame = container.querySelector('[data-mcp-app-frame="borderless"]') as HTMLDivElement
    expect(frame).not.toBeNull()
    expect(window.getComputedStyle(frame).borderStyle).toBe('none')
    expect(window.getComputedStyle(frame).backgroundColor).toBe('rgba(0, 0, 0, 0)')
  })

  it('does not request a View when current authority is unavailable and shows the generic fallback state', () => {
    const { container } = render(<LocaleProvider><McpAppView item={item(false)} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    expect(sendRequest).not.toHaveBeenCalledWith('mcpApp/view/open', expect.anything(), expect.anything())
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('Interactive view is currently unavailable.')
    expect(container.textContent).toContain('fallback')
  })

  it('fails closed and tears down when the iframe sends an oversized bridge envelope', async () => {
    const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))

    await act(async () => {
      await expect(bridgeInstances[0].transport?.send({
        jsonrpc: '2.0',
        method: 'tools/call',
        params: { padding: 'x'.repeat(256 * 1024) }
      })).rejects.toThrow('MCP App bridge message exceeds')
    })

    await waitFor(() => expect(container.querySelector('iframe')).toBeNull())
    expect(container.textContent).toContain('This MCP App could not be opened.')
    expect(container.textContent).toContain('fallback')
    expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
  })

  it('ends the loading state when the sandbox does not become ready', async () => {
    const timeoutSpy = vi.spyOn(window, 'setTimeout')
    const warningSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
      await waitFor(() => expect(bridgeInstances).toHaveLength(1))
      const sandboxTimeout = timeoutSpy.mock.calls.find(([, delay]) => delay === 10_000)?.[0]
      expect(typeof sandboxTimeout).toBe('function')

      act(() => (sandboxTimeout as () => void)())

      await waitFor(() => expect(container.querySelector('iframe')).toBeNull())
      expect(container.textContent).toContain('This MCP App could not be opened.')
      expect(warningSpy).toHaveBeenCalledWith('[MCP App] view failed', {
        code: 'sandbox_ready_timeout',
        viewHandle: 'view-1'
      })
      expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
      await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
    } finally {
      timeoutSpy.mockRestore()
      warningSpy.mockRestore()
    }
  })

  it('fails closed with a diagnostic code when the proxy reports a bridge violation', async () => {
    const warningSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
      await waitFor(() => expect(bridgeInstances).toHaveLength(1))
      const iframe = container.querySelector('iframe') as HTMLIFrameElement

      act(() => {
        window.dispatchEvent(new MessageEvent('message', {
          source: iframe.contentWindow,
          data: { jsonrpc: '2.0', method: MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD, params: {} }
        }))
      })

      await waitFor(() => expect(container.querySelector('iframe')).toBeNull())
      expect(warningSpy).toHaveBeenCalledWith('[MCP App] view failed', {
        code: 'sandbox_bridge_violation',
        viewHandle: 'view-1'
      })
    } finally {
      warningSpy.mockRestore()
    }
  })

  it('tears down the resource and closes the View when unmounted', async () => {
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(rendered.container.querySelector('iframe')).not.toBeNull())
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))

    rendered.unmount()

    expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(bridgeInstances[0].close).toHaveBeenCalledTimes(1))
    expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' })
  })

  it('tears down the current View and opens a new handle when the task changes', async () => {
    let nextHandle = 0
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'mcpApp/view/open') return openResult(`view-${++nextHandle}`)
      return {}
    })
    const rendered = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))

    rendered.rerender(<LocaleProvider><McpAppView item={item()} threadId="thread-2" turnId="turn-1" /></LocaleProvider>)

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'mcpApp/view/open',
      { threadId: 'thread-2', turnId: 'turn-1', itemId: 'item-1' },
      15_000
    ))
    await waitFor(() => expect(bridgeInstances).toHaveLength(2))
    expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
  })
})
