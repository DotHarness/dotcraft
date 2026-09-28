import { Check, Minus } from 'lucide-react'
import { useEffect, useId, useRef, type CSSProperties, type JSX, type ReactNode } from 'react'

export interface CheckboxProps {
  id?: string
  checked: boolean
  indeterminate?: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  label?: ReactNode
  ariaLabel?: string
  style?: CSSProperties
}

export function Checkbox({
  id,
  checked,
  indeterminate = false,
  onChange,
  disabled = false,
  label,
  ariaLabel,
  style
}: CheckboxProps): JSX.Element {
  const generatedId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  useEffect(() => { if (inputRef.current) inputRef.current.indeterminate = indeterminate }, [indeterminate])
  const inputId = id ?? generatedId
  const control = (
    <span className="dc-checkbox__control" aria-hidden="true">
      {indeterminate ? <Minus size={13} strokeWidth={2.5} /> : checked && <Check size={13} strokeWidth={2.5} />}
    </span>
  )

  return (
    <label
      htmlFor={inputId}
      className="dc-checkbox"
      data-disabled={disabled || undefined}
      style={style}
    >
      <input
        ref={inputRef}
        id={inputId}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={(event) => onChange(event.target.checked)}
        className="dc-checkbox__input"
      />
      {control}
      {label != null && <span className="dc-checkbox__label">{label}</span>}
    </label>
  )
}
