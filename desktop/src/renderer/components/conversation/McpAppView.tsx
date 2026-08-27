import { AppBridge, type McpUiHostContext } from '@modelcontextprotocol/ext-apps/app-bridge'
import type { JsonValue } from '@dotcraft/sdk/contracts'
import { Maximize2, Minimize2, Puzzle, ShieldCheck, TriangleAlert } from 'lucide-react'
import { memo, useCallback, useEffect, useRef, useState } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'
import { translate } from '../../../shared/locales'
import { Skeleton } from '../ui/Skeleton'
import { IconButton } from '../ui/IconButton'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'
import {
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  MCP_APP_SANDBOX_PROXY_URL
} from '../../../shared/mcpAppSandbox'
import {
  buildMcpAppDocument,
  SizeLimitedPostMessageTransport
} from './mcpAppSecurity'
import { openDesktopPluginUrl } from '../../plugins/desktopPluginOpenUrl'

const ACTION_TIMEOUT_MS = 120_000
const OPEN_TIMEOUT_MS = 15_000
const SANDBOX_READY_TIMEOUT_MS = 10_000
const INITIALIZE_TIMEOUT_MS = 15_000
const DATA_DELIVERY_TIMEOUT_MS = 15_000
const DEFAULT_HEIGHT = 420
const MIN_HEIGHT = 120
const MAX_HEIGHT = 720
const MIN_WIDTH = 240
const SIZE_UPDATE_INTERVAL_MS = 100
const FRAME_BORDER_WIDTH = 1

interface McpAppViewProps {
  item: ConversationItem
  threadId: string | null
  turnId: string
}

interface McpAppResource {
  uri: string
  mimeType: string
  html: string
  ui: {
    csp?: {
      connectDomains?: string[]
      resourceDomains?: string[]
      frameDomains?: string[]
      baseUriDomains?: string[]
    }
    prefersBorder?: boolean
    requestedDomain?: string
  }
}

interface McpAppOpenResult {
  viewHandle: string
  resource: McpAppResource
  toolInput: Record<string, unknown>
  toolResult: {
    content: unknown[]
    structuredContent?: Record<string, unknown>
    _meta?: Record<string, unknown>
    isError?: boolean
  }
}

type McpAppFailureCode =
  | 'sandbox_ready_timeout'
  | 'initialize_timeout'
  | 'data_delivery_timeout'
  | 'bridge_message_too_large'
  | 'sandbox_bridge_violation'
  | 'resource_bootstrap_failed'
  | 'data_delivery_failed'
  | 'request_teardown'
  | 'bridge_connect_failed'

function theme(): 'light' | 'dark' {
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
}

function positiveDimension(measured: number | undefined, fallback: number): number {
  return measured !== undefined && measured > 0 ? measured : fallback
}

function resolveInlineFrameWidth(
  requestedViewportWidth: number,
  availableWidth: number,
  bordered: boolean
): number {
  const frameChromeWidth = bordered ? FRAME_BORDER_WIDTH * 2 : 0
  return Math.min(
    availableWidth,
    Math.max(MIN_WIDTH, Math.ceil(requestedViewportWidth) + frameChromeWidth)
  )
}

type HostContainerDimensions =
  | { maxWidth: number; maxHeight: number }
  | { width: number; height: number }

function hostContext(locale: string, fullscreen: boolean, containerDimensions: HostContainerDimensions): McpUiHostContext {
  return {
    theme: theme(),
    locale,
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    platform: 'desktop' as const,
    displayMode: fullscreen ? 'fullscreen' as const : 'inline' as const,
    availableDisplayModes: ['inline', 'fullscreen'],
    containerDimensions,
    userAgent: 'DotCraft Desktop',
    deviceCapabilities: {
      touch: navigator.maxTouchPoints > 0,
      hover: window.matchMedia?.('(hover: hover)').matches ?? false
    }
  }
}

