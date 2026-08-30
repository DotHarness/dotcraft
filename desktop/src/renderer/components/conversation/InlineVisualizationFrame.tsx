import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Copy } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { INLINE_VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD, MCP_APP_SANDBOX_PROXY_URL } from '../../../shared/mcpAppSandbox'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'
import { addToast } from '../../stores/toastStore'
import { Skeleton } from '../ui/Skeleton'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { buildInlineVisualizationDocument, INLINE_VISUALIZATION_MAX_HEIGHT, INLINE_VISUALIZATION_MIN_HEIGHT, type InlineVisualizationThemeTokens } from './inlineVisualizationSecurity'

interface Props { threadId: string; turnId: string; itemId: string; file: string }
interface OpenResult { viewHandle: string; fragment: string; mimeType: string }
const INLINE_VISUALIZATION_ACTION_RAIL_WIDTH = 32
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
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'failed'>('idle')
  const [loadRequested, setLoadRequested] = useState(false)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [height, setHeight] = useState(260)
  const [overflowed, setOverflowed] = useState(false)
  const [actionsVisible, setActionsVisible] = useState(false)
  const [copying, setCopying] = useState(false)
  const coarsePointer = typeof window !== 'undefined' && window.matchMedia?.('(pointer: coarse)').matches === true

  useEffect(() => {
    if (loadRequested) return
    const container = containerRef.current
    if (!container) return

    const requestLoad = (): void => {
      setStatus('loading')
      setLoadRequested(true)
    }
    if (typeof IntersectionObserver === 'undefined') {
      requestLoad()
      return
    }

    const root = container.closest<HTMLElement>('[data-testid="message-stream"]')
    const observer = new IntersectionObserver(entries => {
      if (!entries.some(entry => entry.isIntersecting)) return
      observer.disconnect()
      requestLoad()
    }, { root, rootMargin: '320px 0px', threshold: 0.01 })
    observer.observe(container)
    return () => observer.disconnect()
  }, [loadRequested])

  useEffect(() => {
    if (!loadRequested) return
    let cancelled = false
    setStatus('loading')
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
  }, [file, itemId, loadAttempt, loadRequested, threadId, turnId])

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
          params: { html: buildInlineVisualizationDocument(fragmentRef.current, theme, locale, viewIdRef.current, readVisualizationThemeTokens()) }
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
    const updateTheme = (): void => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/theme', params: { theme: document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark', tokens: readVisualizationThemeTokens(), viewId: viewIdRef.current } }, '*')
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
          reducedMotion: window.matchMedia('(prefers-reduced-motion: reduce)').matches,
          pointer: window.matchMedia('(pointer: coarse)').matches ? 'coarse' : 'fine',
          width: frame.clientWidth || Math.max(0, container.clientWidth - INLINE_VISUALIZATION_ACTION_RAIL_WIDTH),
          viewId: viewIdRef.current,
          tokens: readVisualizationThemeTokens()
        }
      }, '*')
    }
    const observer = new ResizeObserver(sendContext)
    if (containerRef.current) observer.observe(containerRef.current)
    sendContext()
    return () => observer.disconnect()
  }, [locale, status])

  async function copyAsImage(): Promise<void> {
    if (copying || status !== 'ready') return
    setCopying(true)
    try {
      await nextPaint()
      await nextPaint()
      const frame = iframeRef.current
      if (!frame) throw new Error('Visualization frame is unavailable.')
      const rect = frame.getBoundingClientRect()
      await window.api.visualization.copyImage({ x: rect.x, y: rect.y, width: rect.width, height: rect.height })
      addToast(t('toast.copied'), 'success', 2000)
    } catch {
      addToast(t('inlineVisualization.copyFailed'), 'error', 3000)
    } finally {
      setCopying(false)
    }
  }

  if (status === 'failed') {
    return (
      <div role="status" style={fallbackStyle}>
        <span>{t('inlineVisualization.unavailable')}</span>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => {
            setStatus('loading')
            setLoadAttempt(attempt => attempt + 1)
          }}
        >
          {t('common.retry')}
        </Button>
      </div>
    )
  }
  return (
    <div
      ref={containerRef}
      style={containerStyle}
      aria-busy={status === 'loading'}
      onMouseEnter={() => setActionsVisible(true)}
      onMouseLeave={() => setActionsVisible(false)}
      onFocusCapture={() => setActionsVisible(true)}
      onBlurCapture={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) setActionsVisible(false) }}
    >
      {status === 'idle' && <div data-testid="inline-visualization-idle" aria-hidden="true" style={idlePlaceholderStyle} />}
      {status === 'loading' && <div role="status" aria-live="polite"><span style={visuallyHiddenStyle}>{t('inlineVisualization.loading')}</span><Skeleton width="100%" height={180} radius={8} /></div>}
      <iframe ref={iframeRef} title={t('inlineVisualization.frameTitle', { file })} sandbox="allow-scripts" referrerPolicy="no-referrer" allow="" style={{ ...iframeStyle, height, display: status === 'ready' ? 'block' : 'none' }} />
      {status === 'ready' && (
        <IconButton
          size={24}
          className="inline-visualization-copy-button"
          label={t('inlineVisualization.copyImage')}
          tooltipLabel={t('inlineVisualization.copyImage')}
          tooltipPlacement="top"
          disabled={copying}
          onClick={() => { void copyAsImage() }}
          style={{ opacity: coarsePointer || actionsVisible ? 1 : 0, pointerEvents: coarsePointer || actionsVisible ? 'auto' : 'none' }}
          icon={<Copy size={14} aria-hidden />}
        />
      )}
      {overflowed && <div role="status" style={overflowHintStyle}>{t('inlineVisualization.scrollHint')}</div>}
    </div>
  )
}

