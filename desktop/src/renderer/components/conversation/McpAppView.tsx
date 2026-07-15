import { AppBridge } from '@modelcontextprotocol/ext-apps/app-bridge'
import { Maximize2, Minimize2, Puzzle, TriangleAlert } from 'lucide-react'
import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useLocale } from '../../contexts/LocaleContext'
import type { ConversationItem } from '../../types/conversation'
import { translate } from '../../../shared/locales'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'
import {
  buildMcpAppDocument,
  MCP_APP_MAX_BRIDGE_MESSAGE_BYTES,
  SizeLimitedPostMessageTransport
} from './mcpAppSecurity'

const ACTION_TIMEOUT_MS = 120_000
const DEFAULT_HEIGHT = 420
const MAX_HEIGHT = 720

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

const SANDBOX_PROXY_HTML = `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; frame-src 'self' data: blob:"></head><body style="margin:0;background:transparent"><script>
(() => {
  let inner = null;
  const maxBytes = ${MCP_APP_MAX_BRIDGE_MESSAGE_BYTES};
  const withinLimit = (message) => {
    try {
      const json = JSON.stringify(message);
      return json !== undefined && new TextEncoder().encode(json).byteLength <= maxBytes;
    }
    catch { return false; }
  };
  const violate = () => {
    if (inner) inner.remove();
    inner = null;
    window.parent.postMessage({ jsonrpc: '2.0', method: 'ui/notifications/sandbox-bridge-violation', params: {} }, '*');
  };
  const forward = (message) => {
    if (!withinLimit(message)) { violate(); return; }
    if (inner && inner.contentWindow) inner.contentWindow.postMessage(message, '*');
  };
  window.addEventListener('message', (event) => {
    if (event.source === window.parent) {
      const message = event.data;
      if (message && message.method === 'ui/notifications/sandbox-resource-ready') {
        const params = message.params || {};
        inner = document.createElement('iframe');
        inner.setAttribute('sandbox', 'allow-scripts');
        inner.setAttribute('referrerpolicy', 'no-referrer');
        inner.style.cssText = 'display:block;width:100%;height:100vh;border:0;background:transparent';
        inner.srcdoc = String(params.html || '');
        document.body.replaceChildren(inner);
        return;
      }
      forward(message);
      return;
    }
    if (inner && event.source === inner.contentWindow) {
      if (!withinLimit(event.data)) { violate(); return; }
      window.parent.postMessage(event.data, '*');
    }
  });
  window.parent.postMessage({ jsonrpc: '2.0', method: 'ui/notifications/sandbox-proxy-ready', params: {} }, '*');
})();
</script></body></html>`

function theme(): 'light' | 'dark' {
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
}

