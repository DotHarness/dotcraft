import type { CSSProperties, JSX } from 'react'
import type { SidebarRowProps } from '@dotcraft/plugin'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { ThreadStatusIndicator } from '../sidebar/ThreadStatusIndicator'

export function DesktopPluginSidebarRow({ icon, label, active = false, threadId, onClick }: SidebarRowProps): JSX.Element {
  return (
    <button
      className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
      type="button"
      data-nav-icon-host=""
      onClick={onClick}
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
