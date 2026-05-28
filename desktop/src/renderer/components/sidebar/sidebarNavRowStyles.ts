import type { CSSProperties } from 'react'

/** Shared geometry for Automations, Skills, Settings, and connection row (sidebar bottom). */
export const SIDEBAR_NAV_ROW_OUTER: CSSProperties = {
  width: 'calc(100% - 8px)',
  margin: '2px 4px',
  padding: '8px 12px',
  borderRadius: 'var(--sidebar-control-radius)',
  border: 'none',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  textAlign: 'left',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  boxSizing: 'border-box'
}

export const SIDEBAR_NAV_ICON_SLOT: CSSProperties = {
  display: 'flex',
  width: 18,
  height: 18,
  flexShrink: 0,
  alignItems: 'center',
  justifyContent: 'center',
  lineHeight: 0
}

export const SIDEBAR_NAV_LABEL: CSSProperties = {
  lineHeight: 'var(--type-ui-line-height)'
}

export const SIDEBAR_NAV_BORDER_INACTIVE: CSSProperties = {
  borderLeft: '3px solid transparent'
}
