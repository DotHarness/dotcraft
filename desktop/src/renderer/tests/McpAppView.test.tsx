import { fireEvent, render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { McpAppView } from '../components/conversation/McpAppView'
import type { ConversationItem } from '../types/conversation'

const { bridgeInstances, MockBridge } = vi.hoisted(() => {
  class HoistedMockBridge {
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
    connect = vi.fn(async () => {})
    sendSandboxResourceReady = vi.fn(async () => {})
    sendToolInput = vi.fn(async () => {})
    sendToolResult = vi.fn(async () => {})
    setHostContext = vi.fn()
    teardownResource = vi.fn(async () => ({}))
    close = vi.fn(async () => {})

    constructor() {
      instances.push(this)
    }
  }
  const instances: HoistedMockBridge[] = []
  return { bridgeInstances: instances, MockBridge: HoistedMockBridge }
})

vi.mock('@modelcontextprotocol/ext-apps/app-bridge', () => ({
  AppBridge: MockBridge,
  PostMessageTransport: class {}
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

beforeEach(() => {
  bridgeInstances.length = 0
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'mcpApp/view/open') {
      return {
        viewHandle: 'view-1',
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
  it('opens only a live eligible item and uses the official bridge with a no-permission sandbox', async () => {
    const { container } = render(<LocaleProvider><McpAppView item={item()} threadId="thread-1" /></LocaleProvider>)

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'mcpApp/view/open',
      { threadId: 'thread-1', itemId: 'item-1' },
      120_000
    ))
    const iframe = await waitFor(() => container.querySelector('iframe') as HTMLIFrameElement)
    expect(iframe.getAttribute('sandbox')).toBe('allow-scripts')
    expect(iframe.getAttribute('allow')).toBe('')
    fireEvent.load(iframe)
    await waitFor(() => expect(bridgeInstances).toHaveLength(1))
    expect(bridgeInstances[0].connect).toHaveBeenCalledTimes(1)
    expect(bridgeInstances[0].sendSandboxResourceReady).toHaveBeenCalledWith(expect.objectContaining({
      sandbox: 'allow-scripts',
      permissions: {}
    }))
  })

  it('does not request a View for history and shows the generic fallback state', () => {
    const { container } = render(<LocaleProvider><McpAppView item={item(false)} threadId="thread-1" /></LocaleProvider>)
    expect(sendRequest).not.toHaveBeenCalledWith('mcpApp/view/open', expect.anything(), expect.anything())
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('Interactive view is unavailable for history.')
    expect(container.textContent).toContain('fallback')
  })
})
