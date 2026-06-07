import { useLocale, useT } from '../../contexts/LocaleContext'
import { resolveLocalizedText } from '../../../shared/locales'
import { connectionStatusLabel } from '../../utils/connectionStatusLabel'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import { usePluginStore } from '../../stores/pluginStore'
import { getDesktopMainViewExtensions } from '../../utils/desktopExtensionRegistry'
import { WorkspaceHeader } from '../sidebar/WorkspaceHeader'
import { NewThreadButton } from '../sidebar/NewThreadButton'
import { ThreadSearch } from '../sidebar/ThreadSearch'
import { ThreadList } from '../sidebar/ThreadList'
import { SidebarFooter } from '../sidebar/SidebarFooter'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { SettingsIcon } from '../ui/AppIcons'
import { MessageSquare, Puzzle, SquareKanban, SquarePen, UsersRound } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

interface SidebarProps {
  workspaceName: string
  workspacePath: string
  localWorkspacePath?: string
  remoteWorkspace?: boolean
  workspaceOpening?: boolean
}

/**
 * Main sidebar panel with the thread list.
 *
 * Structure:
 * 1. WorkspaceHeader (name, path, dropdown)
 * 2. NewThreadButton (Ctrl+N, disabled when disconnected)
 * 3. ThreadSearch (Ctrl+K, debounced)
 * 4. ThreadList (grouped, scrollable)
 * 5. Reserved nav items (Channels, Automations, Skills)
 * 6. SidebarFooter (connection status, version)
 *
 * Collapsed mode (48px): shows first-letter dots for recent threads.
 * Spec §9.8
 */
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
  const plugins = usePluginStore((s) => s.plugins)
  const desktopMainViews = getDesktopMainViewExtensions(plugins)

  const automationsAvailable =
    capabilities?.automations === true || capabilities?.cronManagement === true
  const automationsDisabledTitle =
    !automationsAvailable ? t('sidebar.automationsDisabled') : undefined
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
      <WorkspaceHeader
        workspaceName={workspaceName}
        workspacePath={workspacePath}
      />

      <NewThreadButton />

      <ThreadSearch workspaceName={workspaceName} />

      {/* Thread list -- fills remaining space */}
      <ThreadList
        workspacePath={workspacePath}
        localWorkspacePath={localWorkspacePath ?? workspacePath}
        localActionsDisabled={remoteWorkspace}
        foregroundOpening={workspaceOpening}
        openingWorkspacePath={workspaceOpening ? workspacePath : undefined}
      />

      {/* Phase 2 nav: Automations, Skills — spacing instead of a divider line */}
      <div
        style={{
          marginTop: '8px',
          paddingTop: '8px',
          paddingBottom: '4px',
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
            label={resolveLocalizedText(entry.localizedLabel, entry.label, locale) ?? entry.label}
            active={activeMainView === entry.viewKey}
            onClick={() => setActiveMainView(entry.viewKey)}
            icon={<ExtensionIcon icon={entry.icon} />}
            testId={`nav-extension-${entry.plugin.id}-${entry.extension.id}-${entry.viewId}`}
          />
        ))}
        <SidebarNavRow
          label={t('sidebar.automations')}
          active={activeMainView === 'automations'}
          onClick={() => setActiveMainView('automations')}
          icon={<AutomationsIcon />}
          disabled={!automationsAvailable}
          title={automationsDisabledTitle}
          testId="nav-automations"
        />
        <SidebarNavRow
          label={t('sidebar.skills')}
          active={activeMainView === 'skills'}
          onClick={() => setActiveMainView('skills')}
          icon={<SkillsIcon />}
          testId="nav-skills"
        />
      </div>

      <SidebarFooter />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Phase 2 sidebar rows (Skills, Automations)