function hostContext(locale: string, fullscreen: boolean, height: number) {
  return {
    theme: theme(),
    locale,
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    platform: 'desktop' as const,
    displayMode: fullscreen ? 'fullscreen' as const : 'inline' as const,
    availableDisplayModes: ['inline', 'fullscreen'] as const,
    containerDimensions: { maxWidth: window.innerWidth, height },
    userAgent: 'DotCraft Desktop',
    deviceCapabilities: {
      touch: navigator.maxTouchPoints > 0,
      hover: window.matchMedia?.('(hover: hover)').matches ?? false
    }
  }
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
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const bridgeRef = useRef<AppBridge | null>(null)
  const handleRef = useRef<string | null>(null)
  const [openResult, setOpenResult] = useState<McpAppOpenResult | null>(null)
  const [status, setStatus] = useState<'opening' | 'ready' | 'failed' | 'stale'>('opening')
  const [error, setError] = useState('')
  const [height, setHeight] = useState(DEFAULT_HEIGHT)
  const [fullscreen, setFullscreen] = useState(false)
  const logTimes = useRef<number[]>([])
  const sizeUpdateAt = useRef(0)

  useEffect(() => {
    if (!threadId || !item.mcpAppAvailable) {
      setStatus('stale')
      return
    }
    let cancelled = false
    void window.api.appServer.sendRequest('mcpApp/view/open', {
      threadId,
      turnId,
      itemId: item.id
    }, ACTION_TIMEOUT_MS).then((result) => {
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

  const failView = useCallback((reason: string) => {
    setError(reason)
    setStatus('failed')
    const bridge = bridgeRef.current
    bridgeRef.current = null
    if (bridge) {
      void bridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => bridge.close())
    }
    closeView()
  }, [closeView])

  const setIframeRef = useCallback((node: HTMLIFrameElement | null) => {
    if (iframeRef.current === node) return
    iframeRef.current = node
    const previousBridge = bridgeRef.current
    bridgeRef.current = null
    if (previousBridge) {
      void previousBridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => previousBridge.close())
    }
  }, [])

  useEffect(() => () => {
    const bridge = bridgeRef.current
    bridgeRef.current = null
    if (bridge) {
      void bridge.teardownResource({}, { timeout: 1_000 }).catch(() => {}).finally(() => bridge.close())
    }
    closeView()
  }, [closeView])

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
      { hostContext: hostContext(locale, fullscreen, height) }
    )
    bridgeRef.current = bridge

    bridge.oncalltool = async ({ name, arguments: args }) => {
      return await window.api.appServer.sendRequest('mcpApp/view/tool/call', {
        viewHandle: handle,
        tool: name,
        arguments: args ?? {}
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
        content: content ?? [],
        structuredContent
      })
      return {}
    }
    bridge.onopenlink = async ({ url }) => {
      const validated = await window.api.appServer.sendRequest('mcpApp/view/openLink', { viewHandle: handle, url }) as { url: string }
      await window.api.shell.openExternal(validated.url)
      return {}
    }
    bridge.onrequestdisplaymode = async ({ mode }) => {
      const granted = mode === 'fullscreen' ? 'fullscreen' : 'inline'
      setFullscreen(granted === 'fullscreen')
      return { mode: granted }
    }
    bridge.onrequestteardown = () => closeView()
    bridge.onsizechange = ({ height: requestedHeight }) => {
      const now = Date.now()
      if (now - sizeUpdateAt.current < 100 || typeof requestedHeight !== 'number') return
      sizeUpdateAt.current = now
      setHeight(Math.max(120, Math.min(MAX_HEIGHT, Math.ceil(requestedHeight))))
    }
    bridge.onloggingmessage = ({ level, logger, data }) => {
      const now = Date.now()
      logTimes.current = logTimes.current.filter((sample) => now - sample < 60_000)
      if (logTimes.current.length >= 60) return
      logTimes.current.push(now)
      const safeData = JSON.stringify(data)?.slice(0, 8 * 1024)
      console.debug(`[MCP App:${level}] ${String(logger ?? '').slice(0, 128)}`, safeData)
    }
    bridge.oninitialized = () => {
      void bridge.sendToolInput({ arguments: opened.toolInput })
        .then(() => bridge.sendToolResult(asToolResult(opened.toolResult) as never))
        .then(() => setStatus('ready'))
        .catch((reason) => {
          setError(reason instanceof Error ? reason.message : String(reason))
          setStatus('failed')
        })
    }

    try {
      const transport = new SizeLimitedPostMessageTransport(
        iframe.contentWindow,
        iframe.contentWindow,
        () => failView('')
      )
      await bridge.connect(transport)
      await bridge.sendSandboxResourceReady({
        html: buildMcpAppDocument(opened.resource.html, opened.resource.ui.csp),
        sandbox: 'allow-scripts',
        csp: opened.resource.ui.csp,
        permissions: {}
      })
    } catch (reason) {
      failView(reason instanceof Error ? reason.message : String(reason))
    }
  }, [failView, fullscreen, height, locale, openResult])

  useEffect(() => {
    const update = (): void => {
      bridgeRef.current?.setHostContext(hostContext(locale, fullscreen, height))
    }
    update()
    window.addEventListener(THEME_CHANGED_EVENT, update)
    window.addEventListener('resize', update)
    return () => {
      window.removeEventListener(THEME_CHANGED_EVENT, update)
      window.removeEventListener('resize', update)
    }
  }, [fullscreen, height, locale])

  useEffect(() => {
    if (!fullscreen) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setFullscreen(false)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [fullscreen])

  const body = useMemo(() => {
    if (status === 'stale') {
      return <Fallback icon={<Puzzle size={18} />} title={translate(locale, 'mcpApp.historyUnavailable')} detail={item.result} />
    }
    if (status === 'failed') {
      return <Fallback icon={<TriangleAlert size={18} />} title={translate(locale, 'mcpApp.failed')} detail={error || item.result} />
    }
    return (
      <div style={{ position: 'relative', width: '100%', height, minHeight: 120 }}>
        {status !== 'ready' && (
          <div style={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center', color: 'var(--text-dimmed)', fontSize: 12 }}>
            {translate(locale, 'mcpApp.loading')}
          </div>
        )}
        <iframe
          ref={setIframeRef}
          title={translate(locale, 'mcpApp.title')}
          srcDoc={SANDBOX_PROXY_HTML}
          sandbox="allow-scripts"
          referrerPolicy="no-referrer"
          allow=""
          onLoad={() => void connectBridge()}
          style={{ width: '100%', height: '100%', border: 0, background: 'transparent', opacity: status === 'ready' ? 1 : 0 }}
        />
      </div>
    )
  }, [connectBridge, error, height, item.result, locale, status])

  const card = (
    <div style={{ border: openResult?.resource.ui.prefersBorder === false ? 'none' : '1px solid var(--border-default)', borderRadius: 8, overflow: 'hidden', background: 'var(--bg-secondary)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '6px 8px', color: 'var(--text-secondary)', fontSize: 12 }}>
        <span>{translate(locale, 'mcpApp.title')}</span>
        {status !== 'stale' && (
          <button
            type="button"
            aria-label={fullscreen ? translate(locale, 'mcpApp.exitFullscreen') : translate(locale, 'mcpApp.fullscreen')}
            onClick={() => setFullscreen((value) => !value)}
            style={{ border: 0, background: 'transparent', color: 'inherit', cursor: 'pointer', padding: 4 }}
          >
            {fullscreen ? <Minimize2 size={15} /> : <Maximize2 size={15} />}
          </button>
        )}
      </div>
      {body}
    </div>
  )

  if (!fullscreen) return card
  return createPortal(
    <div style={{ position: 'fixed', inset: 0, zIndex: 10000, padding: 24, background: 'color-mix(in srgb, var(--bg-primary) 92%, transparent)', display: 'grid', placeItems: 'stretch' }}>
      {card}
    </div>,
    document.body
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

export function hasLiveMcpApp(item: ConversationItem): boolean {
  return item.type === 'mcpToolCall' && item.status === 'completed' && item.mcpAppAvailable === true
}

export const McpAppView = memo(McpAppViewImpl)
