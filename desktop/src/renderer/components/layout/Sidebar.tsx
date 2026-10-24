import type { MouseEvent as ReactMouseEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { connectionStatusLabel } from '../../utils/connectionStatusLabel'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { useSshMachinesStore } from '../../stores/sshMachinesStore'
import { addToast } from '../../stores/toastStore'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { NewThreadButton } from '../sidebar/NewThreadButton'
import { ThreadSearch } from '../sidebar/ThreadSearch'
import { ThreadList, ProjectGlyph } from '../sidebar/ThreadList'
import {
  isColdProject,
  isProjectForeground,
  isRemoteProject,
  projectIdentity
} from '../sidebar/projectThreads'
import { SidebarFooter } from '../sidebar/SidebarFooter'
import { sshProjectRef } from '../sidebar/sshRemoteProject'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { SidebarNavIcon } from '../sidebar/SidebarNavIcon'
import { SidebarMoreMenu } from '../sidebar/SidebarMoreMenu'
import { useSidebarDestinations, useUnpinContextMenu } from '../sidebar/sidebarDestinations'
import { prefetchCoreMainView } from '../../core/coreMainViewRoutes'
import { MessageSquare } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

interface SidebarProps {
  workspaceName: string
  workspacePath: string
  localWorkspacePath?: string
  remoteWorkspace?: boolean
  workspaceOpening?: boolean
}

export function Sidebar({
  workspaceName,
  workspacePath,
  localWorkspacePath,
  remoteWorkspace = false,
  workspaceOpening = false
}: SidebarProps): JSX.Element {
  const sidebarCollapsed = useUIStore((s) => s.sidebarCollapsed)
  const { pinned, more } = useSidebarDestinations()
  const unpinMenu = useUnpinContextMenu()

  if (sidebarCollapsed) {
    return <CollapsedSidebar />
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        overflow: 'visible',
        position: 'relative'
      }}
    >
      <NewThreadButton />

      <ThreadSearch workspaceName={workspaceName} />

      {/* No top padding: rows rely on their shared 2px row margin so New chat,
          Search, and these nav items keep one uniform vertical rhythm. */}
      <div
        style={{
          paddingBottom: '6px',
          flexShrink: 0
        }}
      >
        {pinned.map((destination) => (
          <SidebarNavRow
            key={destination.id}
            label={destination.label}
            active={destination.active}
            onClick={destination.select}
            onPrefetch={destination.prefetch}
            onContextMenu={(event) => unpinMenu.open(event, destination.id)}
            icon={destination.icon}
            testId={destination.testId}
          />
        ))}
        <SidebarMoreMenu destinations={more} variant="row" />
        {unpinMenu.menu}
      </div>

      <ThreadList
        workspacePath={workspacePath}
        localWorkspacePath={localWorkspacePath ?? workspacePath}
        localActionsDisabled={remoteWorkspace}
        foregroundOpening={workspaceOpening}
        openingWorkspacePath={workspaceOpening ? workspacePath : undefined}
      />

      <SidebarFooter />
    </div>
  )
}

interface SidebarNavRowProps {
  label: string
  active: boolean
  onClick: () => void
  onPrefetch?: () => void
  onContextMenu?: (event: ReactMouseEvent) => void
  icon: JSX.Element
  disabled?: boolean
  title?: string
  testId?: string
}

function SidebarNavRow({
  label,
  active,
  onClick,
  onPrefetch,
  onContextMenu,
  icon,
  disabled,
  title,
  testId
}: SidebarNavRowProps): JSX.Element {
  const button = (
    <button
      className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
      type="button"
      data-testid={testId}
      data-nav-icon-host=""
      onClick={disabled ? undefined : onClick}
      onContextMenu={onContextMenu}
      onPointerEnter={disabled || active ? undefined : onPrefetch}
      onFocus={disabled || active ? undefined : onPrefetch}
      disabled={disabled}
      data-active={active ? 'true' : undefined}
      style={{
        ...SIDEBAR_NAV_ROW_OUTER,
        ...SIDEBAR_NAV_BORDER_INACTIVE
      }}
    >
      <span style={SIDEBAR_NAV_ICON_SLOT}>{icon}</span>
      <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
    </button>
  )

  if (!title) return button

  return (
    <ActionTooltip
      label={label}
      disabledReason={disabled ? title : undefined}
      wrapperStyle={{ display: 'block', width: '100%' }}
    >
      {button}
    </ActionTooltip>
  )
}

