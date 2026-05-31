import { useRef, useState, lazy, Suspense, type CSSProperties, type ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import type { SystemDetailTab } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { FilePlus2, ListChecks, SquareTerminal, Plus, X, Globe, PanelRightClose, MousePointer2 } from 'lucide-react'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { DetailPanelLauncher } from '../detail/DetailPanelLauncher'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import type { AddTabMenuAction, AddTabMenuRequest } from '../../../shared/addTabMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS, formatShortcutParts, type ShortcutSpec } from '../ui/shortcutKeys'
import { performAddTabAction } from '../../utils/detailTabActions'

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
    openSystemTabs,
    setActiveDetailTab,
    closeSystemTab,
    setActiveViewerTab,
    closeViewerTab,
    toggleDetailPanel
  } = useUIStore()

  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const viewerTabs = useViewerTabStore((s) => s.getThreadState(s.currentThreadId ?? '').tabs)
  const closeViewerTabInStore = useViewerTabStore((s) => s.closeTab)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)

  const changedFiles = useConversationStore((s) => s.changedFiles)

  const changedFileCount = changedFiles.size

  const addButtonRef = useRef<HTMLButtonElement>(null)

  const activeSystemId = activeDetailTab.kind === 'system' ? activeDetailTab.id : null
  const activeViewerId = activeDetailTab.kind === 'viewer' ? activeDetailTab.id : null
  const isLauncher = activeDetailTab.kind === 'launcher'

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

  const handleCloseSystemTab = (id: SystemDetailTab): void => {
    // If this was the active tab and no other system tab remains, fall back to
    // the first viewer tab (else the launcher) — the store resolves the choice.
    closeSystemTab(id, viewerTabs[0]?.id ?? null)
  }

  const handleAddTabAction = (action: AddTabMenuAction | null): void => {
    if (!action) return
    performAddTabAction(action, { threadId: activeThreadId, workspacePath, t })
  }

  const handleOpenAddTabMenu = async (): Promise<void> => {
    const anchor = addButtonRef.current?.getBoundingClientRect()
    if (!anchor) return
    const canOpenWorkspaceTab = Boolean(activeThreadId && workspacePath)
    const fmt = (spec: ShortcutSpec): string => formatShortcutParts(spec).join('+')
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
          shortcut: fmt(ACTION_SHORTCUTS.quickOpen),
          enabled: true
        },
        {
          action: 'newBrowser',
          label: t('detailPanel.addTabNewBrowser'),
          shortcut: fmt(ACTION_SHORTCUTS.newBrowserTab),
          enabled: canOpenWorkspaceTab
        },
        {
          action: 'newTerminal',
          label: t('detailPanel.addTabNewTerminal'),
          shortcut: fmt(ACTION_SHORTCUTS.newTerminalTab),
          enabled: canOpenWorkspaceTab
        },
        // Diff / Progress are dropped from the menu once already open.
        ...(openSystemTabs.includes('changes')
          ? []
          : [{
              action: 'newChanges' as const,
              label: t('detailPanel.tabChanges'),
              shortcut: fmt(ACTION_SHORTCUTS.viewChanges),
              enabled: true
            }]),
        ...(openSystemTabs.includes('plan')
          ? []
          : [{
              action: 'newPlan' as const,
              label: t('detailPanel.tabPlan'),
              enabled: true
            }])
      ]
    }
    const action = await window.api.menu.popupAddTabMenu(request)
    handleAddTabAction(action)
  }

  const systemTabMeta: Record<SystemDetailTab, { label: string; icon: JSX.Element; badge?: number }> = {
    changes: {
      label: t('detailPanel.tabChanges'),
      icon: <FilePlus2 size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: changedFileCount > 0 ? changedFileCount : undefined
    },
    plan: {
      label: t('detailPanel.tabPlan'),
      icon: <ListChecks size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    }
  }

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
        {/* System tabs (Diff / Progress) — icon-only; the icon slot becomes the close button on hover. */}
        {openSystemTabs.map((id) => {
          const meta = systemTabMeta[id]
          return (
            <DetailPanelTab
              key={id}
              active={activeSystemId === id}
              title={meta.label}
              icon={meta.icon}
              badge={meta.badge}
              closeLabel={`${t('viewer.close')} ${meta.label}`}
              onActivate={() => setActiveDetailTab(id)}
              onClose={() => handleCloseSystemTab(id)}
            />
          )
        })}

        {/* Separator — only visible when both system and viewer tabs exist */}
        {openSystemTabs.length > 0 && viewerTabs.length > 0 && (
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

        {/* Viewer tabs — label + leading icon slot that becomes the close button on hover. */}
        {viewerTabs.map((tab) => {
          const automationActive = tab.kind === 'browser' && tab.automationActive === true
          const icon = tab.kind === 'browser'
            ? (automationActive
                ? <MousePointer2 size={14} strokeWidth={2} aria-hidden style={{ display: 'block', color: 'var(--accent)' }} />
                : browserTabIcon(tab.faviconDataUrl))
            : tab.kind === 'terminal'
              ? <SquareTerminal size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
              : <FileTypeIcon path={tab.relativePath} size={14} />
          const automationStyle: CSSProperties = automationActive
            ? {
                backgroundColor: 'rgba(47, 138, 245, 0.10)',
                backgroundImage: 'repeating-linear-gradient(90deg, rgba(47,138,245,0.05) 0px, rgba(47,138,245,0.18) 24px, rgba(47,138,245,0.05) 48px, rgba(47,138,245,0.05) 96px)',
                backgroundSize: '96px 100%',
                animation: 'dotcraft-automation-tab-flow 1.8s linear infinite',
                borderRadius: '6px'
              }
            : {}
          return (
            <DetailPanelTab
              key={tab.id}
              className={automationActive ? 'dotcraft-automation-viewer-tab' : undefined}
              active={activeViewerId === tab.id}
              title={tab.kind === 'browser' ? tab.currentUrl : tab.kind === 'terminal' ? tab.cwd : tab.absolutePath}
              icon={icon}
              label={tab.label}
              closeLabel={`${t('viewer.close')} ${tab.label}`}
              onActivate={() => setActiveViewerTab(tab.id)}
              onClose={() => handleCloseViewerTab(tab.id)}
              maxWidth={160}
              style={automationStyle}
            />
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
        {isLauncher && (
          <DetailPanelLauncher
            onAction={handleAddTabAction}
            canOpenWorkspaceTab={Boolean(activeThreadId && workspacePath)}
          />
        )}
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

/**
 * A single detail-panel tab. The leading slot shows the tab icon and, while the
 * tab is hovered, swaps to a close (×) button — so closing reuses the icon's
 * footprint instead of a separate trailing button, which keeps tabs compact.
 * System tabs render icon-only (no `label`); viewer tabs render icon + label.
 * Local hover state keeps re-renders scoped to the hovered tab.
 */
function DetailPanelTab({
  active,
  title,
  icon,
  label,
  badge,
  closeLabel,
  onActivate,
  onClose,
  className,
  style,
  maxWidth
}: {
  active: boolean
  title: string
  icon: ReactNode
  label?: string
  badge?: number
  closeLabel: string
  onActivate: () => void
  onClose: () => void
  className?: string
  style?: CSSProperties
  maxWidth?: number
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <div
      className={className}
      role="tab"
      aria-selected={active}
      title={title}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onClick={onActivate}
      onAuxClick={(e) => {
        if (e.button === 1) {
          e.preventDefault()
          onClose()
        }
      }}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        height: '100%',
        padding: label === undefined ? '0 8px' : '0 10px',
        fontSize: '13px',
        fontWeight: active ? 500 : 400,
        color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
        backgroundColor: 'transparent',
        boxSizing: 'border-box',
        boxShadow: active ? 'inset 0 -2px 0 var(--accent)' : 'none',
        cursor: 'pointer',
        flexShrink: 0,
        userSelect: 'none',
        transition: 'color 100ms ease, box-shadow 100ms ease',
        ...(maxWidth ? { maxWidth: `${maxWidth}px` } : {}),
        ...style
      }}
    >
      <span style={tabLeadingSlotStyle}>
        {hovered ? (
          <button
            type="button"
            aria-label={closeLabel}
            title={closeLabel}
            onClick={(e) => {
              e.stopPropagation()
              onClose()
            }}
            style={tabCloseButtonStyle}
            onMouseEnter={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover)'
            }}
            onMouseLeave={(e) => {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
            }}
          >
            <X size={12} aria-hidden style={{ display: 'block' }} />
          </button>
        ) : icon}
      </span>
      {label !== undefined && (
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '120px' }}>
          {label}
        </span>
      )}
      {badge !== undefined && (
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            minWidth: '16px',
            height: '16px',
            padding: '0 4px',
            borderRadius: '8px',
            background: active ? 'var(--accent)' : 'var(--bg-tertiary)',
            color: active ? '#ffffff' : 'var(--text-secondary)',
            fontSize: '10px',
            fontWeight: 500
          }}
        >
          {badge}
        </span>
      )}
    </div>
  )
}

const tabLeadingSlotStyle: CSSProperties = {
  position: 'relative',
  width: '16px',
  height: '16px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

const tabCloseButtonStyle: CSSProperties = {
  width: '16px',
  height: '16px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: 'none',
  borderRadius: '3px',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  padding: 0
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
