import type { CSSProperties, JSX } from 'react'

type PillSwitchSize = 'sm' | 'md'

interface PillSwitchProps {
  checked: boolean
  onChange: (checked: boolean) => void
  size?: PillSwitchSize
  disabled?: boolean
  'aria-label'?: string
  'aria-labelledby'?: string
}

const SIZE_MAP: Record<PillSwitchSize, { track: [number, number]; thumb: number; gap: number }> = {
  sm: { track: [32, 18], thumb: 14, gap: 2 },
  md: { track: [36, 20], thumb: 16, gap: 2 }
}

/**
 * A compact pill-style toggle. Visual-only: renders a single button without wrapping label.
 * For a labelled variant with description, use `ToggleSwitch` in the channels panel.
 */
export function PillSwitch({
  checked,
  onChange,
  size = 'md',
  disabled = false,
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledby
}: PillSwitchProps): JSX.Element {
  const { track, thumb, gap } = SIZE_MAP[size]
  const [trackWidth, trackHeight] = track
  const travel = trackWidth - thumb - gap * 2

  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      aria-labelledby={ariaLabelledby}
      disabled={disabled}
      onClick={() => {
        if (!disabled) onChange(!checked)
      }}
      style={trackStyle(trackWidth, trackHeight, checked, disabled)}
    >
      <span aria-hidden style={thumbStyle(thumb, gap, travel, checked)} />
    </button>
  )
}

function trackStyle(
  width: number,
  height: number,
  checked: boolean,
  disabled: boolean
): CSSProperties {
  return {
    flexShrink: 0,
    width,
    height,
    borderRadius: height / 2,
    border: 'none',
    padding: 0,
    cursor: disabled ? 'not-allowed' : 'pointer',
    backgroundColor: checked
      ? disabled
        ? 'color-mix(in srgb, var(--accent) 45%, var(--border-active))'
        : 'var(--accent)'
      : 'var(--border-active)',
    transition: 'background-color 150ms ease',
    position: 'relative',
    outline: 'none',
    opacity: disabled ? 0.7 : 1
  }
}

function thumbStyle(thumb: number, gap: number, travel: number, checked: boolean): CSSProperties {
  return {
    position: 'absolute',
    top: gap,
    left: checked ? gap + travel : gap,
    width: thumb,
    height: thumb,
    borderRadius: '50%',
    backgroundColor: 'white',
    transition: 'left 150ms ease',
    boxShadow: '0 1px 3px rgba(0,0,0,0.25)'
  }
}
