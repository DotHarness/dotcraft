import { ActionTooltip } from './ActionTooltip'

interface RunningSpinnerProps {
  size?: number
  borderWidth?: number
  /** Accessible name and custom tooltip label. When set, the spinner shows an ActionTooltip on hover. */
  label?: string
  testId?: string
}

/**
 * Shared compact running-state spinner for thread and turn activity indicators.
 * When `label` is provided it exposes an accessible name and a custom ActionTooltip
 * (never the browser's native tooltip).
 */
export function RunningSpinner({
  size = 12,
  borderWidth = 2,
  label,
  testId
}: RunningSpinnerProps): JSX.Element {
  const spinner = (
    <span
      aria-label={label}
      data-testid={testId}
      style={{
        display: 'inline-block',
        width: `${size}px`,
        height: `${size}px`,
        border: `${borderWidth}px solid var(--text-dimmed)`,
        borderTopColor: 'var(--accent)',
        borderRadius: '50%',
        animation: 'spin 1s linear infinite',
        flexShrink: 0,
        boxSizing: 'border-box'
      }}
    />
  )

  if (!label) return spinner

  return <ActionTooltip label={label}>{spinner}</ActionTooltip>
}
