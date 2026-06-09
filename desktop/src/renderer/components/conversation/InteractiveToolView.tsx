import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { translate, type AppLocale } from '../../../shared/locales'
import { useLocale } from '../../contexts/LocaleContext'
import type { ConversationItem, ToolUiDescriptor } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useDisplayModeStore, AVAILABLE_DISPLAY_MODES, type DisplayMode } from '../../stores/displayModeStore'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'

/**
 * Renders an App Binding tool's **Interactive Tool UI** (MCP Apps) in a sandboxed iframe.
 *
 * The HTML is served by the main-process `dotcraft-app://` handler (which brokers
 * `ui/resource/read` and applies a per-resource CSP — see `dotcraftAppProtocol.ts`),
 * so the document runs with its own CSP independent of the app shell. The iframe is
 * `sandbox="allow-scripts"` (no `allow-same-origin`): opaque origin, no host DOM, no Node.
 *
 * M-iii makes the bridge **interactive**: beyond the `ui/initialize` handshake and the
 * `tool-input` / `tool-result` push, the host now services UI→host actions —
 * `tools/call` (read-only, gated + audited via `ui/tool/call`), `ui/open-link`
 * (https/mailto host policy), `ui/update-model-context` (model-visible widget state,
 * cleared on teardown), and `ui/message` (visible user turn, rate-limited). The widened
 * CSP for data path B is applied by the main process from the server-validated descriptor.
 */

const DOTCRAFT_APP_SCHEME = 'dotcraft-app'
const DEFAULT_FRAME_HEIGHT = 480
const PROTOCOL_VERSION = '2025-06-18'
const TOOL_CALL_TIMEOUT_MS = 120_000
const ACTION_TIMEOUT_MS = 20_000

/** `ui/message` rate limit: visibility + rate-limit are the safeguards (the iframe gesture is not host-verifiable). */
const MESSAGE_MIN_INTERVAL_MS = 2_000
const MESSAGE_MAX_PER_MINUTE = 10
const JSONRPC_INVALID_REQUEST = -32600
const JSONRPC_METHOD_NOT_FOUND = -32601
const JSONRPC_INTERNAL_ERROR = -32603

interface InteractiveToolViewProps {
  item: ConversationItem
  threadId: string | null
  locale: AppLocale
  /** True when this instance is the expanded (pip/fullscreen) surface rather than the inline card. */
  expanded?: boolean
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

/** Extracts plain text from a `ui/message` payload (string, content array, or content object). */
function extractMessageText(params: Record<string, unknown> | undefined): string {
  const content = params?.content
  if (typeof content === 'string') return content.trim()
  if (Array.isArray(content)) {
    return content
      .map((part) => (part && typeof (part as { text?: unknown }).text === 'string' ? (part as { text: string }).text : ''))
      .join('')
      .trim()
  }
  if (content && typeof (content as { text?: unknown }).text === 'string') return (content as { text: string }).text.trim()
  if (typeof params?.text === 'string') return params.text.trim()
  return ''
}

function toErrorMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  if (typeof err === 'string') return err
  try {
    return JSON.stringify(err)
  } catch {
    return 'Unknown error'
  }
}

