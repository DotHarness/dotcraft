import { useState, type CSSProperties, type JSX, type MouseEvent as ReactMouseEvent } from 'react'
import type { SidebarRowProps } from '@dotcraft/plugin'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { ThreadStatusIndicator } from '../sidebar/ThreadStatusIndicator'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { toContextMenuEntry } from './DesktopPluginMenu'

export function DesktopPluginSidebarRow({
  icon,
  label,
  active = false,
  threadId,
  contextMenu,
  onClick
}: SidebarRowProps): JSX.Element {
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const hasMenu = Boolean(contextMenu?.length)

  const handleContextMenu = (event: ReactMouseEvent<HTMLButtonElement>): void => {
    if (!hasMenu) return
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
        onContextMenu={hasMenu ? handleContextMenu : undefined}
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
      {menuPosition && contextMenu && contextMenu.length > 0 && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={contextMenu.map((entry) => toContextMenuEntry(entry, () => setMenuPosition(null)))}
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
