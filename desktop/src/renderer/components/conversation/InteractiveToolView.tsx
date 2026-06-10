import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Puzzle, ShieldX, TriangleAlert, Unplug } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import { useLocale } from '../../contexts/LocaleContext'
import { derivePluginFunctionResultText, type ConversationItem, type ToolUiDescriptor } from '../../types/conversation'
import { useConversationStore } from '../../stores/conversationStore'
import { useAppBindingStore } from '../../stores/appBindingStore'
import { useDisplayModeStore, AVAILABLE_DISPLAY_MODES, type DisplayMode } from '../../stores/displayModeStore'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { AnsiPre } from './AnsiPre'
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

function isJsonRpcRequest(data: unknown): data is { id: unknown; method: string; params?: unknown; bridgeToken?: unknown } {
  return (
    data != null &&
    typeof data === 'object' &&
    typeof (data as { method?: unknown }).method === 'string' &&
    'id' in (data as object)
  )
}

interface BridgeSession {
  token: string | null
  initialized: boolean
  disabled: boolean
  loadCount: number
}

function createBridgeSession(): BridgeSession {
  return { token: null, initialized: false, disabled: false, loadCount: 0 }
}

function createBridgeToken(): string {
  const crypto = globalThis.crypto
  if (typeof crypto?.randomUUID === 'function') return crypto.randomUUID()
  if (typeof crypto?.getRandomValues === 'function') {
    const bytes = new Uint8Array(32)
    crypto.getRandomValues(bytes)
    return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  }
  throw new Error('Secure random bridge token unavailable.')
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

/**
 * Why the app view can't render. Derived from the thread's app-binding state (and plugin
 * enablement) rather than the iframe's ambiguous `onLoad` — a `dotcraft-app://` resource
 * read refused by AppServer (offline/revoked/expired binding) still fires `onLoad`, so the
 * iframe alone can't distinguish failure from success (see app-binding spec §16 — Desktop
 * shows a safe error display and falls back to the tool result's text).
 */
export type InteractiveToolUnavailableReason = 'disconnected' | 'revoked' | 'pluginDisabled' | 'failed'

/**
 * Map a binding state + plugin-enablement to a degraded reason. Returns null when the card
 * should still attempt to render the iframe (binding `active`/`pending`, or state unknown /
 * not yet fetched) — degradation only kicks in on positive evidence so working apps never
 * regress.
 */
export function deriveUnavailableReason(
  bindingState: string | undefined,
  pluginDisabled: boolean
): InteractiveToolUnavailableReason | null {
  if (pluginDisabled) return 'pluginDisabled'
  switch (bindingState) {
    case 'offline':
      return 'disconnected'
    case 'revoked':
    case 'expired':
    case 'cancelled':
      return 'revoked'
    case 'error':
      return 'failed'
    default:
      return null
  }
}

/**
 * Powerful-feature permissions an app may declare in `_meta.ui.permissions`, mapped to their
 * Permissions-Policy tokens. The iframe is granted exactly the declared (server-validated)
 * features and nothing else — unknown tokens are dropped and, with none declared, every powerful
 * feature stays denied (deny-by-default). See tool-result-presentation.md §11.
 */
const PERMISSION_POLICY: Record<string, string> = {
  camera: 'camera',
  microphone: 'microphone',
  geolocation: 'geolocation',
  clipboardWrite: 'clipboard-write'
}

function buildIframeAllow(permissions: string[] | undefined): string {
  if (!permissions || permissions.length === 0) return ''
  const tokens = permissions
    .map((permission) => PERMISSION_POLICY[permission])
    .filter((token): token is string => typeof token === 'string')
  return Array.from(new Set(tokens)).join('; ')
}

function InteractiveToolViewImpl({ item, threadId, locale, expanded = false }: InteractiveToolViewProps): JSX.Element | null {
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const bridgeRef = useRef<BridgeSession>(createBridgeSession())
  const [loaded, setLoaded] = useState(false)
  const [errored, setErrored] = useState(false)
  const [reloadNonce, setReloadNonce] = useState(0)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const expandedCard = useDisplayModeStore((s) => s.expanded)
  const collapse = useDisplayModeStore((s) => s.collapse)
  const messageTimesRef = useRef<number[]>([])

  const descriptor: ToolUiDescriptor | undefined = item.toolUi
  const resourceUri = descriptor?.resourceUri
  const prefersBorder = descriptor?.prefersBorder !== false
  // Server-validated powerful-feature allow-list; empty → all powerful features denied.
  const iframeAllow = buildIframeAllow(descriptor?.permissions)
  const namespace = item.pluginNamespace
  // Provenance for decoupled UI actions: the originating dynamicToolCall's callId.
  const sourceCallId = item.toolCallId ?? item.id

  // Availability is derived from the thread's app-binding lifecycle, which is kept live by the
  // `thread/appBindings/changed` notification (appBindingStore). A history thread re-entered
  // after the app disconnected / the binding was revoked / the plugin was disabled resolves to
  // an explicit degraded state + text fallback instead of a blank iframe.
  const threadBindings = useAppBindingStore((s) => (threadId ? s.bindingsByThread[threadId] : undefined))
  const appEnabledByNamespace = useAppBindingStore((s) => {
    if (!namespace) return undefined
    const app = s.apps.find((entry) => entry.toolNamespace === namespace)
    return app ? app.enabled : undefined
  })
  const bindingState = useMemo(
    () => (namespace ? threadBindings?.find((b) => b.toolNamespace === namespace)?.state : undefined),
    [namespace, threadBindings]
  )
  const unavailableReason = useMemo(
    () => deriveUnavailableReason(bindingState, appEnabledByNamespace === false),
    [bindingState, appEnabledByNamespace]
  )
  // App view can't render: show an explicit degraded state with the recorded tool result as a
  // text fallback, instead of a blank iframe. `errored` (iframe self-navigation / load error)
  // maps to the generic "failed" reason; binding-derived reasons are more specific.
  const effectiveReason = unavailableReason ?? (errored ? 'failed' : null)

  // Ensure the thread's bindings are loaded (incl. revoked) so a history card can detect its own
  // unavailability; guarded so multiple cards in a thread don't each fire the request.
  useEffect(() => {
    if (!threadId || !resourceUri) return
    const store = useAppBindingStore.getState()
    if (store.bindingsByThread[threadId] === undefined && store.bindingsLoadingByThread[threadId] !== true) {
      void store.fetchThreadBindings(threadId, true)
    }
  }, [threadId, resourceUri])

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

  const resetBridgeSession = useCallback(() => {
    bridgeRef.current = createBridgeSession()
    messageTimesRef.current = []
  }, [])

  const handleRetry = useCallback(() => {
    if (threadId) void useAppBindingStore.getState().fetchThreadBindings(threadId, true)
    resetBridgeSession()
    setErrored(false)
    setLoaded(false)
    setReloadNonce((n) => n + 1)
  }, [threadId, resetBridgeSession])

  const setIframeRef = useCallback((node: HTMLIFrameElement | null) => {
    if (iframeRef.current === node) return
    iframeRef.current = node
    if (node) {
      resetBridgeSession()
      setLoaded(false)
      setErrored(false)
    }
  }, [resetBridgeSession])

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
  const clearModelContext = useCallback(() => {
    const { threadId: tid, namespace: ns, sourceCallId: scid } = teardownRef.current
    if (tid && scid) {
      void window.api.appServer
        .sendRequest('ui/update-model-context', { threadId: tid, namespace: ns, sourceCallId: scid, content: '' }, ACTION_TIMEOUT_MS)
        .catch(() => {})
    }
  }, [])

  const disableBridge = useCallback((showError: boolean) => {
    const bridge = bridgeRef.current
    if (bridge.disabled) return
    bridge.disabled = true
    bridge.initialized = false
    bridge.token = null
    clearModelContext()
    if (showError) setErrored(true)
  }, [clearModelContext])

  useEffect(() => {
    if (effectiveReason) disableBridge(false)
  }, [effectiveReason, disableBridge])

  useEffect(() => () => {
    if (!bridgeRef.current.disabled) {
      iframeRef.current?.contentWindow?.postMessage({ jsonrpc: '2.0', method: 'ui/resource-teardown' }, '*')
    }
    clearModelContext()
  }, [clearModelContext])

  const handleFrameLoad = useCallback(() => {
    const bridge = bridgeRef.current
    if (bridge.loadCount === 0) {
      bridge.loadCount += 1
      setLoaded(true)
      return
    }
    disableBridge(true)
  }, [disableBridge])

  useEffect(() => {
    const respond = (id: unknown, result: unknown): void =>
      bridgeRef.current.disabled ? undefined : postToFrame({ jsonrpc: '2.0', id, result })
    const respondError = (id: unknown, code: number, message: string): void =>
      bridgeRef.current.disabled ? undefined : postToFrame({ jsonrpc: '2.0', id, error: { code, message } })

    const validateBridgeToken = (id: unknown, data: { bridgeToken?: unknown }): boolean => {
      const bridge = bridgeRef.current
      if (bridge.disabled) return false
      if (!bridge.initialized || !bridge.token) {
        respondError(id, JSONRPC_INVALID_REQUEST, 'UI bridge is not initialized.')
        return false
      }
      if (data.bridgeToken !== bridge.token) {
        respondError(id, JSONRPC_INVALID_REQUEST, 'Invalid UI bridge token.')
        return false
      }
      return true
    }

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
      // Sandboxed (opaque-origin) iframe → validate by source, not origin; host actions
      // also require the per-document bridge token returned by the initial handshake.
      if (!iframeRef.current || event.source !== iframeRef.current.contentWindow) return
      const data = event.data
      if (!isJsonRpcRequest(data)) return
      if (bridgeRef.current.disabled) return
      const params = (data.params ?? undefined) as Record<string, unknown> | undefined

      switch (data.method) {
        case 'ui/initialize':
          if (bridgeRef.current.initialized) {
            respondError(data.id, JSONRPC_INVALID_REQUEST, 'UI bridge is already initialized.')
            disableBridge(true)
            return
          }
          if (bridgeRef.current.loadCount > 0) {
            respondError(data.id, JSONRPC_INVALID_REQUEST, 'UI bridge initialization window has closed.')
            disableBridge(true)
            return
          }
          try {
            bridgeRef.current.token = createBridgeToken()
          } catch (err) {
            respondError(data.id, JSONRPC_INTERNAL_ERROR, toErrorMessage(err))
            disableBridge(true)
            return
          }
          bridgeRef.current.initialized = true
          postToFrame({
            jsonrpc: '2.0',
            id: data.id,
            result: {
              protocolVersion: PROTOCOL_VERSION,
              bridgeToken: bridgeRef.current.token,
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
          if (!validateBridgeToken(data.id, data)) return
          void handleToolCall(data.id, params)
          return
        case 'ui/open-link':
          if (!validateBridgeToken(data.id, data)) return
          void handleOpenLink(data.id, params)
          return
        case 'ui/update-model-context':
          if (!validateBridgeToken(data.id, data)) return
          void handleUpdateModelContext(data.id, params)
          return
        case 'ui/set-widget-state':
          if (!validateBridgeToken(data.id, data)) return
          void handleSetWidgetState(data.id, params)
          return
        case 'ui/request-display-mode':
          if (!validateBridgeToken(data.id, data)) return
          handleRequestDisplayMode(data.id, params)
          return
        case 'ui/message':
          if (!validateBridgeToken(data.id, data)) return
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
  useEffect(() => {
    const pushHostContext = (): void => {
      const bridge = bridgeRef.current
      if (!bridge.initialized || bridge.disabled) return
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
    // ui/initialize already carried the initial context; only push when an initialized bridge exists.
    pushHostContext()
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

  if (effectiveReason) {
    return (
      <InteractiveToolDegraded
        reason={effectiveReason}
        item={item}
        locale={locale}
        expanded={expanded}
        prefersBorder={prefersBorder}
        onRetry={handleRetry}
      />
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
      {!loaded && (
        <div style={statusStyle}>{translate(locale, 'interactiveTool.loading')}</div>
      )}
      <iframe
        key={`${src}#${reloadNonce}`}
        ref={setIframeRef}
        className="interactive-tool-view__frame"
        title={descriptor?.domain ?? item.toolName ?? 'App view'}
        src={src}
        sandbox="allow-scripts"
        allow={iframeAllow}
        loading="lazy"
        style={{
          width: '100%',
          height: expanded ? '100%' : DEFAULT_FRAME_HEIGHT,
          flex: expanded ? 1 : undefined,
          border: 'none',
          display: 'block'
        }}
        onLoad={handleFrameLoad}
        onError={() => disableBridge(true)}
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

const REASON_META: Record<
  InteractiveToolUnavailableReason,
  { Icon: typeof Unplug; tone: 'warning' | 'error' | 'neutral'; titleKey: string; descKey: string }
> = {
  disconnected: { Icon: Unplug, tone: 'warning', titleKey: 'interactiveTool.disconnected.title', descKey: 'interactiveTool.disconnected.desc' },
  revoked: { Icon: ShieldX, tone: 'error', titleKey: 'interactiveTool.revoked.title', descKey: 'interactiveTool.revoked.desc' },
  pluginDisabled: { Icon: Puzzle, tone: 'neutral', titleKey: 'interactiveTool.pluginDisabled.title', descKey: 'interactiveTool.pluginDisabled.desc' },
  failed: { Icon: TriangleAlert, tone: 'error', titleKey: 'interactiveTool.failed.title', descKey: 'interactiveTool.failed.desc' }
}

function toneColor(tone: 'warning' | 'error' | 'neutral'): string {
  if (tone === 'warning') return 'var(--warning, #eab308)'
  if (tone === 'error') return 'var(--error, #ef4444)'
  return 'var(--text-dimmed, rgba(255,255,255,0.4))'
}

/**
 * Degraded surface shown when the app view can't render (binding offline/revoked/expired,
 * plugin disabled, or a load failure). Explicit localized state + the recorded tool result as a
 * readable fallback — the interactive UI is never required for correctness (tool-result-presentation §12).
 */
function InteractiveToolDegraded({
  reason,
  item,
  locale,
  expanded,
  prefersBorder,
  onRetry
}: {
  reason: InteractiveToolUnavailableReason
  item: ConversationItem
  locale: AppLocale
  expanded: boolean
  prefersBorder: boolean
  onRetry: () => void
}): JSX.Element {
  const meta = REASON_META[reason]
  const Icon = meta.Icon
  const color = toneColor(meta.tone)
  const resultText = derivePluginFunctionResultText(item.contentItems, item.structuredResult, item.errorMessage)
  const icon: ReactNode = <Icon size={18} strokeWidth={1.6} />

  return (
    <div
      className="interactive-tool-view interactive-tool-view--unavailable"
      style={{
        borderRadius: expanded ? 0 : 8,
        overflow: 'hidden',
        border: expanded ? 'none' : prefersBorder ? '1px solid var(--border-default, rgba(255,255,255,0.1))' : 'none',
        background: 'var(--bg-primary, transparent)',
        ...(expanded ? { height: '100%', display: 'flex', flexDirection: 'column' } : {})
      }}
    >
      <div style={{ ...degradedPanelStyle, ...(expanded ? { flex: 1 } : { minHeight: 188 }) }}>
        <span style={{ ...degradedIconStyle, color, background: `color-mix(in srgb, ${color} 12%, transparent)` }}>
          {icon}
        </span>
        <div style={degradedTitleStyle}>{translate(locale, meta.titleKey)}</div>
        <p style={degradedDescStyle}>{translate(locale, meta.descKey)}</p>
        <button type="button" style={degradedRetryStyle} onClick={onRetry}>
          {translate(locale, 'interactiveTool.retry')}
        </button>
      </div>
      {resultText && (
        <div style={degradedFallbackStyle}>
          <div style={degradedFallbackLabelStyle}>{translate(locale, 'interactiveTool.resultFallbackLabel')}</div>
          <AnsiPre text={resultText} maxHeight={expanded ? undefined : 200} truncatedLinesOver={20} colorWhenNoSgr="var(--text-secondary)" />
        </div>
      )}
    </div>
  )
}

const degradedPanelStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: '9px',
  padding: '22px 18px',
  textAlign: 'center'
}

const degradedIconStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '40px',
  height: '40px',
  borderRadius: '10px'
}

const degradedTitleStyle: CSSProperties = {
  fontSize: '14px',
  fontWeight: 600,
  color: 'var(--text-primary, inherit)'
}

const degradedDescStyle: CSSProperties = {
  margin: 0,
  maxWidth: '340px',
  fontSize: '12.5px',
  lineHeight: 1.5,
  color: 'var(--text-secondary, rgba(255,255,255,0.6))'
}

const degradedRetryStyle: CSSProperties = {
  marginTop: '4px',
  font: 'inherit',
  fontSize: '12.5px',
  fontWeight: 600,
  padding: '6px 14px',
  borderRadius: '7px',
  border: '1px solid var(--text-primary, #eee)',
  background: 'var(--text-primary, #eee)',
  color: 'var(--bg-primary, #111)',
  cursor: 'pointer'
}

const degradedFallbackStyle: CSSProperties = {
  padding: '11px 12px 13px',
  borderTop: '1px solid var(--border-default, rgba(255,255,255,0.1))',
  background: 'color-mix(in srgb, var(--text-primary) 1.5%, transparent)'
}

const degradedFallbackLabelStyle: CSSProperties = {
  fontSize: '11px',
  fontWeight: 600,
  letterSpacing: '0.03em',
  textTransform: 'uppercase',
  color: 'var(--text-dimmed, rgba(255,255,255,0.4))',
  marginBottom: '6px'
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