function InteractiveToolViewImpl({ item, threadId, locale, expanded = false }: InteractiveToolViewProps): JSX.Element | null {
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [errored, setErrored] = useState(false)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const expandedCard = useDisplayModeStore((s) => s.expanded)
  const collapse = useDisplayModeStore((s) => s.collapse)
  const messageTimesRef = useRef<number[]>([])

  const descriptor: ToolUiDescriptor | undefined = item.toolUi
  const resourceUri = descriptor?.resourceUri
  const prefersBorder = descriptor?.prefersBorder !== false
  const namespace = item.pluginNamespace
  // Provenance for decoupled UI actions: the originating dynamicToolCall's callId.
  const sourceCallId = item.toolCallId ?? item.id

  // M-iv display mode. The inline instance shows a placeholder while its card is expanded elsewhere
  // (so only one live iframe exists); the expanded instance reports its granted mode to the iframe.
  const isExpandedElsewhere = !expanded && expandedCard?.item.id === item.id
  const currentMode: DisplayMode = expanded ? expandedCard?.mode ?? 'fullscreen' : 'inline'

  const src = useMemo(() => {
    if (!threadId || !resourceUri) return null
    return buildResourceUrl(threadId, namespace, resourceUri)
  }, [threadId, resourceUri, namespace])

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

  // Clear the model-context block + notify the iframe when the card tears down (unmount /
  // navigation away). Latest ids are read from a ref so the empty-deps cleanup isn't stale.
  const teardownRef = useRef({ threadId, namespace, sourceCallId })
  teardownRef.current = { threadId, namespace, sourceCallId }
  useEffect(() => () => {
    const { threadId: tid, namespace: ns, sourceCallId: scid } = teardownRef.current
    iframeRef.current?.contentWindow?.postMessage({ jsonrpc: '2.0', method: 'ui/resource-teardown' }, '*')
    if (tid && scid) {
      void window.api.appServer
        .sendRequest('ui/update-model-context', { threadId: tid, namespace: ns, sourceCallId: scid, content: '' }, ACTION_TIMEOUT_MS)
        .catch(() => {})
    }
  }, [])

  useEffect(() => {
    const respond = (id: unknown, result: unknown): void =>
      postToFrame({ jsonrpc: '2.0', id, result })
    const respondError = (id: unknown, code: number, message: string): void =>
      postToFrame({ jsonrpc: '2.0', id, error: { code, message } })

    // ui/message safeguard: the host cannot verify a real click inside a sandboxed iframe, so it
    // is added as a visible turn, audited, and rate-limited (per MCP Apps; see M-iii §9).
    const allowMessage = (): boolean => {
      const now = Date.now()
      const times = messageTimesRef.current.filter((t) => now - t < 60_000)
      const last = times[times.length - 1]
      if (times.length >= MESSAGE_MAX_PER_MINUTE || (last != null && now - last < MESSAGE_MIN_INTERVAL_MS)) {
        messageTimesRef.current = times
        return false
      }
      times.push(now)
      messageTimesRef.current = times
      return true
    }

    async function handleToolCall(id: unknown, params: Record<string, unknown> | undefined): Promise<void> {
      const tool = (params?.name ?? params?.tool) as string | undefined
      if (!tool) {
        respondError(id, JSONRPC_INVALID_REQUEST, 'tools/call requires a tool name.')
        return
      }
      try {
        const result = (await window.api.appServer.sendRequest(
          'ui/tool/call',
          { threadId, namespace, tool, arguments: params?.arguments ?? {}, sourceCallId },
          TOOL_CALL_TIMEOUT_MS
        )) as { success?: boolean; structuredResult?: unknown; contentItems?: unknown; _meta?: unknown; errorMessage?: string; errorCode?: string }
        if (result?.success === false) {
          respondError(id, JSONRPC_INTERNAL_ERROR, result.errorMessage ?? result.errorCode ?? 'Tool call failed.')
          return
        }
        respond(id, {
          content: result?.contentItems ?? [],
          structuredContent: result?.structuredResult ?? null,
          _meta: result?._meta ?? null,
          isError: false
        })
      } catch (err) {
        respondError(id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
      }
    }

    async function handleOpenLink(id: unknown, params: Record<string, unknown> | undefined): Promise<void> {
      const url = typeof params?.url === 'string' ? params.url : undefined
      if (!url) {
        respondError(id, JSONRPC_INVALID_REQUEST, 'ui/open-link requires a url.')
        return
      }
      try {
        const result = (await window.api.appServer.sendRequest(
          'ui/open-link',
          { threadId, namespace, url, sourceCallId },
          ACTION_TIMEOUT_MS
        )) as { url?: string }
        await window.api.shell.openExternal(result?.url ?? url)
        respond(id, {})
      } catch (err) {
        respondError(id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
      }
    }

    async function handleUpdateModelContext(id: unknown, params: Record<string, unknown> | undefined): Promise<void> {
      const raw = params?.content
      const content = typeof raw === 'string' ? raw : raw == null ? '' : JSON.stringify(raw)
      const title = typeof params?.title === 'string' ? params.title : undefined
      try {
        await window.api.appServer.sendRequest(
          'ui/update-model-context',
          { threadId, namespace, sourceCallId, title, content },
          ACTION_TIMEOUT_MS
        )
        respond(id, {})
      } catch (err) {
        respondError(id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
      }
    }

    async function handleSetWidgetState(id: unknown, params: Record<string, unknown> | undefined): Promise<void> {
      try {
        await window.api.appServer.sendRequest(
          'item/widget-state/set',
          { threadId, callId: sourceCallId, widgetState: params?.widgetState ?? null },
          ACTION_TIMEOUT_MS
        )
        respond(id, {})
      } catch (err) {
        respondError(id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
      }
    }

    function handleRequestDisplayMode(id: unknown, params: Record<string, unknown> | undefined): void {
      const requested = params?.mode
      if (requested !== 'inline' && requested !== 'pip' && requested !== 'fullscreen') {
        respondError(id, JSONRPC_INVALID_REQUEST, 'ui/request-display-mode requires mode inline|pip|fullscreen.')
        return
      }
      const granted = useDisplayModeStore.getState().requestMode(item, threadId, requested)
      respond(id, { mode: granted })
      postToFrame({
        jsonrpc: '2.0',
        method: 'ui/notifications/host-context-changed',
        params: {
          theme: currentTheme(),
          locale,
          displayMode: granted,
          maxHeight: granted === 'inline' ? DEFAULT_FRAME_HEIGHT : null,
          availableDisplayModes: AVAILABLE_DISPLAY_MODES
        }
      })
    }

    async function handleMessage(id: unknown, params: Record<string, unknown> | undefined): Promise<void> {
      const text = extractMessageText(params)
      if (!text) {
        respondError(id, JSONRPC_INVALID_REQUEST, 'ui/message requires text content.')
        return
      }
      if (!threadId || !workspacePath) {
        respondError(id, JSONRPC_INTERNAL_ERROR, 'No active thread for ui/message.')
        return
      }
      if (!allowMessage()) {
        respondError(id, JSONRPC_INTERNAL_ERROR, 'ui/message rate limit exceeded.')
        return
      }
      try {
        await startTurnWithOptimisticUI({
          threadId,
          workspacePath,
          text,
          fallbackThreadName: '',
          renameThreadFromText: false,
          throwOnStartError: true
        })
        respond(id, {})
      } catch (err) {
        respondError(id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
      }
    }

    function onMessage(event: MessageEvent): void {
      // Sandboxed (opaque-origin) iframe → validate by source, not origin.
      if (!iframeRef.current || event.source !== iframeRef.current.contentWindow) return
      const data = event.data
      if (!isJsonRpcRequest(data)) return
      const params = (data.params ?? undefined) as Record<string, unknown> | undefined

      switch (data.method) {
        case 'ui/initialize':
          postToFrame({
            jsonrpc: '2.0',
            id: data.id,
            result: {
              protocolVersion: PROTOCOL_VERSION,
              hostContext: {
                theme: currentTheme(),
                locale,
                displayMode: currentMode,
                availableDisplayModes: AVAILABLE_DISPLAY_MODES,
                maxHeight: currentMode === 'inline' ? DEFAULT_FRAME_HEIGHT : null
              },
              // M-iii: action capabilities are live (logging arrives in M-iv).
              hostCapabilities: {
                openLinks: true,
                serverTools: true,
                updateModelContext: true,
                message: true,
                logging: false
              },
              // M-iv: restore persisted widget state at/before first paint (no flash).
              widgetState: item.widgetState ?? null
            }
          })
          pushToolData()
          return
        case 'tools/call':
          void handleToolCall(data.id, params)
          return
        case 'ui/open-link':
          void handleOpenLink(data.id, params)
          return
        case 'ui/update-model-context':
          void handleUpdateModelContext(data.id, params)
          return
        case 'ui/set-widget-state':
          void handleSetWidgetState(data.id, params)
          return
        case 'ui/request-display-mode':
          handleRequestDisplayMode(data.id, params)
          return
        case 'ui/message':
          void handleMessage(data.id, params)
          return
        default:
          respondError(data.id, JSONRPC_METHOD_NOT_FOUND, `Unsupported method: ${data.method}`)
      }
    }

    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [postToFrame, pushToolData, locale, threadId, namespace, sourceCallId, workspacePath, item, currentMode])

  // M-iv: live host-context push — re-emit host context on Desktop theme change (event) and on
  // locale change (prop), so the iframe re-themes/re-localizes without a reload.
  const didPushContextRef = useRef(false)
  useEffect(() => {
    const pushHostContext = (): void => {
      postToFrame({
        jsonrpc: '2.0',
        method: 'ui/notifications/host-context-changed',
        params: {
          theme: currentTheme(),
          locale,
          displayMode: currentMode,
          availableDisplayModes: AVAILABLE_DISPLAY_MODES,
          maxHeight: currentMode === 'inline' ? DEFAULT_FRAME_HEIGHT : null
        }
      })
    }
    // ui/initialize already carried the initial context; only push on a real later change.
    if (didPushContextRef.current) pushHostContext()
    else didPushContextRef.current = true
    window.addEventListener(THEME_CHANGED_EVENT, pushHostContext)
    return () => window.removeEventListener(THEME_CHANGED_EVENT, pushHostContext)
  }, [postToFrame, locale, currentMode])

  // The card is open in the floating/fullscreen surface: leave a placeholder so only one iframe is live.
  if (isExpandedElsewhere) {
    return (
      <div className="interactive-tool-view interactive-tool-view--placeholder" style={placeholderStyle}>
        <span>{translate(locale, 'interactiveTool.expandedElsewhere')}</span>
        <button type="button" style={placeholderButtonStyle} onClick={collapse}>
          {translate(locale, 'interactiveTool.collapse')}
        </button>
      </div>
    )
  }

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
        borderRadius: expanded ? '0' : '8px',
        overflow: 'hidden',
        border: expanded ? 'none' : prefersBorder ? '1px solid var(--border-default, rgba(255,255,255,0.1))' : 'none',
        background: 'var(--bg-primary, transparent)',
        ...(expanded ? { height: '100%', display: 'flex', flexDirection: 'column' } : {})
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
          height: expanded ? '100%' : DEFAULT_FRAME_HEIGHT,
          flex: expanded ? 1 : undefined,
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

const placeholderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '12px',
  padding: '10px 12px',
  fontSize: '13px',
  color: 'var(--text-secondary, rgba(255,255,255,0.6))',
  border: '1px dashed var(--border-default, rgba(255,255,255,0.18))',
  borderRadius: '8px'
}

const placeholderButtonStyle: CSSProperties = {
  flexShrink: 0,
  font: 'inherit',
  fontSize: '12px',
  padding: '4px 10px',
  borderRadius: '6px',
  border: '1px solid var(--border-default, rgba(255,255,255,0.18))',
  background: 'var(--bg-secondary, transparent)',
  color: 'var(--text-primary, inherit)',
  cursor: 'pointer'
}

export const InteractiveToolView = memo(InteractiveToolViewImpl)

/**
 * Host surface for a card the iframe has expanded via `ui/request-display-mode`:
 * a floating corner window (`pip`) or a portal overlay over the conversation (`fullscreen`).
 * Re-mounts the iframe in the new surface — Pass-1 `widgetState` restore preserves its state.
 * Mounted once near the conversation root.
 */
function InteractiveToolOverlayImpl(): JSX.Element | null {
  const locale = useLocale()
  const expandedCard = useDisplayModeStore((s) => s.expanded)
  const collapse = useDisplayModeStore((s) => s.collapse)

  useEffect(() => {
    if (!expandedCard) return undefined
    function onKey(e: KeyboardEvent): void {
      if (e.key === 'Escape') collapse()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [expandedCard, collapse])

  if (!expandedCard) return null
  const isFullscreen = expandedCard.mode === 'fullscreen'
  const title = expandedCard.item.toolName ?? expandedCard.item.toolUi?.domain ?? 'App'

  const panel = (
    <div style={isFullscreen ? fullscreenPanelStyle : pipPanelStyle}>
      <div style={overlayHeaderStyle}>
        <span style={overlayTitleStyle}>{title}</span>
        <button
          type="button"
          aria-label={translate(locale, 'interactiveTool.collapse')}
          title={translate(locale, 'interactiveTool.collapse')}
          style={overlayCloseStyle}
          onClick={collapse}
        >
          ✕
        </button>
      </div>
      <div style={{ flex: 1, minHeight: 0 }}>
        <InteractiveToolView item={expandedCard.item} threadId={expandedCard.threadId} locale={locale} expanded />
      </div>
    </div>
  )

  return createPortal(
    isFullscreen ? (
      <div style={fullscreenBackdropStyle} onClick={collapse}>
        <div style={{ height: '100%', display: 'flex' }} onClick={(e) => e.stopPropagation()}>
          {panel}
        </div>
      </div>
    ) : (
      panel
    ),
    document.body
  )
}

export const InteractiveToolOverlay = memo(InteractiveToolOverlayImpl)

const overlayHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px',
  padding: '6px 10px',
  borderBottom: '1px solid var(--border-default, rgba(255,255,255,0.12))',
  background: 'var(--bg-secondary, rgba(0,0,0,0.2))'
}

const overlayTitleStyle: CSSProperties = {
  fontSize: '12px',
  fontWeight: 600,
  color: 'var(--text-primary, inherit)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const overlayCloseStyle: CSSProperties = {
  flexShrink: 0,
  font: 'inherit',
  fontSize: '12px',
  lineHeight: 1,
  width: '24px',
  height: '24px',
  borderRadius: '6px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary, inherit)',
  cursor: 'pointer'
}

const fullscreenBackdropStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 1000,
  background: 'rgba(0,0,0,0.5)',
  padding: '4vh 4vw',
  boxSizing: 'border-box'
}

const fullscreenPanelStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  flexDirection: 'column',
  minHeight: 0,
  borderRadius: '10px',
  overflow: 'hidden',
  background: 'var(--bg-primary, #1e1e22)',
  boxShadow: '0 12px 48px rgba(0,0,0,0.4)'
}

const pipPanelStyle: CSSProperties = {
  position: 'fixed',
  right: '20px',
  bottom: '20px',
  zIndex: 1000,
  width: '360px',
  height: '480px',
  display: 'flex',
  flexDirection: 'column',
  borderRadius: '10px',
  overflow: 'hidden',
  border: '1px solid var(--border-default, rgba(255,255,255,0.12))',
  background: 'var(--bg-primary, #1e1e22)',
  boxShadow: '0 8px 32px rgba(0,0,0,0.4)'
}

/** True when an item declares a renderable Interactive Tool UI. */
export function hasInteractiveToolUi(item: ConversationItem): boolean {
  return typeof item.toolUi?.resourceUri === 'string' && item.toolUi.resourceUri.startsWith('ui://')
}
