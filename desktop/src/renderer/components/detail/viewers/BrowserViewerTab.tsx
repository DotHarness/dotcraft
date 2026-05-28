import { type FormEvent, type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react'
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
import { ActionTooltip } from '../../ui/ActionTooltip'

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
  const tabTitle = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.title ?? '')
  const faviconDataUrl = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.faviconDataUrl)
  const crashed = useViewerTabStore((s) => Boolean(findBrowserTab(s, currentThreadId, tabId)?.crashed))
  const blockedMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.blockedMessage ?? '')
  const downloadMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.downloadMessage ?? '')
  const errorMessage = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.errorMessage ?? '')
  const automationActive = useViewerTabStore((s) => Boolean(findBrowserTab(s, currentThreadId, tabId)?.automationActive))
  const automationSessionName = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.automationSessionName ?? '')
  const lastAutomationAction = useViewerTabStore((s) => findBrowserTab(s, currentThreadId, tabId)?.lastAutomationAction ?? '')
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const updateBrowserTab = useViewerTabStore((s) => s.updateBrowserTab)

  const [urlInput, setUrlInput] = useState('')
  const [editingAddress, setEditingAddress] = useState(false)
  const bodyRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (editingAddress) return
    setUrlInput(currentUrl)
  }, [currentUrl, editingAddress])

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
    void window.api.workspace.viewer.browser.setVisible({ tabId, visible: true })
    void window.api.workspace.viewer.browser.setActive({ tabId })
    return () => {
      void window.api.workspace.viewer.browser.setVisible({ tabId, visible: false })
    }
  }, [existsTab, tabId])

  const pushBounds = useCallback(() => {
    if (!bodyRef.current) return
    const rect = bodyRef.current.getBoundingClientRect()
    if (rect.width <= 1 || rect.height <= 1) return
    void window.api.workspace.viewer.browser.setBounds({
      tabId,
      x: Math.round(rect.left),
      y: Math.round(rect.top),
      width: Math.round(rect.width),
      height: Math.round(rect.height)
    })
  }, [tabId])

  useEffect(() => {
    if (!existsTab) return
    pushBounds()
    const resizeObserver = new ResizeObserver(() => {
      pushBounds()
    })
    if (bodyRef.current) {
      resizeObserver.observe(bodyRef.current)
    }
    const onResize = () => pushBounds()
    const onScroll = () => pushBounds()
    window.addEventListener('resize', onResize)
    window.addEventListener('scroll', onScroll, true)
    return () => {
      resizeObserver.disconnect()
      window.removeEventListener('resize', onResize)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [existsTab, pushBounds, tabId])

  const toolbarDisabled = !existsTab
  const title = useMemo(() => {
    if (!existsTab) return t('viewer.newBrowserTab')
    if (tabTitle.trim()) return tabTitle
    try {
      return new URL(currentUrl).host || t('viewer.newBrowserTab')
    } catch {
      return t('viewer.newBrowserTab')
    }
  }, [currentUrl, existsTab, tabTitle, t])

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
          <input
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
              width: '100%',
              height: '26px',
              border: '1px solid var(--border-default)',
              borderRadius: '4px',
              background: 'var(--bg-tertiary)',
              color: 'var(--text-primary)',
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
            backgroundColor: 'var(--bg-warning, rgba(255,200,0,0.08))'
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

      <div style={{
        padding: '4px 10px',
        borderBottom: '1px solid var(--border-default)',
        fontSize: '12px',
        color: 'var(--text-secondary)',
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        flexShrink: 0
      }}>
        {faviconDataUrl
          ? <img src={faviconDataUrl} alt="" width={14} height={14} style={{ borderRadius: '2px' }} />
          : <Globe size={14} aria-hidden style={{ display: 'block' }} />}
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {automationSessionName.trim() || title}
        </span>
        {automationActive && lastAutomationAction && (
          <span style={{
            flexShrink: 0,
            maxWidth: '120px',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            color: 'var(--accent)',
            fontSize: '11px'
          }}>
            {lastAutomationAction}
          </span>
        )}
      </div>

      <div
        ref={bodyRef}
        style={{
          position: 'relative',
          flex: 1,
          overflow: 'hidden',
          background: 'var(--bg-primary)'
        }}
      />
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
    <ActionTooltip label={title} disabledReason={disabled ? title : undefined} placement="bottom">
      <button
        type="button"
        aria-label={title}
        onClick={onClick}
        disabled={disabled}
        style={{
          width: '24px',
          height: '24px',
          border: 'none',
          borderRadius: '4px',
          background: 'transparent',
          color: disabled ? 'var(--text-disabled, rgba(255,255,255,0.3))' : 'var(--text-secondary)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: disabled ? 'default' : 'pointer',
          padding: 0
        }}
        onMouseEnter={(e) => {
          if (disabled) return
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        {children}
      </button>
    </ActionTooltip>
  )
}
