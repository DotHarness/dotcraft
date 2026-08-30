import { type FormEvent, type ReactNode, useCallback, useEffect, useRef, useState } from 'react'
import {
  ArrowLeft,
  ArrowRight,
  ExternalLink,
  Globe,
  RotateCw,
  Square
} from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { useUIStore } from '../../../stores/uiStore'
import { useTransientOverlayStore } from '../../../stores/transientOverlayStore'
import { IconButton } from '../../ui/IconButton'
import { Input } from '../../ui/Input'

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
  const quickOpenVisible = useUIStore((s) => s.quickOpenVisible)
  const nativeViewBlocked = useTransientOverlayStore((s) => s.nativeViewBlockerCount > 0)

  const [urlInput, setUrlInput] = useState('')
  const [editingAddress, setEditingAddress] = useState(false)
  const bodyRef = useRef<HTMLDivElement>(null)

  // A fresh tab sits on the internal start page (about:blank / a data: start
  // page). Treat those as "no page": empty address bar + a themed empty state,
  // matching the reference design rather than leaking the start-page URL.
  const isBlank = !currentUrl || currentUrl === 'about:blank' || currentUrl.startsWith('data:')
  const isActiveBrowserSurface = existsTab &&
    activeMainView === 'conversation' &&
    detailPanelVisible &&
    !quickOpenVisible &&
    !nativeViewBlocked &&
    activeDetailTab.kind === 'viewer' &&
    activeDetailTab.id === tabId
  const nativeViewVisible = isActiveBrowserSurface && !isBlank

  useEffect(() => {
    if (editingAddress) return
    setUrlInput(isBlank ? '' : currentUrl)
  }, [currentUrl, editingAddress, isBlank])

  useEffect(() => {
    if (!currentThreadId || !workspacePath || !existsTab) return
    const state = useViewerTabStore.getState()
    const found = findBrowserTab(state, currentThreadId, tabId)
    const initialUrl = found?.currentUrl || 'about:blank'
    void window.api.workspace.viewer.browser.create({
      tabId,
      threadId: currentThreadId,
      workspacePath,
      initialUrl
    }).then((snapshot) => {
      if (!currentThreadId) return
      updateBrowserTab(currentThreadId, tabId, {
        currentUrl: snapshot.currentUrl,
        title: snapshot.title,
        ...(snapshot.faviconDataUrl ? { faviconDataUrl: snapshot.faviconDataUrl } : {}),
        canGoBack: snapshot.canGoBack,
        canGoForward: snapshot.canGoForward,
        loading: snapshot.loading
      })
    }).catch(() => {})
  }, [currentThreadId, existsTab, tabId, updateBrowserTab, workspacePath])

  useEffect(() => {
    if (!existsTab) return
    // Keep the native view hidden on the start page so the themed empty state
    // below is visible instead of a blank/white web page.
    const visible = nativeViewVisible
    if (visible) void window.api.workspace.viewer.browser.setActive({ tabId })
    void window.api.workspace.viewer.browser.setVisible({ tabId, visible })
    return () => {
      void window.api.workspace.viewer.browser.setVisible({ tabId, visible: false })
    }
  }, [existsTab, tabId, nativeViewVisible])

  const pushBounds = useCallback(() => {
    if (!nativeViewVisible) return
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
  }, [nativeViewVisible, tabId])

  const scheduleBounds = useCallback(() => {
    if (!nativeViewVisible) return () => {}
    const requestFrame = typeof window.requestAnimationFrame === 'function'
      ? window.requestAnimationFrame.bind(window)
      : (callback: FrameRequestCallback) => window.setTimeout(() => callback(performance.now()), 0)
    const cancelFrame = typeof window.cancelAnimationFrame === 'function'
      ? window.cancelAnimationFrame.bind(window)
      : (handle: number) => window.clearTimeout(handle)
    const frame = requestFrame(() => pushBounds())
    return () => cancelFrame(frame)
  }, [nativeViewVisible, pushBounds])

  useEffect(() => {
    if (!existsTab || !nativeViewVisible) return
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
  }, [existsTab, nativeViewVisible, scheduleBounds])

  const toolbarDisabled = !existsTab
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

  const onSubmit = (e: FormEvent<HTMLFormElement>): void => {
    e.preventDefault()
    void window.api.workspace.viewer.browser.navigate({ tabId, url: urlInput })
    setEditingAddress(false)
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '6px 8px',
        borderBottom: '1px solid var(--border-default)',
        flexShrink: 0
      }}>
        <ToolbarButton
          disabled={toolbarDisabled || !canGoBack}
          title={t('viewer.browser.back')}
          onClick={() => window.api.workspace.viewer.browser.back({ tabId })}
        >
          <ArrowLeft size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
        <ToolbarButton
          disabled={toolbarDisabled || !canGoForward}
          title={t('viewer.browser.forward')}
          onClick={() => window.api.workspace.viewer.browser.forward({ tabId })}
        >
          <ArrowRight size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
        <ToolbarButton
          disabled={toolbarDisabled}
          title={loading ? t('viewer.browser.stop') : t('viewer.browser.reload')}
          onClick={() => {
            if (loading) {
              void window.api.workspace.viewer.browser.stop({ tabId })
            } else {
              void window.api.workspace.viewer.browser.reload({ tabId })
            }
          }}
        >
          {loading
            ? <Square size={12} aria-hidden style={{ display: 'block' }} />
            : <RotateCw size={14} aria-hidden style={{ display: 'block' }} />}
        </ToolbarButton>
        <form onSubmit={onSubmit} style={{ flex: 1, minWidth: 0 }}>
          <Input
            value={urlInput}
            onFocus={() => setEditingAddress(true)}
            onBlur={() => setEditingAddress(false)}
            onChange={(e) => setUrlInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Escape') {
                e.preventDefault()
                setEditingAddress(false)
                void window.api.workspace.viewer.browser.setActive({ tabId })
              }
            }}
            placeholder={t('viewer.browser.urlPlaceholder')}
            spellCheck={false}
            autoCapitalize="off"
            autoCorrect="off"
            style={{
              height: '26px',
              borderRadius: '4px',
              background: 'var(--bg-tertiary)',
              fontSize: '12px',
              padding: '0 8px',
              outline: 'none'
            }}
          />
        </form>
        <ToolbarButton
          disabled={toolbarDisabled}
          title={t('viewer.browser.openExternal')}
          onClick={() => window.api.workspace.viewer.browser.openExternal({ tabId })}
        >
          <ExternalLink size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
      </div>

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

function ToolbarButton({
  title,
  onClick,
  disabled,
  children
}: {
  title: string
  onClick: () => void
  disabled?: boolean
  children: ReactNode
}): JSX.Element {
  return (
    <IconButton
      size={24}
      label={title}
      tooltipLabel={title}
      tooltipPlacement="bottom"
      disabledReason={disabled ? title : undefined}
      onClick={onClick}
      disabled={disabled}
      style={{ borderRadius: 4 }}
      icon={children}
    />
  )
}
