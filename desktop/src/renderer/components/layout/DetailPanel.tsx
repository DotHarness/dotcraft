import { useRef, lazy, Suspense } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { FilePlus2, ListChecks, SquareTerminal, Plus, X, Globe, PanelRightClose, MousePointer2 } from 'lucide-react'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import type { AddTabMenuAction, AddTabMenuRequest } from '../../../shared/addTabMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

interface DetailPanelProps {
  workspacePath?: string
}

function browserTabIcon(faviconDataUrl?: string): JSX.Element {
  if (faviconDataUrl) {
    return (
      <img
        src={faviconDataUrl}
        alt=""
        width={14}
        height={14}
        style={{ display: 'block', borderRadius: '2px', flexShrink: 0 }}
      />
    )
  }
  return <Globe size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

/**
 * Detail Panel — system tabs (Changes / Plan) + dynamic viewer tabs.
 *
 * Tab bar layout:
 *   [changes] [plan] │ [viewer1] [viewer2] … │ [+] [flex] [×]
 */
export function DetailPanel({ workspacePath = '' }: DetailPanelProps): JSX.Element {
  const t = useT()
  const {
    activeDetailTab,
    lastActiveSystemTab,
    setActiveDetailTab,
    setActiveViewerTab,
    closeViewerTab,
    toggleDetailPanel,
    setQuickOpenVisible,
    setDetailPanelVisible
  } = useUIStore()

  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const viewerTabs = useViewerTabStore((s) => s.getThreadState(s.currentThreadId ?? '').tabs)
  const closeViewerTabInStore = useViewerTabStore((s) => s.closeTab)
  const openBrowser = useViewerTabStore((s) => s.openBrowser)
  const openTerminal = useViewerTabStore((s) => s.openTerminal)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)

  const changedFiles = useConversationStore((s) => s.changedFiles)

  const changedFileCount = changedFiles.size

  const addButtonRef = useRef<HTMLButtonElement>(null)

  const isSystemTab = activeDetailTab.kind === 'system'
  const activeSystemId = isSystemTab ? activeDetailTab.id : lastActiveSystemTab
  const activeViewerId = !isSystemTab ? activeDetailTab.id : null

  const handleCloseViewerTab = (tabId: string): void => {
    if (!currentThreadId) return
    const closing = viewerTabs.find((t) => t.id === tabId)
    if (closing?.kind === 'browser') {
      void window.api.workspace.viewer.browser.destroy({ tabId: closing.id })
    } else if (closing?.kind === 'terminal') {
      void window.api.workspace.viewer.terminal.dispose({ tabId: closing.id })
    }
    closeViewerTabInStore(currentThreadId, tabId)

    // If we just closed the active tab, we need to figure out new active tab
    const remaining = viewerTabs.filter((t) => t.id !== tabId)
    const wasActive = activeDetailTab.kind === 'viewer' && activeDetailTab.id === tabId

    if (wasActive) {
      if (remaining.length > 0) {
        // Nearest neighbor was already handled by the store — we need to read it
        const idx = viewerTabs.findIndex((t) => t.id === tabId)
        const newActive = idx > 0
          ? remaining[idx - 1]
          : remaining[0]
        if (newActive) {
          setActiveViewerTab(newActive.id)
        } else {
          closeViewerTab()
        }
      } else {
        closeViewerTab()
      }
    }
  }

  const handleAddTabAction = (action: AddTabMenuAction | null): void => {
    if (action === 'openFile') {
      setQuickOpenVisible(true)
      setDetailPanelVisible(true)
      return
    }

    if (!activeThreadId || !workspacePath) return

    if (action === 'newBrowser') {
      const tabId = openBrowser({
        threadId: activeThreadId,
        initialLabel: t('viewer.newBrowserTab')
      })
      setActiveViewerTab(tabId)
      void window.api.workspace.viewer.browser.create({
        tabId,
        threadId: activeThreadId,
        workspacePath
      })
      return
    }

    if (action === 'newTerminal') {
      const tabId = openTerminal({
        threadId: activeThreadId,
        cwd: workspacePath,
        initialLabel: t('viewer.newTerminalTab')
      })
      setActiveViewerTab(tabId)
    }
  }

  const handleOpenAddTabMenu = async (): Promise<void> => {
    const anchor = addButtonRef.current?.getBoundingClientRect()
    if (!anchor) return
    const canOpenWorkspaceTab = Boolean(activeThreadId && workspacePath)
    const shortcutText =
      window.api.platform === 'darwin'
        ? t('detailPanel.addTabOpenFileShortcut').replace('Ctrl', 'Cmd')
        : t('detailPanel.addTabOpenFileShortcut')
    const theme = document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'
    const request: AddTabMenuRequest = {
      x: anchor.left,
      y: anchor.bottom + 4,
      anchor: {
        left: anchor.left,
        top: anchor.top,
        right: anchor.right,
        bottom: anchor.bottom
      },
      theme,
      items: [
        {
          action: 'openFile',
          label: t('detailPanel.addTabOpenFile'),
          shortcut: shortcutText,
          enabled: true
        },
        {
          action: 'newBrowser',
          label: t('detailPanel.addTabNewBrowser'),
          enabled: canOpenWorkspaceTab
        },
        {
          action: 'newTerminal',
          label: t('detailPanel.addTabNewTerminal'),
          enabled: canOpenWorkspaceTab
        }
      ]
    }
    const action = await window.api.menu.popupAddTabMenu(request)
    handleAddTabAction(action)
  }

  const systemTabs = [
    {
      id: 'changes' as const,
      label: t('detailPanel.tabChanges'),
      icon: <FilePlus2 size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: changedFileCount > 0 ? changedFileCount : undefined
    },
    {
      id: 'plan' as const,
      label: t('detailPanel.tabPlan'),
      icon: <ListChecks size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        background: 'transparent'
      }}
    >
      {/* ── Tab bar ── */}
      {/* No borderBottom here — the unified header line is painted at the
          ThreePanel level so it stays continuous across the DragHandle. */}
      <div
        style={{
          display: 'flex',
          alignItems: 'stretch',
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box',
          flexShrink: 0,
          paddingLeft: '4px',
          overflowX: 'auto',
          overflowY: 'hidden',
          scrollbarWidth: 'none'
        }}
      >
        {/* System tabs */}
        {systemTabs.map((tab) => {
          const isActive = isSystemTab && activeSystemId === tab.id
          return (
            <ActionTooltip key={tab.id} label={tab.label} placement="bottom" wrapperStyle={{ height: '100%' }}>
              <button
                onClick={() => setActiveDetailTab(tab.id)}
                aria-label={tab.label}
                style={{
                display: 'flex',
                alignItems: 'center',
                gap: '5px',
                height: '100%',
                padding: '0 10px',
                fontSize: '13px',
                fontWeight: isActive ? 500 : 400,
                color: isActive ? 'var(--text-primary)' : 'var(--text-secondary)',
                backgroundColor: 'transparent',
                border: 'none',
                boxSizing: 'border-box',
                boxShadow: isActive ? 'inset 0 -2px 0 var(--accent)' : 'none',
                cursor: 'pointer',
                flexShrink: 0,
                transition: 'color 100ms ease, box-shadow 100ms ease'
              }}
            >
              {tab.icon}
              {tab.badge !== undefined && (
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    minWidth: '16px',
                    height: '16px',
                    padding: '0 4px',
                    borderRadius: '8px',
                    background: isActive ? 'var(--accent)' : 'var(--bg-tertiary)',
                    color: isActive ? '#ffffff' : 'var(--text-secondary)',
                    fontSize: '10px',
                    fontWeight: 500
                  }}
                >
                  {tab.badge}
                </span>
              )}
              </button>
            </ActionTooltip>
          )
        })}

        {/* Separator — only visible when viewer tabs exist */}
        {viewerTabs.length > 0 && (
          <div
            aria-hidden
            style={{
              alignSelf: 'center',
              width: '1px',
              height: '16px',
              backgroundColor: 'var(--glass-border)',
              flexShrink: 0,
              margin: '0 4px'
            }}
          />
        )}

        {/* Viewer tabs */}
        {viewerTabs.map((tab) => {
          const isActive = !isSystemTab && activeViewerId === tab.id
          const automationActive = tab.kind === 'browser' && tab.automationActive === true
          return (
            <div
              key={tab.id}
              className={automationActive ? 'dotcraft-automation-viewer-tab' : undefined}
              role="tab"
              aria-selected={isActive}
              title={
                tab.kind === 'browser'
                  ? tab.currentUrl
                  : tab.kind === 'terminal'
                    ? tab.cwd
                    : tab.absolutePath
              }
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '4px',
                height: '100%',
                padding: '0 6px 0 10px',
                fontSize: '13px',
                fontWeight: isActive ? 500 : 400,
                color: isActive ? 'var(--text-primary)' : 'var(--text-secondary)',
                backgroundColor: automationActive ? 'rgba(47, 138, 245, 0.10)' : 'transparent',
                backgroundImage: automationActive
                  ? 'repeating-linear-gradient(90deg, rgba(47,138,245,0.05) 0px, rgba(47,138,245,0.18) 24px, rgba(47,138,245,0.05) 48px, rgba(47,138,245,0.05) 96px)'
                  : 'none',
                backgroundSize: automationActive ? '96px 100%' : undefined,
                animation: automationActive ? 'dotcraft-automation-tab-flow 1.8s linear infinite' : undefined,
                borderRadius: automationActive ? '6px' : undefined,
                boxSizing: 'border-box',
                boxShadow: isActive ? 'inset 0 -2px 0 var(--accent)' : 'none',
                cursor: 'pointer',
                flexShrink: 0,
                transition: 'color 100ms ease, box-shadow 100ms ease',
                userSelect: 'none',
                maxWidth: '160px'
              }}
              onClick={() => setActiveViewerTab(tab.id)}
              onAuxClick={(e) => {
                // Middle-click to close
                if (e.button === 1) {
                  e.preventDefault()
                  handleCloseViewerTab(tab.id)
                }
              }}
            >
              {tab.kind === 'browser'
                ? automationActive
                  ? <MousePointer2 size={14} strokeWidth={2} aria-hidden style={{ display: 'block', color: 'var(--accent)' }} />
                  : browserTabIcon(tab.faviconDataUrl)
                : tab.kind === 'terminal'
                  ? <SquareTerminal size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                  : <FileTypeIcon path={tab.relativePath} size={14} />}
              <span
                style={{
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  maxWidth: '100px'
                }}
              >
                {tab.label}
              </span>
              <ActionTooltip label={t('viewer.close')} placement="bottom">
                <button
                  onClick={(e) => {
                    e.stopPropagation()
                    handleCloseViewerTab(tab.id)
                  }}
                  aria-label={`${t('viewer.close')} ${tab.label}`}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    width: '16px',
                    height: '16px',
                    borderRadius: '3px',
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--text-secondary)',
                    cursor: 'pointer',
                    padding: 0,
                    flexShrink: 0,
                    opacity: isActive ? 1 : 0
                  }}
                  onMouseEnter={(e) => {
                    ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.1))'
                    ;(e.currentTarget as HTMLButtonElement).style.opacity = '1'
                  }}
                  onMouseLeave={(e) => {
                    ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                    ;(e.currentTarget as HTMLButtonElement).style.opacity = isActive ? '1' : '0'
                  }}
              >
                  <X size={10} aria-hidden style={{ display: 'block' }} />
                </button>
              </ActionTooltip>
            </div>
          )
        })}

        {/* Add tab (+) button */}
        <ActionTooltip label={t('detailPanel.addTab')} placement="bottom" wrapperStyle={{ height: '100%' }}>
          <button
            ref={addButtonRef}
            onClick={() => {
              void handleOpenAddTabMenu()
            }}
            aria-label={t('detailPanel.addTab')}
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              height: '100%',
              padding: '0 6px',
              border: 'none',
              background: 'transparent',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              flexShrink: 0
            }}
        >
            <Plus size={14} aria-hidden style={{ display: 'block' }} />
          </button>
        </ActionTooltip>

        <div style={{ flex: 1 }} />

        {/* Close panel button — ghost icon, mirrors ThreadHeader's open-panel button */}
        <ActionTooltip
          label={t('detailPanel.closeAria')}
          shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
          placement="bottom"
          wrapperStyle={{ alignSelf: 'center' }}
        >
          <button
            onClick={toggleDetailPanel}
            aria-label={t('detailPanel.closeAria')}
            style={{
              alignSelf: 'center',
              width: '28px',
              height: '28px',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              padding: 0,
              border: 'none',
              borderRadius: '6px',
              backgroundColor: 'transparent',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              flexShrink: 0,
              marginRight: '4px',
              transition: 'background-color 100ms ease, color 100ms ease'
            }}
            onMouseEnter={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-tertiary)'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
            }}
            onMouseLeave={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
              ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
            }}
        >
            <PanelRightClose size={16} aria-hidden />
          </button>
        </ActionTooltip>
      </div>

      {/* ── Panel body ──
          The 1px inset shadow on the left draws the vertical arm of the
          T divider, starting exactly below the overlay header
          line. Using inset shadow (not borderLeft) avoids a 1px layout shift. */}
      <div
        style={{
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: 'inset 1px 0 0 0 var(--detail-divider-border, var(--glass-border))'
        }}
      >
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'changes' && (
          <ChangesTab workspacePath={workspacePath} />
        )}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'plan' && <PlanTab />}
        {activeDetailTab.kind === 'viewer' && (
          <ViewerTabContainer tabId={activeDetailTab.id} />
        )}
      </div>

    </div>
  )
}

const LazyViewerTab = lazy(() => import('../detail/ViewerTab').then((m) => ({ default: m.ViewerTab })))

/** Lazy-loads the ViewerTab component to avoid shipping Monaco in the initial bundle. */
function ViewerTabContainer({ tabId }: { tabId: string }): JSX.Element {
  return (
    <Suspense fallback={
      <div style={{ padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
        Loading…
      </div>
    }>
      <LazyViewerTab tabId={tabId} />
    </Suspense>
  )
}
