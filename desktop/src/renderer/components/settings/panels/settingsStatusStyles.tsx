import type { CSSProperties } from 'react'

export { StatusIndicator, type StatusIndicatorTone, type StatusTone } from '../../ui/StatusIndicator'

export function statusTextStyle(): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 4,
    fontSize: 12.5,
    lineHeight: 1,
    color: 'var(--text-secondary)'
  }
}
