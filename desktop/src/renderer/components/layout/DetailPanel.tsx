import { useRef, useState, lazy, Suspense, type CSSProperties, type ReactNode } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import type { SystemDetailTab } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { FilePlus2, FolderOpen, ListChecks, SquareTerminal, Plus, X, Globe, PanelRightClose, MousePointer2, Bot, Workflow } from 'lucide-react'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { SubagentsTab } from '../detail/SubagentsTab'
import { isSubAgentChildRunning, useSubAgentStore } from '../../stores/subAgentStore'
import { AddTabPopupWindow } from '../detail/AddTabPopupWindow'
import { DetailPanelLauncher } from '../detail/DetailPanelLauncher'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import {
  resolveAddTabPopupPayload,
  type AddTabMenuAction,
  type AddTabPopupPayload,
  type AddTabMenuRequest
} from '../../../shared/addTabMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS, formatShortcutParts, type ShortcutSpec } from '../ui/shortcutKeys'
import { performAddTabAction } from '../../utils/detailTabActions'
import { IconButton } from '../ui/IconButton'

interface DetailPanelProps {
  workspacePath?: string
  remoteWorkspace?: boolean
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

export function DetailPanel({
  workspacePath = '',
  remoteWorkspace = false
}: DetailPanelProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
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
  const runningSubagentCount = useSubAgentStore((s) =>
    activeThreadId
      ? (s.childrenByParent.get(activeThreadId) ?? []).filter(isSubAgentChildRunning).length
      : 0
  )

  const addButtonRef = useRef<HTMLButtonElement>(null)
  const [addTabMenu, setAddTabMenu] = useState<AddTabPopupPayload | null>(null)

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
    if (remoteWorkspace && (action === 'openFile' || action === 'newTerminal' || action === 'newChanges')) {
      return
    }
    performAddTabAction(action, { threadId: activeThreadId, workspacePath, t })
  }

