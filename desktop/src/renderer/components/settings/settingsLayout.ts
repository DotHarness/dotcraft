import type { CSSProperties } from 'react'

export function settingsMainStyle(): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    overflowY: 'auto',
    // The top inset clears the dialog's close button, which sits over the page's trailing corner.
    padding: '48px 28px 28px'
  }
}

export function settingsContentContainerStyle(): CSSProperties {
  return {
    width: '100%',
    maxWidth: '760px',
    margin: '0 auto',
    boxSizing: 'border-box'
  }
}
