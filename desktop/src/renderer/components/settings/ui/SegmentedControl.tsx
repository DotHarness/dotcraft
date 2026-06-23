import type { CSSProperties, JSX } from 'react'

export interface SegmentedOption<T extends string> {
  value: T
  label: string
}

interface SegmentedControlProps<T extends string> {
  value: T
  options: SegmentedOption<T>[]
  onChange: (value: T) => void
  ariaLabel: string
  disabled?: boolean
}

/**
 * Neutral pill segmented control for small mutually-exclusive choices (e.g. System/On/Off).
 * The active segment uses a raised neutral surface, not an accent fill, per DESIGN.md.
 */
export function SegmentedControl<T extends string>({
  value,
  options,
  onChange,
  ariaLabel,
  disabled = false
}: SegmentedControlProps<T>): JSX.Element {
  return (
    <div role="group" aria-label={ariaLabel} style={trackStyle}>
      {options.map((option) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={active}
            disabled={disabled}
            onClick={() => {
              if (!active && !disabled) onChange(option.value)
            }}
            style={segmentStyle(active, disabled)}
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
}

const trackStyle: CSSProperties = {
  display: 'inline-flex',
  background: 'var(--bg-tertiary)',
  borderRadius: 999,
  padding: 3,
  gap: 2
}

function segmentStyle(active: boolean, disabled: boolean): CSSProperties {
  return {
    appearance: 'none',
    border: 'none',
    borderRadius: 999,
    padding: '6px 13px',
    fontSize: '12.5px',
    fontWeight: active ? 600 : 500,
    lineHeight: 1,
    cursor: disabled ? 'default' : 'pointer',
    background: active ? 'var(--bg-elevated)' : 'transparent',
    color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
    boxShadow: active ? 'var(--shadow-md)' : 'none',
    opacity: disabled ? 0.6 : 1,
    transition: 'background 130ms ease, color 130ms ease'
  }
}