  const handleOpenAddTabMenu = (): void => {
    if (addTabMenu) {
      setAddTabMenu(null)
      return
    }
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
      locale,
      items: [
        {
          action: 'openFile',
          label: t('detailPanel.addTabOpenFile'),
          enabled: canOpenWorkspaceTab && !remoteWorkspace
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
          enabled: canOpenWorkspaceTab && !remoteWorkspace
        },
        ...(openSystemTabs.includes('changes')
          ? []
          : [{
              action: 'newChanges' as const,
              label: t('detailPanel.tabChanges'),
              shortcut: fmt(ACTION_SHORTCUTS.viewChanges),
              enabled: !remoteWorkspace
            }]),
        ...(openSystemTabs.includes('plan')
          ? []
          : [{
              action: 'newPlan' as const,
              label: t('detailPanel.tabPlan'),
              shortcut: fmt(ACTION_SHORTCUTS.newPlan),
              enabled: true
            }]),
        ...(openSystemTabs.includes('subagents')
          ? []
          : [{
              action: 'newSubagents' as const,
              label: t('detailPanel.tabSubagents'),
              enabled: true
            }])
      ]
    }
    setAddTabMenu(resolveAddTabPopupPayload(request, {
      width: window.innerWidth,
      height: window.innerHeight
    }))
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
    },
    subagents: {
      label: t('detailPanel.tabSubagents'),
      icon: <Bot size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: runningSubagentCount > 0 ? runningSubagentCount : undefined
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
      {/* Spacing separates the tab bar from the panel body; no divider. */}
      <div
        style={{
          display: 'flex',
          alignItems: 'stretch',
          gap: '6px',
          height: 'var(--chrome-header-height)',
          boxSizing: 'border-box',
          flexShrink: 0,
          paddingLeft: '4px',
          overflowX: 'auto',
          overflowY: 'hidden',
          scrollbarWidth: 'none'
        }}
      >
        {/* System tabs (Changes / Checks) — icon + label, so the label stays a
            click target; the icon slot becomes the close button on hover. */}
        {openSystemTabs.map((id) => {
          const meta = systemTabMeta[id]
          return (
            <DetailPanelTab
              key={id}
              active={activeSystemId === id}
              title={meta.label}
              icon={meta.icon}
              label={meta.label}
              badge={meta.badge}
              closeLabel={`${t('viewer.close')} ${meta.label}`}
              onActivate={() => setActiveDetailTab(id)}
              onClose={() => handleCloseSystemTab(id)}
            />
          )
        })}

        {viewerTabs.map((tab) => {
          const automationActive = tab.kind === 'browser' && tab.automationActive === true
          const icon = tab.kind === 'browser'
            ? (automationActive
                ? <MousePointer2 size={14} strokeWidth={2} aria-hidden style={{ display: 'block', color: 'var(--accent)' }} />
                : browserTabIcon(tab.faviconDataUrl))
            : tab.kind === 'terminal'
              ? <SquareTerminal size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
              : tab.kind === 'workflow'
                ? <Workflow size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
              : tab.kind === 'files'
                ? <FolderOpen size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                : <FileTypeIcon path={tab.relativePath} size={14} />
          return (
            <DetailPanelTab
              key={tab.id}
              className={automationActive ? 'dotcraft-automation-viewer-tab' : undefined}
              active={activeViewerId === tab.id}
              title={tab.kind === 'browser' ? tab.currentUrl : tab.kind === 'terminal' ? tab.cwd : tab.kind === 'workflow' ? tab.label : tab.kind === 'files' ? tab.label : tab.absolutePath}
              icon={icon}
              label={tab.label}
              closeLabel={`${t('viewer.close')} ${tab.label}`}
              onActivate={() => setActiveViewerTab(tab.id)}
              onClose={() => handleCloseViewerTab(tab.id)}
              maxWidth={160}
            />
          )
        })}

        <div style={{ display: 'flex', alignItems: 'center', height: '100%' }}>
          <IconButton
            ref={addButtonRef}
            size={28}
            label={t('detailPanel.addTab')}
            tooltipLabel={t('detailPanel.addTab')}
            tooltipPlacement="bottom"
            onClick={() => {
              void handleOpenAddTabMenu()
            }}
            icon={<Plus size={14} aria-hidden style={{ display: 'block' }} />}
          />
        </div>

        <AddTabPopupWindow
          payload={addTabMenu}
          onResolve={(action) => {
            setAddTabMenu(null)
            handleAddTabAction(action)
          }}
        />

        <div style={{ flex: 1 }} />

        <IconButton
          size={28}
          label={t('detailPanel.closeAria')}
          tooltipLabel={t('detailPanel.closeAria')}
          shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
          tooltipPlacement="bottom"
          tooltipWrapperStyle={{ alignSelf: 'center' }}
          onClick={toggleDetailPanel}
          style={{ marginRight: '4px' }}
          icon={<PanelRightClose size={16} aria-hidden />}
        />
      </div>

      {/* No border or shadow here: ThreePanel paints the vertical arm of the T
          divider after its clipped content surface. */}
      <div
        style={{
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {isLauncher && (
          <DetailPanelLauncher
            onAction={handleAddTabAction}
            canOpenWorkspaceTab={Boolean(activeThreadId && workspacePath)}
            remoteWorkspace={remoteWorkspace}
          />
        )}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'changes' && !remoteWorkspace && (
          <ChangesTab workspacePath={workspacePath} />
        )}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'plan' && <PlanTab />}
        {activeDetailTab.kind === 'system' && activeDetailTab.id === 'subagents' && <SubagentsTab />}
        {activeDetailTab.kind === 'viewer' && (
          <ViewerTabContainer tabId={activeDetailTab.id} />
        )}
      </div>

    </div>
  )
}

/**
 * The leading slot shows the tab icon and swaps to a close button on hover, so
 * closing reuses that footprint instead of a separate trailing button. System
 * tabs render icon-only (no `label`); viewer tabs render icon + label.
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
  const tab = (
    <div
      className={className ? `dotcraft-detail-panel-tab ${className}` : 'dotcraft-detail-panel-tab'}
      role="tab"
      aria-selected={active}
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
        padding: label === undefined ? '0 8px' : undefined,
        ...(maxWidth ? { maxWidth: `${maxWidth}px` } : {}),
        ...style
      }}
    >
      <span className="dotcraft-detail-panel-tab__leading">
        {hovered ? (
          <IconButton
            size={16}
            radius={3}
            aria-label={closeLabel}
            label={closeLabel}
            onClick={(e) => {
              e.stopPropagation()
              onClose()
            }}
            icon={<X size={12} aria-hidden style={{ display: 'block' }} />}
          />
        ) : icon}
      </span>
      {label !== undefined && (
        <span className="dotcraft-detail-panel-tab__label">
          {label}
        </span>
      )}
      {badge !== undefined && (
        <span className="dotcraft-detail-panel-tab__badge">
          {badge}
        </span>
      )}
    </div>
  )

  if (!title) return tab
  return (
    <ActionTooltip label={title} placement="bottom" wrapperStyle={{ height: '100%' }}>
      {tab}
    </ActionTooltip>
  )
}

const LazyViewerTab = lazy(() => import('../detail/ViewerTab').then((m) => ({ default: m.ViewerTab })))

/** Lazy-loads the ViewerTab component to keep the code view and its grammars out of the initial bundle. */
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
