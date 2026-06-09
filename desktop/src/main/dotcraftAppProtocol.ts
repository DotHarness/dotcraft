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

/** `_meta.ui.csp` echoed by `ui/resource/read` — the server-validated descriptor (M-iii data path B). */
interface UiToolCsp {
  connectDomains?: string[]
  resourceDomains?: string[]
  frameDomains?: string[]
}

interface UiResourceReadResult {
  contents?: Array<{ uri?: string; mimeType?: string; text?: string }>
  csp?: UiToolCsp | null
}

/**
 * Only well-formed http(s)/ws(s) origins are accepted into the CSP, and any entry containing CSP
 * delimiters is dropped — a UI resource must not be able to inject directives.
 */
const CSP_ORIGIN_PATTERN = /^(https?|wss?):\/\/[^\s;,'"]+$/i

function sanitizeCspDomains(domains: string[] | undefined): string[] {
  if (!Array.isArray(domains)) return []
  return domains
    .map((d) => (typeof d === 'string' ? d.trim() : ''))
    .filter((d) => CSP_ORIGIN_PATTERN.test(d))
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
 * sandbox — not the CSP — is the isolation boundary.
 *
 * M-iii (data path B): `connect-src` / `frame-src` and the passive resource
 * sources (`img`/`style`/`font`/`media`) are widened from the server-validated
 * `_meta.ui.csp` (passed here, never sourced from the iframe). With no csp the
 * default is network-denied (M-ii baseline). `script-src` is never widened —
 * external scripts stay disallowed regardless of `resourceDomains`.
 */
export function buildInteractiveToolCsp(csp?: UiToolCsp | null): string {
  const connect = sanitizeCspDomains(csp?.connectDomains)
  const resource = sanitizeCspDomains(csp?.resourceDomains)
  const frame = sanitizeCspDomains(csp?.frameDomains)
  const resourceSuffix = resource.length ? ` ${resource.join(' ')}` : ''

  const directives = [
    "default-src 'none'",
    "script-src 'unsafe-inline'",
    `style-src 'unsafe-inline'${resourceSuffix}`,
    `img-src data: blob:${resourceSuffix}`,
    `font-src data:${resourceSuffix}`,
    `media-src data: blob:${resourceSuffix}`,
    "base-uri 'none'",
    "form-action 'none'"
  ]
  if (connect.length) directives.push(`connect-src ${connect.join(' ')}`)
  if (frame.length) directives.push(`frame-src ${frame.join(' ')}`)
  return directives.join('; ')
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
        'Content-Security-Policy': buildInteractiveToolCsp(result?.csp),
        'X-Content-Type-Options': 'nosniff'
      }
    })
  } catch {
    return new Response(null, { status: 500 })
  }
}
