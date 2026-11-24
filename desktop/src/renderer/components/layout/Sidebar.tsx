import { useId, type MouseEvent as ReactMouseEvent } from 'react'
import { SpaceFeatureGuide } from '../featureGuides/SpaceFeatureGuide'
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
import { useSidebarDestinations, useUnpinContextMenu, type SidebarDestination } from '../sidebar/sidebarDestinations'

interface SidebarProps {
  showFeatureGuides?: boolean
  workspaceName: string
  workspacePath: string
  workspaceOpening?: boolean
}

export function Sidebar({
  showFeatureGuides = false,
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
          <PinnedDestination
            key={destination.id}
            destination={destination}
            showFeatureGuides={showFeatureGuides}
            onContextMenu={(event) => unpinMenu.open(event, destination.id)}
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

function PinnedDestination({
  destination,
  showFeatureGuides,
  onContextMenu
}: {
  destination: SidebarDestination
  showFeatureGuides: boolean
  onContextMenu: (event: ReactMouseEvent) => void
}): JSX.Element {
  const anchorName = `--dc-space-${useId()}`
  const { label, active, select, prefetch, icon, testId, space } = destination
  return (
    <>
      <button
        className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
        type="button"
        data-testid={testId}
        data-nav-icon-host=""
        onClick={select}
        onContextMenu={onContextMenu}
        onPointerEnter={active ? undefined : prefetch}
        onFocus={active ? undefined : prefetch}
        data-active={active ? 'true' : undefined}
        style={{ ...SIDEBAR_NAV_ROW_OUTER, ...SIDEBAR_NAV_BORDER_INACTIVE, anchorName: space?.guide ? anchorName : undefined }}
      >
        <span style={SIDEBAR_NAV_ICON_SLOT}>{icon}</span>
        <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
      </button>
      {showFeatureGuides && space?.guide && <SpaceFeatureGuide space={space} anchorName={anchorName} />}
    </>
  )
}
