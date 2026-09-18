import type { JSX } from 'react'
import { Spinner } from './Spinner'

/** Four fills carry every state; the label distinguishes the quiet ones in words. */
export type StatusTone = 'success' | 'warning' | 'error' | 'neutral'

/** `pending` is a transitional state, so it spins instead of claiming a result. */
export type StatusIndicatorTone = StatusTone | 'pending'

export function StatusIndicator({
  tone,
  label
}: {
  tone: StatusIndicatorTone
  label?: string
}): JSX.Element {
  return (
    <span
      className="dc-status-indicator"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      {tone === 'pending' ? (
        <Spinner size={14} />
      ) : (
        <span className="dc-status-indicator__dot" data-tone={tone === 'neutral' ? undefined : tone} />
      )}
    </span>
  )
}
