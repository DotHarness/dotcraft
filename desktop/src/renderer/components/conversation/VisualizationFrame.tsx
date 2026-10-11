import { useContext, useEffect, useId, useRef, useState } from 'react'
import type { JsonValue } from '@dotcraft/sdk/contracts'
import { CircleAlert, Copy, Maximize2, SlidersHorizontal } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { VISUALIZATION_DOWNLOAD_METHOD, VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD, MCP_APP_SANDBOX_PROXY_READY_METHOD, MCP_APP_SANDBOX_PROXY_URL } from '../../../shared/mcpAppSandbox'
import { THEME_CHANGED_EVENT } from '../../../shared/theme'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { Skeleton } from '../ui/Skeleton'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { buildVisualizationDocument, VISUALIZATION_MAX_HEIGHT, VISUALIZATION_MIN_HEIGHT, type VisualizationDisplayMode, type VisualizationWidgetState } from './visualizationSecurity'
import { copyVisualizationImage, openExternalLinkAfterConfirmation, openVisualizationPopup, readVisualizationDownload, readVisualizationLinkUrl, readVisualizationThemeTokens } from './visualizationHost'
import { visualizationFileName } from './visualizationParser'
import { ConversationVisualizationsContext, galleryForVisualization, type VisualizationEntry } from './visualizationGallery'
import { VisualizationViewer, type VisualizationFullscreenHost, type VisualizationViewerKey } from './VisualizationViewer'
import { useVisualizationWideLayout } from './useVisualizationWideLayout'
import { subscribeVisualizationReload } from './visualizationReloads'
import { publishVisualizationWidgetState, subscribeVisualizationWidgetState } from './visualizationWidgetStates'
import { VisualizationDesignPanel } from './VisualizationDesignPanel'
import { clearVisualizationDesignValues, useVisualizationDesignState } from './useVisualizationDesignState'
import { buildVisualizationChangesContext } from './visualizationDesignControls'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { useNearMessageViewport } from './useNearMessageViewport'

