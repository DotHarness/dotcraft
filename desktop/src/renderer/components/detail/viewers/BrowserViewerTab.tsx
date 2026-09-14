import { useCallback, useEffect, useRef, useState } from 'react'
import { Globe } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { useUIStore } from '../../../stores/uiStore'
import { BrowserFeedbackControls, BrowserFindBar } from './BrowserFeedbackControls'
import { BrowserPageFeedback } from './BrowserPageFeedback'
import { BrowserToolbar } from './BrowserToolbar'
import { useBrowserFeedback } from './useBrowserFeedback'

interface BrowserViewerTabProps {
  tabId: string
}

type ViewerStoreSnapshot = ReturnType<typeof useViewerTabStore.getState>

function findBrowserTab(
  state: ViewerStoreSnapshot,
  threadId: string | null,
  tabId: string
) {
  if (!threadId) return null
  const found = state.getThreadState(threadId).tabs.find((item) => item.id === tabId)
  return found?.kind === 'browser' ? found : null
}

export function BrowserViewerTab({ tabId }: BrowserViewerTabProps): JSX.Element {
  const t = useT()
  const feedback = useBrowserFeedback(tabId)
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const existsTab = useViewerTabStore((s) => Boolean(findBrowserTab(s, currentThreadId, tabId)))
  const loading = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.loading ?? false)
  const canGoBack = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.canGoBack ?? false)
  const canGoForward = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.canGoForward ?? false)
  const currentUrl = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.currentUrl ?? '')
  const crashed = useViewerTabStore((s) => Boolean(findBrowserTab(s, currentThreadId, tabId)?.crashed))
  const blockedMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.blockedMessage ?? '')
  const downloadMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.downloadMessage ?? '')
  const errorMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.errorMessage ?? '')
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const updateBrowserTab = useViewerTabStore((s) => s.updateBrowserTab)
  const activeMainView = useUIStore((s) => s.activeMainView)
  const activeDetailTab = useUIStore((s) => s.activeDetailTab)
  const detailPanelVisible = useUIStore((s) => s.detailPanelVisible)

  const [readyTab, setReadyTab] = useState<string | null>(null)
  const bodyRef = useRef<HTMLDivElement>(null)

  // A fresh tab sits on the internal start page (about:blank / a data: start
  // page). Treat those as "no page": empty address bar + a themed empty state,
  // matching the reference design rather than leaking the start-page URL.
  const isBlank = !currentUrl || currentUrl === 'about:blank' || currentUrl.startsWith('data:')
  const isActiveBrowserSurface = existsTab &&
    activeMainView === 'conversation' &&
    detailPanelVisible &&
    activeDetailTab.kind === 'viewer' &&
    activeDetailTab.id === tabId
  const pageVisible = isActiveBrowserSurface && !isBlank && readyTab === tabId

  useEffect(() => {
    if (!currentThreadId || !workspacePath || !existsTab) return
    let disposed = false
    const state = useViewerTabStore.getState()
    const found = findBrowserTab(state, currentThreadId, tabId)
    const initialUrl = found?.currentUrl || 'about:blank'
    void window.api.workspace.viewer.browser.create({
      tabId,
      threadId: currentThreadId,
      workspacePath,
      initialUrl
    }).then((snapshot) => {
      if (disposed) return
      setReadyTab(tabId)
      updateBrowserTab(currentThreadId, tabId, {
        currentUrl: snapshot.currentUrl,
        title: snapshot.title,
        ...(snapshot.faviconDataUrl ? { faviconDataUrl: snapshot.faviconDataUrl } : {}),
        canGoBack: snapshot.canGoBack,
        canGoForward: snapshot.canGoForward,
        loading: snapshot.loading
      })
      return window.api.workspace.viewer.browser.enableFeedback({ tabId, labels: {
        copyLink: t('viewer.browser.copyLink'), newTab: t('viewer.browser.newTab'),
        external: t('viewer.browser.openExternal'), inspect: t('viewer.browser.inspect'), quoteSelection: t('viewer.browser.annotate')
      } }).then(feedback.initialize)
    }).catch(error => {
      if (!disposed) updateBrowserTab(currentThreadId, tabId, { errorMessage: String(error instanceof Error ? error.message : error) })
    })
    return () => { disposed = true }
  }, [currentThreadId, existsTab, tabId, updateBrowserTab, workspacePath, t, feedback.initialize])

  useEffect(() => {
    if (!existsTab) return
    const visible = pageVisible
    if (visible) void window.api.workspace.viewer.browser.setActive({ tabId })
    void window.api.workspace.viewer.browser.setVisible({ tabId, visible })
    return () => {
      void window.api.workspace.viewer.browser.setVisible({ tabId, visible: false })
    }
  }, [existsTab, tabId, pageVisible])

  const pushBounds = useCallback(() => {
    if (!pageVisible) return
    if (!bodyRef.current) return
    if (!bodyRef.current.isConnected) return
    const rect = bodyRef.current.getBoundingClientRect()
    if (rect.width <= 1 || rect.height <= 1) return
    if (rect.right <= 0 || rect.bottom <= 0 || rect.left >= window.innerWidth || rect.top >= window.innerHeight) return
    void window.api.workspace.viewer.browser.setBounds({
      tabId,
      x: Math.round(rect.left),
      y: Math.round(rect.top),
      width: Math.round(rect.width),
      height: Math.round(rect.height)
    })
  }, [pageVisible, tabId])

  const scheduleBounds = useCallback(() => {
    if (!pageVisible) return () => {}
    const requestFrame = typeof window.requestAnimationFrame === 'function'
      ? window.requestAnimationFrame.bind(window)
      : (callback: FrameRequestCallback) => window.setTimeout(() => callback(performance.now()), 0)
    const cancelFrame = typeof window.cancelAnimationFrame === 'function'
      ? window.cancelAnimationFrame.bind(window)
      : (handle: number) => window.clearTimeout(handle)
    const frame = requestFrame(() => pushBounds())
    return () => cancelFrame(frame)
  }, [pageVisible, pushBounds])

  useEffect(() => {
    if (!existsTab || !pageVisible) return
    let cancelPendingBounds: (() => void) | null = null
    const queueBounds = () => {
      cancelPendingBounds?.()
      cancelPendingBounds = scheduleBounds()
    }
    queueBounds()
    const resizeObserver = new ResizeObserver(() => {
      queueBounds()
    })
    if (bodyRef.current) {
      resizeObserver.observe(bodyRef.current)
    }
    const onResize = () => queueBounds()
    const onScroll = () => queueBounds()
    window.addEventListener('resize', onResize)
    window.addEventListener('scroll', onScroll, true)
    return () => {
      cancelPendingBounds?.()
      resizeObserver.disconnect()
      window.removeEventListener('resize', onResize)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [existsTab, pageVisible, scheduleBounds])

  if (!existsTab) {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: '13px'
      }}>
        {t('viewer.missingFile')}
      </div>
    )
  }

  return (
    <div style={{ position: 'relative', display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      <BrowserToolbar
        url={isBlank ? '' : currentUrl}
        loading={loading}
        canGoBack={canGoBack}
        canGoForward={canGoForward}
        onBack={() => window.api.workspace.viewer.browser.back({ tabId })}
        onForward={() => window.api.workspace.viewer.browser.forward({ tabId })}
        onReload={() => window.api.workspace.viewer.browser.reload({ tabId })}
        onStop={() => window.api.workspace.viewer.browser.stop({ tabId })}
        onNavigate={(url) => { void window.api.workspace.viewer.browser.navigate({ tabId, url }) }}
        onOpenExternal={() => window.api.workspace.viewer.browser.openExternal({ tabId })}
        onAddressDismiss={() => { void window.api.workspace.viewer.browser.setActive({ tabId }) }}
        annotate={<BrowserPageFeedback tabId={tabId} threadId={currentThreadId} body={bodyRef} disabled={isBlank} run={feedback.run} />}
        controls={<BrowserFeedbackControls active={isActiveBrowserSurface} tabId={tabId} state={feedback.state} run={feedback.run} />}
      />
      <BrowserFindBar active={isActiveBrowserSurface} tabId={tabId} state={feedback.state.find} run={feedback.run} />
      {feedback.state.error && <div role="alert">{feedback.state.error}</div>}

      {(blockedMessage || downloadMessage || crashed || errorMessage) && (
        <div
          role="status"
          style={{
            padding: '6px 10px',
            borderBottom: '1px solid var(--border-default)',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            backgroundColor: 'var(--warning-bg)'
          }}
        >
          {crashed && (
            <span>
              {t('viewer.browser.crashed')}
              {' '}
              <button
                type="button"
                onClick={() => window.api.workspace.viewer.browser.reload({ tabId })}
                style={{
                  border: 'none',
                  background: 'transparent',
                  color: 'var(--accent)',
                  cursor: 'pointer',
                  padding: 0
                }}
              >
                {t('viewer.browser.reloadTab')}
              </button>
            </span>
          )}
          {!crashed && (blockedMessage || downloadMessage || errorMessage)}
        </div>
      )}

      <div
        ref={bodyRef}
        style={{
          position: 'relative',
          flex: 1,
          overflow: 'hidden',
          background: 'var(--bg-primary)'
        }}
      >
        {isBlank && (
          <div style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '10px',
            padding: '24px',
            textAlign: 'center',
            pointerEvents: 'none'
          }}>
            <Globe size={48} strokeWidth={1.25} aria-hidden style={{ display: 'block', color: 'var(--text-dimmed)' }} />
            <span style={{ fontSize: '14px', fontWeight: 500, color: 'var(--text-primary)' }}>
              {t('viewer.browser.startTitle')}
            </span>
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
              {t('viewer.browser.startPageHint')}
            </span>
          </div>
        )}
      </div>
    </div>
  )
}
