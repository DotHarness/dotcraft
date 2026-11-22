import type { MouseEvent as ReactMouseEvent } from 'react'
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
import { SidebarMoreMenu } from '../sidebar/SidebarMoreMenu'
import { DesktopPluginIcon, useSidebarDestinations, useUnpinContextMenu } from '../sidebar/sidebarDestinations'
import { useLocale } from '../../contexts/LocaleContext'
import { resolveDesktopPluginLabel, useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import { enterSpace } from '../../stores/spaceStore'

interface SidebarProps {
  workspaceName: string
  workspacePath: string
  workspaceOpening?: boolean
}

export function Sidebar({
  workspaceName,
  workspacePath,
  workspaceOpening = false
}: SidebarProps): JSX.Element {
  const { pinned, more } = useSidebarDestinations()
  const unpinMenu = useUnpinContextMenu()

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

      <SpaceNavRows />

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
        <SidebarMoreMenu destinations={more} />
        {unpinMenu.menu}
      </div>

      <ThreadList
        workspacePath={workspacePath}
        foregroundOpening={workspaceOpening}
        openingWorkspacePath={workspaceOpening ? workspacePath : undefined}
      />

      <SidebarFooter />
    </div>
  )
}

function SpaceNavRows(): JSX.Element {
  const locale = useLocale()
  const spaces = useDesktopPluginRegistry((s) => s.spaces)
  return (
    <>
      {spaces.map((space) => (
        <SidebarNavRow
          key={space.spaceKey}
          label={resolveDesktopPluginLabel(space.label, locale)}
          active={false}
          onClick={() => enterSpace(space)}
          icon={<DesktopPluginIcon icon={space.icon} />}
          testId={`nav-space-${space.pluginId}-${space.id}`}
        />
      ))}
    </>
  )
}

interface SidebarNavRowProps {
  label: string
  active: boolean
  onClick: () => void
  onPrefetch?: () => void
  onContextMenu?: (event: ReactMouseEvent) => void
  icon: JSX.Element
  testId?: string
}

function SidebarNavRow({
  label,
  active,
  onClick,
  onPrefetch,
  onContextMenu,
  icon,
  testId
}: SidebarNavRowProps): JSX.Element {
  return (
    <button
      className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
      type="button"
      data-testid={testId}
      data-nav-icon-host=""
      onClick={onClick}
      onContextMenu={onContextMenu}
      onPointerEnter={active ? undefined : onPrefetch}
      onFocus={active ? undefined : onPrefetch}
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
}