function CollapsedSidebar(): JSX.Element {
  const t = useT()
  const { status, errorMessage } = useConnectionStore()
  const { threadList, setActiveThreadId } = useThreadStore()
  const { activeMainView, setActiveMainView, goToNewChat } = useUIStore()
  const { pinned, more } = useSidebarDestinations()
  const unpinMenu = useUnpinContextMenu()
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const foregroundWorkspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)

  const colorMap: Record<string, string> = {
    connecting: 'var(--warning)',
    connected: 'var(--success)',
    disconnected: 'var(--error)',
    error: 'var(--error)'
  }

  // Mirror the expanded Projects rail with one folder icon per project. Fall back
  // to recent-thread dots only while neither Projects nor Chats are available.
  const showProjects = projects.length > 0
  const showWorkspaceRail = showProjects || chat != null
  const recentThreads = threadList.slice(0, 5)

  async function handleProjectClick(
    project: WorkspaceProjectSummary,
    isForeground: boolean
  ): Promise<void> {
    // Promote a background local project to foreground; remote projects are
    // foreground-only and never switched. Then surface the conversation view.
    const sshRef = sshProjectRef(project)
    if (!isForeground && sshRef) {
      try {
        await useSshMachinesStore.getState().openProject(sshRef.machineId, sshRef.projectId)
      } catch (err) {
        addToast(err instanceof Error ? err.message : String(err), 'error')
        return
      }
    } else if (!isForeground && !isRemoteProject(project)) {
      try {
        await window.api.workspace.switch(project.path)
      } catch (err) {
        console.error('Failed to switch workspace from collapsed sidebar:', err)
        return
      }
    }
    setActiveMainView('conversation')
  }

  async function handleChatClick(isForeground: boolean): Promise<void> {
    if (!chat) return
    if (!isForeground) {
      try {
        await window.api.workspace.switch(chat.path)
      } catch (err) {
        console.error('Failed to switch to Chats from collapsed sidebar:', err)
        return
      }
    }
    setActiveMainView('conversation')
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        height: '100%',
        padding: '8px 0',
        gap: '6px'
      }}
    >
      <IconButton
        data-nav-icon-host=""
        icon={<SidebarNavIcon name="new-chat" />}
        label={t('sidebar.newThreadLabel')}
        tooltipLabel={t('sidebar.newThreadLabel')}
        shortcut={ACTION_SHORTCUTS.newThread}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        onClick={() => goToNewChat()}
      />

      {pinned.map((destination) => (
        <IconButton
          key={destination.id}
          data-nav-icon-host=""
          data-testid={destination.testId}
          icon={destination.icon}
          label={destination.label}
          tooltipLabel={destination.label}
          tooltipPlacement="right"
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          active={destination.active}
          onClick={destination.select}
          onContextMenu={(event) => unpinMenu.open(event, destination.id)}
          onPointerEnter={destination.prefetch}
          onFocus={destination.prefetch}
        />
      ))}
      <SidebarMoreMenu destinations={more} variant="icon" />
      {unpinMenu.menu}

      <div
        style={{
          flex: 1,
          minHeight: 0,
          width: '100%',
          overflowY: 'auto',
          overflowX: 'hidden',
          scrollbarWidth: 'none',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: '6px'
        }}
      >
        {showWorkspaceRail
          ? (
            <>
              {projects.map((project) => {
                const isForeground = isProjectForeground(
                  project,
                  foregroundProjectId,
                  foregroundWorkspacePath
                )
                const label = project.name || project.path
                return (
                  <IconButton
                    key={projectIdentity(project)}
                    icon={(
                      <ProjectGlyph
                        project={project}
                        collapsed={!isForeground}
                        cold={isColdProject(project)}
                        active={isForeground}
                      />
                    )}
                    label={label}
                    tooltipLabel={label}
                    tooltipPlacement="right"
                    size={32}
                    radius={8}
                    className="dc-sidebar-icon-button"
                    active={isForeground}
                    aria-current={isForeground ? 'true' : undefined}
                    onClick={() => void handleProjectClick(project, isForeground)}
                  />
                )
              })}
              {chat && (() => {
                const isForeground = isProjectForeground(
                  chat,
                  foregroundProjectId,
                  foregroundWorkspacePath
                )
                return (
                  <IconButton
                    key={projectIdentity(chat)}
                    icon={<MessageSquare size={16} strokeWidth={1.8} aria-hidden />}
                    label={t('chatsRail.title')}
                    tooltipLabel={t('chatsRail.title')}
                    tooltipPlacement="right"
                    size={32}
                    radius={8}
                    className="dc-sidebar-icon-button"
                    active={isForeground}
                    aria-current={isForeground ? 'true' : undefined}
                    onClick={() => void handleChatClick(isForeground)}
                  />
                )
              })()}
            </>
          )
          : recentThreads.map((thread) => {
              const letter = (thread.displayName ?? 'N')[0].toUpperCase()
              return (
                <IconButton
                  key={thread.id}
                  icon={letter}
                  label={thread.displayName ?? t('sidebar.newConversation')}
                  tooltipLabel={thread.displayName ?? t('sidebar.newConversation')}
                  tooltipPlacement="right"
                  size={32}
                  radius={8}
                  className="dc-sidebar-icon-button"
                  onClick={() => {
                    setActiveThreadId(thread.id)
                    setActiveMainView('conversation')
                  }}
                  style={{
                    fontSize: 'var(--type-secondary-size)',
                    lineHeight: 'var(--type-secondary-line-height)',
                    fontWeight: 'var(--type-ui-emphasis-weight)',
                    backgroundColor: 'var(--sidebar-control-active)'
                  }}
                />
              )
            })}
      </div>

      <IconButton
        data-nav-icon-host=""
        icon={<SidebarNavIcon name="settings" />}
        label={t('sidebar.openSettingsAria')}
        tooltipLabel={t('sidebar.openSettingsAria')}
        shortcut={ACTION_SHORTCUTS.settings}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={activeMainView === 'settings'}
        onClick={() => setActiveMainView('settings')}
        onPointerEnter={() => prefetchCoreMainView('settings')}
        onFocus={() => prefetchCoreMainView('settings')}
      />

      {/* Connection status dot — only when the projects rail (which carries its own
          per-project status dots) is not shown. */}
      {!showProjects && (
        <ActionTooltip
          label={t('connection.statusTitle', {
            status: connectionStatusLabel(status, errorMessage, t)
          })}
        >
          <div
            style={{
              width: '8px',
              height: '8px',
              borderRadius: '50%',
              backgroundColor: colorMap[status] ?? 'var(--text-dimmed)',
              marginBottom: '8px'
            }}
            aria-label={t('connection.statusTitle', {
              status: connectionStatusLabel(status, errorMessage, t)
            })}
          />
        </ActionTooltip>
      )}
    </div>
  )
}
