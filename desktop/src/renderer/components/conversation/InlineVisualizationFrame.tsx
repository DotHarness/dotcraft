import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { INLINE_VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD, MCP_APP_SANDBOX_PROXY_URL } from '../../../shared/mcpAppSandbox'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'
import { Skeleton } from '../ui/Skeleton'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { buildInlineVisualizationDocument, INLINE_VISUALIZATION_MAX_HEIGHT, INLINE_VISUALIZATION_MIN_HEIGHT } from './inlineVisualizationSecurity'

interface Props { threadId: string; turnId: string; itemId: string; file: string }
interface OpenResult { viewHandle: string; fragment: string; mimeType: string }
export function InlineVisualizationFrame({ threadId, turnId, itemId, file }: Props): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const confirm = useConfirmDialog()
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)
  const handleRef = useRef<string | null>(null)
  const fragmentRef = useRef<string | null>(null)
  const bridgeEventTimesRef = useRef<number[]>([])
  const confirmationPendingRef = useRef(false)
  const viewIdRef = useRef(crypto.randomUUID())
  const [status, setStatus] = useState<'opening' | 'ready' | 'failed'>('opening')
  const [height, setHeight] = useState(260)
  const [overflowed, setOverflowed] = useState(false)

  useEffect(() => {
    let cancelled = false
    setStatus('opening')
    void window.api.appServer.sendRequest('visualization/view/open', { threadId, turnId, itemId, file }, 15_000)
      .then(result => {
        const opened = result as OpenResult
        if (cancelled) {
          void window.api.appServer.sendRequest('visualization/view/close', { viewHandle: opened.viewHandle }).catch(() => {})
          return
        }
        handleRef.current = opened.viewHandle
        fragmentRef.current = opened.fragment
        if (iframeRef.current) iframeRef.current.src = MCP_APP_SANDBOX_PROXY_URL
      })
      .catch(() => { if (!cancelled) setStatus('failed') })
    return () => {
      cancelled = true
      const handle = handleRef.current
      handleRef.current = null
      if (handle) void window.api.appServer.sendRequest('visualization/view/close', { viewHandle: handle }).catch(() => {})
    }
  }, [file, itemId, threadId, turnId])

  useEffect(() => {
    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframeRef.current?.contentWindow) return
      const message = event.data as { method?: string; params?: Record<string, unknown> } | null
      if (message?.method?.startsWith('visualization/')) {
        const now = Date.now()
        const recent = bridgeEventTimesRef.current.filter(sample => now - sample < 60_000)
        if (recent.length >= 60) return
        recent.push(now)
        bridgeEventTimesRef.current = recent
      }
      if (message?.method === MCP_APP_SANDBOX_PROXY_READY_METHOD && fragmentRef.current != null) {
        const theme = document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
        iframeRef.current.contentWindow?.postMessage({
          method: INLINE_VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD,
          params: { html: buildInlineVisualizationDocument(fragmentRef.current, theme, locale, viewIdRef.current) }
        }, '*')
      } else if (message?.params?.viewId !== viewIdRef.current) {
        return
      } else if (message?.method === 'visualization/ready') {
        setStatus('ready')
      } else if (message?.method === 'visualization/resize') {
        const requested = Number(message.params?.height)
        if (Number.isFinite(requested) && requested > 0) {
          const needsOverflow = requested > INLINE_VISUALIZATION_MAX_HEIGHT
          setOverflowed(needsOverflow)
          setHeight(Math.max(INLINE_VISUALIZATION_MIN_HEIGHT, Math.min(INLINE_VISUALIZATION_MAX_HEIGHT, Math.ceil(requested))))
          iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/overflow', params: { enabled: needsOverflow, viewId: viewIdRef.current } }, '*')
        }
      } else if (message?.method === 'visualization/followUp') {
        const id = typeof message.params?.id === 'string' ? message.params.id : ''
        const prompt = typeof message.params?.prompt === 'string' ? message.params.prompt : ''
        const title = typeof message.params?.title === 'string' ? message.params.title : t('inlineVisualization.followUpTitle')
        if (!id || id.length > 128 || !prompt || new TextEncoder().encode(prompt).byteLength > 16 * 1024 || title.length > 250 || confirmationPendingRef.current) {
          iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/followUpResult', params: { id, ok: false, error: t('inlineVisualization.cancelled'), viewId: viewIdRef.current } }, '*')
          return
        }
        confirmationPendingRef.current = true
        void confirm({
          title: t('inlineVisualization.followUpTitle'),
          message: `${title}\n\n${prompt}`,
          confirmLabel: t('inlineVisualization.send'),
          cancelLabel: t('common.cancel')
        }).then(accepted => {
          confirmationPendingRef.current = false
          if (!accepted) {
            iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/followUpResult', params: { id, ok: false, error: t('inlineVisualization.cancelled'), viewId: viewIdRef.current } }, '*')
            return
          }
          const handle = handleRef.current
          if (!handle) {
            iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/followUpResult', params: { id, ok: false, error: t('inlineVisualization.unavailable'), viewId: viewIdRef.current } }, '*')
            return
          }
          void window.api.appServer.sendRequest('visualization/view/message', { viewHandle: handle, prompt })
            .then(result => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/followUpResult', params: { id, ok: true, result, viewId: viewIdRef.current } }, '*'))
            .catch(error => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/followUpResult', params: { id, ok: false, error: String(error), viewId: viewIdRef.current } }, '*'))
        })
      }
    }
    window.addEventListener('message', onMessage)
    const updateTheme = (): void => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/theme', params: { theme: document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark', viewId: viewIdRef.current } }, '*')
    window.addEventListener(THEME_CHANGED_EVENT, updateTheme)
    return () => { window.removeEventListener('message', onMessage); window.removeEventListener(THEME_CHANGED_EVENT, updateTheme) }
  }, [confirm, locale, t])

  useEffect(() => {
    const sendContext = (): void => {
      const container = containerRef.current
      const frame = iframeRef.current
      if (!container || !frame?.contentWindow) return
      frame.contentWindow.postMessage({
        method: 'visualization/context',
        params: {
          locale,
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
          accent: getComputedStyle(document.documentElement).getPropertyValue('--accent-color').trim() || undefined,
          reducedMotion: window.matchMedia('(prefers-reduced-motion: reduce)').matches,
          pointer: window.matchMedia('(pointer: coarse)').matches ? 'coarse' : 'fine',
          width: container.clientWidth,
          viewId: viewIdRef.current
        }
      }, '*')
    }
    const observer = new ResizeObserver(sendContext)
    if (containerRef.current) observer.observe(containerRef.current)
    sendContext()
    return () => observer.disconnect()
  }, [locale, status])

  if (status === 'failed') return <div role="status" style={fallbackStyle}>{t('inlineVisualization.unavailable')}</div>
  return (
    <div ref={containerRef} style={containerStyle} aria-busy={status !== 'ready'}>
      {status !== 'ready' && <div role="status" aria-live="polite"><span style={visuallyHiddenStyle}>{t('inlineVisualization.loading')}</span><Skeleton width="100%" height={180} radius={8} /></div>}
      <iframe ref={iframeRef} title={t('inlineVisualization.frameTitle', { file })} sandbox="allow-scripts" referrerPolicy="no-referrer" allow="" style={{ ...iframeStyle, height, display: status === 'ready' ? 'block' : 'none' }} />
      {overflowed && <div role="status" style={overflowHintStyle}>{t('inlineVisualization.scrollHint')}</div>}
    </div>
  )
}

const containerStyle: CSSProperties = { width: '100%', minWidth: 0, margin: '8px 0' }
const iframeStyle: CSSProperties = { width: '100%', border: 0, background: 'transparent', overflow: 'hidden' }
const fallbackStyle: CSSProperties = { margin: '8px 0', padding: '10px 12px', borderRadius: '8px', background: 'var(--bg-secondary)', color: 'var(--text-secondary)', fontSize: '12px' }
const overflowHintStyle: CSSProperties = { marginTop: '4px', color: 'var(--text-secondary)', fontSize: '11px' }
const visuallyHiddenStyle: CSSProperties = { position: 'absolute', width: 1, height: 1, padding: 0, margin: -1, overflow: 'hidden', clip: 'rect(0,0,0,0)', whiteSpace: 'nowrap', border: 0 }