/**
 * Resolve a human-readable app attribution for the host header. The MCP Apps
 * spec treats the resource `name`/tool title as display identity and the sandbox
 * `domain` as a technical origin, not a label — so the domain never becomes the
 * title (it is surfaced only as the sandbox affordance tooltip). We use the tool
 * name (or plugin namespace) as the closest available identity, falling back to
 * the generic label. Returns null when no real identity is available.
 */
function resolveAppName(item: ConversationItem): string | null {
  const toolName = item.toolName?.trim()
  if (toolName && toolName !== 'tool') return toolName
  const namespace = item.pluginNamespace?.trim()
  return namespace ? namespace : null
}

function asToolResult(result: McpAppOpenResult['toolResult']): Record<string, unknown> {
  return {
    content: result.content ?? [],
    ...(result.structuredContent ? { structuredContent: result.structuredContent } : {}),
    ...(result._meta ? { _meta: result._meta } : {}),
    ...(result.isError ? { isError: true } : {})
  }
}

function McpAppViewImpl({ item, threadId, turnId }: McpAppViewProps): JSX.Element {
  const locale = useLocale()
  const surfaceRef = useRef<HTMLDivElement | null>(null)
  const viewContainerRef = useRef<HTMLDivElement | null>(null)
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const bridgeRef = useRef<AppBridge | null>(null)
  const handleRef = useRef<string | null>(null)
  const [openResult, setOpenResult] = useState<McpAppOpenResult | null>(null)
  const [status, setStatus] = useState<'opening' | 'ready' | 'failed' | 'stale'>('opening')
  const [error, setError] = useState('')
  const [height, setHeight] = useState(DEFAULT_HEIGHT)
  const [frameWidth, setFrameWidth] = useState<number | null>(null)
  const [fullscreen, setFullscreen] = useState(false)
  const fullscreenRef = useRef(fullscreen)
  const logTimes = useRef<number[]>([])
  const sizeUpdateAt = useRef(0)
  const pendingSizeRef = useRef<{ width?: number; height?: number }>({})
  const sizeUpdateTimerRef = useRef<number | null>(null)
  const hostContextUpdateAt = useRef(0)
  const hostContextTimerRef = useRef<number | null>(null)
  const phaseTimeoutRef = useRef<number | null>(null)
  const borderless = openResult?.resource.ui.prefersBorder === false

  fullscreenRef.current = fullscreen

  useEffect(() => {
    if (phaseTimeoutRef.current !== null) {
      window.clearTimeout(phaseTimeoutRef.current)
      phaseTimeoutRef.current = null
    }
    if (sizeUpdateTimerRef.current !== null) {
      window.clearTimeout(sizeUpdateTimerRef.current)
      sizeUpdateTimerRef.current = null
    }
    pendingSizeRef.current = {}
    sizeUpdateAt.current = 0
    const previousBridge = bridgeRef.current
    bridgeRef.current = null
    if (previousBridge) {
      void previousBridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => previousBridge.close())
    }
    const previousHandle = handleRef.current
    handleRef.current = null
    if (previousHandle) {
      void window.api.appServer.sendRequest('mcpApp/view/close', { viewHandle: previousHandle }).catch(() => {})
    }
    setOpenResult(null)
    setError('')
    setHeight(DEFAULT_HEIGHT)
    setFrameWidth(null)
    setFullscreen(false)
    if (!threadId || !item.mcpAppAvailable) {
      setStatus('stale')
      return
    }
    setStatus('opening')
    let cancelled = false
    void window.api.appServer.sendRequest('mcpApp/view/open', {
      threadId,
      turnId,
      itemId: item.id
    }, OPEN_TIMEOUT_MS).then((result) => {
      if (cancelled) {
        const handle = (result as McpAppOpenResult).viewHandle
        if (handle) void window.api.appServer.sendRequest('mcpApp/view/close', { viewHandle: handle }).catch(() => {})
        return
      }
      const opened = result as McpAppOpenResult
      handleRef.current = opened.viewHandle
      setOpenResult(opened)
    }).catch((reason: unknown) => {
      if (!cancelled) {
        setError(reason instanceof Error ? reason.message : String(reason))
        setStatus('failed')
      }
    })
    return () => {
      cancelled = true
    }
  }, [item.id, item.mcpAppAvailable, threadId, turnId])

  const closeView = useCallback(() => {
    const handle = handleRef.current
    handleRef.current = null
    if (handle) void window.api.appServer.sendRequest('mcpApp/view/close', { viewHandle: handle }).catch(() => {})
  }, [])

  const clearPhaseTimeout = useCallback(() => {
    if (phaseTimeoutRef.current === null) return
    window.clearTimeout(phaseTimeoutRef.current)
    phaseTimeoutRef.current = null
  }, [])

  const failView = useCallback((reason: string, code?: McpAppFailureCode) => {
    if (code) {
      console.warn('[MCP App] view failed', { code, viewHandle: handleRef.current })
    }
    clearPhaseTimeout()
    setError(reason)
    setStatus('failed')
    const bridge = bridgeRef.current
    bridgeRef.current = null
    if (bridge) {
      void bridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => bridge.close())
    }
    closeView()
  }, [clearPhaseTimeout, closeView])

  const armPhaseTimeout = useCallback((timeoutMs: number, code: McpAppFailureCode) => {
    clearPhaseTimeout()
    phaseTimeoutRef.current = window.setTimeout(() => failView('', code), timeoutMs)
  }, [clearPhaseTimeout, failView])

  const setIframeRef = useCallback((node: HTMLIFrameElement | null) => {
    if (iframeRef.current === node) return
    iframeRef.current = node
    const previousBridge = bridgeRef.current
    bridgeRef.current = null
    if (previousBridge) {
      void previousBridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => previousBridge.close())
    }
  }, [])

  const currentContainerDimensions = useCallback((): HostContainerDimensions => {
    if (fullscreenRef.current) {
      const container = viewContainerRef.current
      return {
        width: Math.max(1, Math.round(positiveDimension(container?.clientWidth, window.innerWidth))),
        height: Math.max(1, Math.round(positiveDimension(container?.clientHeight, window.innerHeight)))
      }
    }
    return {
      maxWidth: Math.max(1, Math.round(positiveDimension(surfaceRef.current?.clientWidth, window.innerWidth))),
      maxHeight: MAX_HEIGHT
    }
  }, [])

  const makeHostContext = useCallback(() => {
    return hostContext(locale, fullscreenRef.current, currentContainerDimensions())
  }, [currentContainerDimensions, locale])

  const applyPendingSize = useCallback(() => {
    sizeUpdateTimerRef.current = null
    sizeUpdateAt.current = Date.now()
    if (fullscreenRef.current) {
      pendingSizeRef.current = {}
      return
    }
    const requested = pendingSizeRef.current
    pendingSizeRef.current = {}
    if (requested.width !== undefined) {
      const availableWidth = positiveDimension(surfaceRef.current?.clientWidth, window.innerWidth)
      setFrameWidth(resolveInlineFrameWidth(requested.width, availableWidth, !borderless))
    }
    if (requested.height !== undefined) {
      setHeight(Math.max(MIN_HEIGHT, Math.min(MAX_HEIGHT, Math.ceil(requested.height))))
    }
  }, [borderless])

  const handleSizeChange = useCallback(({ width: requestedWidth, height: requestedHeight }: { width?: number; height?: number }) => {
    if (fullscreenRef.current) return
    if (typeof requestedWidth === 'number' && Number.isFinite(requestedWidth) && requestedWidth > 0) {
      pendingSizeRef.current.width = requestedWidth
    }
    if (typeof requestedHeight === 'number' && Number.isFinite(requestedHeight) && requestedHeight > 0) {
      pendingSizeRef.current.height = requestedHeight
    }
    if (pendingSizeRef.current.width === undefined && pendingSizeRef.current.height === undefined) return

    const remaining = SIZE_UPDATE_INTERVAL_MS - (Date.now() - sizeUpdateAt.current)
    if (remaining <= 0) {
      if (sizeUpdateTimerRef.current !== null) window.clearTimeout(sizeUpdateTimerRef.current)
      applyPendingSize()
      return
    }
    if (sizeUpdateTimerRef.current === null) {
      sizeUpdateTimerRef.current = window.setTimeout(applyPendingSize, remaining)
    }
  }, [applyPendingSize])

  useEffect(() => () => {
    clearPhaseTimeout()
    if (sizeUpdateTimerRef.current !== null) window.clearTimeout(sizeUpdateTimerRef.current)
    if (hostContextTimerRef.current !== null) window.clearTimeout(hostContextTimerRef.current)
    const bridge = bridgeRef.current
    bridgeRef.current = null
    if (bridge) {
      void bridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => bridge.close())
    }
    closeView()
  }, [clearPhaseTimeout, closeView])

  useEffect(() => window.api.appServer.onNotification((notification) => {
    if (notification.method !== 'mcpApp/view/status/updated') return
    const params = notification.params as { viewHandle?: string; fallbackText?: string } | undefined
    if (!params || params.viewHandle !== handleRef.current) return
    failView(params.fallbackText ?? '')
  }), [failView])

  const connectBridge = useCallback(async () => {
    const iframe = iframeRef.current
    const opened = openResult
    if (!iframe?.contentWindow || !opened || bridgeRef.current) return
    const handle = opened.viewHandle
    const bridge = new AppBridge(
      null,
      { name: 'DotCraft Desktop', version: '1' },
      {
        openLinks: {},
        serverTools: {},
        serverResources: {},
        logging: {},
        updateModelContext: { text: {}, image: {}, structuredContent: {} },
        message: { text: {} },
        sandbox: { permissions: {} }
      },
      { hostContext: makeHostContext() }
    )
    bridgeRef.current = bridge

    bridge.oncalltool = async ({ name, arguments: args }) => {
      return await window.api.appServer.sendRequest('mcpApp/view/tool/call', {
        viewHandle: handle,
        tool: name,
        arguments: (args ?? {}) as JsonValue
      }, ACTION_TIMEOUT_MS) as never
    }
    ;(bridge as AppBridge & { onlisttools?: (params: Record<string, never>) => Promise<unknown> }).onlisttools = async () => {
      return await window.api.appServer.sendRequest('mcpApp/view/tools/list', { viewHandle: handle })
    }
    bridge.onreadresource = async ({ uri }) => {
      return await window.api.appServer.sendRequest('mcpApp/view/resource/read', {
        viewHandle: handle,
        uri
      }, ACTION_TIMEOUT_MS) as never
    }
    bridge.onlistresources = async () => ({ resources: [] })
    bridge.onlistresourcetemplates = async () => ({ resourceTemplates: [] })
    bridge.onmessage = async ({ role, content }) => {
      const textBlocks = content.filter((block) => block.type === 'text')
      if (role !== 'user' || content.length !== 1 || textBlocks.length !== 1) return { isError: true }
      await window.api.appServer.sendRequest('mcpApp/view/message', {
        viewHandle: handle,
        role,
        content: textBlocks[0]
      })
      return {}
    }
    bridge.onupdatemodelcontext = async ({ content, structuredContent }) => {
      await window.api.appServer.sendRequest('mcpApp/view/modelContext/update', {
        viewHandle: handle,
        content: (content ?? []) as JsonValue,
        structuredContent: structuredContent as JsonValue | undefined
      })
      return {}
    }
    bridge.onopenlink = async ({ url }) => {
      const scheme = new URL(url).protocol.toLowerCase()
      if (scheme !== 'https:' && scheme !== 'http:' && scheme !== 'mailto:') {
        if (openDesktopPluginUrl(url)) return {}
        throw new Error('The MCP App link scheme is not allowed.')
      }
      const validated = await window.api.appServer.sendRequest('mcpApp/view/openLink', { viewHandle: handle, url }) as { url: string }
      await window.api.shell.openExternal(validated.url)
      return {}
    }
    bridge.onrequestdisplaymode = async ({ mode }) => {
      const granted = mode === 'fullscreen' ? 'fullscreen' : 'inline'
      setFullscreen(granted === 'fullscreen')
      return { mode: granted }
    }
    bridge.onrequestteardown = () => failView('', 'request_teardown')
    bridge.onsizechange = handleSizeChange
    bridge.onloggingmessage = ({ level, logger, data }) => {
      const now = Date.now()
      logTimes.current = logTimes.current.filter((sample) => now - sample < 60_000)
      if (logTimes.current.length >= 60) return
      logTimes.current.push(now)
      const safeData = JSON.stringify(data)?.slice(0, 8 * 1024)
      console.debug(`[MCP App:${level}] ${String(logger ?? '').slice(0, 128)}`, safeData)
    }
    let sandboxReady = false
    ;(bridge as AppBridge & { onsandboxready?: () => void }).onsandboxready = () => {
      if (sandboxReady) return
      sandboxReady = true
      clearPhaseTimeout()
      armPhaseTimeout(INITIALIZE_TIMEOUT_MS, 'initialize_timeout')
      void bridge.sendSandboxResourceReady({
        html: buildMcpAppDocument(opened.resource.html, opened.resource.ui.csp),
        sandbox: 'allow-scripts',
        csp: opened.resource.ui.csp,
        permissions: {}
      }).catch((reason) => failView(reason instanceof Error ? reason.message : String(reason), 'resource_bootstrap_failed'))
    }
    bridge.oninitialized = () => {
      clearPhaseTimeout()
      armPhaseTimeout(DATA_DELIVERY_TIMEOUT_MS, 'data_delivery_timeout')
      void bridge.sendToolInput({ arguments: opened.toolInput })
        .then(() => bridge.sendToolResult(asToolResult(opened.toolResult) as never))
        .then(() => {
          clearPhaseTimeout()
          setStatus('ready')
        })
        .catch((reason) => failView(reason instanceof Error ? reason.message : String(reason), 'data_delivery_failed'))
    }

    try {
      const transport = new SizeLimitedPostMessageTransport(
        iframe.contentWindow,
        iframe.contentWindow,
        () => failView('', 'bridge_message_too_large')
      )
      await bridge.connect(transport)
      armPhaseTimeout(SANDBOX_READY_TIMEOUT_MS, 'sandbox_ready_timeout')
      iframe.src = MCP_APP_SANDBOX_PROXY_URL
    } catch (reason) {
      failView(reason instanceof Error ? reason.message : String(reason), 'bridge_connect_failed')
    }
  }, [armPhaseTimeout, clearPhaseTimeout, failView, handleSizeChange, makeHostContext, openResult])

  useEffect(() => {
    if (status !== 'opening' || !openResult) return
    void connectBridge()
  }, [connectBridge, openResult, status])

  useEffect(() => {
    if (status !== 'opening' && status !== 'ready') return
    const onBridgeViolation = (event: MessageEvent): void => {
      if (event.source !== iframeRef.current?.contentWindow) return
      const message = event.data as { method?: unknown } | null
      if (message?.method === MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD) {
        failView('', 'sandbox_bridge_violation')
      }
    }
    window.addEventListener('message', onBridgeViolation, true)
    return () => window.removeEventListener('message', onBridgeViolation, true)
  }, [failView, status])

  useEffect(() => {
    const update = (): void => {
      hostContextTimerRef.current = null
      hostContextUpdateAt.current = Date.now()
      bridgeRef.current?.setHostContext(makeHostContext())
    }
    const scheduleUpdate = (): void => {
      const remaining = SIZE_UPDATE_INTERVAL_MS - (Date.now() - hostContextUpdateAt.current)
      if (remaining <= 0) {
        if (hostContextTimerRef.current !== null) window.clearTimeout(hostContextTimerRef.current)
        update()
        return
      }
      if (hostContextTimerRef.current === null) {
        hostContextTimerRef.current = window.setTimeout(update, remaining)
      }
    }

    scheduleUpdate()
    const observer = new ResizeObserver(scheduleUpdate)
    if (surfaceRef.current) observer.observe(surfaceRef.current)
    if (viewContainerRef.current) observer.observe(viewContainerRef.current)
    window.addEventListener(THEME_CHANGED_EVENT, scheduleUpdate)
    return () => {
      observer.disconnect()
      window.removeEventListener(THEME_CHANGED_EVENT, scheduleUpdate)
      if (hostContextTimerRef.current !== null) {
        window.clearTimeout(hostContextTimerRef.current)
        hostContextTimerRef.current = null
      }
    }
  }, [fullscreen, makeHostContext, openResult])

  useEffect(() => {
    if (!fullscreen) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setFullscreen(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [fullscreen])

  const body = (() => {
    if (status === 'stale') {
      return <Fallback icon={<Puzzle size={18} />} title={translate(locale, 'mcpApp.unavailable')} detail={item.result} />
    }
    if (status === 'failed') {
      return <Fallback icon={<TriangleAlert size={18} />} title={translate(locale, 'mcpApp.failed')} detail={error || item.result} />
    }
    if (!openResult) {
      return (
        <div style={{ position: 'relative', width: '100%', height: fullscreen ? '100%' : height, minHeight: MIN_HEIGHT, flex: fullscreen ? 1 : undefined }}>
          <LoadingSkeleton label={translate(locale, 'mcpApp.loading')} />
        </div>
      )
    }
    return (
      <div
        ref={viewContainerRef}
        style={{
          position: 'relative',
          width: '100%',
          height: fullscreen ? 'auto' : height,
          minHeight: MIN_HEIGHT,
          flex: fullscreen ? 1 : undefined,
          minWidth: 0
        }}
      >
        {status !== 'ready' && (
          <div style={{ position: 'absolute', inset: 0 }}>
            <LoadingSkeleton label={translate(locale, 'mcpApp.loading')} />
          </div>
        )}
        <iframe
          ref={setIframeRef}
          title={translate(locale, 'mcpApp.title')}
          src="about:blank"
          sandbox="allow-scripts"
          referrerPolicy="no-referrer"
          allow=""
          style={{ width: '100%', height: '100%', border: 0, background: 'transparent', opacity: status === 'ready' ? 1 : 0 }}
        />
      </div>
    )
  })()

  const appName = resolveAppName(item)
  const sandboxDomain = openResult?.resource.ui.requestedDomain?.trim()
  const sandboxTooltip = sandboxDomain
    ? translate(locale, 'mcpApp.sandboxedFrom', { domain: sandboxDomain })
    : translate(locale, 'mcpApp.sandboxedTooltip')

  return (
    <div
      ref={surfaceRef}
      data-mcp-app-display-mode={fullscreen ? 'fullscreen' : 'inline'}
      style={fullscreen
        ? {
            position: 'fixed',
            inset: 0,
            zIndex: 10000,
            padding: 24,
            boxSizing: 'border-box',
            background: 'color-mix(in srgb, var(--bg-primary) 92%, transparent)',
            display: 'flex'
          }
        : { width: '100%' }}
    >
      <div
        data-mcp-app-frame={borderless ? 'borderless' : 'bordered'}
        style={{
          border: borderless ? 'none' : `${FRAME_BORDER_WIDTH}px solid var(--border-default)`,
          borderRadius: borderless ? 0 : 8,
          overflow: 'hidden',
          background: borderless ? 'transparent' : 'var(--bg-secondary)',
          display: 'flex',
          flexDirection: 'column',
          width: fullscreen ? '100%' : frameWidth === null ? '100%' : `${frameWidth}px`,
          maxWidth: '100%',
          height: fullscreen ? '100%' : undefined,
          minWidth: 0
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, padding: '6px 8px 6px 10px', color: 'var(--text-secondary)', fontSize: 12, flexShrink: 0 }}>
          <span style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
            <span
              aria-hidden
              style={{ width: 18, height: 18, borderRadius: 5, background: 'var(--bg-tertiary)', display: 'grid', placeItems: 'center', color: 'var(--text-secondary)', flexShrink: 0 }}
            >
              <Puzzle size={12} />
            </span>
            <span style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {appName
                ? <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{appName}</span>
                : translate(locale, 'mcpApp.title')}
            </span>
          </span>
          {status !== 'stale' && (
            <span style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
              <span
                title={sandboxTooltip}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 3, color: 'var(--text-dimmed)', fontSize: 11, padding: '1px 6px 1px 5px', borderRadius: 999, border: '1px solid var(--border-default)', cursor: 'default' }}
              >
                <ShieldCheck size={12} />
                {translate(locale, 'mcpApp.sandboxed')}
              </span>
              <IconButton
                size={24}
                label={fullscreen ? translate(locale, 'mcpApp.exitFullscreen') : translate(locale, 'mcpApp.fullscreen')}
                tooltipLabel={fullscreen ? translate(locale, 'mcpApp.exitFullscreen') : translate(locale, 'mcpApp.fullscreen')}
                tooltipPlacement="top"
                onClick={() => setFullscreen((value) => !value)}
                style={{ color: 'inherit', borderRadius: 6 }}
                icon={fullscreen ? <Minimize2 size={15} /> : <Maximize2 size={15} />}
              />
            </span>
          )}
        </div>
        {body}
      </div>
    </div>
  )
}