const containerStyle: CSSProperties = { position: 'relative', width: '100%', minWidth: 0, margin: '8px 0', paddingRight: INLINE_VISUALIZATION_ACTION_RAIL_WIDTH, boxSizing: 'border-box' }
const iframeStyle: CSSProperties = { width: '100%', border: 0, background: 'transparent', overflow: 'hidden' }
const idlePlaceholderStyle: CSSProperties = { width: '100%', height: 180, borderRadius: 8, background: 'var(--bg-tertiary)', opacity: 0.55 }
const fallbackStyle: CSSProperties = { display: 'flex', minHeight: 44, margin: '8px 0', padding: '8px 10px 8px 12px', alignItems: 'center', justifyContent: 'space-between', gap: 12, borderRadius: 8, background: 'var(--bg-secondary)', color: 'var(--text-secondary)', fontSize: 12 }
const overflowHintStyle: CSSProperties = { marginTop: '4px', color: 'var(--text-secondary)', fontSize: '11px' }
const visuallyHiddenStyle: CSSProperties = { position: 'absolute', width: 1, height: 1, padding: 0, margin: -1, overflow: 'hidden', clip: 'rect(0,0,0,0)', whiteSpace: 'nowrap', border: 0 }

function readVisualizationThemeTokens(): InlineVisualizationThemeTokens {
  const styles = getComputedStyle(document.documentElement)
  const read = (name: string, fallback: string): string => styles.getPropertyValue(name).trim() || fallback
  return {
    background: read('--bg-primary', 'rgb(24 24 24)'),
    foreground: read('--text-primary', 'rgb(255 255 255)'),
    card: read('--bg-secondary', 'rgb(38 38 38)'),
    cardForeground: read('--text-primary', 'rgb(255 255 255)'),
    primary: read('--accent', 'rgb(131 195 255)'),
    primaryForeground: read('--bg-primary', 'rgb(24 24 24)'),
    secondary: read('--bg-secondary', 'rgb(38 38 38)'),
    secondaryForeground: read('--text-primary', 'rgb(255 255 255)'),
    muted: read('--bg-tertiary', 'rgb(48 48 48)'),
    mutedForeground: read('--text-secondary', 'rgb(184 184 184)'),
    accent: read('--bg-active', 'rgb(54 54 54)'),
    accentForeground: read('--text-primary', 'rgb(255 255 255)'),
    border: read('--border-default', 'rgb(255 255 255 / 12%)'),
    input: read('--border-active', 'rgb(255 255 255 / 20%)'),
    ring: read('--accent', 'rgb(131 195 255)'),
    fontFamily: read('--font-ui', '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif')
  }
}

function nextPaint(): Promise<void> {
  return new Promise(resolve => requestAnimationFrame(() => resolve()))
}
