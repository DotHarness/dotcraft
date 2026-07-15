import { describe, expect, it, vi } from 'vitest'
import {
  buildMcpAppDocument,
  isBridgeMessageWithinLimit,
  isSandboxResourceBootstrapWithinLimit,
  MCP_APP_MAX_BRIDGE_MESSAGE_BYTES,
  MCP_APP_MAX_RESOURCE_BYTES,
  SizeLimitedPostMessageTransport
} from '../components/conversation/mcpAppSecurity'

describe('MCP App bridge security', () => {
  it('rejects an envelope larger than 256 KiB before transport dispatch', async () => {
    const iframe = document.createElement('iframe')
    document.body.appendChild(iframe)
    const source = iframe.contentWindow!
    const onLimitExceeded = vi.fn()
    const transport = new SizeLimitedPostMessageTransport(source, source, onLimitExceeded)
    const onMessage = vi.fn()
    transport.onmessage = onMessage
    await transport.start()

    window.dispatchEvent(new MessageEvent('message', {
      source,
      data: { jsonrpc: '2.0', method: 'tools/call', params: { value: '界'.repeat(MCP_APP_MAX_BRIDGE_MESSAGE_BYTES) } }
    }))

    expect(onLimitExceeded).toHaveBeenCalledTimes(1)
    expect(onMessage).not.toHaveBeenCalled()
    await transport.close()
    iframe.remove()
  })

  it('measures serialized UTF-8 bytes rather than JavaScript character count', () => {
    expect(isBridgeMessageWithinLimit({ value: 'x'.repeat(100) })).toBe(true)
    expect(isBridgeMessageWithinLimit({ value: '界'.repeat(MCP_APP_MAX_BRIDGE_MESSAGE_BYTES / 2) })).toBe(false)
  })

  it('allows a large trusted resource bootstrap without relaxing ordinary bridge messages', async () => {
    const html = '<main>' + 'x'.repeat(840_997) + '</main>'
    const bootstrap = {
      jsonrpc: '2.0' as const,
      method: 'ui/notifications/sandbox-resource-ready',
      params: { html, sandbox: 'allow-scripts', csp: {}, permissions: {} }
    }

    expect(isBridgeMessageWithinLimit(bootstrap)).toBe(false)
    expect(isSandboxResourceBootstrapWithinLimit(bootstrap)).toBe(true)
    expect(isSandboxResourceBootstrapWithinLimit({
      ...bootstrap,
      method: 'tools/call'
    })).toBe(false)

    const iframe = document.createElement('iframe')
    document.body.appendChild(iframe)
    const source = iframe.contentWindow!
    const onLimitExceeded = vi.fn()
    const transport = new SizeLimitedPostMessageTransport(source, source, onLimitExceeded)
    await transport.start()
    const debug = vi.spyOn(console, 'debug').mockImplementation(() => {})
    await expect(transport.send(bootstrap)).resolves.toBeUndefined()
    debug.mockRestore()
    expect(onLimitExceeded).not.toHaveBeenCalled()
    await transport.close()
    iframe.remove()
  })

  it('rejects a resource bootstrap whose HTML exceeds the 2 MiB resource limit', () => {
    expect(isSandboxResourceBootstrapWithinLimit({
      jsonrpc: '2.0',
      method: 'ui/notifications/sandbox-resource-ready',
      params: { html: 'x'.repeat(MCP_APP_MAX_RESOURCE_BYTES + 1) }
    })).toBe(false)
  })

  it('rebuilds malformed hostile HTML with the host CSP before every script', () => {
    const html = '<!-- <head>fake</head> --><script>window.ran = true</script><head><meta http-equiv="Content-Security-Policy" content="default-src *"></head><body>app</body>'
    const rebuilt = buildMcpAppDocument(html, {
      connectDomains: ['https://api.example.com', "https://bad.example; script-src *"],
      resourceDomains: ['https://cdn.example.com']
    })
    const parsed = new DOMParser().parseFromString(rebuilt, 'text/html')
    const policy = parsed.head.firstElementChild

    expect(policy?.tagName).toBe('META')
    expect(policy?.getAttribute('http-equiv')).toBe('Content-Security-Policy')
    expect(policy?.getAttribute('content')).toContain("default-src 'none'")
    expect(policy?.getAttribute('content')).toContain('connect-src https://api.example.com')
    expect(policy?.getAttribute('content')).not.toContain('bad.example')
    expect(parsed.querySelectorAll('meta[http-equiv="Content-Security-Policy"]')).toHaveLength(1)
    expect([...parsed.head.children].indexOf(policy!)).toBeLessThan([...parsed.head.children].indexOf(parsed.querySelector('script')!))
  })
})
