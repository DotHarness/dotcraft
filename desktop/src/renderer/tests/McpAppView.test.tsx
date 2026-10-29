import './setupPluginRuntime'
import { StrictMode } from 'react'
import { act, render, waitFor } from '@testing-library/react'
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
const { openDesktopPluginUrl } = vi.hoisted(() => ({ openDesktopPluginUrl: vi.fn(() => false) }))

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
vi.mock('../plugins/desktopPluginOpenUrl', () => ({ openDesktopPluginUrl }))

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
    mcpAppResourceUri: available ? 'ui://chart/view' : undefined
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
  openDesktopPluginUrl.mockReset().mockReturnValue(false)
  ;(window as unknown as { api: unknown }).api = {
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    appServer: { sendRequest, onNotification },
    shell: { openExternal: vi.fn().mockResolvedValue(undefined) }
  }
})

describe('McpAppView', () => {
  it('routes handled custom schemes internally and rejects unclaimed schemes before shell access', async () => {
    render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    const openLink = bridgeInstances[0].onopenlink as (params: { url: string }) => Promise<unknown>
    const shell = window.api.shell.openExternal as ReturnType<typeof vi.fn>

    openDesktopPluginUrl.mockReturnValueOnce(true)
    await expect(openLink({ url: 'oratorio://open/board' })).resolves.toEqual({})
    expect(openDesktopPluginUrl).toHaveBeenCalledWith('oratorio://open/board')
    expect(sendRequest).not.toHaveBeenCalledWith('mcpApp/view/openLink', expect.anything())
    expect(shell).not.toHaveBeenCalled()

    await expect(openLink({ url: 'unknown://open/item' })).rejects.toThrow('link scheme is not allowed')
    expect(shell).not.toHaveBeenCalled()
  })

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
    expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
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

  it('keeps the bridge answering View tool calls under StrictMode', async () => {
    render(<StrictMode><LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider></StrictMode>)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    act(() => bridgeInstances[0].oninitialized?.())
    await waitFor(() => expect(bridgeInstances[0].sendToolResult).toHaveBeenCalledTimes(1))

    expect(bridgeInstances[0].close).not.toHaveBeenCalled()
    sendRequest.mockResolvedValueOnce({ content: [{ type: 'text', text: 'saved' }] })
    const callTool = bridgeInstances[0].oncalltool as (params: { name: string; arguments?: Record<string, unknown> }) => Promise<unknown>
    await expect(callTool({ name: 'save', arguments: { value: 1 } })).resolves.toEqual({ content: [{ type: 'text', text: 'saved' }] })
    expect(sendRequest).toHaveBeenCalledWith(
      'mcpApp/view/tool/call',
      { viewHandle: 'view-1', tool: 'save', arguments: { value: 1 } },
      120_000
    )
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
