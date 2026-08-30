import { useEffect, useRef, type CSSProperties, type JSX, type KeyboardEvent } from 'react'

export interface SliderProps {
  value: number
  min: number
  max: number
  step?: number
  onValueChange: (value: number) => void
  onValueCommit?: (value: number) => void
  ariaLabel: string
  valueText?: string
  disabled?: boolean
}

export function Slider({
  value,
  min,
  max,
  step = 1,
  onValueChange,
  onValueCommit,
  ariaLabel,
  valueText,
  disabled = false
}: SliderProps): JSX.Element {
  const latestValue = useRef(value)
  const committedValue = useRef(value)
  const pendingCommit = useRef(false)
  const progress = max > min
    ? Math.min(100, Math.max(0, ((value - min) / (max - min)) * 100))
    : 0

  useEffect(() => {
    latestValue.current = value
    if (!pendingCommit.current) committedValue.current = value
  }, [value])

  const commit = (): void => {
    if (!pendingCommit.current) return
    pendingCommit.current = false
    committedValue.current = latestValue.current
    onValueCommit?.(latestValue.current)
  }

  const commitKey = (event: KeyboardEvent<HTMLInputElement>): void => {
    if ([
      'ArrowLeft',
      'ArrowRight',
      'ArrowUp',
      'ArrowDown',
      'Home',
      'End',
      'PageUp',
      'PageDown'
    ].includes(event.key)) commit()
  }

  return (
    <div
      className="dc-slider"
      data-disabled={disabled ? 'true' : undefined}
      style={{ '--dc-slider-progress': `${progress}%` } as CSSProperties}
    >
      <input
        className="dc-slider__input"
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-valuetext={valueText}
        onChange={(event) => {
          const nextValue = Number(event.currentTarget.value)
          latestValue.current = nextValue
          pendingCommit.current = nextValue !== committedValue.current
          onValueChange(nextValue)
        }}
        onPointerUp={commit}
        onPointerCancel={commit}
        onKeyUp={commitKey}
        onBlur={commit}
      />
      {valueText && <output className="dc-slider__value">{valueText}</output>}
    </div>
  )
}
