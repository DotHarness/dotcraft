import type { CSSProperties } from 'react'

export function settingsMainStyle(): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    overflowY: 'auto',
    padding: '20px'
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
