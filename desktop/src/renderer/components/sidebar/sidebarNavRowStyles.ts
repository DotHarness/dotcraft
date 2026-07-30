import type { CSSProperties } from 'react'

/**
 * Shared row height for the sidebar "button" rows — New chat, Search, the nav
 * destinations (Channels/Automations/Skills), Settings, and project headers — so
 * they all line up. Thread rows are intentionally a little taller (content rows).
 */
export const SIDEBAR_ROW_MIN_HEIGHT = '30px'

/**
 * Shared horizontal origin for expanded-sidebar section labels, project icon
 * slots, and section-level empty states. Full-width rows reach this origin via
 * their 4px outer inset plus 12px inner padding.
 */
export const SIDEBAR_RAIL_CONTENT_INSET = '16px'

/** Shared geometry for New chat, Search, Automations, Skills, Settings, and project header rows. */
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
  borderLeft: 'none'
}