// ---------------------------------------------------------------------------

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
      className="dotcraft-sidebar-control-radius"
      type="button"
      data-testid={testId}
      onClick={disabled ? undefined : onClick}
      disabled={disabled}
      style={{
        ...SIDEBAR_NAV_ROW_OUTER,
        cursor: disabled ? 'default' : 'pointer',
        backgroundColor: active ? 'var(--sidebar-control-active)' : 'transparent',
        ...SIDEBAR_NAV_BORDER_INACTIVE,
        color: disabled ? 'var(--text-tertiary)' : active ? 'var(--text-primary)' : 'var(--text-secondary)',
        opacity: disabled ? 0.5 : 1,
        transition: 'background-color 120ms ease, color 120ms ease'
      }}
      onMouseEnter={(e) => {
        if (!active && !disabled) (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--sidebar-control-hover)'
      }}
      onMouseLeave={(e) => {
        if (!active && !disabled) (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
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

function ExtensionIcon({ icon }: { icon?: string | null }): JSX.Element {
  const Glyph = resolveExtensionIcon(icon)
  return <Glyph size={16} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
}

// Maps the optional `icon` a desktop-extension surface declares to a built-in
// glyph. Unknown or omitted names fall back to the generic extension icon, so
// extensions never need to ship raster assets for a sidebar nav entry.
function resolveExtensionIcon(icon?: string | null): typeof UsersRound {
  switch (icon) {
    case 'board':
    case 'kanban':
      return SquareKanban
    default:
      return UsersRound
  }
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

// ---------------------------------------------------------------------------
// Collapsed sidebar (48px wide)
// ---------------------------------------------------------------------------

function CollapsedSidebar(): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const { status, errorMessage, capabilities: collapsedCaps } = useConnectionStore()
  const { threadList, setActiveThreadId } = useThreadStore()
  const { activeMainView, setActiveMainView, goToNewChat } = useUIStore()
  const plugins = usePluginStore((s) => s.plugins)
  const desktopMainViews = getDesktopMainViewExtensions(plugins)
  const collapsedAutomationsAvailable =
    collapsedCaps?.automations === true || collapsedCaps?.cronManagement === true

  const colorMap: Record<string, string> = {
    connecting: 'var(--warning)',
    connected: 'var(--success)',
    disconnected: 'var(--error)',
    error: 'var(--error)'
  }

  // Show up to 5 recent thread dots in collapsed mode
  const recentThreads = threadList.slice(0, 5)

  function handleNewThread(): void {
    if (status !== 'connected') return
    goToNewChat()
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
      {/* New thread icon */}
      <ActionTooltip
        label={t('sidebar.newThreadLabel')}
        shortcut={status === 'connected' ? ACTION_SHORTCUTS.newThread : undefined}
        disabledReason={
          status !== 'connected'
            ? t('connection.statusTitle', {
              status: connectionStatusLabel(status, errorMessage, t)
            })
            : undefined
        }
        placement="right"
      >
        <button
          onClick={handleNewThread}
          disabled={status !== 'connected'}
          style={{
            ...iconButtonStyle,
            backgroundColor: 'transparent',
            color: status === 'connected' ? 'var(--text-secondary)' : 'var(--text-tertiary)',
            fontSize: '18px',
            lineHeight: 'var(--type-ui-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
            opacity: status !== 'connected' ? 0.5 : 1
          }}
          aria-label={t('sidebar.newThreadLabel')}
        >
          <SquarePen size={16} strokeWidth={1.8} aria-hidden="true" />
        </button>
      </ActionTooltip>

      {/* Thread dots: first letter of each recent thread */}
      {recentThreads.map((thread) => {
        const letter = (thread.displayName ?? 'N')[0].toUpperCase()
        return (
          <ActionTooltip
            key={thread.id}
            label={thread.displayName ?? t('sidebar.newConversation')}
            placement="right"
          >
            <button
              onClick={() => {
                setActiveThreadId(thread.id)
                setActiveMainView('conversation')
              }}
              style={{
                ...iconButtonStyle,
                fontSize: 'var(--type-secondary-size)',
                lineHeight: 'var(--type-secondary-line-height)',
                fontWeight: 'var(--type-ui-emphasis-weight)',
                backgroundColor: 'var(--sidebar-control-active)'
              }}
              aria-label={thread.displayName ?? t('sidebar.newConversation')}
            >
              {letter}
            </button>
          </ActionTooltip>
        )
      })}

      {/* Spacer */}
      <div style={{ flex: 1 }} />

      <CollapsedNavTooltip label={t('sidebar.channels')}>
        <button
          type="button"
          onClick={() => setActiveMainView('channels')}
          style={{
            ...iconButtonStyle,
            backgroundColor: activeMainView === 'channels' ? 'var(--sidebar-control-active)' : 'transparent',
            color: activeMainView === 'channels' ? 'var(--accent)' : 'var(--text-secondary)'
          }}
          aria-label={t('sidebar.channels')}
        >
          <ChannelsIcon />
        </button>
      </CollapsedNavTooltip>
      {desktopMainViews.map((entry) => {
        const label = resolveLocalizedText(entry.localizedLabel, entry.label, locale) ?? entry.label
        return (
        <CollapsedNavTooltip key={entry.viewKey} label={label}>
          <button
            type="button"
            onClick={() => setActiveMainView(entry.viewKey)}
            style={{
              ...iconButtonStyle,
              backgroundColor: activeMainView === entry.viewKey ? 'var(--sidebar-control-active)' : 'transparent',
              color: activeMainView === entry.viewKey ? 'var(--accent)' : 'var(--text-secondary)'
            }}
            aria-label={label}
          >
            <ExtensionIcon icon={entry.icon} />
          </button>
        </CollapsedNavTooltip>
        )
      })}
      <CollapsedNavTooltip
        label={t('sidebar.automations')}
        disabledReason={!collapsedAutomationsAvailable ? t('sidebar.automationsDisabled') : undefined}
      >
        <button
          type="button"
          onClick={collapsedAutomationsAvailable ? () => setActiveMainView('automations') : undefined}
          disabled={!collapsedAutomationsAvailable}
          style={{
            ...iconButtonStyle,
            backgroundColor: activeMainView === 'automations' ? 'var(--sidebar-control-active)' : 'transparent',
            color: activeMainView === 'automations' ? 'var(--accent)' : 'var(--text-secondary)',
            opacity: collapsedAutomationsAvailable ? 1 : 0.4
          }}
          aria-label={t('sidebar.automations')}
        >
          <AutomationsIcon />
        </button>
      </CollapsedNavTooltip>
      <CollapsedNavTooltip label={t('sidebar.skills')}>
        <button
          type="button"
          onClick={() => setActiveMainView('skills')}
          style={{
            ...iconButtonStyle,
            backgroundColor: activeMainView === 'skills' ? 'var(--sidebar-control-active)' : 'transparent',
            color: activeMainView === 'skills' ? 'var(--accent)' : 'var(--text-secondary)'
          }}
          aria-label={t('sidebar.skills')}
        >
          <SkillsIcon />
        </button>
      </CollapsedNavTooltip>

      {/* Settings icon button */}
      <ActionTooltip label={t('sidebar.openSettingsAria')} shortcut={ACTION_SHORTCUTS.settings} placement="right">
        <button
          onClick={() => setActiveMainView('settings')}
          aria-label={t('sidebar.openSettingsAria')}
          style={{
            ...iconButtonStyle,
            backgroundColor: activeMainView === 'settings' ? 'var(--sidebar-control-active)' : 'transparent',
            color: activeMainView === 'settings' ? 'var(--accent)' : 'var(--text-secondary)'
          }}
        >
          <SettingsIcon />
        </button>
      </ActionTooltip>

      {/* Connection status dot */}
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
    </div>
  )
}

function CollapsedNavTooltip({
  label,
  disabledReason,
  children
}: {
  label: string
  disabledReason?: string
  children: JSX.Element
}): JSX.Element {
  return (
    <ActionTooltip label={label} disabledReason={disabledReason} placement="right">
      {children}
    </ActionTooltip>
  )
}

const iconButtonStyle: React.CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: 'var(--sidebar-icon-control-radius)',
  backgroundColor: 'transparent',
  border: 'none',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)'
}
