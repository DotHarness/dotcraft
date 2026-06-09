import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem, ToolUiDescriptor } from '../../types/conversation'

/**
 * Renders an App Binding tool's **Interactive Tool UI** (MCP Apps) in a sandboxed iframe.
 *
 * The HTML is served by the main-process `dotcraft-app://` handler (which brokers
 * `ui/resource/read` and applies a per-resource CSP — see `dotcraftAppProtocol.ts`),
 * so the document runs with its own CSP independent of the app shell. The iframe is
 * `sandbox="allow-scripts"` (no `allow-same-origin`): opaque origin, no host DOM, no Node.
 *
 * M-ii is **read-only**: the host completes the `ui/initialize` handshake and pushes
 * `tool-input` / `tool-result` over the postMessage bridge. UI→host actions
 * (`tools/call`, `ui/open-link`, `ui/message`, …) arrive in M-iii.
 */

const DOTCRAFT_APP_SCHEME = 'dotcraft-app'
const DEFAULT_FRAME_HEIGHT = 480
const PROTOCOL_VERSION = '2025-06-18'

interface InteractiveToolViewProps {
  item: ConversationItem
  threadId: string | null
  locale: AppLocale
}

/** Mirrors `buildDotCraftAppUrl` in main/dotcraftAppProtocol.ts. */
function buildResourceUrl(threadId: string, namespace: string | undefined, uri: string): string {
  const params = new URLSearchParams()
  params.set('threadId', threadId)
  if (namespace) params.set('namespace', namespace)
  params.set('uri', uri)
  return `${DOTCRAFT_APP_SCHEME}://resource/?${params.toString()}`
}

function currentTheme(): 'light' | 'dark' {
  if (typeof document === 'undefined') return 'dark'
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
}

function isJsonRpcRequest(data: unknown): data is { id: unknown; method: string; params?: unknown } {
  return (
    data != null &&
    typeof data === 'object' &&
    typeof (data as { method?: unknown }).method === 'string' &&
    'id' in (data as object)
  )
}

function InteractiveToolViewImpl({ item, threadId, locale }: InteractiveToolViewProps): JSX.Element | null {
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [errored, setErrored] = useState(false)

  const descriptor: ToolUiDescriptor | undefined = item.toolUi
  const resourceUri = descriptor?.resourceUri
  const prefersBorder = descriptor?.prefersBorder !== false

  const src = useMemo(() => {
    if (!threadId || !resourceUri) return null
    return buildResourceUrl(threadId, item.pluginNamespace, resourceUri)
  }, [threadId, resourceUri, item.pluginNamespace])

  // The host bridge peer: respond to ui/initialize and push tool-input / tool-result.
  const postToFrame = useCallback((message: unknown) => {
    iframeRef.current?.contentWindow?.postMessage(message, '*')
  }, [])

  const pushToolData = useCallback(() => {
    postToFrame({
      jsonrpc: '2.0',
      method: 'ui/notifications/tool-input',
      params: { toolInput: item.arguments ?? {} }
    })
    postToFrame({
      jsonrpc: '2.0',
      method: 'ui/notifications/tool-result',
      params: {
        content: item.contentItems ?? [],
        structuredContent: item.structuredResult ?? null,
        _meta: item.meta ?? null,
        isError: item.success === false
      }
    })
  }, [postToFrame, item.arguments, item.contentItems, item.structuredResult, item.meta, item.success])

  useEffect(() => {
    function onMessage(event: MessageEvent): void {
      // Sandboxed (opaque-origin) iframe → validate by source, not origin.
      if (!iframeRef.current || event.source !== iframeRef.current.contentWindow) return
      const data = event.data
      if (!isJsonRpcRequest(data)) return

      if (data.method === 'ui/initialize') {
        postToFrame({
          jsonrpc: '2.0',
          id: data.id,
          result: {
            protocolVersion: PROTOCOL_VERSION,
            hostContext: {
              theme: currentTheme(),
              locale,
              displayMode: 'inline',
              maxHeight: DEFAULT_FRAME_HEIGHT
            },
            // Read-only host (M-ii): action capabilities arrive in M-iii.
            hostCapabilities: {
              openLinks: false,
              serverTools: false,
              updateModelContext: false,
              message: false,
              logging: false
            }
          }
        })
        pushToolData()
        return
      }

      // Any other UI→host request is unsupported until M-iii.
      postToFrame({
        jsonrpc: '2.0',
        id: data.id,
        error: { code: -32601, message: 'Interactive tool action not available (read-only host).' }
      })
    }

    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [postToFrame, pushToolData, locale])

  if (!src) {
    return (
      <div className="interactive-tool-view" style={fallbackStyle}>
        {translate(locale, 'interactiveTool.unavailable')}
      </div>
    )
  }

  return (
    <div
      className="interactive-tool-view"
      style={{
        position: 'relative',
        borderRadius: '8px',
        overflow: 'hidden',
        border: prefersBorder ? '1px solid var(--border-default, rgba(255,255,255,0.1))' : 'none',
        background: 'var(--bg-primary, transparent)'
      }}
    >
      {!loaded && !errored && (
        <div style={statusStyle}>{translate(locale, 'interactiveTool.loading')}</div>
      )}
      {errored && (
        <div style={{ ...statusStyle, color: 'var(--text-error, #e5484d)' }}>
          {translate(locale, 'interactiveTool.error')}
        </div>
      )}
      <iframe
        ref={iframeRef}
        className="interactive-tool-view__frame"
        title={descriptor?.domain ?? item.toolName ?? 'App view'}
        src={src}
        sandbox="allow-scripts"
        loading="lazy"
        style={{
          width: '100%',
          height: DEFAULT_FRAME_HEIGHT,
          border: 'none',
          display: errored ? 'none' : 'block'
        }}
        onLoad={() => setLoaded(true)}
        onError={() => setErrored(true)}
      />
    </div>
  )
}

const statusStyle: CSSProperties = {
  position: 'absolute',
  top: '50%',
  left: '50%',
  transform: 'translate(-50%, -50%)',
  fontSize: '13px',
  color: 'var(--text-secondary, rgba(255,255,255,0.6))',
  pointerEvents: 'none'
}

const fallbackStyle: CSSProperties = {
  padding: '12px',
  fontSize: '13px',
  color: 'var(--text-secondary, rgba(255,255,255,0.6))',
  border: '1px solid var(--border-default, rgba(255,255,255,0.1))',
  borderRadius: '8px'
}

export const InteractiveToolView = memo(InteractiveToolViewImpl)

/** True when an item declares a renderable Interactive Tool UI. */
export function hasInteractiveToolUi(item: ConversationItem): boolean {
  return typeof item.toolUi?.resourceUri === 'string' && item.toolUi.resourceUri.startsWith('ui://')
}
