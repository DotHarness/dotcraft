import { act, render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { McpAppView } from '../components/conversation/McpAppView'
import type { ConversationItem } from '../types/conversation'

const { bridgeInstances, MockBridge } = vi.hoisted(() => {
  class HoistedMockBridge {
    transport?: { start: () => Promise<void>; close: () => Promise<void> }
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
    onrequestdisplaymode?: unknown
    onrequestteardown?: unknown
    onsizechange?: unknown
    onloggingmessage?: unknown
    connect = vi.fn(async (transport: { start: () => Promise<void>; close: () => Promise<void> }) => {
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

    constructor() {
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

function item(available = true): ConversationItem {
  return {
    id: 'item-1',
    type: 'mcpToolCall',
    status: 'completed',
    createdAt: new Date(0).toISOString(),
    toolName: 'chart',
    result: 'fallback',
    mcpAppAvailable: available
  }
}

function openResult(viewHandle: string): Record<string, unknown> {
  return {
    viewHandle,
    resource: {
      uri: 'ui://charts/view',
      mimeType: 'text/html;profile=mcp-app',
      html: '<html><head></head><body>chart</body></html>',
      ui: { csp: {}, prefersBorder: true }
    },
    toolInput: { range: 7 },
    toolResult: { content: [{ type: 'text', text: 'ok' }], isError: false }
  }
}

beforeEach(() => {
  bridgeInstances.length = 0
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
    const iframe = container.querySelector('iframe') as HTMLIFrameElement
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))

    act(() => {
      window.dispatchEvent(new MessageEvent('message', {
        source: iframe.contentWindow,
        data: { jsonrpc: '2.0', method: 'tools/call', params: { padding: 'x'.repeat(256 * 1024) } }
      }))
    })

    await waitFor(() => expect(container.querySelector('iframe')).toBeNull())
    expect(container.textContent).toContain('This MCP App could not be opened.')
    expect(container.textContent).toContain('fallback')
    expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
  })

  it('ends the loading state when the sandbox does not become ready', async () => {
    const timeoutSpy = vi.spyOn(window, 'setTimeout')
    try {
      const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" turnId="turn-1" /></LocaleProvider>)
      await waitFor(() => expect(bridgeInstances).toHaveLength(1))
      const sandboxTimeout = timeoutSpy.mock.calls.find(([, delay]) => delay === 10_000)?.[0]
      expect(typeof sandboxTimeout).toBe('function')

      act(() => (sandboxTimeout as () => void)())

      await waitFor(() => expect(container.querySelector('iframe')).toBeNull())
      expect(container.textContent).toContain('This MCP App could not be opened.')
      expect(bridgeInstances[0].teardownResource).toHaveBeenCalledTimes(1)
      await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('mcpApp/view/close', { viewHandle: 'view-1' }))
    } finally {
      timeoutSpy.mockRestore()
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
