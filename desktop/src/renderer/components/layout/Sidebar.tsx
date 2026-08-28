import { useLocale, useT } from '../../contexts/LocaleContext'
import type { DesktopPluginContributionIcon } from '@dotcraft/plugin'
import { connectionStatusLabel } from '../../utils/connectionStatusLabel'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import {
  resolveDesktopPluginLabel,
  useDesktopPluginRegistry
} from '../../plugins/desktopPluginRegistry'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { NewThreadButton } from '../sidebar/NewThreadButton'
import { ThreadSearch } from '../sidebar/ThreadSearch'
import {
  ThreadList,
  ProjectGlyph,
  isColdProject,
  isProjectForeground,
  isRemoteProject,
  projectIdentity
} from '../sidebar/ThreadList'
import { SidebarFooter } from '../sidebar/SidebarFooter'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { SettingsIcon } from '../ui/AppIcons'
import { Bot, MessageSquare, Puzzle, SquarePen } from 'lucide-react'
import { resolveDesktopPluginIcon } from '../desktopPlugins/DesktopPluginIcon'
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
  const t = useT()
  const locale = useLocale()
  const { sidebarCollapsed, activeMainView, setActiveMainView } = useUIStore()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const desktopMainViews = useDesktopPluginRegistry((state) => state.mainViews)

  const automationsAvailable =
    capabilities?.automations === true || capabilities?.cronManagement === true
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
        <SidebarNavRow
          label={t('sidebar.channels')}
          active={activeMainView === 'channels'}
          onClick={() => setActiveMainView('channels')}
          icon={<ChannelsIcon />}
          testId="nav-channels"
        />
        {desktopMainViews.map((entry) => (
          <SidebarNavRow
            key={entry.viewKey}
            label={resolveDesktopPluginLabel(entry.label, locale)}
            active={activeMainView === entry.viewKey}
            onClick={() => setActiveMainView(entry.viewKey)}
            icon={<DesktopPluginIcon icon={entry.icon} />}
            testId={`nav-desktop-plugin-${entry.pluginId}-${entry.id}`}
          />
        ))}
        <SidebarNavRow
          label={t('sidebar.agents')}
          active={activeMainView === 'agents'}
          onClick={() => setActiveMainView('agents')}
          icon={<AgentsIcon />}
          testId="nav-agents"
        />
        {automationsAvailable && (
          <SidebarNavRow
            label={t('sidebar.automations')}
            active={activeMainView === 'automations'}
            onClick={() => setActiveMainView('automations')}
            icon={<AutomationsIcon />}
            testId="nav-automations"
          />
        )}
        <SidebarNavRow
          label={t('sidebar.skills')}
          active={activeMainView === 'skills'}
          onClick={() => setActiveMainView('skills')}
          icon={<SkillsIcon />}
          testId="nav-skills"
        />
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
  icon: JSX.Element
  disabled?: boolean
  title?: string
  testId?: string
}

function SidebarNavRow({
  label,
  active,
  onClick,
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
      onClick={disabled ? undefined : onClick}
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

function SkillsIcon(): JSX.Element {
  return <Puzzle size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function ChannelsIcon(): JSX.Element {
  return <MessageSquare size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function AgentsIcon(): JSX.Element {
  return <Bot size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function DesktopPluginIcon({ icon }: { icon?: DesktopPluginContributionIcon | null }): JSX.Element {
  const Glyph = resolveDesktopPluginIcon(icon)
  return <Glyph size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

function AutomationsIcon(): JSX.Element {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      style={{ display: 'block' }}
    >
      <circle cx="12" cy="12" r="9" />
      <line x1="12" y1="12" x2="12" y2="8" />
      <line x1="12" y1="12" x2="16" y2="12" />
    </svg>
  )
}

function CollapsedSidebar(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const { status, errorMessage, capabilities: collapsedCaps } = useConnectionStore()
  const { threadList, setActiveThreadId } = useThreadStore()
  const { activeMainView, setActiveMainView, goToNewChat } = useUIStore()
  const desktopMainViews = useDesktopPluginRegistry((state) => state.mainViews)
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const foregroundWorkspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const collapsedAutomationsAvailable =
    collapsedCaps?.automations === true || collapsedCaps?.cronManagement === true

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

  function handleNewThread(): void {
    if (status !== 'connected') return
    goToNewChat()
  }

  async function handleProjectClick(
    project: WorkspaceProjectSummary,
    isForeground: boolean
  ): Promise<void> {
    // Promote a background local project to foreground; remote projects are
    // foreground-only and never switched. Then surface the conversation view.
    if (!isForeground && !isRemoteProject(project)) {
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
        icon={<SquarePen size={16} strokeWidth={1.8} aria-hidden="true" />}
        label={t('sidebar.newThreadLabel')}
        tooltipLabel={t('sidebar.newThreadLabel')}
        shortcut={status === 'connected' ? ACTION_SHORTCUTS.newThread : undefined}
        tooltipPlacement="right"
        disabledReason={
          status !== 'connected'
            ? t('connection.statusTitle', {
              status: connectionStatusLabel(status, errorMessage, t)
            })
            : undefined
        }
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        onClick={handleNewThread}
        disabled={status !== 'connected'}
      />

      <IconButton
        icon={<ChannelsIcon />}
        label={t('sidebar.channels')}
        tooltipLabel={t('sidebar.channels')}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={activeMainView === 'channels'}
        onClick={() => setActiveMainView('channels')}
      />
      {desktopMainViews.map((entry) => {
        const label = resolveDesktopPluginLabel(entry.label, locale)
        return (
        <IconButton
          key={entry.viewKey}
          icon={<DesktopPluginIcon icon={entry.icon} />}
          label={label}
          tooltipLabel={label}
          tooltipPlacement="right"
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          active={activeMainView === entry.viewKey}
          onClick={() => setActiveMainView(entry.viewKey)}
        />
        )
      })}
      <IconButton
        icon={<AgentsIcon />}
        label={t('sidebar.agents')}
        tooltipLabel={t('sidebar.agents')}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={activeMainView === 'agents'}
        onClick={() => setActiveMainView('agents')}
      />
      {collapsedAutomationsAvailable && (
        <IconButton
          icon={<AutomationsIcon />}
          label={t('sidebar.automations')}
          tooltipLabel={t('sidebar.automations')}
          tooltipPlacement="right"
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          active={activeMainView === 'automations'}
          onClick={() => setActiveMainView('automations')}
        />
      )}
      <IconButton
        icon={<SkillsIcon />}
        label={t('sidebar.skills')}
        tooltipLabel={t('sidebar.skills')}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={activeMainView === 'skills'}
        onClick={() => setActiveMainView('skills')}
      />

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
        icon={<SettingsIcon />}
        label={t('sidebar.openSettingsAria')}
        tooltipLabel={t('sidebar.openSettingsAria')}
        shortcut={ACTION_SHORTCUTS.settings}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={activeMainView === 'settings'}
        onClick={() => setActiveMainView('settings')}
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
