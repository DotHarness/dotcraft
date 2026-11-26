import type { CSSProperties } from 'react'

export const SIDEBAR_ROW_MIN_HEIGHT = 'var(--sidebar-row-min-height)'

/**
 * Shared horizontal origin for expanded-sidebar section labels, project icon
 * slots, and section-level empty states. Full-width rows reach this origin via
 * their 4px outer inset plus 12px inner padding.
 */
export const SIDEBAR_RAIL_CONTENT_INSET = 'var(--sidebar-rail-content-inset)'

/** Shared geometry for New chat, Automations, Skills, Settings, and project header rows. */
export const SIDEBAR_NAV_ROW_OUTER: CSSProperties = {
  width: 'calc(100% - 8px)',
  margin: '2px 4px',
  minHeight: SIDEBAR_ROW_MIN_HEIGHT,
  padding: '0 12px',
  borderRadius: 'var(--sidebar-row-radius)',
  border: 'none',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  textAlign: 'left',
  display: 'flex',
  alignItems: 'center',
  gap: 'var(--sidebar-leading-gap)',
  boxSizing: 'border-box'
}

export const SIDEBAR_NAV_ICON_SLOT: CSSProperties = {
  display: 'flex',
  width: 'var(--sidebar-leading-slot-width)',
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
  borderLeft: 'none'
}
