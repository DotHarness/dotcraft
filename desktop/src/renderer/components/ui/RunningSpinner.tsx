interface RunningSpinnerProps {
  size?: number
  borderWidth?: number
  title?: string
  testId?: string
}

/**
 * Shared compact running-state spinner for thread and turn activity indicators.
 */
export function RunningSpinner({
  size = 12,
  borderWidth = 2,
  title,
  testId
}: RunningSpinnerProps): JSX.Element {
  return (
    <span
      aria-label={title}
      title={title}
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
}
