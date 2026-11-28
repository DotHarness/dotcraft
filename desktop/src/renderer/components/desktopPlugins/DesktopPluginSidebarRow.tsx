import { useState, type CSSProperties, type JSX, type MouseEvent as ReactMouseEvent } from 'react'
import type { SidebarRowProps } from '@dotcraft/plugin'
import { useT } from '../../contexts/LocaleContext'
import { useOwnedThread } from '../../stores/ownedThreadsStore'
import { buildOwnedThreadMenuEntries } from '../../utils/ownedThreadLifecycle'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { ThreadStatusIndicator } from '../sidebar/ThreadStatusIndicator'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'

export function DesktopPluginSidebarRow({ icon, label, active = false, threadId, onClick }: SidebarRowProps): JSX.Element {
  const t = useT()
  const ownedThread = useOwnedThread(threadId)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)

  const handleContextMenu = (event: ReactMouseEvent<HTMLButtonElement>): void => {
    if (!ownedThread) return
    event.preventDefault()
    setMenuPosition({ x: event.clientX, y: event.clientY })
  }

  return (
    <>
      <button
        className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
        type="button"
        data-nav-icon-host=""
        onClick={onClick}
        onContextMenu={ownedThread ? handleContextMenu : undefined}
        style={rowStyle}
        aria-current={active ? 'page' : undefined}
        data-active={active ? 'true' : undefined}
      >
        <span style={SIDEBAR_NAV_ICON_SLOT}>{icon}</span>
        <span style={labelStyle}>{label}</span>
        {threadId && (
          <span style={statusStyle}>
            <ThreadStatusIndicator threadId={threadId} active={active} unreadVariant="count" includeLinkedChats />
          </span>
        )}
      </button>
      {menuPosition && ownedThread && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={buildOwnedThreadMenuEntries(ownedThread, t, () => setMenuPosition(null))}
        />
      )}
    </>
  )
}

const rowStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE
}

const labelStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  textAlign: 'start'
}

const statusStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  minWidth: '24px',
  marginInlineEnd: '6px'
}