/**
 * Body-filling skeleton for the MCP App host frame. The shape (a heading row, a
 * large content block, and a control row) mirrors a typical app view so the
 * loading placeholder matches the content that arrives, per DESIGN "Loading &
 * Progress". Marked role=status/aria-busy so the removed text label is still
 * announced.
 */
function LoadingSkeleton({ label }: { label: string }): JSX.Element {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={label}
      style={{ position: 'absolute', inset: 0, padding: 14, display: 'flex', flexDirection: 'column', gap: 12 }}
    >
      <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
        <Skeleton width={120} height={14} />
        <Skeleton width={60} height={14} style={{ marginLeft: 'auto' }} />
      </div>
      <Skeleton width="100%" height="auto" radius={6} style={{ flex: 1 }} />
      <div style={{ display: 'flex', gap: 12 }}>
        <Skeleton width={80} height={26} radius={6} />
        <Skeleton width={80} height={26} radius={6} />
      </div>
    </div>
  )
}

function Fallback({ icon, title, detail }: { icon: JSX.Element; title: string; detail?: string }): JSX.Element {
  return (
    <div style={{ display: 'flex', gap: 10, padding: 14, color: 'var(--text-secondary)' }}>
      {icon}
      <div>
        <div style={{ fontWeight: 600 }}>{title}</div>
        {detail && <pre style={{ whiteSpace: 'pre-wrap', margin: '8px 0 0', font: 'inherit', color: 'var(--text-dimmed)' }}>{detail}</pre>}
      </div>
    </div>
  )
}

export function hasAvailableMcpApp(item: ConversationItem): boolean {
  return item.type === 'mcpToolCall' && item.status === 'completed' && item.mcpAppAvailable === true
}

export const McpAppView = memo(McpAppViewImpl)
