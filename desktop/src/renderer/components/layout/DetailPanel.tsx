import { useRef, useState, lazy, Suspense } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { SystemDetailTab } from '../../stores/detailPanelStore'
import { useDetailPanelPresentation } from '../../stores/detailPanelPresentation'
import { useDetailPanelStore } from '../../stores/detailPanelStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { latestTurnDiff, turnPatchTotals } from '../../stores/turnDiffs'
import { FilePlus2, FolderOpen, ListChecks, Plus, Globe, MousePointer2, Bot, MessagesSquare, Workflow, CalendarClock, Maximize2, Minimize2, Link } from 'lucide-react'
import { TerminalGlyph } from '../ui/TerminalGlyph'
import { ChangesTab } from '../detail/ChangesTab'
import { PlanTab } from '../detail/PlanTab'
import { SubagentsTab } from '../detail/SubagentsTab'
import { BoardTab } from '../detail/BoardTab'
import { SourcesTab } from '../conversation/summary/SourcesTab'
import { isSubAgentChildWorking, useSubAgentStore } from '../../stores/subAgentStore'
import { useSubAgentTaskActivity } from '../../stores/subAgentTaskActivity'
import { AddTabPopupWindow } from '../detail/AddTabPopupWindow'
import { DetailPanelLauncher } from '../detail/DetailPanelLauncher'
import { SidebarIcon } from '../ui/AppIcons'
import { FileTypeIcon } from '../ui/FileTypeIcon'
import { SideChatTabIcon } from '../sideChat/SideChatTabIcon'
import { useCanOpenSideChat } from '../../utils/sideChat'
import {
  resolveAddTabPopupPayload,
  type AddTabMenuAction,
  type AddTabPopupPayload,
  type AddTabMenuRequest
} from '../../../shared/addTabMenu'
import { ACTION_SHORTCUTS, formatShortcutParts, type ShortcutSpec } from '../ui/shortcutKeys'
import { performAddTabAction } from '../../utils/detailTabActions'
import { IconButton } from '../ui/IconButton'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { DetailPanelTab } from './DetailPanelTab'
import { useDetailPanelTabClose, type DetailTabRef } from './useDetailPanelTabClose'
import { useDetailTabMenuItems } from './detailTabMenu'
import { useTabStripOverflow } from './useTabStripOverflow'
import { useHostFilesUnavailable } from '../../hooks/useHostFilesUnavailable'

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
  const hostFilesUnavailable = useHostFilesUnavailable()
  const {
    activeDetailTab,
    openSystemTabs,
    setActiveDetailTab,
    setActiveViewerTab,
    toggleDetailPanel,
    detailPanelFullView,
    detailPanelCompact,
    detailPanelVisible,
    toggleDetailPanelFullView
  } = { ...useDetailPanelPresentation(), ...useDetailPanelStore() }

  const currentThreadId = useDetailPanelStore((s) => s.currentThreadId)
  const viewerTabs = useDetailPanelStore((s) => s.getThreadState(s.currentThreadId ?? '').tabs)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)

  const fileCount = useConversationStore((s) => turnPatchTotals(latestTurnDiff(s.turnDiffs, s.turns)?.files ?? []).files)
  const subagents = useSubAgentStore((s) => s.childrenByParent)
  useThreadStore((s) => s.runtimeSnapshots)
  useSubAgentTaskActivity()
  const runningSubagentCount = activeThreadId
    ? (subagents.get(activeThreadId) ?? []).filter(isSubAgentChildWorking).length : 0

  const sideChatAvailable = useCanOpenSideChat()
  const boardAvailable = useConnectionStore((state) => state.capabilities?.subAgentSessions === true)
  const addButtonRef = useRef<HTMLButtonElement>(null)
  const [addTabMenu, setAddTabMenu] = useState<AddTabPopupPayload | null>(null)

  const systemTabsAvailable = activeThreadId != null
  const canOpenBoard = systemTabsAvailable && boardAvailable && !openSystemTabs.includes('board')
  const systemTabs = systemTabsAvailable ? openSystemTabs : []
  const canOpenWorkspaceTab = Boolean(currentThreadId && workspacePath)
  const activeSystemId = activeDetailTab.kind === 'system' && systemTabsAvailable ? activeDetailTab.id : null
  const activeViewerId = activeDetailTab.kind === 'viewer' ? activeDetailTab.id : null
  const isLauncher = activeDetailTab.kind === 'launcher' || (activeDetailTab.kind === 'system' && !systemTabsAvailable)

  const tabScrollerRef = useRef<HTMLDivElement>(null)
  const activeTabKey = activeSystemId ? `system:${activeSystemId}` : activeViewerId ? `viewer:${activeViewerId}` : null
  const tabEdges = useTabStripOverflow(
    tabScrollerRef,
    activeTabKey,
    [...systemTabs, ...viewerTabs.map((tab) => tab.id)].join('|')
  )

  const tabClose = useDetailPanelTabClose()
  const { closeTab } = tabClose
  const [tabMenu, setTabMenu] = useState<{ ref: DetailTabRef; position: ContextMenuPosition } | null>(null)
  const tabMenuItems = useDetailTabMenuItems(tabClose, remoteWorkspace)

  const handleAddTabAction = (action: AddTabMenuAction | null): void => {
    if (!action) return
    if ((action === 'openFile' && hostFilesUnavailable) || (action === 'newTerminal' && remoteWorkspace)) {
      return
    }
    performAddTabAction(action, { workspacePath, t })
  }

  const handleOpenAddTabMenu = (): void => {
    if (addTabMenu) {
      setAddTabMenu(null)
      return
    }
    const anchor = addButtonRef.current?.getBoundingClientRect()
    if (!anchor) return
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
          enabled: canOpenWorkspaceTab && !hostFilesUnavailable
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
        ...(!systemTabsAvailable || openSystemTabs.includes('changes')
          ? []
          : [{
              action: 'newChanges' as const,
              label: t('detailPanel.tabChanges'),
              shortcut: fmt(ACTION_SHORTCUTS.viewChanges),
              enabled: true
            }]),
        ...(!systemTabsAvailable || openSystemTabs.includes('plan')
          ? []
          : [{
              action: 'newPlan' as const,
              label: t('detailPanel.tabPlan'),
              shortcut: fmt(ACTION_SHORTCUTS.newPlan),
              enabled: true
            }]),
        ...(!systemTabsAvailable || openSystemTabs.includes('subagents')
          ? []
          : [{
              action: 'newSubagents' as const,
              label: t('detailPanel.tabSubagents'),
              enabled: true
            }]),
        ...(!canOpenBoard
          ? []
          : [{
              action: 'newBoard' as const,
              label: t('detailPanel.tabBoard'),
              enabled: true
            }]),
        ...(!sideChatAvailable
          ? []
          : [{
              action: 'newSideChat' as const,
              label: t('detailPanel.addTabSideChat'),
              shortcut: fmt(ACTION_SHORTCUTS.newSideChat),
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
      badge: fileCount > 0 ? fileCount : undefined
    },
    plan: {
      label: t('detailPanel.tabPlan'),
      icon: <ListChecks size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    },
    subagents: {
      label: t('detailPanel.tabSubagents'),
      icon: <Bot size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />,
      badge: runningSubagentCount > 0 ? runningSubagentCount : undefined
    },
    board: {
      label: t('detailPanel.tabBoard'),
      icon: <MessagesSquare size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
    },
    sources: {
      label: t('summary.sources'),
      icon: <Link size={16} strokeWidth={2} aria-hidden />
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
      <div className="dotcraft-detail-panel-header">
        <div className="dotcraft-detail-panel-header__strip">
          <div
            ref={tabScrollerRef}
            className="dotcraft-detail-panel-tabs"
            role="tablist"
            data-overflow-start={tabEdges.start ? 'true' : undefined}
            data-overflow-end={tabEdges.end ? 'true' : undefined}
          >
            {systemTabs.map((id) => {
              const meta = systemTabMeta[id]
              return (
                <DetailPanelTab
                  key={id}
                  tabKey={`system:${id}`}
                  active={activeSystemId === id}
                  title={meta.label}
                  icon={meta.icon}
                  label={meta.label}
                  badge={meta.badge}
                  closeLabel={`${t('viewer.close')} ${meta.label}`}
                  onActivate={() => setActiveDetailTab(id)}
                  onClose={() => { void closeTab({ kind: 'system', id }) }}
                  onContextMenu={(position) => setTabMenu({ ref: { kind: 'system', id }, position })}
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
                  ? <TerminalGlyph size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                  : tab.kind === 'workflow'
                    ? <Workflow size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                  : tab.kind === 'automation'
                    ? <CalendarClock size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                  : tab.kind === 'sideChat'
                    ? <SideChatTabIcon sideChatKey={tab.sideChatKey} />
                  : tab.kind === 'files'
                    ? <FolderOpen size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
                    : <FileTypeIcon path={tab.relativePath} size={14} />
              return (
                <DetailPanelTab
                  key={tab.id}
                  tabKey={`viewer:${tab.id}`}
                  className={automationActive ? 'dotcraft-automation-viewer-tab' : undefined}
                  active={activeViewerId === tab.id}
                  title={tab.kind === 'browser' ? tab.currentUrl : tab.kind === 'terminal' ? tab.cwd : tab.kind === 'file' ? tab.absolutePath : tab.label}
                  icon={icon}
                  label={tab.label}
                  closeLabel={`${t('viewer.close')} ${tab.label}`}
                  onActivate={() => setActiveViewerTab(tab.id)}
                  onClose={() => { void closeTab({ kind: 'viewer', id: tab.id }) }}
                  onContextMenu={(position) => setTabMenu({ ref: { kind: 'viewer', id: tab.id }, position })}
                />
              )
            })}
          </div>

          <IconButton
            ref={addButtonRef}
            size={28}
            label={t('detailPanel.addTab')}
            tooltipLabel={t('detailPanel.addTab')}
            tooltipPlacement="bottom"
            onClick={() => {
              void handleOpenAddTabMenu()
            }}
            icon={<Plus size={16} aria-hidden style={{ display: "block" }} />}
          />
        </div>

        <AddTabPopupWindow
          payload={addTabMenu}
          onResolve={(action) => {
            setAddTabMenu(null)
            handleAddTabAction(action)
          }}
        />

        {tabMenu && (
          <ContextMenu
            items={tabMenuItems(tabMenu.ref)}
            position={tabMenu.position}
            onClose={() => setTabMenu(null)}
          />
        )}

        <div className="dotcraft-detail-panel-header__actions">
          {!isLauncher && !detailPanelCompact && (
            <IconButton
              size={28}
              active={detailPanelFullView}
              activeTone="neutral"
              label={t(detailPanelFullView ? 'detailPanel.exitFullView' : 'detailPanel.enterFullView')}
              tooltipLabel={t(detailPanelFullView ? 'detailPanel.exitFullView' : 'detailPanel.enterFullView')}
              shortcut={ACTION_SHORTCUTS.toggleDetailFullView}
              tooltipPlacement="bottom"
              tooltipWrapperStyle={{ alignSelf: 'center' }}
              onClick={toggleDetailPanelFullView}
              icon={detailPanelFullView
                ? <Minimize2 size={16} aria-hidden />
                : <Maximize2 size={16} aria-hidden />}
            />
          )}

          <IconButton
            size={28}
            label={t('detailPanel.closeAria')}
            tooltipLabel={t('detailPanel.closeAria')}
            shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
            tooltipPlacement="bottom"
            tooltipWrapperStyle={{ alignSelf: 'center' }}
            onClick={toggleDetailPanel}
            icon={<SidebarIcon side="right" />}
          />
        </div>
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
            canOpenWorkspaceTab={canOpenWorkspaceTab}
            systemTabsAvailable={systemTabsAvailable}
            canOpenBoard={canOpenBoard}
            canOpenSideChat={sideChatAvailable}
            remoteWorkspace={remoteWorkspace}
          />
        )}
        {activeSystemId === 'changes' && <ChangesTab workspacePath={workspacePath} />}
        {activeSystemId === 'plan' && <PlanTab />}
        {activeSystemId === 'subagents' && <SubagentsTab />}
        {activeSystemId === 'board' && <BoardTab />}
        {activeSystemId === 'sources' && activeThreadId && (
          <SourcesTab key={activeThreadId} threadId={activeThreadId} workspacePath={workspacePath || null} />
        )}
        {detailPanelVisible && activeDetailTab.kind === 'viewer' && (
          <ViewerTabContainer tabId={activeDetailTab.id} />
        )}
      </div>

    </div>
  )
}

const LazyViewerTab = lazy(() => import('../detail/ViewerTab').then((m) => ({ default: m.ViewerTab })))

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
