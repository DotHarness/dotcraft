/**
 * Registers the `dotcraft-app://` custom Electron protocol that serves an App
 * Binding app's **Interactive Tool UI** HTML into a sandboxed iframe.
 *
 * Why a custom scheme (not srcdoc/blob): a `srcdoc`/`blob:` iframe is
 * `about:srcdoc` / blob-origin and **inherits the parent document's CSP**, so
 * the production CSP (`script-src 'self' …`, no `'unsafe-inline'`) would block
 * the app's own scripts. A document served by this scheme carries its **own**
 * per-resource CSP (built here), independent of the parent — the same pattern
 * already used by `dotcraft-viewer:` / `dotcraft-plugin:`.
 *
 * Security contract:
 *  - The handler only brokers `ui/resource/read` to AppServer, which itself
 *    validates that the `ui://` URI is declared by a tool attached to the
 *    thread's binding (reads outside the binding are rejected server-side).
 *  - The served document gets a restrictive CSP; the iframe is mounted with
 *    `sandbox="allow-scripts"` (no `allow-same-origin`) → opaque origin, no DOM
 *    access to the host, no Node.
 *
 * URL format:
 *   `dotcraft-app://resource/?threadId=<id>&namespace=<ns>&uri=<ui://…>`
 * The origin (`dotcraft-app://resource`) is constant; only the query varies.
 */
import { protocol } from 'electron'
import type { WireProtocolClient } from './WireProtocolClient'

export const DOTCRAFT_APP_SCHEME = 'dotcraft-app'
const APP_HOST = 'resource'
const RESOURCE_READ_TIMEOUT_MS = 30_000

let handlerInstalled = false
let resolveClient: (() => WireProtocolClient | null) | null = null

interface UiResourceReadResult {
  contents?: Array<{ uri?: string; mimeType?: string; text?: string }>
}

/** Must be called before `app.whenReady()` to mark the scheme as privileged. */
export function registerDotCraftAppScheme(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: DOTCRAFT_APP_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: true,
        bypassCSP: false,
        stream: false,
        corsEnabled: true
      }
    }
  ])
}

/**
 * Installs the `protocol.handle` handler. Must be called inside `app.whenReady()`.
 * `clientResolver` returns the active AppServer wire client (or null when offline).
 */
export function installDotCraftAppProtocolHandler(
  clientResolver: () => WireProtocolClient | null
): void {
  resolveClient = clientResolver
  if (handlerInstalled) return
  handlerInstalled = true
  protocol.handle(DOTCRAFT_APP_SCHEME, handleDotCraftAppRequest)
}

/** Builds the iframe `src` URL for an interactive tool UI resource. */
export function buildDotCraftAppUrl(
  threadId: string,
  namespace: string | null | undefined,
  uri: string
): string {
  const params = new URLSearchParams()
  params.set('threadId', threadId)
  if (namespace) params.set('namespace', namespace)
  params.set('uri', uri)
  return `${DOTCRAFT_APP_SCHEME}://${APP_HOST}/?${params.toString()}`
}

/**
 * Restrictive CSP for the interactive tool iframe document. The iframe is the
 * app's own (trusted, locally-installed) HTML running sandboxed with an opaque
 * origin and no Node; inline script/style are permitted there because the
 * sandbox — not the CSP — is the isolation boundary. M-ii is bridge-only (no
 * network); `connect-src`/`frame-src` are widened from `_meta.ui.csp` in M-iii.
 */
export function buildInteractiveToolCsp(): string {
  return [
    "default-src 'none'",
    "script-src 'unsafe-inline'",
    "style-src 'unsafe-inline'",
    "img-src data: blob:",
    "font-src data:",
    "media-src data: blob:",
    "base-uri 'none'",
    "form-action 'none'"
  ].join('; ')
}

export async function handleDotCraftAppRequest(request: Request): Promise<Response> {
  try {
    const parsed = new URL(request.url)
    if (parsed.hostname !== APP_HOST) {
      return new Response(null, { status: 404 })
    }

    const threadId = parsed.searchParams.get('threadId') ?? ''
    const namespace = parsed.searchParams.get('namespace') || undefined
    const uri = parsed.searchParams.get('uri') ?? ''
    if (!threadId || !uri || !uri.startsWith('ui://')) {
      return new Response(null, { status: 400 })
    }

    const client = resolveClient?.()
    if (!client) {
      return new Response(null, { status: 503 })
    }

    const result = await client.sendRequest<UiResourceReadResult>(
      'ui/resource/read',
      { threadId, namespace, uri },
      RESOURCE_READ_TIMEOUT_MS
    )
    const html = result?.contents?.find((entry) => typeof entry.text === 'string')?.text
    if (typeof html !== 'string') {
      return new Response(null, { status: 404 })
    }

    return new Response(html, {
      status: 200,
      headers: {
        'Content-Type': 'text/html; charset=utf-8',
        'Content-Security-Policy': buildInteractiveToolCsp(),
        'X-Content-Type-Options': 'nosniff'
      }
    })
  } catch {
    return new Response(null, { status: 500 })
  }
}