interface Props { threadId: string; turnId: string; itemId: string; path: string; title?: string; mode?: 'wide'; fullscreen?: VisualizationFullscreenHost }
interface OpenResult { viewHandle: string; fragment: string; mimeType: string; widgetState?: VisualizationWidgetState | null }
interface DeliveredGlobals { displayMode: VisualizationDisplayMode; maxHeight: number }
const VISUALIZATION_ACTION_RAIL_WIDTH = 32
const VIEWER_KEYS = new Set<string>(['Escape', 'ArrowLeft', 'ArrowRight'])
const MAX_WIDGET_STATE_BYTES = 16 * 1024
const MAX_FOLLOW_UP_CONTEXT_BYTES = 32 * 1024
const MAX_RUNTIME_ERROR_LENGTH = 1000
export function VisualizationFrame({ threadId, turnId, itemId, path, title, mode, fullscreen }: Props): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const confirm = useConfirmDialog()
  const getConversationVisualizations = useContext(ConversationVisualizationsContext)
  const iframeRef = useRef<HTMLIFrameElement | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)
  const handleRef = useRef<string | null>(null)
  const fragmentRef = useRef<string | null>(null)
  const widgetStateRef = useRef<VisualizationWidgetState | null>(null)
  const bridgeEventTimesRef = useRef<number[]>([])
  const confirmationPendingRef = useRef(false)
  const downloadPendingRef = useRef(false)
  const dismissPopupRef = useRef<(() => void) | null>(null)
  const viewIdRef = useRef(crypto.randomUUID())
  const designPanelId = useId()
  const [status, setStatus] = useState<'idle' | 'loading' | 'ready' | 'failed'>('idle')
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [contentHeight, setContentHeight] = useState(260)
  const [actionsVisible, setActionsVisible] = useState(false)
  const [copying, setCopying] = useState(false)
  const [runtimeError, setRuntimeError] = useState<string | null>(null)
  const [viewer, setViewer] = useState<{ entries: VisualizationEntry[]; index: number } | null>(null)
  const [inlineDesignOpen, setInlineDesignOpen] = useState(false)
  const design = useVisualizationDesignState({
    threadId,
    path,
    viewId: viewIdRef.current,
    post: values => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/designControls/set', params: { values, viewId: viewIdRef.current } }, '*')
  })
  const { clear: clearDesign, receive: receiveDesign, showingOriginal, toggleOriginal } = design
  const designAvailable = (design.controls?.groups.length ?? 0) > 0
  const designOpen = designAvailable && (fullscreen ? fullscreen.designControlsOpen : inlineDesignOpen)
  const setDesignOpen = (open: boolean): void => fullscreen ? fullscreen.onDesignControlsOpenChange(open) : setInlineDesignOpen(open)
  const coarsePointer = window.matchMedia('(pointer: coarse)').matches
  const displayMode: VisualizationDisplayMode = fullscreen ? 'fullscreen' : 'inline'
  const heightCap = fullscreen ? Math.max(VISUALIZATION_MIN_HEIGHT, fullscreen.maxHeight) : VISUALIZATION_MAX_HEIGHT
  const overflowed = contentHeight > heightCap
  const height = Math.max(VISUALIZATION_MIN_HEIGHT, Math.min(heightCap, contentHeight))
  const loadRequested = useNearMessageViewport(containerRef, fullscreen == null) || fullscreen != null
  const wideStyle = useVisualizationWideLayout(containerRef, mode === 'wide' && !fullscreen, VISUALIZATION_ACTION_RAIL_WIDTH)
  const fullscreenRef = useRef(fullscreen)
  fullscreenRef.current = fullscreen
  const globalsRef = useRef<DeliveredGlobals>({ displayMode, maxHeight: heightCap })
  globalsRef.current = { displayMode, maxHeight: heightCap }
  const deliveredGlobalsRef = useRef<DeliveredGlobals | null>(null)
  const openViewer = (): void => setViewer(current => current ?? galleryForVisualization({ threadId, turnId, itemId, path, title, mode }, getConversationVisualizations))
  const openViewerRef = useRef(openViewer)
  openViewerRef.current = openViewer

  useEffect(() => {
    if (!loadRequested) return
    return subscribeVisualizationReload(threadId, path, sourceItemId => {
      if (sourceItemId === itemId) return
      clearVisualizationDesignValues(threadId, path)
      setLoadAttempt(attempt => attempt + 1)
    })
  }, [itemId, loadRequested, path, threadId])

  useEffect(() => {
    if (!loadRequested) return
    let cancelled = false
    setStatus('loading')
    setRuntimeError(null)
    clearDesign()
    setInlineDesignOpen(false)
    void window.api.appServer.sendRequest('visualization/view/open', { threadId, turnId, itemId, path }, 15_000)
      .then(result => {
        const opened = result as OpenResult
        if (cancelled) {
          void window.api.appServer.sendRequest('visualization/view/close', { viewHandle: opened.viewHandle }).catch(() => {})
          return
        }
        handleRef.current = opened.viewHandle
        fragmentRef.current = opened.fragment
        widgetStateRef.current = opened.widgetState ?? null
        if (iframeRef.current) iframeRef.current.src = MCP_APP_SANDBOX_PROXY_URL
      })
      .catch(() => { if (!cancelled) setStatus('failed') })
    return () => {
      cancelled = true
      const handle = handleRef.current
      handleRef.current = null
      if (handle) void window.api.appServer.sendRequest('visualization/view/close', { viewHandle: handle }).catch(() => {})
    }
  }, [clearDesign, itemId, loadAttempt, loadRequested, path, threadId, turnId])

  useEffect(() => {
    if (!loadRequested) return
    return subscribeVisualizationWidgetState(threadId, path, viewIdRef.current, state => {
      widgetStateRef.current = state
      iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/globals', params: { globals: { widgetState: state }, viewId: viewIdRef.current } }, '*')
    })
  }, [loadRequested, path, threadId])

  useEffect(() => {
    const postResult = (id: string, ok: boolean, detail: { result?: unknown; error?: string } = {}): void =>
      iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/result', params: { id, ok, ...detail, viewId: viewIdRef.current } }, '*')
    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframeRef.current?.contentWindow) return
      const message = event.data as { method?: string; params?: Record<string, unknown> } | null
      const requestId = typeof message?.params?.id === 'string' ? message.params.id : ''
      if (message?.method?.startsWith('visualization/')) {
        const now = Date.now()
        const recent = bridgeEventTimesRef.current.filter(sample => now - sample < 60_000)
        if (recent.length >= 60) {
          if (requestId && message.params?.viewId === viewIdRef.current) postResult(requestId, false, { error: 'Rate limited' })
          return
        }
        recent.push(now)
        bridgeEventTimesRef.current = recent
      }
      if (message?.method === MCP_APP_SANDBOX_PROXY_READY_METHOD && fragmentRef.current != null) {
        const theme = document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
        iframeRef.current.contentWindow?.postMessage({
          method: VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD,
          params: { html: buildVisualizationDocument(fragmentRef.current, theme, locale, viewIdRef.current, { widgetState: widgetStateRef.current, ...globalsRef.current }, {
            design: t('visualization.design'),
            designs: t('visualization.designs'),
            previousDesign: t('visualization.previousDesign'),
            nextDesign: t('visualization.nextDesign')
          }, readVisualizationThemeTokens()) }
        }, '*')
        deliveredGlobalsRef.current = globalsRef.current
      } else if (message?.params?.viewId !== viewIdRef.current) {
        return
      } else if (message?.method === 'visualization/ready') {
        setStatus('ready')
      } else if (message?.method === 'visualization/resize') {
        const requested = Number(message.params?.height)
        if (Number.isFinite(requested) && requested > 0) setContentHeight(Math.ceil(requested))
      } else if (message?.method === 'visualization/requestDisplayMode') {
        const requested = message.params?.mode
        if (!requestId || requestId.length > 128 || (requested !== 'inline' && requested !== 'fullscreen')) {
          postResult(requestId, false, { error: 'Unsupported display mode' })
          return
        }
        const host = fullscreenRef.current
        if (host) {
          postResult(requestId, true, { result: { mode: requested } })
          if (requested === 'inline') host.onExit()
          return
        }
        const granted = requested === 'fullscreen' && navigator.userActivation?.isActive !== false ? 'fullscreen' : 'inline'
        if (granted === 'fullscreen') openViewerRef.current()
        postResult(requestId, true, { result: { mode: granted } })
      } else if (message?.method === 'visualization/designControls') {
        receiveDesign(message.params)
      } else if (message?.method === 'visualization/key') {
        const key = message.params?.key
        if (typeof key === 'string' && VIEWER_KEYS.has(key)) fullscreenRef.current?.onKey(key as VisualizationViewerKey)
      } else if (message?.method === 'visualization/setWidgetState') {
        const state = readWidgetState(message.params?.state)
        const handle = handleRef.current
        if (!requestId || requestId.length > 128 || !state || jsonByteLength(state) > MAX_WIDGET_STATE_BYTES) {
          postResult(requestId, false, { error: 'Invalid widget state' })
          return
        }
        if (!handle) {
          postResult(requestId, false, { error: t('visualization.unavailable') })
          return
        }
        widgetStateRef.current = state
        void window.api.appServer.sendRequest('visualization/view/widgetState/update', { viewHandle: handle, ...state })
          .then(() => {
            postResult(requestId, true)
            publishVisualizationWidgetState(threadId, path, viewIdRef.current, state)
          })
          .catch(error => postResult(requestId, false, { error: String(error) }))
      } else if (message?.method === 'visualization/followUp') {
        const prompt = typeof message.params?.prompt === 'string' ? message.params.prompt : ''
        const title = typeof message.params?.title === 'string' ? message.params.title : t('visualization.followUpTitle')
        const context = (message.params?.context ?? undefined) as JsonValue | undefined
        if (!requestId || requestId.length > 128 || !prompt || new TextEncoder().encode(prompt).byteLength > 16 * 1024 || title.length > 250
          || (context !== undefined && jsonByteLength(context) > MAX_FOLLOW_UP_CONTEXT_BYTES) || confirmationPendingRef.current) {
          postResult(requestId, false, { error: t('visualization.cancelled') })
          return
        }
        confirmationPendingRef.current = true
        void confirm({
          title: t('visualization.followUpTitle'),
          message: context === undefined ? `${title}\n\n${prompt}` : `${title}\n\n${prompt}\n\n${t('visualization.followUpContextIncluded')}`,
          confirmLabel: t('visualization.send'),
          cancelLabel: t('common.cancel')
        }).then(accepted => {
          confirmationPendingRef.current = false
          if (!accepted) {
            postResult(requestId, false, { error: t('visualization.cancelled') })
            return
          }
          const handle = handleRef.current
          if (!handle) {
            postResult(requestId, false, { error: t('visualization.unavailable') })
            return
          }
          void window.api.appServer.sendRequest('visualization/view/message', { viewHandle: handle, prompt, ...(context === undefined ? {} : { context }) })
            .then(result => postResult(requestId, true, { result }))
            .catch(error => postResult(requestId, false, { error: String(error) }))
        })
      } else if (message?.method === 'visualization/error') {
        const detail = typeof message.params?.message === 'string' ? message.params.message.trim().slice(0, MAX_RUNTIME_ERROR_LENGTH) : ''
        setRuntimeError(current => current ?? (detail || 'Error'))
      } else if (message?.method === 'visualization/pickColor' || message?.method === 'visualization/selectMenu') {
        const post = (method: string, params: Record<string, unknown>): void => iframeRef.current?.contentWindow?.postMessage({ method, params: { ...params, viewId: viewIdRef.current } }, '*')
        const popup = requestId && requestId.length <= 128 && !dismissPopupRef.current && navigator.userActivation?.isActive !== false
          ? openVisualizationPopup(message.method, message.params, iframeRef.current, t('colorPicker.title'), post)
          : null
        if (!popup) {
          postResult(requestId, false, { error: 'Cancelled' })
          return
        }
        dismissPopupRef.current = popup.dismiss
        void popup.result.then(result => {
          dismissPopupRef.current = null
          if (result) postResult(requestId, true, { result })
          else postResult(requestId, false, { error: 'Cancelled' })
        })
      } else if (message?.method === 'visualization/openLink') {
        const url = readVisualizationLinkUrl(message.params?.url)
        if (!url || confirmationPendingRef.current) return
        confirmationPendingRef.current = true
        void openExternalLinkAfterConfirmation(url, confirm, t).finally(() => { confirmationPendingRef.current = false })
      } else if (message?.method === VISUALIZATION_DOWNLOAD_METHOD) {
        if (downloadPendingRef.current) return
        const download = readVisualizationDownload(message.params)
        if (download.kind !== 'file') {
          addToast(t(download.kind === 'tooLarge' ? 'visualization.downloadTooLarge' : 'visualization.downloadFailed'), 'error', 3000)
          return
        }
        downloadPendingRef.current = true
        void window.api.shell.saveFileAs({ data: download.data, suggestedName: download.name })
          .catch(() => addToast(t('visualization.downloadFailed'), 'error', 3000))
          .finally(() => { downloadPendingRef.current = false })
      }
    }
    window.addEventListener('message', onMessage)
    const updateTheme = (): void => iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/theme', params: { theme: document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark', tokens: readVisualizationThemeTokens(), viewId: viewIdRef.current } }, '*')
    window.addEventListener(THEME_CHANGED_EVENT, updateTheme)
    const cursorObserver = new MutationObserver(updateTheme)
    cursorObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-pointer-cursors'] })
    return () => {
      window.removeEventListener('message', onMessage)
      window.removeEventListener(THEME_CHANGED_EVENT, updateTheme)
      cursorObserver.disconnect()
      dismissPopupRef.current?.()
    }
  }, [confirm, locale, path, receiveDesign, t, threadId])

  useEffect(() => {
    if (status !== 'ready') return
    iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/overflow', params: { enabled: overflowed, viewId: viewIdRef.current } }, '*')
  }, [overflowed, status])

  useEffect(() => {
    const delivered = deliveredGlobalsRef.current
    if (status !== 'ready' || !delivered || (delivered.displayMode === displayMode && delivered.maxHeight === heightCap)) return
    deliveredGlobalsRef.current = { displayMode, maxHeight: heightCap }
    iframeRef.current?.contentWindow?.postMessage({ method: 'visualization/globals', params: { globals: { displayMode, maxHeight: heightCap }, viewId: viewIdRef.current } }, '*')
  }, [displayMode, heightCap, status])

  useEffect(() => {
    fullscreenRef.current?.onReadyChange(status === 'ready')
  }, [status])

  useEffect(() => {
    fullscreenRef.current?.onDesignControlsAvailableChange(designAvailable)
  }, [designAvailable])

  useEffect(() => {
    if (!designOpen && showingOriginal) toggleOriginal()
  }, [designOpen, showingOriginal, toggleOriginal])

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
          width: frame.clientWidth || Math.max(0, container.clientWidth - VISUALIZATION_ACTION_RAIL_WIDTH),
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
    await copyVisualizationImage(() => iframeRef.current, t)
    setCopying(false)
  }

  const viewerNode = viewer && (
    <VisualizationViewer
      entries={viewer.entries}
      initialIndex={viewer.index}
      onClose={() => setViewer(null)}
      renderView={(entry, host) => (
        <VisualizationFrame
          key={entry.key}
          threadId={entry.threadId}
          turnId={entry.turnId}
          itemId={entry.itemId}
          path={entry.path}
          title={entry.title}
          mode={entry.mode}
          fullscreen={host}
        />
      )}
    />
  )
  const actionsShown = coarsePointer || actionsVisible || designOpen

  if (status === 'failed') {
    return (
      <>
        <div role="status" className="visualization-fallback">
          <span>{t('visualization.unavailable')}</span>
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
        {viewerNode}
      </>
    )
  }
  return (
    <>
      <div
        ref={containerRef}
        className={fullscreen ? 'visualization-host--fullscreen' : wideStyle ? 'visualization-host visualization-wide' : 'visualization-host'}
        style={wideStyle}
        aria-busy={status === 'loading'}
        onMouseEnter={() => setActionsVisible(true)}
        onMouseLeave={() => setActionsVisible(false)}
        onFocusCapture={() => setActionsVisible(true)}
        onBlurCapture={(event) => { if (!event.currentTarget.contains(event.relatedTarget)) setActionsVisible(false) }}
      >
        {status === 'idle' && <div data-testid="visualization-idle" aria-hidden="true" className="visualization-placeholder" />}
        {status === 'loading' && <div role="status" aria-live="polite"><span className="visualization-visually-hidden">{t('visualization.loading')}</span><Skeleton width="100%" height={180} radius={8} /></div>}
        <iframe ref={iframeRef} title={title ?? t('visualization.frameTitle', { file: visualizationFileName(path) })} sandbox="allow-scripts" referrerPolicy="no-referrer" allow="" className="visualization-frame" style={{ height, display: status === 'ready' ? 'block' : 'none' }} />
        {status === 'ready' && !fullscreen && (
          <div className="visualization-actions" style={{ opacity: actionsShown ? 1 : 0, pointerEvents: actionsShown ? 'auto' : 'none' }}>
            <IconButton
              size={24}
              className="visualization-action"
              label={t('visualization.copyImage')}
              tooltipLabel={t('visualization.copyImage')}
              tooltipPlacement="top"
              disabled={copying}
              onClick={() => { void copyAsImage() }}
              icon={<Copy size={14} aria-hidden />}
            />
            <IconButton
              size={24}
              className="visualization-action"
              label={t('visualization.openFullscreen')}
              tooltipLabel={t('visualization.openFullscreen')}
              tooltipPlacement="top"
              onClick={openViewer}
              icon={<Maximize2 size={14} aria-hidden />}
            />
            {designAvailable && (
              <IconButton
                size={24}
                className="visualization-action"
                label={t('visualization.designControls')}
                tooltipLabel={t('visualization.designControls')}
                tooltipPlacement="top"
                active={designOpen}
                activeTone="neutral"
                aria-expanded={designOpen}
                aria-controls={designOpen ? designPanelId : undefined}
                onClick={() => setDesignOpen(!designOpen)}
                icon={<SlidersHorizontal size={14} aria-hidden />}
              />
            )}
          </div>
        )}
        {status === 'ready' && designOpen && (
          <VisualizationDesignPanel
            id={designPanelId}
            state={design}
            hidden={copying || fullscreen?.capturing}
            onClose={() => setDesignOpen(false)}
            onAddToMessage={() => {
              const context = design.controls && buildVisualizationChangesContext(path, title ?? visualizationFileName(path), design.controls, design.values)
              if (context) useComposerContextStore.getState().addContext(threadId, context)
            }}
          />
        )}
        {status === 'ready' && runtimeError != null && (
          <div role="status" className="visualization-notice">
            <CircleAlert size={14} aria-hidden className="visualization-notice__icon" />
            <span className="visualization-notice__text">{t('visualization.runtimeError')}</span>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => useUIStore.getState().setComposerPrefill(t('visualization.fixPrompt', { file: visualizationFileName(path), error: runtimeError }))}
            >
              {t('visualization.fixAction')}
            </Button>
          </div>
        )}
        {overflowed && !fullscreen && <div role="status" className="visualization-scroll-hint">{t('visualization.scrollHint')}</div>}
      </div>
      {viewerNode}
    </>
  )
}

function readWidgetState(value: unknown): VisualizationWidgetState | null {
  if (value == null || typeof value !== 'object') return null
  const state = value as Partial<VisualizationWidgetState>
  return { modelContent: state.modelContent ?? null, privateContent: state.privateContent ?? null }
}

function jsonByteLength(value: unknown): number {
  try {
    return new TextEncoder().encode(JSON.stringify(value) ?? '').byteLength
  } catch {
    return Number.POSITIVE_INFINITY
  }
}
