import type { JSX } from 'react'
import { ActionTooltip } from './ActionTooltip'

interface SpinnerProps {
  size?: number
  label?: string
  className?: string
  testId?: string
}

export function Spinner({ size = 14, label, className, testId }: SpinnerProps): JSX.Element {
  const spinner = (
    <svg
      className={className ? `dc-spinner ${className}` : 'dc-spinner'}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      data-testid={testId}
    >
      <circle className="dc-spinner__track" cx="12" cy="12" r="7" stroke="currentColor" strokeWidth="2" />
      <circle cx="12" cy="12" r="7" stroke="currentColor" strokeWidth="2" strokeDasharray="33 44" />
    </svg>
  )

  if (!label) return spinner

  return <ActionTooltip label={label}>{spinner}</ActionTooltip>
}
