import { PostMessageTransport } from '@modelcontextprotocol/ext-apps/app-bridge'
import type { Transport, TransportSendOptions } from '@modelcontextprotocol/sdk/shared/transport.js'
import type { JSONRPCMessage, MessageExtraInfo } from '@modelcontextprotocol/sdk/types.js'

export const MCP_APP_MAX_BRIDGE_MESSAGE_BYTES = 256 * 1024

export class McpAppBridgeMessageTooLargeError extends Error {
  constructor() {
    super(`MCP App bridge message exceeds ${MCP_APP_MAX_BRIDGE_MESSAGE_BYTES} bytes`)
    this.name = 'McpAppBridgeMessageTooLargeError'
  }
}

export function bridgeMessageByteLength(message: unknown): number {
  try {
    const json = JSON.stringify(message)
    return json === undefined ? Number.POSITIVE_INFINITY : new TextEncoder().encode(json).byteLength
  } catch {
    return Number.POSITIVE_INFINITY
  }
}

export function isBridgeMessageWithinLimit(message: unknown): boolean {
  return bridgeMessageByteLength(message) <= MCP_APP_MAX_BRIDGE_MESSAGE_BYTES
}

/**
 * Keeps the official MCP Apps transport while enforcing the host's envelope limit before the
 * SDK sees inbound messages and before it posts outbound messages.
 */
export class SizeLimitedPostMessageTransport implements Transport {
  private readonly inner: PostMessageTransport
  private started = false
  private violated = false

  onclose?: () => void
  onerror?: (error: Error) => void
  onmessage?: <T extends JSONRPCMessage>(message: T, extra?: MessageExtraInfo) => void
  sessionId?: string

  constructor(
    eventTarget: Window,
    private readonly eventSource: MessageEventSource,
    private readonly onLimitExceeded: () => void
  ) {
    this.inner = new PostMessageTransport(eventTarget, eventSource)
  }

  async start(): Promise<void> {
    if (this.started) return
    this.started = true
    // Capture runs before the official transport's bubble listener, so oversized or unserializable
    // envelopes never reach JSON-RPC parsing.
    window.addEventListener('message', this.guardInbound, true)
    this.inner.onclose = () => this.onclose?.()
    this.inner.onerror = (error) => this.onerror?.(error)
    this.inner.onmessage = (message, extra) => this.onmessage?.(message, extra)
    await this.inner.start()
  }

  async send(message: JSONRPCMessage, options?: TransportSendOptions): Promise<void> {
    if (!isBridgeMessageWithinLimit(message)) {
      this.reportViolation()
      throw new McpAppBridgeMessageTooLargeError()
    }
    await this.inner.send(message, options)
  }

  async close(): Promise<void> {
    window.removeEventListener('message', this.guardInbound, true)
    this.started = false
    await this.inner.close()
  }

  setProtocolVersion(version: string): void {
    this.inner.setProtocolVersion?.(version)
  }

  private readonly guardInbound = (event: MessageEvent): void => {
    if (event.source !== this.eventSource || isBridgeMessageWithinLimit(event.data)) return
    event.stopImmediatePropagation()
    this.reportViolation()
  }

  private reportViolation(): void {
    if (this.violated) return
    this.violated = true
    this.onLimitExceeded()
  }
}

interface McpAppCsp {
  connectDomains?: string[]
  resourceDomains?: string[]
  frameDomains?: string[]
  baseUriDomains?: string[]
}

const CSP_ORIGIN_PATTERN = /^(https?|wss?):\/\/[^\s;,'"]+$/i

function sanitizeDomains(domains: readonly string[] | undefined): string[] {
  if (!Array.isArray(domains)) return []
  return domains
    .map((domain) => typeof domain === 'string' ? domain.trim() : '')
    .filter((domain) => CSP_ORIGIN_PATTERN.test(domain))
}

export function buildMcpAppCsp(declared?: McpAppCsp): string {
  const resourceDomains = sanitizeDomains(declared?.resourceDomains)
  const sources = (domains: readonly string[], extra: readonly string[] = []): string => {
    const values = [...extra, ...domains]
    return values.length > 0 ? values.join(' ') : "'none'"
  }
  return [
    "default-src 'none'",
    `script-src ${sources(resourceDomains, ["'unsafe-inline'"])}`,
    `style-src ${sources(resourceDomains, ["'unsafe-inline'"])}`,
    `connect-src ${sources(sanitizeDomains(declared?.connectDomains))}`,
    `img-src ${sources(resourceDomains, ['data:', 'blob:'])}`,
    `font-src ${sources(resourceDomains, ['data:'])}`,
    `media-src ${sources(resourceDomains, ['data:', 'blob:'])}`,
    `frame-src ${sources(sanitizeDomains(declared?.frameDomains))}`,
    `base-uri ${sources(sanitizeDomains(declared?.baseUriDomains))}`,
    "form-action 'none'",
    "object-src 'none'"
  ].join('; ')
}

/**
 * Parses inertly and serializes a complete document with the host policy as the first head node.
 * This avoids regex insertion being confused by comments, templates, or malformed head markup.
 */
export function buildMcpAppDocument(html: string, declared?: McpAppCsp): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  parsed.querySelectorAll('meta[http-equiv]').forEach((element) => {
    if (element.getAttribute('http-equiv')?.toLowerCase() === 'content-security-policy') element.remove()
  })
  const policy = parsed.createElement('meta')
  policy.setAttribute('http-equiv', 'Content-Security-Policy')
  policy.setAttribute('content', buildMcpAppCsp(declared))
  parsed.head.prepend(policy)
  return `<!doctype html>${parsed.documentElement.outerHTML}`
}
